namespace DumpToTxt.Core;

/// <summary>
/// Where a finished dump is delivered. <see cref="File"/> is the legacy behavior
/// (write the selected format, then the app opens it). Clipboard and Stdout deliver text.
/// </summary>
public enum OutputTarget
{
    /// <summary>Write the dump to a .txt file in the output directory (legacy default).</summary>
    File,
    /// <summary>Copy the rendered dump to the clipboard (handled by the GUI layer).</summary>
    Clipboard,
    /// <summary>Write the rendered dump to standard output.</summary>
    Stdout,
}
