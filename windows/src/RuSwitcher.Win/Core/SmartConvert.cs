using System.Text;

namespace RuSwitcher.Win.Core;

/// <summary>
/// Smart per-word conversion of a selection — the Windows counterpart of the macOS
/// <c>SmartConvert.selection</c>. A word is flipped to the opposite layout only if it is NOT a
/// real word as typed AND becomes a real word once flipped (so correct text and intentional
/// foreign words are kept). If the spell-check dictionary is unavailable, falls back to a plain
/// one-way flip. Direction: source (current) layout → target (opposite), the common mis-typed case.
/// </summary>
internal static class SmartConvert
{
    /// <summary>Per-word smart conversion against the ordered candidate set (#42): for each word
    /// the first dictionary-valid rendering wins, same ranking as the manual trigger. Without a
    /// dictionary — the old plain one-way flip into the first candidate.</summary>
    public static string Selection(string text, IntPtr sourceHkl, IReadOnlyList<IntPtr> targetHkls)
    {
        if (targetHkls.Count == 0) return text;
        if (!Dict.Available) return KeyMapper.ConvertText(text, sourceHkl, targetHkls[0]);

        string srcTag = LangTag(sourceHkl);
        var maps = new List<(Dictionary<char, char> fwd, string tag)>(targetHkls.Count);
        foreach (var hkl in targetHkls)
            maps.Add((KeyMapper.BuildPairMap(sourceHkl, hkl), LangTag(hkl)));

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                sb.Append(text[i++]);
                continue;
            }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            string word = text.Substring(start, i - start);

            string core = LetterCore(word);
            if (core.Length == 0) { sb.Append(word); continue; }
            var candidates = new List<(string flipped, string flippedCore, string tgtTag)>(maps.Count);
            foreach (var (fwd, tag) in maps)
            {
                string flipped = Flip(word, fwd);
                candidates.Add((flipped, LetterCore(flipped), tag));
            }
            sb.Append(PickWordConversion(word, core, srcTag, candidates, Dict.IsValidWord,
                Settings.Current.NeverConvert, Settings.Current.AlwaysConvert));
        }
        return sb.ToString();
    }

    /// <summary>The per-word verdict against the candidate list, in set order. Precedence is the
    /// pair version's, unchanged: always-list (on the CONVERTED form) → never-list (typed form) →
    /// keep real source words (iPhone, стоит) → first candidate whose rendering is a real word of
    /// its own language → keep. Pure, for tests.</summary>
    internal static string PickWordConversion(string word, string core, string srcTag,
        IReadOnlyList<(string flipped, string flippedCore, string tgtTag)> candidates,
        Func<string, string, bool> isValidWord, ICollection<string> never, ICollection<string> always)
    {
        foreach (var c in candidates)
            if (c.flippedCore.Length > 0 && always.Contains(c.flippedCore.ToLowerInvariant()))
                return c.flipped;
        if (never.Contains(core.ToLowerInvariant())) return word;

        if (core.Length < 2) return word;                       // too short to judge — keep
        if (isValidWord(core, srcTag)) return word;             // already a real word — keep

        foreach (var c in candidates)
            if (c.flippedCore.Length >= 2 && isValidWord(c.flippedCore, c.tgtTag))
                return c.flipped;                               // gibberish that becomes a real word

        return word;                                            // unresolved (name/brand/unknown) — keep
    }

    private static string Flip(string s, Dictionary<char, char> map)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(map.TryGetValue(c, out char m) ? m : c);
        return sb.ToString();
    }

    private static string LetterCore(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && !char.IsLetter(s[a])) a++;
        while (b > a && !char.IsLetter(s[b - 1])) b--;
        return s.Substring(a, b - a);
    }

    /// <summary>BCP-47-ish language tag from a keyboard layout handle (low word = LANGID).</summary>
    public static string LangTag(IntPtr hkl)
    {
        int primary = (int)((long)hkl & 0x3FF);
        return primary switch
        {
            0x19 => "ru",
            0x09 => "en",
            0x22 => "uk",
            0x23 => "be",
            0x02 => "bg",
            0x0D => "he",
            0x08 => "el",
            0x2B => "hy",
            0x37 => "ka",
            _ => "en",
        };
    }
}
