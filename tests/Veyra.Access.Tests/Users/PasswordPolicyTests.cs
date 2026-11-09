using Microsoft.Extensions.Options;
using Veyra.Access.Users;

namespace Veyra.Access.Tests.Users;

public class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new(Options.Create(new AccessOptions()));

    [Fact]
    public void Check_ElevenCharacters_ReturnsError()
    {
        Assert.NotNull(_policy.Check("abcdefghijk"));
    }

    [Fact]
    public void Check_TwelveCharacters_ReturnsNull()
    {
        Assert.Null(_policy.Check("abcdefghijkl"));
    }

    [Fact]
    public void Check_LongerThan1024Characters_ReturnsError()
    {
        // Hashing is deliberately slow, so very long inputs are refused before they reach the hasher.
        Assert.NotNull(_policy.Check(new string('a', 1025)));
    }
}
