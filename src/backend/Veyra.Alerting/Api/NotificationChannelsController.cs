using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Alerting.Notifications;
using Veyra.Core.Security;

namespace Veyra.Alerting.Api;

/// <summary>
/// Manages where alert notifications go. Administrators only, because webhook URLs and addresses are sensitive.
/// </summary>
[ApiController]
[Route("api/v1/notification-channels")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AlertingErrors]
public sealed class NotificationChannelsController : ControllerBase
{
    private readonly ChannelService _channels;

    private readonly IOptions<AlertingOptions> _options;

    /// <summary>Creates the controller. The Alerting module builds it, so its service can stay internal.</summary>
    /// <param name="channels">Manages the channels.</param>
    /// <param name="options">The Alerting settings with the SMTP server.</param>
    internal NotificationChannelsController(ChannelService channels, IOptions<AlertingOptions> options)
    {
        _channels = channels;
        _options = options;
    }

    /// <summary>Lists the email server and every channel with its recent activity.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server and the channels.</returns>
    [HttpGet]
    public async Task<NotificationChannelsResponse> ListAsync(CancellationToken cancellationToken)
    {
        var smtp = _options.Value.Notifications.Smtp;
        var channels = await _channels.ListAsync(cancellationToken);

        return new NotificationChannelsResponse(
            new EmailServerResponse(smtp.IsConfigured, smtp.Host, smtp.Port, smtp.Security, smtp.From),
            channels.Select(entry => ToResponse(entry.Channel, entry.Activity)).ToList());
    }

    /// <summary>Adds a channel.</summary>
    /// <param name="input">The channel.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored channel.</returns>
    /// <response code="201">The channel was added.</response>
    /// <response code="400">The input breaks a rule; the title says which.</response>
    /// <response code="409">Another channel has the name.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(ChannelInput input, CancellationToken cancellationToken)
    {
        var channel = await _channels.CreateAsync(input, cancellationToken);

        return Created(
            $"/api/v1/notification-channels/{channel.Id}",
            ToResponse(channel, _channels.NoActivity(channel.Id)));
    }

    /// <summary>Changes a channel. Leave a webhook's URL out to keep the stored one.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="input">The channel as it should be.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored channel.</returns>
    /// <response code="200">The channel was changed.</response>
    /// <response code="404">No channel has this ID.</response>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, ChannelInput input, CancellationToken cancellationToken) =>
        await _channels.UpdateAsync(id, input, cancellationToken) is { } channel
            ? Ok(ToResponse(channel, _channels.NoActivity(channel.Id)))
            : NotFound();

    /// <summary>Deletes a channel and its delivery history.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The channel was deleted.</response>
    /// <response code="404">No channel has this ID.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await _channels.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Sends a test message to a stored channel, now.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How it went. A failed send is still a 200, with the reason.</returns>
    /// <response code="200">The result of the try.</response>
    /// <response code="404">No channel has this ID.</response>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType<SendResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestAsync(Guid id, CancellationToken cancellationToken) =>
        await _channels.TestAsync(id, cancellationToken) is { } result ? Ok(result) : NotFound();

    /// <summary>
    /// Sends a test message to a channel as entered, before it is saved. With <paramref name="id"/>, a webhook left
    /// without a URL is tested with the stored one.
    /// </summary>
    /// <param name="input">The channel as entered.</param>
    /// <param name="id">The stored channel being changed, if any.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How it went. A failed send is still a 200, with the reason.</returns>
    /// <response code="200">The result of the try.</response>
    /// <response code="400">The input breaks a rule.</response>
    [HttpPost("test")]
    [Consumes("application/json")]
    [ProducesResponseType<SendResult>(StatusCodes.Status200OK)]
    public Task<SendResult> TestDraftAsync(
        ChannelInput input,
        [FromQuery] Guid? id,
        CancellationToken cancellationToken) =>
        _channels.TestDraftAsync(input, id, cancellationToken);

    private static ChannelResponse ToResponse(NotificationChannel channel, ChannelActivity activity) => new(
        channel.Id,
        channel.Name,
        channel.Kind,
        channel.Enabled,
        channel.SendWarning,
        channel.SendCaution,
        channel.SendResolved,
        channel.WebhookUrlHint,
        channel.EmailRecipients,
        channel.CreatedAt,
        channel.UpdatedAt,
        new ChannelActivityResponse(
            activity.Days,
            activity.Days.Sum(day => day.Sent),
            activity.Days.Sum(day => day.Failed),
            activity.LastAttemptAt,
            activity.LastError,
            activity.LastStatus,
            activity.NextAttemptAt));
}
