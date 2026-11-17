using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access.Sessions;
using Veyra.Access.Settings;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// The Settings screen's API: every module's settings, a review before saving, the save, the history with revert, and
/// export and import. Admins only (ADR 0017).
/// </summary>
[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AccessErrors]
public sealed class SettingsController : ControllerBase
{
    private const int _maxHistoryPage = 200;

    private readonly SettingsService _settings;

    internal SettingsController(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Every setting with its value, default, and who saved it, and the version to save changes from.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The settings.</returns>
    [HttpGet]
    public Task<SettingsPageResponse> GetAsync(CancellationToken cancellationToken) =>
        _settings.PageAsync(cancellationToken);

    /// <summary>Checks changes and says what they would do, without saving anything.</summary>
    /// <param name="request">The changes.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The review.</returns>
    [HttpPost("review")]
    public Task<SettingsReviewResponse> ReviewAsync(
        SettingsReviewRequest request,
        CancellationToken cancellationToken) =>
        _settings.ReviewAsync(request.Changes, cancellationToken);

    /// <summary>Saves changes, all or nothing.</summary>
    /// <param name="request">The changes and the version they were made from.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// 200 with the settings after the save; 400 with the review when it refused them; 409 when another save came
    /// first.
    /// </returns>
    [HttpPut]
    [ProducesResponseType<SettingsPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SettingsReviewResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveAsync(SettingsSaveRequest request, CancellationToken cancellationToken) =>
        Answer(await _settings.SaveAsync(request, User.UserId(), cancellationToken));

    /// <summary>Changes newest first, filtered by section, person, and time.</summary>
    /// <param name="section">
    /// Only this section's changes, such as <c>Data</c>, or <c>Databases</c> and <c>Agents</c> for monitored systems.
    /// </param>
    /// <param name="changedBy">Only changes saved by this account.</param>
    /// <param name="from">Only changes saved at or after this time.</param>
    /// <param name="before">The <c>next</c> value of the previous page.</param>
    /// <param name="limit">The most changes on one page, at most 200.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page.</returns>
    [HttpGet("history")]
    public Task<SettingsHistoryResponse> HistoryAsync(
        [FromQuery] string? section,
        [FromQuery] Guid? changedBy,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] long? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        _settings.HistoryAsync(
            new SettingHistoryQuery(
                ChangedBy: changedBy,
                From: from,
                Before: before,
                Limit: Math.Clamp(limit, 1, _maxHistoryPage),
                Names: section is null ? null : _settings.KeysOf(section),
                Subjects: section is null ? null : SettingsService.SubjectsOf(section)),
            cancellationToken);

    /// <summary>Sets a setting back to the value it had before a change in the history, as a new change.</summary>
    /// <param name="id">The change's number.</param>
    /// <param name="request">The version the person saw.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>As a save; 404 when there is no such change.</returns>
    [HttpPost("history/{id:long}/revert")]
    [ProducesResponseType<SettingsPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SettingsReviewResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevertAsync(
        long id,
        SettingsRevertRequest request,
        CancellationToken cancellationToken) =>
        await _settings.RevertAsync(id, request, User.UserId(), cancellationToken) is { } result
            ? Answer(result)
            : NotFound();

    /// <summary>Every saved value except secrets, as a file to download.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file.</returns>
    [HttpGet("export")]
    public async Task<IActionResult> ExportAsync(CancellationToken cancellationToken)
    {
        var file = await _settings.ExportAsync(cancellationToken);
        var json = JsonSerializer.SerializeToUtf8Bytes(file, JsonSerializerOptions.Web);

        return File(json, "application/json", $"veyra-settings-{file.ExportedAt:yyyy-MM-dd}.json");
    }

    /// <summary>
    /// Reviews what importing a file would change. The screen then saves it with its file name as origin.
    /// </summary>
    /// <param name="file">The exported file.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The review.</returns>
    [HttpPost("import")]
    public Task<SettingsReviewResponse> ImportAsync(SettingsFile file, CancellationToken cancellationToken) =>
        _settings.ImportAsync(file, cancellationToken);

    private IActionResult Answer(SettingsSaveResult result) => result switch
    {
        { Page: { } page } => Ok(page),
        { IsConflict: true } => Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Someone else saved settings in the meantime. Look at the latest values and save again.",
        }),
        _ => BadRequest(result.Review),
    };
}
