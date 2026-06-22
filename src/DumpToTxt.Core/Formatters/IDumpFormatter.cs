namespace DumpToTxt.Core;

/// <summary>
/// Renders a gathered <see cref="DumpModel"/> into the final text for one
/// <see cref="OutputStyle"/>. Formatters are pure: no IO, no global state.
/// </summary>
public interface IDumpFormatter
{
    OutputStyle Style { get; }

    /// <summary>Renders the dump body. The returned text has no BOM; the caller adds one when writing a file.</summary>
    string Render(DumpModel model, DumpConfig cfg);
}
