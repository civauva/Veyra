namespace Veyra.Agents.Api;

/// <summary>An integration's package page (ADR 0028).</summary>
/// <param name="Integration">The integration as the list shows it.</param>
/// <param name="Package">Its template version and what it needs.</param>
/// <param name="Collector">The Collector that reads it.</param>
/// <param name="Validation">Its last validation.</param>
/// <param name="Modules">Each module with what its reads sent over the window.</param>
/// <param name="Checks">Its active reads with SQL Server's time for each.</param>
/// <param name="NextSteps">What the installation still lacks.</param>
/// <param name="History">Its latest changes, newest first.</param>
public sealed record IntegrationDetailResponse(
    IntegrationItemResponse Integration,
    PackageResponse Package,
    CollectorResponse Collector,
    ValidationResponse Validation,
    IReadOnlyList<ModuleFiguresResponse> Modules,
    IReadOnlyList<CheckResponse> Checks,
    IReadOnlyList<NextStepResponse> NextSteps,
    IReadOnlyList<HistoryEntryResponse> History);
