# Jobs and pipelines

Veyra shows every run of a job on its Jobs screens and on the Overview's pipelines (ADR 0027): SQL Server Agent jobs
from the agent's job history ([`sqlserver.md`](sqlserver.md)), and every other job from the traces it already
receives. Hangfire, Quartz.NET, hosted services, ETL workers, and CI/CD pipelines need no Veyra package; they need one
root span per run that names the job.

## What makes a run

A **root span** (a span without a parent) is a run of a job when it carries one of the job name attributes:

| Attribute | Used by |
| --- | --- |
| `cicd.pipeline.name` | The OpenTelemetry CI/CD convention, for pipelines |
| `job.name` | Quartz.NET, Hangfire integrations, and most hand-made instrumentation |

The list is a setting: **Settings → Data → Jobs → Job name attributes** in the Advanced view
(`Veyra:Storage:Jobs:NameAttributes`), separated by commas, the first one found wins. Add your own attribute there if
your instrumentation names jobs differently; it applies to spans written from then on.

From the root span, Veyra reads:

- **The job.** The attribute's value, on the root span's service (`service.name`) and environment
  (`deployment.environment.name`). Two services with a job of the same name have two jobs.
- **The run's ID.** `cicd.pipeline.run.id` when present, otherwise the trace ID.
- **Status.** A root span with error status failed; any other succeeded.
- **Steps.** The root span's direct children, in start order, with their name, duration, status, and status message
  or exception. Children that arrive after their root, from another process, are not among them; the SDKs export a
  child before its parent ends, so a job's own children always are.
- **Records and retries,** when the root span carries them, as whole numbers:

  | Attribute | Shown as |
  | --- | --- |
  | `records.processed` or `job.records.processed` | Records processed |
  | `records.total` or `job.records.total` | Records expected |
  | `retry.count` or `job.retry.count` | Retries |

Runs are kept 30 days (**Settings → Data → Jobs → Keep**, Advanced view, `VEYRA_JOB_RUN_RETENTION_DAYS`), longer
than the 7 days of traces. An older run keeps its steps and figures; its trace link says the trace has expired.

## Keep job traces when sampling

A run Veyra never receives is a run it cannot show. With head sampling in the SDK, give job spans a sampler of their
own or a ratio of 1; with the Collector's tail sampling ([`traces.md`](traces.md)), add a policy that keeps every trace
whose root has `job.name` or `cicd.pipeline.name`:

```yaml
- name: jobs
  type: string_attribute
  string_attribute: { key: job.name, values: [".+"], enabled_regex_matching: true }
```

## A hosted service (`BackgroundService`)

Start one span per run with an `ActivitySource` of your own, and register the source with the SDK. The span must have
no parent; a loop in a hosted service has none, but code that runs inside a request must clear `Activity.Current`
first, or the run becomes part of the request's trace.

```csharp
public sealed class InvoiceImport(ILogger<InvoiceImport> logger) : BackgroundService
{
    private static readonly ActivitySource Jobs = new("Billing.Jobs");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var run = Jobs.StartActivity("nightly-import");
            run?.SetTag("job.name", "nightly-import");

            try
            {
                var rows = await ExtractAsync(stoppingToken);   // each step a child span: Jobs.StartActivity("extract")
                var loaded = await LoadAsync(rows, stoppingToken);

                run?.SetTag("records.total", rows.Count);
                run?.SetTag("records.processed", loaded);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                run?.SetStatus(ActivityStatusCode.Error, exception.Message);
                run?.AddException(exception);
                logger.LogError(exception, "Invoice import failed");
            }
        }
    }
}
```

```csharp
.WithTracing(tracing => tracing.AddSource("Billing.Jobs").AddOtlpExporter())
```

## Quartz.NET and Hangfire

Their OpenTelemetry instrumentation packages trace each execution. Check which attributes the spans carry in the trace
view: if the job's name is in an attribute other than `job.name`, add that attribute to **Job name attributes**. Two
things to watch:

- **Root spans only.** Hangfire carries the trace of the request that enqueued a job into its execution, which makes
  the execution a child of that request, not a run. Start the run's span without a parent, as above, and link the
  enqueuing trace if you want to keep the connection.
- **One span per run.** Where an instrumentation gives no root span with a job name, wrap the job's work the same way
  as the hosted service above: in Quartz.NET in `IJob.Execute`, with `context.JobDetail.Key.Name` as the name and
  `context.RefireCount` as `job.retry.count`; in Hangfire in the job method or a server filter's `OnPerforming` and
  `OnPerformed`, with the retry count from the job's `RetryCount` parameter.

## CI/CD pipelines

Pipelines that export OpenTelemetry traces with the CI/CD semantic conventions, a root span per pipeline run with
`cicd.pipeline.name` and `cicd.pipeline.run.id`, show as jobs of the service that sent them, with one run per
pipeline run and its tasks as steps.
