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
    /// <summary>Markdown with stable file boundaries and minimal decoration for AI ingestion.</summary>
    MarkdownAi,
    /// <summary>Markdown with summary sections omitted.</summary>
    MarkdownCompact,
    /// <summary>JSON without presentation whitespace.</summary>
    JsonCompact,
    /// <summary>XML without presentation whitespace.</summary>
    XmlCompact,
    /// <summary>Navigable Microsoft Word Open XML document.</summary>
    Docx,
}
