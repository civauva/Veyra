# Coding standards

Code is written to be read by people. These rules add detail to the conventions in [`AGENTS.md`](../../AGENTS.md),
and `.editorconfig`, the analyzers, ESLint, and Prettier enforce what can be enforced.

## Layout and formatting

- **Indentation.** Spaces, never tab characters: 4 for C#, 2 for TypeScript, JSON, YAML, and project files, as
  `.editorconfig` defines.
- **Line length.** At most 120 characters.
- **Blank lines.** One blank line between members, and one between the logical steps inside a method. Never two
  blank lines in a row.
- **Braces.** Always use braces, also for one-line `if` bodies. In C#, braces go on their own line.
- **Wrapping.** When a call or a signature does not fit on one line, put each argument or parameter on its own
  line. A fluent chain puts each call on its own line.
- **One thing per line.** One statement per line and one declaration per line.
- **Short methods.** Prefer guard clauses and early returns over nesting. A method that needs scrolling is split.
- **No nested ternaries.** Use `if` or a `switch` expression instead.
- **Formatters.** Run `dotnet format` and Prettier before committing. Formatting changes stay out of unrelated
  changes.

## Comments

### What to comment

- **Why, not what.** A comment explains intent, a constraint, or a trade-off that the code cannot show, such as a
  unit, an invariant, a performance reason, or the section of the OTLP specification being followed.
- **Every non-obvious decision.** If a reviewer would ask "why is this here?", the answer belongs in a comment.
- **No noise.** No comments that repeat the code, and no commented-out code. A `TODO` names its work item.

### XML documentation in C#

- **Where.** Every public and protected type and member has an XML comment. Internal types and members get one
  whenever their purpose is not obvious from the name, which is most of the time.
- **Tags.**
  - `<summary>` says what the member does, in one or two sentences.
  - `<param>` and `<returns>` describe each value, including its unit and whether it can be null or empty.
  - `<exception>` lists the exceptions a caller should expect.
  - `<remarks>` explains why, and anything a caller must know.
  - `<example>` is added when usage is not obvious.
  - `<see cref="..."/>` links related types.
- **Output.** XML comments generate the API reference and feed the OpenAPI document of the REST API.
- **Enforcement.** The build will generate documentation files and fail on missing comments for public members
  (`CS1591`). This is being switched on in `Directory.Build.props` and is not active yet.

### TSDoc in TypeScript

- Every exported component, hook, function, and type has a `/** ... */` comment.
- Component props are documented field by field.
- `@param`, `@returns`, and `@example` are used as in C#.

## Example in C#

```csharp
namespace Veyra.Ingestion.Logs;

/// <summary>
/// Accepts decoded OTLP log batches and queues them for storage.
/// </summary>
/// <remarks>
/// The queue is bounded. When it is full, the batch is rejected so that the sender retries later,
/// as the OTLP specification asks. Data is never dropped silently.
/// </remarks>
internal sealed partial class LogBatchReceiver(
    Channel<LogBatch> queue,
    TimeProvider clock,
    ILogger<LogBatchReceiver> logger)
{
    /// <summary>
    /// Queues one batch of log records for storage.
    /// </summary>
    /// <param name="batch">The decoded batch. It may be empty.</param>
    /// <returns>
    /// <see langword="true"/> when the batch was queued or was empty;
    /// <see langword="false"/> when the queue is full and the sender must retry.
    /// </returns>
    public bool TryReceive(LogBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Records.Count == 0)
        {
            return true;
        }

        // Stamp the arrival time once per batch, so clock skew on the sender can be detected later.
        var stamped = batch with { ReceivedAt = clock.GetUtcNow() };

        if (!queue.Writer.TryWrite(stamped))
        {
            LogQueueFull(batch.Records.Count);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Logs a rejected batch. Source-generated, because it runs on the ingestion hot path.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Log queue is full; rejected {RecordCount} records")]
    private partial void LogQueueFull(int recordCount);
}
```

## Example in TypeScript

```tsx
/** Props for {@link HealthBadge}. */
interface HealthBadgeProps {
  /** The block's health, as set by alerting. Never computed in the browser. */
  health: "ok" | "caution" | "warning" | "unknown";

  /** The alert number from the Needs you pane, when an alert is behind the state. */
  alertNumber?: number;
}

/**
 * Shows a block's health as a coloured badge with a text label.
 *
 * The label is always rendered, because colour alone is not accessible.
 */
export function HealthBadge({ health, alertNumber }: HealthBadgeProps) {
  const label = alertNumber === undefined ? health : `${health} · ${alertNumber}`;

  return (
    <span className={`badge badge-${health}`}>
      {label}
    </span>
  );
}
```

## C# conventions

- **Visibility.** Types are `sealed` and `internal` unless they must be public or inherited.
- **Constructors.** Primary constructors for dependency injection. `record` for immutable data.
- **Async.** Async all the way. Every async method ends in `Async` and takes a `CancellationToken`, which is passed
  on to every call that accepts one.
- **Time.** Read the clock only through `TimeProvider`. Store and send `DateTimeOffset` in UTC.
- **Logging.** Structured templates only. Hot paths use source-generated `LoggerMessage` methods.
- **State.** No mutable static state. Configuration comes through validated options classes.
- **Folders.** Inside each module, code is grouped by feature, for example `Logs/`, `Retention/`, `Groupings/`, not
  by technical layer.

## SQL

- **Names.** Lower case with underscores, for example `log_record` and `observed_at`.
- **Schemas.** One schema per module, for example `ingestion` and `query`.
- **Parameters.** Always parameterised. SQL is never built from user input.
- **Migrations.** Forward-only scripts named `V0001__create_log_record.sql`, numbered per module.

## REST API

- **Controllers.** One `sealed` controller per resource, with attribute routing and `[ApiController]`. Actions stay
  thin: they validate the request, call the module, and map the result. Every action documents its responses with
  `<response code="...">` XML tags.
- **Paths.** Lower case, plural nouns, under `/api/v1`, for example `/api/v1/logs`.
- **JSON.** camelCase property names. Timestamps in ISO 8601 UTC.
- **Errors.** Problem details (RFC 9457).
- **Paging.** Cursor-based paging for lists that can grow.

## Tests

- **Names.** `Method_Scenario_ExpectedResult`.
- **Shape.** Arrange, act, and assert, separated by one blank line each.
- **Isolation.** Unit tests have no I/O and use a fake `TimeProvider`.
