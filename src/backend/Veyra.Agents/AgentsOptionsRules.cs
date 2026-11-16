using Veyra.Core.Settings;

namespace Veyra.Agents;

/// <summary>What <see cref="AgentsOptions"/> must meet at startup.</summary>
public sealed class AgentsOptionsRules : OptionsRules<AgentsOptions>
{
    private const string _prefix = "Veyra:Agents:";

    /// <inheritdoc />
    protected override IEnumerable<(bool Holds, string Message)> Rules(AgentsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        yield return (
            Uri.TryCreate(options.BuiltInServerUrl, UriKind.Absolute, out var url)
                && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp),
            _prefix + "BuiltInServerUrl must be a full http or https address, for example http://veyra:8080.");
    }
}
