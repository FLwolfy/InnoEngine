namespace Inno.Rendering;

/// <summary>
/// Represents a mutable keyword set.
/// </summary>
public sealed class MaterialKeywords
{
    private readonly HashSet<string> m_keywords = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> values => m_keywords;

    public bool Contains(string keyword) => m_keywords.Contains(keyword);

    public void Enable(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("Keyword cannot be empty.", nameof(keyword));
        }

        m_keywords.Add(keyword);
    }

    public void Disable(string keyword)
    {
        m_keywords.Remove(keyword);
    }
}
