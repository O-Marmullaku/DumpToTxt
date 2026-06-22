namespace DumpToTxt.Core;

/// <summary>
/// Output format for a dump.
/// <see cref="Classic"/> reproduces the original DumpToTxt .txt layout exactly;
/// the others are repomix-inspired styles added in the revamp.
/// </summary>
public enum OutputStyle
{
    /// <summary>The original DumpToTxt layout: DIRECTORY LIST + FILE CONTENTS, written to the Desktop.</summary>
    Classic,
    Plain,
    Markdown,
    Xml,
    Json,
}
