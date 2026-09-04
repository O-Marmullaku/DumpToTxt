namespace DumpToTxt.Core;

public enum OutputFormatKind { Text, Markdown, Json, Xml, Word }

public sealed record OutputFormatOption(OutputFormatKind Kind, string DisplayName, string Extension);

/// <summary>
/// Presents format and layout as separate decisions while retaining <see cref="DumpConfig.Style"/>
/// as the backward-compatible serialized value.
/// </summary>
public static class OutputStyleCatalog
{
    public static IReadOnlyList<OutputFormatOption> Formats { get; } = new[]
    {
        new OutputFormatOption(OutputFormatKind.Text, "Clean text (.txt)", ".txt"),
        new OutputFormatOption(OutputFormatKind.Markdown, "Markdown (.md)", ".md"),
        new OutputFormatOption(OutputFormatKind.Json, "Structured data (.json)", ".json"),
        new OutputFormatOption(OutputFormatKind.Xml, "XML data (.xml)", ".xml"),
        new OutputFormatOption(OutputFormatKind.Word, "Word document (.docx)", ".docx"),
    };

    public static OutputFormatKind Format(OutputStyle style) => style switch
    {
        OutputStyle.Markdown or OutputStyle.MarkdownAi or OutputStyle.MarkdownCompact => OutputFormatKind.Markdown,
        OutputStyle.Json or OutputStyle.JsonCompact => OutputFormatKind.Json,
        OutputStyle.Xml or OutputStyle.XmlCompact => OutputFormatKind.Xml,
        OutputStyle.Docx => OutputFormatKind.Word,
        _ => OutputFormatKind.Text,
    };

    public static IReadOnlyList<OutputStyle> Layouts(OutputFormatKind format) => format switch
    {
        OutputFormatKind.Text => new[] { OutputStyle.Plain, OutputStyle.Classic },
        OutputFormatKind.Markdown => new[] { OutputStyle.Markdown, OutputStyle.MarkdownAi, OutputStyle.MarkdownCompact },
        OutputFormatKind.Json => new[] { OutputStyle.Json, OutputStyle.JsonCompact },
        OutputFormatKind.Xml => new[] { OutputStyle.Xml, OutputStyle.XmlCompact },
        OutputFormatKind.Word => new[] { OutputStyle.Docx },
        _ => new[] { OutputStyle.Plain },
    };

    public static OutputStyle DefaultStyle(OutputFormatKind format) => Layouts(format)[0];

    public static string FormatName(OutputFormatKind format) =>
        Formats.First(x => x.Kind == format).DisplayName;

    public static string LayoutName(OutputStyle style) => style switch
    {
        OutputStyle.Plain => "Standard",
        OutputStyle.Classic => "Classic",
        OutputStyle.Markdown => "Standard",
        OutputStyle.MarkdownAi => "AI-friendly",
        OutputStyle.MarkdownCompact => "Compact",
        OutputStyle.Json or OutputStyle.Xml => "Readable",
        OutputStyle.JsonCompact or OutputStyle.XmlCompact => "Compact",
        OutputStyle.Docx => "Navigable",
        _ => style.ToString(),
    };

    public static string Extension(OutputStyle style) => Format(style) switch
    {
        OutputFormatKind.Markdown => ".md",
        OutputFormatKind.Json => ".json",
        OutputFormatKind.Xml => ".xml",
        OutputFormatKind.Word => ".docx",
        _ => ".txt",
    };

    public static bool IsWord(OutputStyle style) => Format(style) == OutputFormatKind.Word;
}
