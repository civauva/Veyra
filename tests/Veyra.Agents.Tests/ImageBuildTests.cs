using System.Text.RegularExpressions;

namespace Veyra.Agents.Tests;

/// <summary>
/// The image is built from a narrow context: .dockerignore lets each template in by name and the Dockerfile copies each
/// one. A template the Agents module embeds but either file leaves out builds here and fails only in the image build.
/// </summary>
public partial class ImageBuildTests
{
    [Fact]
    public void Dockerfile_EmbeddedTemplates_AreAllCopiedIntoTheBuild()
    {
        var root = RepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "backend", "Veyra.Agents", "Veyra.Agents.csproj"));
        var dockerfile = File.ReadAllText(Path.Combine(root, "deploy", "Dockerfile"));
        var ignored = File.ReadAllLines(Path.Combine(root, ".dockerignore"));

        var templates = EmbeddedTemplate()
            .Matches(project)
            .Select(match => match.Groups["path"].Value.Replace('\\', '/'))
            .ToList();

        Assert.NotEmpty(templates);

        foreach (var template in templates)
        {
            Assert.Contains($"COPY {template} {template}", dockerfile, StringComparison.Ordinal);
            Assert.Contains($"!{template}", ignored);
        }
    }

    private static string RepositoryRoot()
    {
        var root = Directory.GetParent(AppContext.BaseDirectory)!;

        while (!Directory.Exists(Path.Combine(root.FullName, "deploy")))
        {
            root = root.Parent!;
        }

        return root.FullName;
    }

    [GeneratedRegex(@"<EmbeddedResource Include=""\.\.\\\.\.\\\.\.\\(?<path>integrations\\[^""]+)""")]
    private static partial Regex EmbeddedTemplate();
}
