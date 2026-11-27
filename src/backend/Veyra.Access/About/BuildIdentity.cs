using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace Veyra.Access.About;

/// <summary>
/// Tells one build of the app from another, so an open page knows a newer one is running and offers to reload (VEY-82).
/// It is a hash of the app's <c>index.html</c>: Vite names every bundle by its content, so any change to the app
/// changes the file, while a release whose app did not change asks nobody to reload. Without a built app, as in
/// development, the version stands in.
/// </summary>
internal sealed class BuildIdentity
{
    private readonly Lazy<string> _value;

    /// <summary>Creates the identity of the app this host serves.</summary>
    /// <param name="environment">Where the built app lies.</param>
    public BuildIdentity(IWebHostEnvironment environment)
    {
        _value = new Lazy<string>(() => Read(environment));
    }

    /// <summary>The identity: twelve hex digits of the hash, or the version without a built app.</summary>
    public string Value => _value.Value;

    private static string Read(IWebHostEnvironment environment)
    {
        var index = environment.WebRootFileProvider.GetFileInfo("index.html");

        if (!index.Exists)
        {
            return VeyraVersion.Current;
        }

        using var stream = index.CreateReadStream();

        return Convert.ToHexStringLower(SHA256.HashData(stream))[..12];
    }
}
