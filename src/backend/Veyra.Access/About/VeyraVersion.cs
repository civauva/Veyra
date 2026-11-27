using System.Reflection;

namespace Veyra.Access.About;

/// <summary>
/// The release this build of Veyra is, as <c>Directory.Build.props</c> sets it for every project (VEY-82).
/// </summary>
internal static class VeyraVersion
{
    /// <summary>
    /// The version, such as <c>0.4.1</c>, without the build suffix the SDK may add after a plus sign, so it matches its
    /// heading in the release notes.
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(VeyraVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? informational : informational[..plus];
    }
}
