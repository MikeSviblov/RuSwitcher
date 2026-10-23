using static RuSwitcher.Win.Native.Win32;

namespace RuSwitcher.Win.Core;

/// <summary>
/// Installed layouts, the current/opposite pair, and switching — the Windows counterpart
/// of the macOS <c>LayoutSwitcher</c> (TIS). Layout switch is sent to the focused window
/// via WM_INPUTLANGCHANGEREQUEST, so it takes effect where the text actually is.
/// </summary>
internal static class LayoutSwitcher
{
    /// <summary>All installed keyboard layouts (HKLs).</summary>
    public static IntPtr[] Installed()
    {
        int n = GetKeyboardLayoutList(0, null);
        if (n <= 0) return Array.Empty<IntPtr>();
        var list = new IntPtr[n];
        GetKeyboardLayoutList(n, list);
        return list;
    }

    /// <summary>The user's ordered conversion set (#42): <see cref="Settings.LayoutSet"/> filtered
    /// to the actually installed HKLs, in set order. An empty or degenerate (&lt;2 installed
    /// entries) set falls back to all installed layouts — the previous behaviour.</summary>
    public static IReadOnlyList<IntPtr> ActiveSet()
    {
        var installed = Installed();
        var wanted = Settings.Current.LayoutSet;
        if (wanted.Count == 0) return installed;
        var result = new List<IntPtr>(wanted.Count);
        foreach (long stored in wanted)
            foreach (var hkl in installed)
                if ((hkl.ToInt64() & 0xFFFFFFFF) == stored && !result.Contains(hkl))
                    result.Add(hkl);
        return result.Count >= 2 ? result : installed;
    }

    /// <summary>HKL of the layout active in the focused window's thread.</summary>
    public static IntPtr Current()
    {
        IntPtr hwnd = GetForegroundWindow();
        uint tid = GetWindowThreadProcessId(hwnd, out _);
        return GetKeyboardLayout(tid);
    }

    /// <summary>The other layout of the pair (MVP: the first installed HKL that isn't current).</summary>
    public static IntPtr? Opposite()
    {
        IntPtr cur = Current();
        foreach (var hkl in Installed())
            if (hkl != cur) return hkl;
        return null;
    }

    /// <summary>Switch the focused window to <paramref name="hkl"/>.</summary>
    public static void SwitchTo(IntPtr hkl)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            PostMessageW(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        Sound.Switch();   // issue #7: optional cue on layout change
    }
}
