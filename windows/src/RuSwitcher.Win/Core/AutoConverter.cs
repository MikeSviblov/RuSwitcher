using System.Linq;

namespace RuSwitcher.Win.Core;

/// <summary>
/// As-you-type auto conversion — the Windows counterpart of the macOS <c>LayoutDetector.decide</c>
/// + <c>AutoSwitch</c>. On a word boundary it looks at the just-typed word and, only when the
/// dictionary is confident it was typed in the wrong layout, flips it into the opposite layout and
/// switches the keyboard. Precision over recall: at any uncertainty it does nothing (the manual
/// trigger always works). Beta, default OFF.
/// </summary>
internal static class AutoConverter
{
    /// <summary>Try to auto-convert a completed word. Runs on the message loop (NOT inside the LL-hook
    /// callback) AFTER the real Space has already been delivered to the app — so it deletes the word
    /// PLUS that trailing space (<paramref name="keys"/>.Count + 1 backspaces) and re-types the
    /// converted word followed by a space. Deferring off the hook thread keeps the callback O(1) and
    /// avoids the LowLevelHooksTimeout / silent-unhook trap (COM + SendInput must not run in-callback).
    /// Returns true if it converted; on "keep" it does nothing (the word + space stay as typed).</summary>
    public static bool TryConvertWord(IReadOnlyList<TypedKey> keys)
    {
        if (keys.Count == 0) return false;

        IntPtr sourceHkl = LayoutSwitcher.Current();
        string typed = KeyMapper.ConvertWord(keys, sourceHkl);
        string srcTag = SmartConvert.LangTag(sourceHkl);
        bool caps = keys.All(k => k.Caps);

        // N-layout set (#42): run the UNCHANGED pairwise verdict once per candidate layout and
        // aggregate — convert only when EXACTLY ONE candidate says yes; zero or several mean
        // ambiguity → do nothing (precision over recall, the manual trigger still works).
        // With two layouts there is a single candidate and behaviour is byte-identical.
        var candidates = new List<(string converted, string tgtTag)>();
        var hkls = new List<IntPtr>();
        foreach (var hkl in LayoutSwitcher.ActiveSet())
        {
            if (hkl == sourceHkl) continue;
            hkls.Add(hkl);
            candidates.Add((KeyMapper.ConvertWord(keys, hkl), SmartConvert.LangTag(hkl)));
        }
        int pick = PickAutoTargetIndex(typed, candidates, srcTag, caps,
            Dict.Available, Dict.IsValidWord,
            Settings.Current.NeverConvert, Settings.Current.AlwaysConvert);
        if (pick < 0) return false;

        string converted = candidates[pick].converted;
        IntPtr targetHkl = hkls[pick];

        TextInjector.Replace(backspaces: keys.Count + 1, text: converted + " ");
        LayoutSwitcher.SwitchTo(targetHkl);
        Converter.NoteAutoConversion(converted, typed, targetHkl, sourceHkl);
        return true;
    }

    /// <summary>The N-set aggregation (#42): index of the single candidate whose pairwise verdict
    /// is "convert", or -1 when none or more than one agree (ambiguous → keep). Pure, for tests.</summary>
    internal static int PickAutoTargetIndex(string typed,
        IReadOnlyList<(string converted, string tgtTag)> candidates, string srcTag, bool caps,
        bool dictAvailable, Func<string, string, bool> isValidWord,
        ICollection<string> never, ICollection<string> always)
    {
        int pick = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            var (converted, tgtTag) = candidates[i];
            if (converted.Length == 0 || converted == typed) continue;
            if (!ShouldConvertPure(typed, converted, srcTag, tgtTag, caps,
                    dictAvailable, isValidWord, never, always)) continue;
            if (pick >= 0) return -1;   // two candidates claim the word → ambiguous
            pick = i;
        }
        return pick;
    }

    /// <summary>The verdict logic, ported from the macOS <c>LayoutDetector.decide</c>. Pure: the
    /// dictionary and exception lists are passed in, so it is fully unit-testable. Returns true only
    /// when the word should be flipped to the opposite layout.</summary>
    internal static bool ShouldConvertPure(string typed, string converted, string srcTag, string tgtTag,
        bool caps, bool dictAvailable, Func<string, string, bool> isValidWord,
        ICollection<string> never, ICollection<string> always)
    {
        string typedLc = typed.ToLowerInvariant();
        string convertedLc = converted.ToLowerInvariant();

        // Explicit overrides (checked before the soft vetoes).
        if (always.Contains(convertedLc)) return true;
        if (never.Contains(typedLc)) return false;

        // --- soft vetoes (cheap, before the dictionary) ---
        if (typed.Length < 3) return false;                 // 1–2 letters: too many cross-layout collisions
        // Letter veto, macOS parity (AutoSwitch.decide letter veto + the caller's
        // trailing-punctuation split): enough that ONE side is all letters — letters living on
        // punctuation keys make the typed image mixed while the CONVERSION is a clean word
        // (the Greek tonos dead key: «e;inai»→«είναι»; ЙЦУКЕН's ж: «rf;tncz»→«кажется»).
        // A TRAILING non-letter stays vetoed: «yt.» may be the abbreviation "yt" + full stop,
        // not «нею» — the same ambiguity macOS bails on (relaxing it is upstream issue #35).
        // Digits/URL/code/email keep both sides mixed → still vetoed.
        if (!typed.All(char.IsLetter))
        {
            if (!converted.All(char.IsLetter)) return false;
            if (!char.IsLetter(typed[^1])) return false;
        }
        if (!caps)                                          // under Caps Lock these two aren't acronyms/camelCase
        {
            if (IsAllCaps(typed)) return false;             // acronyms
            if (LooksLikeCode(typed)) return false;         // camelCase / mixed scripts
        }

        // --- Hebrew cross-script pairs (positive signal only, from the non-Hebrew side) ---
        bool srcHe = srcTag == "he", tgtHe = tgtTag == "he";
        if (srcHe || tgtHe)
        {
            string sideTag = srcHe ? tgtTag : srcTag;
            if (sideTag == "he" || !dictAvailable) return false;
            if (srcHe)
            {
                // The EN image of correct Hebrew can be "word + punctuation" and falsely pass the
                // dictionary — only feed it an all-letter image.
                if (!converted.All(char.IsLetter)) return false;
                return isValidWord(convertedLc, sideTag);
            }
            // Typed on the non-Hebrew side: never auto-convert toward Hebrew (direction unresolvable
            // without a Hebrew dictionary — precision over recall). The manual trigger still works.
            return false;
        }

        // --- common same-alphabet-swap case ---
        if (!dictAvailable) return false;
        if (!isValidWord(convertedLc, tgtTag)) return false;   // flipped form isn't a real word → keep
        if (isValidWord(typedLc, srcTag)) return false;        // typed form is already real → keep
        return true;
    }

    private static bool IsAllCaps(string s) =>
        s == s.ToUpperInvariant() && s != s.ToLowerInvariant();

    /// <summary>Looks like a code identifier: an internal capital (camelCase/PascalCase) or a mix of
    /// Latin and Cyrillic in one token — almost always code, not a word.</summary>
    private static bool LooksLikeCode(string s)
    {
        for (int i = 1; i < s.Length; i++)
            if (char.IsUpper(s[i])) return true;

        bool latin = false, cyr = false;
        foreach (char c in s)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin = true;
            else if (c is >= 'Ѐ' and <= 'ӿ') cyr = true;
        }
        return latin && cyr;
    }
}
