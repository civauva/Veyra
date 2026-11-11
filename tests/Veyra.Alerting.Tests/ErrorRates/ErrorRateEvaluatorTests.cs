using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.ErrorRates;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.ErrorRates;

[Collection(PostgresGroup.Name)]
public class ErrorRateEvaluatorTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_noon);
    private readonly FakeLogRates _rates = new();

    [Fact]
    public async Task EvaluateAsync_MeasuresTheConfiguredWindowUpToNow()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        Assert.Equal((_noon.AddMinutes(-5), _noon), _rates.LastWindow);
    }

    [Fact]
    public async Task EvaluateAsync_HighErrorRatio_OpensWarning()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _rates.Rates = [new ServiceLogRate("production", "payments-gateway", 100, 10)];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        var alert = Assert.Single(await OpenAsync(database));
        Assert.Equal("error-rate", alert.Rule);
        Assert.Equal("production", alert.Environment);
        Assert.Equal("payments-gateway", alert.Subject.Name);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(0.1, alert.Value);
        Assert.Equal(0.05, alert.Threshold);
        Assert.Equal(TimeSpan.FromMinutes(5), alert.Window);
        Assert.Equal(_noon, alert.OpenedAt);
    }

    [Fact]
    public async Task EvaluateAsync_HealthyServices_OpenNothing()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _rates.Rates = [new ServiceLogRate("production", "orders", 1000, 2), new ServiceLogRate(null, "quiet", 3, 3)];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database));
    }

    [Fact]
    public async Task EvaluateAsync_CautionRisingToWarning_KeepsTheSameAlert()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _rates.Rates = [new ServiceLogRate("production", "orders", 100, 2)];
        await evaluator.EvaluateAsync(CancellationToken.None);
        var caution = Assert.Single(await OpenAsync(database));

        _clock.Advance(TimeSpan.FromSeconds(30));
        _rates.Rates = [new ServiceLogRate("production", "orders", 100, 30)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        var warning = Assert.Single(await OpenAsync(database));
        Assert.Equal(caution.Id, warning.Id);
        Assert.Equal(AlertSeverity.Warning, warning.Severity);
        Assert.Equal(0.05, warning.Threshold);
        Assert.Equal(0.3, warning.Peak);
        Assert.Equal(_noon, warning.OpenedAt);
        Assert.Equal(_noon.AddSeconds(30), warning.UpdatedAt);
    }

    [Fact]
    public async Task EvaluateAsync_RatioRecovers_ResolvesAndKeepsPeak()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _rates.Rates = [new ServiceLogRate("production", "orders", 100, 20)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(7));
        _rates.Rates = [new ServiceLogRate("production", "orders", 1000, 1)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database));
        var resolved = Assert.Single(await new AlertStore(database).ListAsync(_noon, CancellationToken.None));
        Assert.Equal(_noon.AddMinutes(7), resolved.ResolvedAt);
        Assert.Equal(0.2, resolved.Peak);
        Assert.Equal(0.001, resolved.Value);
    }

    [Fact]
    public async Task EvaluateAsync_ServiceFallsSilent_AlertStaysOpen()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _rates.Rates = [new ServiceLogRate("production", "orders", 100, 20)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        _rates.Rates = [];
        await evaluator.EvaluateAsync(CancellationToken.None);

        var alert = Assert.Single(await OpenAsync(database));
        Assert.Equal(0.2, alert.Value);
    }

    [Fact]
    public async Task EvaluateAsync_SameServiceInTwoEnvironments_OpensOneAlertEach()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _rates.Rates =
        [
            new ServiceLogRate("production", "orders", 100, 20),
            new ServiceLogRate("staging", "orders", 100, 20),
            new ServiceLogRate(null, "orders", 100, 20),
        ];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        Assert.Equal([null, "production", "staging"], (await OpenAsync(database)).Select(a => a.Environment).Order());
    }

    [Fact]
    public async Task EvaluateAsync_RuleDisabled_OpensNothing()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _rates.Rates = [new ServiceLogRate("production", "orders", 100, 50)];
        var options = new AlertingOptions { ErrorRate = new ErrorRateOptions { Enabled = false } };

        await Evaluator(database, options).EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database));
        Assert.Null(_rates.LastWindow);
    }

    private ErrorRateEvaluator Evaluator(NpgsqlDataSource database, AlertingOptions? options = null) => new(
        _rates,
        new AlertStore(database),
        new AlertTransitions(new AlertStore(database), NullLogger<AlertTransitions>.Instance),
        Options.Create(options ?? new AlertingOptions()),
        _clock);

    private static Task<IReadOnlyList<AlertState>> OpenAsync(NpgsqlDataSource database) =>
        new AlertStore(database).ListOpenAsync("error-rate", CancellationToken.None);
}
