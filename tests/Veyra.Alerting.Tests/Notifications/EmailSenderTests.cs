using Microsoft.Extensions.Options;
using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Tests.Notifications;

[Collection(MailpitGroup.Name)]
public class EmailSenderTests(MailpitFixture mailpit)
{
    [Fact]
    public async Task SendAsync_SendsOneEmailToEveryRecipient()
    {
        var first = $"{Guid.NewGuid():N}@example.com";
        var second = $"{Guid.NewGuid():N}@example.com";

        var result = await Sender().SendAsync(
            [first, second],
            "On-call",
            NotificationMessages.Test("On-call"),
            CancellationToken.None);

        Assert.Equal(new SendResult(true, null, null), result);
        var email = Assert.Single(await mailpit.ReceivedByAsync(first));
        Assert.Equal("[Veyra] Test from Veyra: notifications to On-call work.", email.Subject);
        Assert.Equal("veyra@example.com", email.From);
        Assert.Equal([first, second], email.To);
        Assert.Contains("\"On-call\"", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithUserName_SignsIn()
    {
        var to = $"{Guid.NewGuid():N}@example.com";
        var smtp = Smtp();
        smtp.Username = "veyra";
        smtp.Password = "secret";

        var result = await Sender(smtp).SendAsync([to], "ops", NotificationMessages.Test("ops"), default);

        Assert.True(result.Succeeded, result.Error);
        Assert.Single(await mailpit.ReceivedByAsync(to));
    }

    [Fact]
    public async Task SendAsync_NoServerSetUp_FailsWithoutTrying()
    {
        var result = await Sender(new SmtpOptions()).SendAsync(
            ["a@example.com"],
            "ops",
            NotificationMessages.Test("ops"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("VEYRA_SMTP_HOST", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ServerDown_FailsWithTheReason()
    {
        var smtp = Smtp();
        smtp.Host = "127.0.0.1";
        smtp.Port = 9;

        var result = await Sender(smtp).SendAsync(["a@example.com"], "ops", NotificationMessages.Test("ops"), default);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Email failed: ", result.Error, StringComparison.Ordinal);
    }

    private SmtpOptions Smtp() => new()
    {
        Host = mailpit.Host,
        Port = mailpit.SmtpPort,
        Security = SmtpSecurity.None,
        From = "Veyra <veyra@example.com>",
    };

    private EmailSender Sender(SmtpOptions? smtp = null) => new(Options.Create(new AlertingOptions
    {
        Notifications = new NotificationOptions { Smtp = smtp ?? Smtp() },
    }));
}
