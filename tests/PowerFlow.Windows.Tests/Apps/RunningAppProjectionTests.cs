using PowerFlow.Windows.Apps;
using Xunit;

namespace PowerFlow.Windows.Tests.Apps;

public sealed class RunningAppProjectionTests
{
    [Fact]
    public void Project_FiltersUnusableProcesses_DeduplicatesPaths_AndSortsByName()
    {
        var input = new[]
        {
            new RunningAppCandidate(1, "Zulu", @"C:\\Apps\\z.exe"),
            new RunningAppCandidate(2, "Alpha", @"C:\\Apps\\a.exe"),
            new RunningAppCandidate(3, "Alpha duplicate", @"c:\\apps\\A.exe"),
            new RunningAppCandidate(4, "NoPath", null),
            new RunningAppCandidate(5, "", @"C:\\Apps\\hidden.exe")
        };

        var result = RunningAppProjection.Project(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].DisplayName);
        Assert.Equal(@"C:\\Apps\\a.exe", result[0].ExecutablePath);
        Assert.Equal("Zulu", result[1].DisplayName);
    }
}
