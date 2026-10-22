using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Atomic.Controls;
using Atomic.Core;
using Atomic.Pages;

namespace Atomic;

public partial class MainWindow : Window
{
    public static MainWindow I { get; private set; } = null!;

    readonly Dictionary<string, FrameworkElement> _pages = [];
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    TaskCompletionSource<bool>? _dialog;
    string _current = "dashboard";
    string _dialogChoice = "cancel";
    /// <summary>Close for real, without asking (quit from the tray, or this window is being replaced).</summary>
    public bool ForceClose { get; set; }
    int _toastNo;

    /// <param name="previous">The window this one replaces after a language change: same place, same page.</param>
    public MainWindow(MainWindow? previous = null)
    {
        InitializeComponent();
        I = this;
        _toastTimer.Tick += (_, _) => HideToast();
        SourceInitialized += (_, _) =>
        {
            // rounded window corners on Windows 11 (ignored on Windows 10)
            int round = 2;
            Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int));
        };
        StateChanged += (_, _) =>
        {
            // a maximized borderless window hangs over the screen edges
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            BtnMax.Content = WindowState == WindowState.Maximized ? "" : "";
        };
        Closed += (_, _) => _toastTimer.Stop();
        Loaded += OnLoaded;

        if (previous != null)
        {
            var b = previous.WindowState == WindowState.Normal ? new Rect(previous.Left, previous.Top, previous.Width, previous.Height) : previous.RestoreBounds;
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (b.Left, b.Top, b.Width, b.Height);
            if (previous.WindowState == WindowState.Maximized) WindowState = WindowState.Maximized;
            _current = previous._current;
        }

        // the logo is built in only when Logo.png was there at build time (it is not in the public repo)
        try
        {
            LogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Atomic;component/Logo.png"));
            LogoImage.Visibility = Visibility.Visible;
        }
        catch (IOException) { }
        LangText.Text = L.Thai ? "ไทย" : "EN";
        BtnLang.ToolTip = L.Thai ? "Switch to English" : "เปลี่ยนเป็นภาษาไทย";
        if (AppState.RebootNeeded) SetRebootNeeded();

        int i = 0;
        foreach (FrameworkElement item in Nav.Children) Fx.Enter(item, fromY: -10, delayMs: 60 + i++ * 35);
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!Sys.IsAdmin) ReadOnlyPill.Visibility = Visibility.Visible;

        if (!AppState.Scanned)
        {
            await RunBusy(L.T("Scanning your system"), L.T("Detecting hardware and reading current Windows settings..."), () =>
            {
                AppState.Pc = SystemInfo.Scan();
                AppState.Engine.Load(Catalog.Build(AppState.Pc));
                AppState.Engine.RefreshAll();
            });
            AppState.Scanned = true;
        }

        var pc = AppState.Pc;
        DeviceIcon.Text = pc.IsLaptop ? "\uE7F8" : "\uE977";
        DeviceText.Text = pc.DeviceKindText.ToUpperInvariant();
        DeviceInfo.ToolTip = L.F("Detected: {0} - recommendations are tuned for it", pc.DeviceKindText);
        BtnBoost.ToolTip = L.F("Apply every tweak recommended for this {0} and switch on Auto Game Mode", pc.DeviceKindText.ToLowerInvariant());

        _pages["dashboard"] = new DashboardPage();
        _pages["optimize"] = new OptimizePage();
        _pages["cleaner"] = new CleanerPage();
        _pages["boost"] = new BoostPage();
        _pages["gamemode"] = new GameModePage();
        _pages["startup"] = new StartupPage();
        _pages["scanner"] = new ScannerPage();
        _pages["settings"] = new SettingsPage();
        // every page stays in the window, hidden: showing one is then a matter of switching it on
        foreach (var page in _pages.Values) { page.Visibility = Visibility.Collapsed; PageHost.Children.Add(page); }
        Navigate(_current);
        Warm();
        // the watcher reports from its own thread; it only runs when settings can actually be changed
        AppState.Game.Notice += (message, kind) => Dispatcher.BeginInvoke(() => Toast(message, kind));
        AppState.Game.TweaksChanged += () => Dispatcher.BeginInvoke(Ops.RaiseChanged);
        if (Sys.IsAdmin) { AppState.Game.Start(); _ = Task.Run(Autostart.Repair); }
        if (!AppState.Stats.Start()) return;
        Sys.Log($"START  {AppState.Name} {AppState.Version}  {pc.CpuName} | {pc.Gpu?.Name} | {Sys.Bytes(pc.RamBytes)} | {pc.DeviceKind} | admin={Sys.IsAdmin}");
    }

    // ------------------------------------------------------------ navigation
    public void Navigate(string key)
    {
        foreach (var rb in Nav.Children.OfType<RadioButton>())
            if ((string)rb.CommandParameter == key && rb.IsChecked != true) { rb.IsChecked = true; return; }
        Show(key);
    }

    void Nav_Checked(object sender, RoutedEventArgs e) => Show((string)((RadioButton)sender).CommandParameter);

    void Show(string key)
    {
        if (!_pages.TryGetValue(key, out var page) || page.Visibility == Visibility.Visible) return;
        foreach (var other in _pages.Values) if (other != page) other.Visibility = Visibility.Collapsed;
        _current = key;
        page.Visibility = Visibility.Visible;
        Fx.PageIn(page);
    }

    /// <summary>
    /// The first time a page is shown WPF has to build everything in it, which is what made changing pages stutter.
    /// So right after start, whenever the app has nothing else to do, each page is built once out of sight.
    /// </summary>
    async void Warm()
    {
        foreach (var page in _pages.Values.ToList())
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (page.Visibility != Visibility.Collapsed) continue;
            page.Visibility = Visibility.Hidden;   // laid out, not drawn
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (page.Visibility == Visibility.Hidden) page.Visibility = Visibility.Collapsed;
        }
    }

    // ------------------------------------------------------------ busy / toast / dialog
    public async Task RunBusy(string title, string text, Action work)
    {
        BusyTitle.Text = title;
        BusyText.Text = text;
        Fx.PopIn(BusyOverlay, BusyCard);
        Spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        try { await Task.Run(work); }
        catch (Exception ex) { Sys.Log($"{title} failed: {ex}"); Toast(L.F("{0} failed: {1}", title, ex.Message), "error"); }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            Spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    /// <summary>Safe to call from the worker inside RunBusy.</summary>
    public void BusyStatus(string text) => Dispatcher.BeginInvoke(() => BusyText.Text = text);

    public void Toast(string message, string kind = "ok")
    {
        _toastNo++;
        ToastText.Text = message;
        ToastDot.Fill = (Brush)FindResource(kind switch { "warn" => "Yellow", "error" => "Red", "info" => "Purple", _ => "Green" });
        ToastBox.Visibility = Visibility.Visible;
        Fx.Enter(ToastBox, fromY: 26, ms: 300);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    void HideToast()
    {
        _toastTimer.Stop();
        int no = _toastNo;
        // a newer toast may have replaced this one while it was fading
        Fx.FadeOut(ToastBox, () => { if (no == _toastNo) ToastBox.Visibility = Visibility.Collapsed; });
    }

    public Task<bool> Ask(string title, string message, string? ok = null)
    {
        DialogTitle.Text = title;
        DialogText.Text = message;
        DialogOk.Content = ok ?? L.T("Continue");
        DialogAlt.Visibility = DialogRememberRow.Visibility = Visibility.Collapsed;
        Fx.PopIn(DialogOverlay, DialogCard);
        _dialog = new TaskCompletionSource<bool>();
        return _dialog.Task;
    }

    void CloseDialog(string choice)
    {
        _dialogChoice = choice;
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialog?.TrySetResult(choice == "ok");
    }
    void DialogOk_Click(object sender, RoutedEventArgs e) => CloseDialog("ok");
    void DialogAlt_Click(object sender, RoutedEventArgs e) => CloseDialog("alt");
    void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog("cancel");

    // ------------------------------------------------------------ closing: quit, or keep running in the tray
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        string action = AppState.Settings.CloseAction;
        if (ForceClose || action == "exit") return;
        e.Cancel = true;
        if (action == "tray") App.HideWindow();
        else if (DialogOverlay.Visibility != Visibility.Visible) AskHowToClose();
    }

    async void AskHowToClose()
    {
        DialogRemember.IsChecked = false;
        var asked = Ask(L.T("Close Atomic?"),
            L.T("Keep it running in the background and Auto Game Mode and Heat Guard go on working. You can open it again from the icon next to the clock.\n\nQuit stops all of that."),
            L.T("Run in the background"));
        DialogAlt.Content = L.T("Quit");
        DialogAlt.Visibility = DialogRememberRow.Visibility = Visibility.Visible;
        await asked;
        if (_dialogChoice == "cancel") return;
        bool tray = _dialogChoice == "ok";
        if (DialogRemember.IsChecked == true)
        {
            AppState.Settings.CloseAction = tray ? "tray" : "exit";
            AppState.Settings.Save();
        }
        if (tray) App.HideWindow(); else App.Quit();
    }

    public void SetRebootNeeded()
    {
        AppState.RebootNeeded = true;
        BellDot.Visibility = Visibility.Visible;
        BtnBell.ToolTip = L.T("Restart Windows to finish applying your changes");
    }

    async void BtnBell_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.RebootNeeded) { Toast(L.T("Nothing pending - no restart needed."), "info"); return; }
        if (await Ask(L.T("Restart required"), L.T("Some changes only take effect after Windows restarts. Save your work first.\n\nRestart now?"), L.T("Restart now")))
            Sys.Run("shutdown.exe", "/r /t 5 /c \"Atomic: restarting to apply optimizations\"");
    }

    async void BtnBoost_Click(object sender, RoutedEventArgs e) => await Ops.ApplyRecommended();

    /// <summary>Game mode settings were changed from outside its page.</summary>
    public void GameModeChanged() => (_pages.GetValueOrDefault("gamemode") as GameModePage)?.Reload();

    // ------------------------------------------------------------ language
    void BtnLang_Click(object sender, RoutedEventArgs e) => SetLanguage(L.Thai ? "en" : "th");

    public void SetLanguage(string lang)
    {
        AppState.Settings.Language = lang;
        Dispatcher.BeginInvoke(App.Restyle);
    }

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>User-level operations shared by the pages: confirm, restore point, apply, refresh, report.</summary>
public static class Ops
{
    static MainWindow W => MainWindow.I;
    static TweakEngine Engine => AppState.Engine;

    /// <summary>Raised on the UI thread after tweak states changed.</summary>
    public static event Action? Changed;
    public static void RaiseChanged() => Changed?.Invoke();
    public static void ClearListeners() => Changed = null;

    public static bool RequireAdmin()
    {
        if (Sys.IsAdmin) return true;
        W.Toast(L.T("Read-only mode: start Atomic as Administrator to change settings."), "warn");
        return false;
    }

    /// <summary>System Protection is the user's choice: Atomic reads it but never switches it on.</summary>
    public static bool SystemProtectionOn =>
        Sys.RegInt(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "RPSessionInterval") == 1;

    public static bool CreateRestorePoint()
    {
        if (!SystemProtectionOn) { Sys.Log("RESTORE POINT  skipped - System Protection is off"); return false; }
        var r = Sys.PS("""
            $ErrorActionPreference='Stop'
            try {
                $k='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
                $old=(Get-ItemProperty $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
                Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord
                Checkpoint-Computer -Description 'Atomic' -RestorePointType MODIFY_SETTINGS
                if ($null -eq $old) { Remove-ItemProperty $k -Name SystemRestorePointCreationFrequency } else { Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value $old -Type DWord }
                'OK'
            } catch { 'FAIL ' + $_.Exception.Message }
            """, 300000).Trim();
        Sys.Log("RESTORE POINT  " + r);
        return r.EndsWith("OK");
    }

    /// <summary>One restore point before the first change of a session.</summary>
    static async Task EnsureRestorePoint()
    {
        if (!AppState.Settings.AutoRestorePoint || AppState.RestorePointDone) return;
        AppState.RestorePointDone = true;
        if (!SystemProtectionOn) { Sys.Log("RESTORE POINT  skipped - System Protection is off"); return; }
        bool ok = false;
        await W.RunBusy(L.T("Creating restore point"), L.T("Saving a System Restore point before the first change..."), () => ok = CreateRestorePoint());
        if (!ok) W.Toast(L.T("Could not create a restore point. Original values are still backed up."), "warn");
    }

    /// <summary>A tweak that did not fully take is undone again so nothing is left half-applied (removed apps cannot be undone).</summary>
    static bool RollBackIfIncomplete(Tweak t)
    {
        if (t.IsOn || t.Appx.Length > 0) return false;
        Sys.Log($"  {t.Id} did not fully apply - rolling back");
        Engine.Revert(t);
        return true;
    }

    static bool NeedsConfirm(Tweak t) =>
        t.Tags.Contains("Not Reversible") || (AppState.Settings.ConfirmRisky && (t.Risk != "Safe" || t.Tags.Contains("Feature Breaking") || t.Tags.Contains("Security Risk")));

    public static async Task Toggle(Tweak t, bool on)
    {
        bool done = false;
        try
        {
            if (!RequireAdmin()) return;
            if (on && NeedsConfirm(t))
            {
                string warn = t.Tags.Contains("Security Risk") ? L.T("This lowers Windows security in exchange for performance.") + "\n\n"
                            : t.Tags.Contains("Not Reversible") ? L.T("Removed apps cannot be restored by Atomic - reinstall them from the Microsoft Store if needed.") + "\n\n" : "";
                if (!await W.Ask(t.DisplayName, warn + t.DisplayDesc, L.T("Apply"))) return;
            }
            await EnsureRestorePoint();
            List<string> errors = [];
            await W.RunBusy(L.T(on ? "Applying" : "Reverting"), t.DisplayName, () =>
            {
                if (on) errors = Engine.Apply(t); else Engine.Revert(t);
                Engine.RefreshAll(t.NeedsSnapshot);
                if (on && RollBackIfIncomplete(t)) Engine.RefreshAll(t.NeedsSnapshot);
            });
            done = true;
            string suffix = t.NeedsReboot ? " " + L.T("Restart to take effect.") : t.Tags.Contains("Sign-out") ? " " + L.T("Sign out to take effect.") : "";
            if (t.IsOn == on)
            {
                if (t.NeedsReboot) W.SetRebootNeeded();
                W.Toast(L.F(on ? "Applied: {0}." : "Reverted: {0}.", t.DisplayName) + suffix);
            }
            else if (on) W.Toast(L.F(errors.Count > 0 ? "Windows blocked this change, so it was rolled back: {0}." : "{0} did not take effect on this PC and was rolled back - see the log.", t.DisplayName), "warn");
            else W.Toast(L.F("{0} could not be fully reverted on this PC - see the log.", t.DisplayName), "warn");
        }
        finally
        {
            if (!done) t.IsOn = t.IsOn;   // snap the switch back to the real state
            RaiseChanged();
        }
    }

    public static async Task ApplyRecommended()
    {
        if (!RequireAdmin()) return;
        var todo = Engine.All.Where(t => t.Rec && t.Available && !t.IsOn && t.Risk == "Safe").ToList();
        bool gameMode = !AppState.Settings.GameMode;   // One-Click Boost switches Auto Game Mode on as well
        if (todo.Count == 0 && !gameMode) { W.Toast(L.T("Everything recommended for this PC is already applied."), "info"); return; }
        int apps = todo.Count(t => t.Appx.Length > 0);
        string msg = todo.Count == 0 ? L.T("Everything recommended for this PC is already applied.")
                   : L.F("{0} recommended tweaks will be applied for this {1}.", todo.Count, AppState.Pc.DeviceKindText.ToLowerInvariant()) + "\n\n"
                   + L.T("Original values are backed up first and every tweak can be switched off again on the Windows Tweaks page.")
                   + (apps > 0 ? "\n\n" + L.F("This includes {0} group(s) of preinstalled Store apps, which can only be restored from the Microsoft Store.", apps) : "")
                   + "\n\n" + L.T("Advanced security trade-offs are never included.");
        if (gameMode) msg += "\n\n" + L.T("Auto Game Mode will be switched on as well, so the PC is prepared whenever a game starts. Closing apps stays the way you have set it.");
        if (!await W.Ask(L.T("Apply recommended optimizations"), msg, L.T("Optimize now"))) return;
        if (gameMode) EnableGameMode();
        if (todo.Count == 0) { W.Toast(L.T("Auto Game Mode is on.")); return; }
        await EnsureRestorePoint();
        await W.RunBusy(L.T("Optimizing"), L.T("Starting..."), () =>
        {
            for (int i = 0; i < todo.Count; i++)
            {
                W.BusyStatus($"{i + 1} / {todo.Count}  -  {todo[i].DisplayName}");
                Engine.Apply(todo[i]);
            }
            W.BusyStatus(L.T("Verifying..."));
            Engine.RefreshAll(true);
            if (todo.Count(RollBackIfIncomplete) > 0) Engine.RefreshAll(true);
        });
        if (todo.Any(t => t.IsOn && t.NeedsReboot)) W.SetRebootNeeded();
        int ok = todo.Count(t => t.IsOn);
        W.Toast((ok == todo.Count ? L.F("{0} optimizations applied. Restart Windows to finish.", ok)
                                  : L.F("{0} of {1} optimizations applied; {2} could not be applied on this PC and were left unchanged. Restart Windows to finish.", ok, todo.Count, todo.Count - ok))
                + (gameMode ? " " + L.T("Auto Game Mode is on.") : ""), ok == todo.Count ? "ok" : "warn");
        RaiseChanged();
    }

    /// <summary>Master switch plus the helpers that are safe for everyone; closing apps, standby clearing and the timer stay as the user set them.</summary>
    static void EnableGameMode()
    {
        var s = AppState.Settings;
        s.GameMode = s.GmHeat = s.GmReopen = s.GmFreeRam = s.GmPower = s.GmPriority = s.GmGpu = true;
        s.Save();
        AppState.Game.Start();
        Sys.Log("GAME MODE  enabled by One-Click Boost");
        W.GameModeChanged();
    }

    public static async Task RevertAll()
    {
        if (!RequireAdmin()) return;
        // only what Atomic changed itself: settings that were already in this state are left alone
        var todo = Engine.All.Where(t => Engine.HasBackup(t.Id) && t.Appx.Length == 0).Reverse().ToList();
        if (todo.Count == 0) { W.Toast(L.T("Atomic has not changed anything that can be reverted."), "info"); return; }
        if (!await W.Ask(L.T("Revert all tweaks"), L.F("{0} tweaks applied by Atomic will be restored to the original values saved in the backup.\n\nRemoved Store apps are not reinstalled.", todo.Count), L.T("Revert all"))) return;
        await W.RunBusy(L.T("Reverting"), L.T("Starting..."), () =>
        {
            for (int i = 0; i < todo.Count; i++)
            {
                W.BusyStatus($"{i + 1} / {todo.Count}  -  {todo[i].DisplayName}");
                Engine.Revert(todo[i]);
            }
            Engine.RefreshAll(true);
        });
        W.SetRebootNeeded();
        W.Toast(L.T("All tweaks reverted. Restart Windows to finish."));
        RaiseChanged();
    }
}
