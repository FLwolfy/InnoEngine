namespace Inno.Adapter.Text;

/// <summary>
/// Selects a replaceable Unicode shaping and rasterization implementation.
/// </summary>
public enum TextBackend
{
    /// <summary>
    /// Uses the bundled FreeType and HarfBuzz implementation.
    /// </summary>
    FreeTypeHarfBuzz
}
