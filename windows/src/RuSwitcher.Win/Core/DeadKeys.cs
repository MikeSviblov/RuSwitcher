using System.Text;

namespace RuSwitcher.Win.Core;

/// <summary>
/// Shared dead-key composition rule (spec: shared/testdata/README.md, issue #42): a SPACING
/// accent produced by a dead key attaches as a combining mark to the NEXT base character,
/// then the string is NFC-normalized. Portable — needs no OS dead-key state, so converting
/// a word "through" a dead-key layout (Greek tonos on «;») reproduces what the user saw:
/// keys «e ; i n a i» render as «είναι» in the Greek layout and «e;inai» in the US one.
/// A trailing accent with no following base character stays as the spacing char.
/// </summary>
internal static class DeadKeys
{
    // Spacing accent -> combining mark(s). U+0384 GREEK TONOS → U+0301 COMBINING ACUTE,
    // U+00A8 DIAERESIS → U+0308 COMBINING DIAERESIS, U+0385 GREEK DIALYTIKA TONOS → both.
    private static readonly Dictionary<char, string> Combining = new()
    {
        ['΄'] = "́",
        ['¨'] = "̈",
        ['΅'] = "̈́",
    };

    /// <summary>Apply the composition rule to a rendered key sequence.</summary>
    public static string Compose(string s)
    {
        if (!s.Any(Combining.ContainsKey)) return s;
        var sb = new StringBuilder(s.Length + 2);
        var pendingMarks = new StringBuilder();   // combining marks awaiting a base char
        var pendingSpacing = new StringBuilder(); // their original spacing chars (for a trailing accent)
        foreach (char c in s)
        {
            if (Combining.TryGetValue(c, out string? marks))
            {
                pendingMarks.Append(marks);       // stacked accents (¨ then ΄) merge
                pendingSpacing.Append(c);
                continue;
            }
            sb.Append(c);
            if (pendingMarks.Length > 0)
            {
                sb.Append(pendingMarks);
                pendingMarks.Clear();
                pendingSpacing.Clear();
            }
        }
        sb.Append(pendingSpacing);                // no base char followed — keep spacing form
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
