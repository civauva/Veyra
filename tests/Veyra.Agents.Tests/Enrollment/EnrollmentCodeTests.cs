using Veyra.Agents.Enrollment;

namespace Veyra.Agents.Tests.Enrollment;

public class EnrollmentCodeTests
{
    [Fact]
    public void Generate_Code_IsThreeGroupsOfFourEasyToReadCharacters()
    {
        var code = EnrollmentCode.Generate();

        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", code);
        Assert.NotEqual(code, EnrollmentCode.Generate());
    }

    [Theory]
    [InlineData("k7qm-4twx-9pra", "K7QM-4TWX-9PRA")]
    [InlineData(" K7QM 4TWX 9PRA ", "K7QM-4TWX-9PRA")]
    [InlineData("K7QM4TWX9PRA", "K7QM-4TWX-9PRA")]
    public void Normalize_TypedCode_IsTheCodeAsIssued(string typed, string issued)
    {
        Assert.Equal(issued, EnrollmentCode.Normalize(typed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("K7QM-4TWX")]
    [InlineData("K7QM-4TWX-9PRA-EXTRA")]
    [InlineData("K7QM-4TWX-9PR!")]
    public void Normalize_NotACode_IsNull(string typed)
    {
        Assert.Null(EnrollmentCode.Normalize(typed));
    }

    [Fact]
    public void Hash_SameCodeTypedDifferently_IsTheSame()
    {
        var typed = EnrollmentCode.Normalize("k7qm4twx9pra")!;

        Assert.Equal(EnrollmentCode.Hash("K7QM-4TWX-9PRA"), EnrollmentCode.Hash(typed));
    }
}
