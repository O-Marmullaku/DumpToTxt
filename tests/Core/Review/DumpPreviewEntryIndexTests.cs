using DumpToTxt.Core;

namespace DumpToTxt.Tests;

public sealed class DumpPreviewEntryIndexTests
{
    [Fact]
    public void Descendants_ReturnOnlyEntriesBelowTheRequestedPath()
    {
        DumpPreviewEntry[] entries =
        [
            Entry(@"src\a.cs"),
            Entry(@"src\nested\b.cs"),
            Entry("README.md"),
        ];

        var index = new DumpPreviewEntryIndex(entries);

        Assert.Equal(new[] { @"src\a.cs", @"src\nested\b.cs" },
            index.Descendants("src").Select(entry => entry.RelativePath).ToArray());
        Assert.Equal(new[] { @"src\nested\b.cs" },
            index.Descendants("src/nested").Select(entry => entry.RelativePath).ToArray());
        Assert.Equal(new[] { "README.md" },
            index.Descendants("README.md").Select(entry => entry.RelativePath).ToArray());
        Assert.Empty(index.Descendants("missing"));
    }

    private static DumpPreviewEntry Entry(string relativePath) => new(
        relativePath, relativePath, DumpFileTypeKey.FromFileName(relativePath), 1, 1, true);
}
