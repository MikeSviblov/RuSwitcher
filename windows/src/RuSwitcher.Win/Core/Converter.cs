using System.Windows.Forms;
using static RuSwitcher.Win.Native.Win32;

namespace RuSwitcher.Win.Core;

/// <summary>
/// Orchestrates a manual conversion of the last typed word: take the buffer, render it in the
/// opposite layout, delete + reinsert, then switch the layout — the Windows counterpart of the
/// macOS <c>AppDelegate.onAltTap</c>. A second trigger with no typing in between reverses it
/// (reconvert/undo, toggling back and forth) — the counterpart of <c>onAltReconvert</c>.
/// </summary>
internal static class Converter
{
    // Last AUTO conversion, for reconvert/learn-from-undo. "_a" is what's currently on screen;
    // "_b" is the alternative. Each side carries its layout so undo restores it too.
    private static string _aText = "";
    private static string _bText = "";
    private static IntPtr _aHkl;
    private static IntPtr _bHkl;
    private static bool _lastWasAuto;   // last conversion came from as-you-type auto (learn-from-undo)

    // Last MANUAL conversion (#42, N-layout set): the full candidate cycle — [picked, остальные
    // кандидаты, оригинал]. Repeated triggers step through it and wrap around; with two layouts
    // the cycle is [converted, original] — exactly the old toggle.
    private static List<(string text, IntPtr hkl)>? _cycle;
    private static int _cycleIx;

    /// <summary>True if the last conversion can be reversed (nothing typed since).</summary>
    public static bool CanReconvert => _cycle is { Count: >= 2 } || _aText.Length > 0;

    /// <summary>Any real typing invalidates a pending reconvert (the word on screen changed).</summary>
    public static void ClearReconvert() { _aText = ""; _bText = ""; _lastWasAuto = false; _cycle = null; }

    /// <summary>Record an auto-conversion so the trigger can reverse it — and so reversing it teaches
    /// an exception (learn-from-undo). <paramref name="onScreen"/> is what's now shown; the
    /// <paramref name="alternative"/> is the original typed word restored on undo.</summary>
    public static void NoteAutoConversion(string onScreen, string alternative, IntPtr onScreenHkl, IntPtr altHkl)
    {
        _aText = onScreen; _aHkl = onScreenHkl;
        _bText = alternative; _bHkl = altHkl;
        _lastWasAuto = true;
        _cycle = null;   // an auto-conversion replaces any pending manual cycle
    }

    // Trailing keys whose char in the CURRENT layout is sentence punctuation are kept literally,
    // not converted — otherwise «ghbdtn,» would become «приветб» (the comma key is «б» in ЙЦУКЕН).
    // issue #15. The ambiguous ю/б/ж tails (e.g. «зуб» = "pe,") are left to the user, as on macOS.
    private static readonly HashSet<char> TrailingPunct = new() { ',', '.', '!', '?', ';', ':', ')' };

    /// <summary>Convert the buffered word into the best other layout. Returns true if it acted.</summary>
    public static bool ConvertLastWord(KeystrokeBuffer buffer)
    {
        if (buffer.IsEmpty) return false;

        IntPtr sourceHkl = LayoutSwitcher.Current();

        // Split off trailing real punctuation (kept as typed).
        var keys = buffer.CurrentWord;
        int coreCount = keys.Count;
        var suffix = new System.Text.StringBuilder();
        while (coreCount > 0 &&
               KeyMapper.TranslateIn(keys[coreCount - 1], sourceHkl) is { } pc && TrailingPunct.Contains(pc))
        {
            suffix.Insert(0, pc);
            coreCount--;
        }
        if (coreCount == 0) return false;   // nothing but punctuation

        var core = new List<TypedKey>(coreCount);
        for (int i = 0; i < coreCount; i++) core.Add(keys[i]);
        string suf = suffix.ToString();

        // N-layout set (#42): with 3+ installed layouts «the first HKL that isn't current» is an
        // arbitrary target («руддщ» became «ηελλο», not "hello"). Render the core in EVERY other
        // layout and prefer the first dictionary-valid candidate; with no dictionary signal, fall
        // back to the first non-current layout — byte-identical to the old pair behaviour.
        var hkls = new List<IntPtr>();
        var candidates = new List<(string converted, string langTag)>();
        foreach (var hkl in LayoutSwitcher.ActiveSet())
        {
            if (hkl == sourceHkl) continue;
            hkls.Add(hkl);
            candidates.Add((KeyMapper.ConvertWord(core, hkl), SmartConvert.LangTag(hkl)));
        }
        int pick = PickTargetIndex(candidates, Dict.Available, Dict.IsValidWord);
        if (pick < 0) return false;
        IntPtr targetHkl = hkls[pick];

        string convertedCore = candidates[pick].converted;
        string originalCore = KeyMapper.ConvertWord(core, sourceHkl);  // as it was typed

        string converted = convertedCore + suf;
        string original = originalCore + suf;

        TextInjector.Replace(backspaces: coreCount + suf.Length, text: converted);
        LayoutSwitcher.SwitchTo(targetHkl);

        // Repeated triggers cycle through the remaining candidates and finally back to the
        // original (#42); the auto-undo pair does not apply to a manual convert.
        var others = new List<(string, IntPtr)>(hkls.Count);
        for (int i = 0; i < hkls.Count; i++)
            if (i != pick) others.Add((candidates[i].converted + suf, hkls[i]));
        _cycle = BuildCycle((converted, targetHkl), others, (original, sourceHkl));
        _cycleIx = 0;
        _aText = ""; _bText = ""; _lastWasAuto = false;
        buffer.Reset();
        return true;
    }

    /// <summary>The manual-trigger cycle (#42): picked candidate first, then the remaining
    /// distinct candidates, then the original — repeated triggers step through and wrap. With
    /// two layouts this is [converted, original], exactly the old toggle. Pure, for tests.</summary>
    internal static List<(string text, IntPtr hkl)> BuildCycle((string text, IntPtr hkl) picked,
        IReadOnlyList<(string text, IntPtr hkl)> others, (string text, IntPtr hkl) original)
    {
        var cycle = new List<(string text, IntPtr hkl)> { picked };
        foreach (var o in others)
            if (o.text.Length > 0 && o.text != original.text && cycle.All(c => c.text != o.text))
                cycle.Add(o);
        cycle.Add(original);
        return cycle;
    }

    /// <summary>Pick the conversion target among the non-current layouts: the FIRST candidate whose
    /// rendering is a dictionary-valid word of its own language wins (so «руддщ» goes to "hello",
    /// not to its Greek image); without a dictionary signal — the first candidate with any
    /// rendering, which with two layouts is exactly the old pair behaviour. Pure and injectable
    /// for tests; -1 when nothing renders.</summary>
    internal static int PickTargetIndex(IReadOnlyList<(string converted, string langTag)> candidates,
        bool dictAvailable, Func<string, string, bool> isValidWord)
    {
        if (dictAvailable)
            for (int i = 0; i < candidates.Count; i++)
            {
                var (c, tag) = candidates[i];
                if (c.Length > 0 && c.All(char.IsLetter) && isValidWord(c.ToLowerInvariant(), tag))
                    return i;
            }
        for (int i = 0; i < candidates.Count; i++)
            if (candidates[i].converted.Length > 0) return i;
        return -1;
    }

    /// <summary>Repeated trigger: step through the manual candidate cycle, or reverse the last
    /// auto-conversion (with learn-from-undo).</summary>
    public static bool Reconvert()
    {
        if (_cycle is { Count: >= 2 } cyc)
        {
            var cur = cyc[_cycleIx];
            _cycleIx = (_cycleIx + 1) % cyc.Count;
            var next = cyc[_cycleIx];
            TextInjector.Replace(backspaces: cur.text.Length, text: next.text);
            LayoutSwitcher.SwitchTo(next.hkl);
            return true;
        }

        if (_aText.Length == 0) return false;

        // Learn-from-undo: reversing an auto-conversion means the user rejected it — remember never to
        // auto-convert that typed word again (mirrors the macOS learn-from-undo).
        if (_lastWasAuto)
        {
            string word = LetterCoreLower(_bText);   // _bText is the original typed word being restored
            var never = Settings.Current.NeverConvert;
            if (word.Length >= 2 && !never.Contains(word))
            {
                never.Add(word);
                Settings.Current.Save();
            }
            _lastWasAuto = false;   // only teach once
        }

        TextInjector.Replace(backspaces: _aText.Length, text: _bText);
        LayoutSwitcher.SwitchTo(_bHkl);

        (_aText, _bText) = (_bText, _aText);   // toggle: a third trigger redoes the conversion
        (_aHkl, _bHkl) = (_bHkl, _aHkl);
        return true;
    }

    /// <summary>Convert the current selection via a clipboard round-trip (the counterpart of the
    /// macOS <c>convertViaClipboard</c>): Ctrl+C → convert the text char-by-char in the opposite
    /// layout → Ctrl+V, restoring the user's clipboard afterwards. One-way flip by the current
    /// layout (smart per-word conversion is a later parity step). No selection / no-op → false.
    /// MUST run on the message loop (STA), never inside the hook callback.</summary>
    public static bool ConvertSelection(bool smart)
    {
        IntPtr sourceHkl = LayoutSwitcher.Current();
        if (LayoutSwitcher.Opposite() is not { } targetHkl) return false;

        string? saved = SafeGetText();
        SafeClear();
        TextInjector.SendCtrl(VK_C);
        Thread.Sleep(60);                       // let the focused app place the selection on the clipboard
        string sel = SafeGetText() ?? "";
        if (sel.Length == 0) { RestoreClipboard(saved); return false; }   // nothing selected

        string converted = smart
            ? SmartConvert.Selection(sel, sourceHkl, targetHkl)
            : KeyMapper.ConvertText(sel, sourceHkl, targetHkl);
        if (converted == sel) { RestoreClipboard(saved); return false; }  // no-op

        SafeSetText(converted);
        TextInjector.SendCtrl(VK_V);
        Thread.Sleep(60);                       // let the paste happen before we restore the clipboard
        RestoreClipboard(saved);
        return true;
    }

    /// <summary>issue #24: convert the whole current line — select it with Shift+Home, then run
    /// the selection conversion. Works in normal apps and terminals that support Shift+Home
    /// selection. On a no-op, collapse the selection (End) so the line isn't left highlighted.</summary>
    public static bool ConvertLine(bool smart)
    {
        TextInjector.SendShift(VK_HOME);   // select from cursor to line start
        Thread.Sleep(40);
        bool ok = ConvertSelection(smart);
        if (!ok) TextInjector.SendKey(VK_END);   // drop the selection (go to line end)
        return ok;
    }

    // Trim non-letters off both ends and lowercase — the key used for the never-convert exception list.
    private static string LetterCoreLower(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && !char.IsLetter(s[a])) a++;
        while (b > a && !char.IsLetter(s[b - 1])) b--;
        return s.Substring(a, b - a).ToLowerInvariant();
    }

    // Clipboard is shared + can be briefly locked by other apps — retry, never throw.
    private static string? SafeGetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { return null; }
    }
    private static void SafeSetText(string s)
    {
        for (int i = 0; i < 6; i++) { try { Clipboard.SetText(s); return; } catch { Thread.Sleep(15); } }
    }
    private static void SafeClear()
    {
        for (int i = 0; i < 6; i++) { try { Clipboard.Clear(); return; } catch { Thread.Sleep(15); } }
    }
    private static void RestoreClipboard(string? saved)
    {
        if (string.IsNullOrEmpty(saved)) SafeClear(); else SafeSetText(saved);
    }
}
