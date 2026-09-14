using DumpToTxt.Core;
using Xunit;

namespace DumpToTxt.Tests;

public sealed class PresetSelectionTests
{
    [Theory]
    [InlineData("Docs only", "readme.md", "program.cs")]
    [InlineData("Frontend", "app.ts", "notes.md")]
    public void ContentPresetOverridesRememberedAllText(string name, string included, string excluded)
    {
        var config = DumpConfig.CreateDefault();
        config.LastSelectionMode = DumpSelectionMode.Thorough;
        Presets.ByName(name)!.Apply(config);
        var selection = DumpContentSelection.FromMode(config.LastSelectionMode, config);
        Assert.True(selection.AllowsType(DumpFileTypeKey.FromFileName(included)));
        Assert.False(selection.AllowsType(DumpFileTypeKey.FromFileName(excluded)));
    }

    [Fact]
    public void ChangedFilesPresetPreservesContentMode()
    {
        var config = DumpConfig.CreateDefault();
        config.LastSelectionMode = DumpSelectionMode.None;
        Presets.ChangedFiles.Apply(config);
        Assert.Equal(DumpSelectionMode.None, config.LastSelectionMode);
        Assert.True(config.OnlyGitChanged);
    }
}
