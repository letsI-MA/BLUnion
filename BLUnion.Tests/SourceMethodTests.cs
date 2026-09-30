using BLUnion.Models;
using Xunit;

namespace BLUnion.Tests;

public class SourceMethodTests
{
    public static IEnumerable<object[]> AllMethodsAndLanguages() =>
        from method in Enum.GetValues<SourceMethod>()
        from language in Enum.GetValues<DisplayLanguage>()
        select new object[] { method, language };

    [Theory]
    [MemberData(nameof(AllMethodsAndLanguages))]
    public void GetDisplayName_ReturnsNonEmptyText(SourceMethod method, DisplayLanguage language)
    {
        Assert.False(string.IsNullOrWhiteSpace(method.GetDisplayName(language)));
    }
}
