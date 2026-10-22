using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atomic.Core;

namespace Atomic.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        OptRestore.IsChecked = AppState.Settings.AutoRestorePoint;
        OptSounds.IsChecked = AppState.Settings.Sounds;
        ShowAutostart();
        OptConfirm.IsChecked = AppState.Settings.ConfirmRisky;
        (AppState.Settings.CloseAction switch { "tray" => CloseTray, "exit" => CloseExit, _ => CloseAsk }).IsChecked = true;
        foreach (var rb in new[] { CloseAsk, CloseTray, CloseExit })
            rb.Checked += (s, _) => { AppState.Settings.CloseAction = (string)((RadioButton)s).Tag; AppState.Settings.Save(); };
        About.Text = $"{AppState.Name} {AppState.Version}  -  {L.F("{0} tweaks available for this PC", AppState.Engine.All.Count)}\n"
                   + $"{L.T(Sys.IsAdmin ? "Running as Administrator" : "Read-only mode (not elevated)")}  -  log: {Sys.LogFile}";

        (L.Thai ? LangTh : LangEn).IsChecked = true;
        foreach (var rb in new[] { LangEn, LangTh })
            rb.Checked += (s, _) => MainWindow.I.SetLanguage((string)((RadioButton)s).Tag);

        Ops.Changed += UpdateInfo;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            // "remember my choice" in the close question may have changed it since this page was built
            (AppState.Settings.CloseAction switch { "tray" => CloseTray, "exit" => CloseExit, _ => CloseAsk }).IsChecked = true;
            UpdateInfo();
            LoadLog();
        };
    }

    void UpdateInfo()
    {
        BackupInfo.Text = L.F("{0} tweak(s) have their original Windows values saved and can be restored exactly.", AppState.Engine.BackupCount);
        BackupTip.ToolTip = L.F("Stored in: {0}", TweakEngine.BackupFile) + "\n"
                          + L.T("Switching a tweak off restores its saved value. Removed Store apps are the only thing that cannot be undone here.") + "\n"
                          + L.T(Ops.SystemProtectionOn ? "System Protection is on: restore points can be created."
                                                       : "System Protection is off on this PC, so no restore points are made. Atomic never turns it on.");
    }

    void LoadLog()
    {
        try
        {
            LogText.Text = File.Exists(Sys.LogFile) ? string.Join("\n", File.ReadLines(Sys.LogFile).TakeLast(300)) : L.T("No activity yet.");
            LogScroll.ScrollToEnd();
        }
        catch (Exception ex) { LogText.Text = ex.Message; }
    }

    void Opt_Click(object sender, RoutedEventArgs e)
    {
        AppState.Settings.AutoRestorePoint = OptRestore.IsChecked == true;
        AppState.Settings.ConfirmRisky = OptConfirm.IsChecked == true;
        AppState.Settings.Sounds = OptSounds.IsChecked == true;
        AppState.Settings.Save();
    }

    /// <summary>Asking Task Scheduler takes a moment: off the UI thread.</summary>
    async void ShowAutostart()
    {
        bool on = await Task.Run(() => Autostart.IsOn);
        OptAutostart.IsChecked = on;
        OptAutostart.IsEnabled = true;
    }

    async void Autostart_Click(object sender, RoutedEventArgs e)
    {
        bool want = OptAutostart.IsChecked == true, ok = false;
        if (!Ops.RequireAdmin()) { OptAutostart.IsChecked = !want; return; }
        await MainWindow.I.RunBusy(L.T(want ? "Turning on start with Windows" : "Turning off start with Windows"), L.T("Updating Task Scheduler..."), () => ok = Autostart.Set(want));
        if (ok) MainWindow.I.Toast(L.T(want ? "Atomic now starts with Windows, hidden next to the clock." : "Atomic no longer starts with Windows."));
        else
        {
            OptAutostart.IsChecked = !want;
            MainWindow.I.Toast(L.T("Could not change start with Windows - see the activity log."), "warn");
        }
    }

    async void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        if (!Ops.RequireAdmin()) return;
        if (!Ops.SystemProtectionOn)
        {
            if (await MainWindow.I.Ask(L.T("System Protection is off"), L.T("Windows cannot create restore points while System Protection is off, and Atomic will not turn it on for you.\n\nOpen the Windows System Protection settings so you can decide yourself?"), L.T("Open settings")))
                Sys.Open(Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe"));
            return;
        }
        bool ok = false;
        await MainWindow.I.RunBusy(L.T("Creating restore point"), L.T("This can take a minute..."), () => ok = Ops.CreateRestorePoint());
        if (ok) { AppState.RestorePointDone = true; MainWindow.I.Toast(L.T("Restore point created.")); }
        else MainWindow.I.Toast(L.T("Could not create a restore point - see the activity log."), "warn");
        LoadLog();
    }

    void OpenRestore_Click(object sender, RoutedEventArgs e) => Sys.Open(Path.Combine(Environment.SystemDirectory, "rstrui.exe"));
    void OpenFolder_Click(object sender, RoutedEventArgs e) => Sys.Open("explorer.exe", $"\"{Sys.DataDir}\"");
    async void RevertAll_Click(object sender, RoutedEventArgs e) { await Ops.RevertAll(); LoadLog(); }
    void ReloadLog_Click(object sender, RoutedEventArgs e) => LoadLog();
}
