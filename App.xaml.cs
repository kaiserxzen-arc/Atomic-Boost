using System.Diagnostics;
using System.Windows;
using Atomic.Core;

namespace Atomic;

public partial class App : Application
{
    Mutex? _instance;   // held for the lifetime of the app
    static System.Windows.Forms.NotifyIcon? _tray;
    static bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // changing system settings needs Administrator rights; --no-elevate opens the app read-only
        if (!Sys.IsAdmin && !e.Args.Contains("--no-elevate"))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Join(" ", e.Args)) { UseShellExecute = true, Verb = "runas" });
            }
            catch
            {
                L.Set(AppState.Settings.Language);
                MessageBox.Show(L.T("Atomic needs Administrator rights to change system settings."), AppState.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Shutdown();
            return;
        }

        // one elevated instance only: two would both watch for games and compete for the hotkey
        L.Set(AppState.Settings.Language);
        if (Sys.IsAdmin)
        {
            bool first;
            try { _instance = new Mutex(true, "Atomic.Instance", out first); } catch { first = false; }
            if (!first)
            {
                // it may be sitting in the tray: ask the running instance to show its window
                try { EventWaitHandle.OpenExisting("Atomic.Show").Set(); }
                catch { MessageBox.Show(L.T("Atomic is already running."), AppState.Name, MessageBoxButton.OK, MessageBoxImage.Information); }
                Shutdown();
                return;
            }
            var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Atomic.Show");
            new Thread(() => { while (true) { show.WaitOne(); Dispatcher.BeginInvoke(ShowWindow); } }) { IsBackground = true, Name = "ShowSignal" }.Start();
        }

        DispatcherUnhandledException += (_, a) =>
        {
            Sys.Log("UI error: " + a.Exception);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Sys.Log("Fatal error: " + a.ExceptionObject);
        L.Set(AppState.Settings.Language);
        Palette.Apply();
        Sound.Hook();
        var window = new MainWindow();
        // started with Windows: straight to the icon next to the clock, the window only comes when it is clicked
        if (e.Args.Contains("--tray"))
        {
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowState = WindowState.Minimized;
            window.Loaded += (_, _) => { window.Hide(); window.ShowInTaskbar = true; };
        }
        window.Show();
        BuildTray();
    }

    // ------------------------------------------------------------ tray icon (the app can keep running without its window)
    /// <summary>Icon next to the clock: click brings the window back, right-click opens the menu. Built again when the language changes.</summary>
    static void BuildTray()
    {
        _tray?.Dispose();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(L.T("Open Atomic"), null, (_, _) => ShowWindow());
        menu.Items.Add(L.T("Quit Atomic"), null, (_, _) => Quit());
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = AppState.Name, ContextMenuStrip = menu, Visible = true
        };
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowWindow(); };
    }

    public static void ShowWindow()
    {
        var w = Atomic.MainWindow.I;
        w.Show();
        w.ShowInTaskbar = true;
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    /// <summary>The window goes away, the app stays: Auto Game Mode and Heat Guard keep working.</summary>
    public static void HideWindow()
    {
        Atomic.MainWindow.I.Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray?.ShowBalloonTip(4000, AppState.Name, L.T("Atomic keeps running here. Click the icon to open it again."), System.Windows.Forms.ToolTipIcon.None);
    }

    public static void Quit()
    {
        var w = Atomic.MainWindow.I;
        w.ForceClose = true;
        w.Close();
    }

    /// <summary>A game mode session that is still running gets its changes undone before the app goes away.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        AppState.Game.Stop();
        _tray?.Dispose();   // otherwise a dead icon stays next to the clock
        base.OnExit(e);
    }

    /// <summary>Language changed: texts are read when the UI is built, so build a fresh window in place of the old one.</summary>
    public static void Restyle()
    {
        var old = Atomic.MainWindow.I;
        AppState.Settings.Save();
        L.Set(AppState.Settings.Language);
        Palette.Apply();
        Ops.ClearListeners();
        AppState.Stats.ClearListeners();
        AppState.Game.ClearListeners();
        var fresh = new MainWindow(old);
        Current.MainWindow = fresh;
        fresh.Show();
        old.ForceClose = true;
        old.Close();
        BuildTray();
    }
}
