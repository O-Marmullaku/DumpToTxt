namespace DumpToTxt.Core;

/// <summary>
/// Output format for a dump.
/// <see cref="Classic"/> retains the C# golden directory-list/file-block contract;
/// other values select the supported text, structured-data and Word layouts.
/// </summary>
public enum OutputStyle
{
    /// <summary>The flat DIRECTORY LIST + FILE CONTENTS layout. Destination is configured separately.</summary>
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
