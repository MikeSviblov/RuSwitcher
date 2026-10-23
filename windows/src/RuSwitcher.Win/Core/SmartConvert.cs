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
    /// <summary>Per-word smart conversion against the ordered layout set (#42). A MIXED selection
    /// holds words typed in different layouts, so the direction is decided PER WORD: char maps are
    /// built for every ordered pair of set layouts, a word valid in any set language is kept, and
    /// otherwise the first dictionary-valid rendering wins (set order). <paramref name="setHkls"/>
    /// is the full set INCLUDING the current layout; without a dictionary — the old plain one-way
    /// flip from <paramref name="sourceHkl"/> into the first other layout.</summary>
    public static string Selection(string text, IntPtr sourceHkl, IReadOnlyList<IntPtr> setHkls)
    {
        if (setHkls.Count < 2) return text;
        if (!Dict.Available)
        {
            foreach (var hkl in setHkls)
                if (hkl != sourceHkl) return KeyMapper.ConvertText(text, sourceHkl, hkl);
            return text;
        }

        var setTags = new List<string>(setHkls.Count);
        foreach (var hkl in setHkls)
            if (!setTags.Contains(LangTag(hkl))) setTags.Add(LangTag(hkl));

        // Char maps for every ordered pair of the set (a word may have been typed in ANY of them).
        var maps = new List<(Dictionary<char, char> fwd, string tgtTag)>();
        foreach (var src in setHkls)
            foreach (var tgt in setHkls)
                if (src != tgt)
                    maps.Add((KeyMapper.BuildPairMap(src, tgt), LangTag(tgt)));

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
                if (flipped != word) candidates.Add((flipped, LetterCore(flipped), tag));
            }
            sb.Append(PickWordConversion(word, core, setTags, candidates, Dict.IsValidWord,
                Settings.Current.NeverConvert, Settings.Current.AlwaysConvert));
        }
        return sb.ToString();
    }

    /// <summary>The per-word verdict against the candidate list, in set order. Precedence is the
    /// pair version's, unchanged: always-list (on the CONVERTED form) → never-list (typed form) →
    /// keep words that are real in ANY set language (iPhone, стоит — whatever layout they came
    /// from) → first candidate whose rendering is a real word of its own language → keep.
    /// Pure, for tests.</summary>
    internal static string PickWordConversion(string word, string core, IReadOnlyList<string> setTags,
        IReadOnlyList<(string flipped, string flippedCore, string tgtTag)> candidates,
        Func<string, string, bool> isValidWord, ICollection<string> never, ICollection<string> always)
    {
        foreach (var c in candidates)
            if (c.flippedCore.Length > 0 && always.Contains(c.flippedCore.ToLowerInvariant()))
                return c.flipped;
        if (never.Contains(core.ToLowerInvariant())) return word;

        if (core.Length < 2) return word;                       // too short to judge — keep
        foreach (var tag in setTags)
            if (isValidWord(core, tag)) return word;            // already a real word — keep

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
