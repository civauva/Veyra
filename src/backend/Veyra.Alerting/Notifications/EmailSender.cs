using System.Globalization;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Veyra.Alerting.Notifications;

/// <summary>Sends notifications as plain-text email through the SMTP server in Veyra's settings.</summary>
/// <param name="options">The Alerting settings with the SMTP server and the time limit per try.</param>
internal sealed class EmailSender(IOptions<AlertingOptions> options)
{
    /// <summary>Sends one message to every recipient in one email.</summary>
    /// <param name="recipients">The addresses.</param>
    /// <param name="channelName">The channel's name, named in the email so readers know where it came from.</param>
    /// <param name="message">What to send.</param>
    /// <param name="cancellationToken">Cancels the try when Veyra stops.</param>
    /// <returns>How the try ended. Never throws for SMTP or network failures.</returns>
    public async Task<SendResult> SendAsync(
        IReadOnlyList<string> recipients,
        string channelName,
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value.Notifications;
        var smtp = settings.Smtp;

        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.From))
        {
            return SendResult.Failed("No email server is set up. Set VEYRA_SMTP_HOST and VEYRA_SMTP_FROM.");
        }

        using var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(smtp.From));

        foreach (var recipient in recipients)
        {
            email.To.Add(MailboxAddress.Parse(recipient));
        }

        email.Subject = "[Veyra] " + message.Text;
        email.Body = new TextPart("plain") { Text = NotificationMessages.ToEmailBody(message, channelName) };

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(settings.SendTimeout);

        using var client = new SmtpClient { Timeout = (int)settings.SendTimeout.TotalMilliseconds };

        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, Security(smtp.Security), limit.Token);

            if (!string.IsNullOrEmpty(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, limit.Token);
            }

            await client.SendAsync(email, limit.Token);
            await client.DisconnectAsync(quit: true, limit.Token);

            return new SendResult(true, null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SendResult.Failed(string.Create(
                CultureInfo.InvariantCulture,
                $"The email server did not answer within {settings.SendTimeout.TotalSeconds:0.#} s"));
        }
        catch (AuthenticationException)
        {
            return SendResult.Failed("The email server refused the user name or password.");
        }
        catch (Exception exception)
            when (exception is SmtpCommandException or SmtpProtocolException or SslHandshakeException
                or ServiceNotConnectedException or SocketException or IOException)
        {
            return SendResult.Failed($"Email failed: {exception.Message}");
        }
    }

    private static SecureSocketOptions Security(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };
}
