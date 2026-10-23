using System.Drawing;
using System.Windows.Forms;
using RuSwitcher.Win.Core;

namespace RuSwitcher.Win.UI;

/// <summary>Settings window (WinForms) — the Windows counterpart of the macOS settings window.
/// Edits Settings.Current live (saved on each change). Trigger changes are surfaced via
/// <see cref="TriggerChanged"/>/<see cref="SwitchChanged"/> so the running detectors update at once.</summary>
internal sealed class SettingsForm : Form
{
    public event Action<TriggerKind>? TriggerChanged;
    /// <summary>Raised when the layout-switch hotkey changes (so the running detector updates).</summary>
    public event Action? SwitchChanged;

    public SettingsForm()
    {
        var s = Settings.Current;

        Text = L10n.T("settings.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 360);

        int y = 16;

        var lblTrigger = new Label { Text = L10n.T("settings.trigger"), Left = 16, Top = y + 4, AutoSize = true };
        var cmbTrigger = new ComboBox { Left = 150, Top = y, Width = 234, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbTrigger.Items.AddRange(new object[]
        {
            Tray.TrayIcon.TriggerName(TriggerKind.CtrlDoubleTap),
            Tray.TrayIcon.TriggerName(TriggerKind.ShiftDoubleTap),
            Tray.TrayIcon.TriggerName(TriggerKind.PauseBreak),
        });
        cmbTrigger.SelectedIndex = (int)s.Trigger;
        cmbTrigger.SelectedIndexChanged += (_, _) =>
        {
            var t = (TriggerKind)cmbTrigger.SelectedIndex;
            s.Trigger = t; s.Save();
            TriggerChanged?.Invoke(t);
        };
        y += 34;

        var chkWhole = MakeCheck(L10n.T("settings.wholeline"), ref y, s.ConvertWholeLine,
            v => { s.ConvertWholeLine = v; s.Save(); });
        var chkSmart = MakeCheck(L10n.T("settings.smart"), ref y, s.SmartConversion,
            v => { s.SmartConversion = v; s.Save(); });
        var chkAuto = MakeCheck(L10n.T("settings.auto"), ref y, s.AutoConvert,
            v => { s.AutoConvert = v; s.Save(); });
        var chkSound = MakeCheck(L10n.T("settings.sound"), ref y, s.SoundOnSwitch,
            v => { s.SoundOnSwitch = v; s.Save(); });
        var chkStart = MakeCheck(L10n.T("settings.startup"), ref y, AutoStart.IsEnabled(),
            v => AutoStart.SetEnabled(v));
        var chkPerApp = MakeCheck(L10n.T("settings.perapp"), ref y, s.PerAppLayout,
            v => { s.PerAppLayout = v; s.Save(); });
        var chkUpdates = MakeCheck(L10n.T("settings.updates"), ref y, s.CheckUpdatesEnabled,
            v => { s.CheckUpdatesEnabled = v; s.Save(); });

        y += 6;
        var lblSwitch = new Label { Text = L10n.T("settings.switchhotkey"), Left = 16, Top = y + 4, AutoSize = true };
        var cmbSwitch = new ComboBox { Left = 180, Top = y, Width = 204, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbSwitch.Items.AddRange(new object[]
        {
            L10n.T("settings.off"),
            Tray.TrayIcon.TriggerName(TriggerKind.CtrlDoubleTap),
            Tray.TrayIcon.TriggerName(TriggerKind.ShiftDoubleTap),
            Tray.TrayIcon.TriggerName(TriggerKind.PauseBreak),
        });
        cmbSwitch.SelectedIndex = s.SwitchTriggerEnabled ? (int)s.SwitchTrigger + 1 : 0;
        cmbSwitch.SelectedIndexChanged += (_, _) =>
        {
            int i = cmbSwitch.SelectedIndex;
            s.SwitchTriggerEnabled = i > 0;
            if (i > 0) s.SwitchTrigger = (TriggerKind)(i - 1);
            s.Save();
            SwitchChanged?.Invoke();
        };
        y += 40;

        // --- N-layout set (#42): which layouts take part in conversion and in what order ---
        var lblLayouts = new Label { Text = L10n.T("settings.layouts"), Left = 16, Top = y, AutoSize = true };
        y += 20;
        var lstLayouts = new CheckedListBox { Left = 16, Top = y, Width = 300, Height = 76, CheckOnClick = true, IntegralHeight = false };
        var btnUp = new Button { Text = L10n.T("settings.layouts.up"), Left = 324, Top = y, Width = 60 };
        var btnDown = new Button { Text = L10n.T("settings.layouts.down"), Left = 324, Top = y + 30, Width = 60 };

        // Active-set layouts first (checked, in set order), then the remaining installed ones.
        var active = LayoutSwitcher.ActiveSet();
        foreach (var hkl in active) lstLayouts.Items.Add(new LayoutItem(hkl), true);
        foreach (var hkl in LayoutSwitcher.Installed())
            if (!active.Contains(hkl)) lstLayouts.Items.Add(new LayoutItem(hkl), false);

        void SaveLayoutSet()
        {
            var set = new List<long>();
            for (int i = 0; i < lstLayouts.Items.Count; i++)
                if (lstLayouts.GetItemChecked(i))
                    set.Add(((LayoutItem)lstLayouts.Items[i]).Hkl.ToInt64() & 0xFFFFFFFF);
            // Fewer than two checked can't form a conversion set → fall back to "all installed".
            s.LayoutSet = set.Count >= 2 ? set : new List<long>();
            s.Save();
        }
        // ItemCheck fires BEFORE the state flips — save after the message is fully handled.
        lstLayouts.ItemCheck += (_, _) => BeginInvoke(SaveLayoutSet);

        void MoveSelected(int delta)
        {
            int i = lstLayouts.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= lstLayouts.Items.Count) return;
            var item = lstLayouts.Items[i];
            bool check = lstLayouts.GetItemChecked(i);
            lstLayouts.Items.RemoveAt(i);
            lstLayouts.Items.Insert(j, item);
            lstLayouts.SetItemChecked(j, check);
            lstLayouts.SelectedIndex = j;
            SaveLayoutSet();
        }
        btnUp.Click += (_, _) => MoveSelected(-1);
        btnDown.Click += (_, _) => MoveSelected(+1);
        y += 84;

        var btnExceptions = new Button { Text = L10n.T("settings.exceptions"), Left = 16, Top = y, Width = 130 };
        btnExceptions.Click += (_, _) => { using var ex = new ExceptionsForm(); ex.ShowDialog(this); };

        var link = new LinkLabel { Text = "github.com/rashn/RuSwitcher", Left = 156, Top = y + 4, AutoSize = true };
        link.LinkClicked += (_, _) => OpenUrl("https://github.com/rashn/RuSwitcher");
        y += 40;

        var btnClose = new Button { Text = L10n.T("settings.close"), Left = 294, Top = y, Width = 90, DialogResult = DialogResult.OK };
        AcceptButton = btnClose;
        ClientSize = new Size(400, y + 40);

        Controls.AddRange(new Control[]
        {
            lblTrigger, cmbTrigger, chkWhole, chkSmart, chkAuto, chkSound, chkStart, chkPerApp,
            chkUpdates, lblSwitch, cmbSwitch, lblLayouts, lstLayouts, btnUp, btnDown,
            btnExceptions, link, btnClose,
        });
    }

    /// <summary>A CheckedListBox row: an installed layout shown by its native language name.</summary>
    private sealed record LayoutItem(IntPtr Hkl)
    {
        public override string ToString()
        {
            try { return System.Globalization.CultureInfo.GetCultureInfo((int)(Hkl.ToInt64() & 0xFFFF)).NativeName; }
            catch { return SmartConvert.LangTag(Hkl); }
        }
    }

    private CheckBox MakeCheck(string text, ref int top, bool value, Action<bool> onChange)
    {
        var cb = new CheckBox { Text = text, Left = 16, Top = top, Width = 368, Checked = value };
        cb.CheckedChanged += (_, _) => onChange(cb.Checked);
        top += 26;
        return cb;
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}
