using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Atomic.Controls;
using Atomic.Core;

namespace Atomic.Pages;

public class GmOption
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Desc { get; init; } = "";
    public bool On { get; set; }
}

public class GmApp
{
    public string Exe { get; init; } = "";
    public string State { get; init; } = "";
    public bool On { get; set; }
}

public partial class GameModePage : UserControl
{
    static Settings S => AppState.Settings;
    static GameMode Game => AppState.Game;
    static MainWindow W => MainWindow.I;

    const string IFEO = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    const string ExePattern = @"^[\w\-\. \(\)\[\]]+\.exe$";
    static readonly string[] ProtectedExe = ["explorer.exe", "svchost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "winlogon.exe", "services.exe", "smss.exe", "wininit.exe"];

    public GameModePage()
    {
        InitializeComponent();
        Fx.GlowBorder(Hero);
        ShowSwitches();
        GameInfo.ToolTip = L.T("Games listed here always count as games for Auto Game Mode, even outside a game library, and always start with High CPU priority. Enter the game's .exe name or browse to it.");

        // the frame rate of a running session moves; everything else on the page only changes on events
        var live = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        live.Tick += (_, _) => { if (IsVisible && Game.Active != null) ShowStatus(); };
        live.Start();

        Game.Changed += () => Dispatcher.BeginInvoke(Refresh);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Refresh();
    }

    static GmOption Opt(string key, bool on, string title, string desc) => new() { Key = key, On = on, Title = L.T(title), Desc = L.T(desc) };

    /// <summary>Shows the switches as the settings have them now (also after One-Click Boost changed them from outside).</summary>
    void ShowSwitches()
    {
        Master.IsChecked = S.GameMode;
        var options = new List<GmOption>
        {
            Opt("close", S.GmClose, "Close the apps on my list", "Off until you switch it on. Only the apps switched on in the list on the right are closed."),
            new()
            {
                Key = "heat", On = S.GmHeat, Title = L.T("Heat Guard"),
                Desc = L.F("When the CPU stays at {0} C or the GPU at {1} C or more for 30 seconds, CPU turbo is turned down one step. The step is remembered and used from the start of every game on this PC; when the game closes, turbo is put back.", S.HeatCpu, S.HeatGpu)
            },
            Opt("reopen", S.GmReopen, "Reopen them afterwards", "Apps that were closed are started again when you quit the game."),
            Opt("ram", S.GmFreeRam, "Free up RAM", "Trims the memory of idle apps once, right after the game starts. The game itself is left alone."),
            Opt("power", S.GmPower, "Best performance power mode", "Switches the Windows power mode while you play and restores your own setting afterwards."),
            Opt("priority", S.GmPriority, "High CPU priority for the game", "Background work cannot take CPU time from the game. Games protected by anti-cheat may refuse this and simply stay at normal priority."),
        };
        if (AppState.Pc.Gpus.Count > 1)
            options.Add(Opt("gpu", S.GmGpu, "Run games on the high-performance GPU", "Sets the Windows graphics preference for a game that has none yet. Takes effect the next time the game starts."));
        options.Add(Opt("standby", S.GmStandby, "Clear standby memory when RAM runs low", "During long sessions cached files fill the RAM. When less than 1 GB is free, the cache is released."));
        options.Add(Opt("timer", S.GmTimer, "Hold a 0.5 ms system timer", "Most games ask for a precise timer themselves. On Windows 11 this only reaches the game when the Global Timer Resolution Requests tweak is on."));
        Options.ItemsSource = options;
    }

    public void Reload()
    {
        ShowSwitches();
        Refresh();
    }

    // ------------------------------------------------------------ status + lists
    void Refresh()
    {
        ShowStatus();
        ShowGames();
        ShowNotGames();
        ShowApps();
        HeatCard.Visibility = S.HeatLevel > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeatText.Text = L.F("Step {0} of {1}: {2} while a game is running, because this PC ran too hot. Outside games the CPU keeps its full speed. Reset makes the PC start again without a limit.", S.HeatLevel, HeatGuard.MaxLevel, HeatGuard.Describe());
    }

    void ShowStatus()
    {
        var active = Game.Active;
        string dot = "Dim";
        if (!Sys.IsAdmin)
        {
            StatusTitle.Text = L.T("Needs Administrator");
            StatusText.Text = L.T("Read-only mode: start Atomic as Administrator to change settings.");
        }
        else if (!S.GameMode)
        {
            StatusTitle.Text = L.T("Off");
            StatusText.Text = L.T("Switch it on and leave Atomic open - minimized is fine. Nothing happens until a game is in the foreground.");
        }
        else if (active == null)
        {
            dot = "Accent2";
            StatusTitle.Text = L.T("Watching for games");
            StatusText.Text = L.T("Leave Atomic open - minimized is fine. Game mode starts when a game is in the foreground.");
        }
        else
        {
            dot = "Green";
            StatusTitle.Text = L.F("Playing: {0}", active.Game);
            string temps = (Game.CpuTempNow is double ct ? $"  -  CPU {ct:N0} C" : "") + (Game.GpuTempNow is double gt ? $"  -  GPU {gt:N0} C" : "");
            StatusText.Text = L.F("Since {0:HH:mm}", active.Start) + temps + (Game.Done.Count > 0 ? "\n" + string.Join("  -  ", Game.Done) : "");
        }
        StatusDot.Fill = (Brush)FindResource(dot);
        BtnNotGame.Visibility = Sys.IsAdmin && S.GameMode && active != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void ShowApps()
    {
        var apps = S.GmApps.ToList();
        // looking up window handles of every process takes a moment: off the UI thread
        Task.Run(() =>
        {
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var windowed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    running.Add(p.ProcessName);
                    if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId) windowed.Add(p.ProcessName);
                }
                catch { }
                finally { p.Dispose(); }
            }
            Dispatcher.BeginInvoke(() =>
            {
                Apps.ItemsSource = apps.Select(a => new GmApp { Exe = a.Exe, On = a.On, State = running.Contains(a.Exe) ? L.T("running") : "" }).ToList();
                Running.Children.Clear();
                foreach (var name in windowed.Where(n => !GameMode.IsProtected(n) && apps.All(a => !a.Exe.Equals(n, StringComparison.OrdinalIgnoreCase))
                                                         && !S.Games.Contains(n + ".exe", StringComparer.OrdinalIgnoreCase)).Take(14))
                {
                    var chip = new Button { Style = (Style)FindResource("GhostBtn"), Content = "+ " + name, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 6) };
                    chip.Click += (_, _) => AddApp(name);
                    Running.Children.Add(chip);
                }
                RunningTitle.Visibility = Running.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        });
    }

    void ShowNotGames()
    {
        NotGames.Children.Clear();
        foreach (var exe in S.NotGames.ToList())
        {
            var chip = new Button { Style = (Style)FindResource("GhostBtn"), Content = exe + "  ✕", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 6) };
            chip.Click += (_, _) => { S.NotGames = S.NotGames.Where(n => n != exe).ToList(); S.Save(); ShowNotGames(); };
            NotGames.Children.Add(chip);
        }
        NotGamesCard.Visibility = S.NotGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ switches
    void Master_Click(object sender, RoutedEventArgs e)
    {
        if (!Ops.RequireAdmin()) { Master.IsChecked = S.GameMode; return; }
        S.GameMode = Master.IsChecked == true;
        S.Save();
        Game.Start();
        Sys.Log("GAME MODE  " + (S.GameMode ? "enabled" : "disabled"));
        Refresh();
    }

    void Option_Click(object sender, RoutedEventArgs e)
    {
        if (((CheckBox)sender).DataContext is not GmOption o) return;
        switch (o.Key)
        {
            case "close": S.GmClose = o.On; break;
            case "reopen": S.GmReopen = o.On; break;
            case "ram": S.GmFreeRam = o.On; break;
            case "power": S.GmPower = o.On; break;
            case "priority": S.GmPriority = o.On; break;
            case "gpu": S.GmGpu = o.On; break;
            case "standby": S.GmStandby = o.On; break;
            case "timer": S.GmTimer = o.On; break;
            case "heat": S.GmHeat = o.On; break;
        }
        S.Save();
    }

    // ------------------------------------------------------------ close list
    void App_Click(object sender, RoutedEventArgs e)
    {
        if (((CheckBox)sender).DataContext is not GmApp row) return;
        // the list is replaced, not edited, because the watcher reads it from its own thread
        S.GmApps = S.GmApps.Select(a => a.Exe == row.Exe ? a with { On = row.On } : a).ToList();
        S.Save();
    }

    void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        string exe = (string)((Button)sender).Tag;
        S.GmApps = S.GmApps.Where(a => a.Exe != exe).ToList();
        S.Save();
        ShowApps();
    }

    void AppExe_TextChanged(object sender, TextChangedEventArgs e) => AppHint.Visibility = AppExe.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    void AppExe_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddApp_Click(sender, e); }
    void AddApp_Click(object sender, RoutedEventArgs e) { if (AddApp(AppExe.Text)) AppExe.Text = ""; }

    bool AddApp(string text)
    {
        string name = Path.GetFileName(text.Trim().Trim('"'));
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (!Regex.IsMatch(name, @"^[\w\-\. \(\)\[\]]+$"))
        {
            W.Toast(L.T("Enter the app's .exe name, e.g. spotify.exe"), "warn");
            return false;
        }
        if (GameMode.IsProtected(name) || S.Games.Contains(name + ".exe", StringComparer.OrdinalIgnoreCase))
        {
            W.Toast(L.F("{0} is part of Windows, a game launcher or one of your games - it cannot be put on the close list.", name), "warn");
            return false;
        }
        if (S.GmApps.All(a => !a.Exe.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            S.GmApps = [.. S.GmApps, new CloseApp(name, true)];
            S.Save();
        }
        ShowApps();
        return true;
    }

    async void ResetHeat_Click(object sender, RoutedEventArgs e)
    {
        if (!Ops.RequireAdmin()) return;
        await W.RunBusy(L.T("Resetting Heat Guard"), L.T("Putting the CPU power settings back..."), () =>
        {
            HeatGuard.Reset();
            S.Save();
            // recommendations depend on the guard's step: build the tweak list again
            AppState.Engine.Load(Catalog.Build(AppState.Pc));
            AppState.Engine.RefreshAll();
        });
        Ops.RaiseChanged();
        W.Toast(L.T("Heat Guard reset - games start without a turbo limit again."), "info");
        Refresh();
    }

    // ------------------------------------------------------------ my games (also High priority through IFEO PerfOptions)
    void ShowGames()
    {
        var games = AppState.Settings.Games;
        Games.ItemsSource = games.ToList();
        NoGames.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void GameExe_TextChanged(object sender, TextChangedEventArgs e) => GameHint.Visibility = GameExe.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    void GameExe_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddGame_Click(sender, e); }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = L.T("Programs (*.exe)") + "|*.exe", Title = L.T("Select the game's .exe") };
        if (dlg.ShowDialog(W) == true) GameExe.Text = Path.GetFileName(dlg.FileName);
    }

    void AddGame_Click(object sender, RoutedEventArgs e)
    {
        if (!Ops.RequireAdmin()) return;
        string exe = Path.GetFileName(GameExe.Text.Trim().Trim('"'));
        if (exe.Length > 0 && !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
        if (!Regex.IsMatch(exe, ExePattern) || ProtectedExe.Contains(exe, StringComparer.OrdinalIgnoreCase))
        {
            W.Toast(L.T("Enter a valid game .exe name, e.g. cs2.exe"), "warn");
            return;
        }
        try
        {
            Sys.RegSet($@"{IFEO}\{exe}\PerfOptions", "CpuPriorityClass", RegistryValueKind.DWord, 3);
            var games = AppState.Settings.Games;
            games.RemoveAll(g => g.Equals(exe, StringComparison.OrdinalIgnoreCase));
            games.Add(exe);
            AppState.Settings.Save();
            Sys.Log($"GAME PRIORITY  added {exe}");
            GameExe.Text = "";
            ShowGames();
            W.Toast(L.F("{0} will now start with High CPU priority.", exe));
        }
        catch (Exception ex) { W.Toast(L.F("Could not set priority: {0}", ex.Message), "error"); }
    }

    void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (!Ops.RequireAdmin()) return;
        string exe = (string)((Button)sender).Tag;
        if (Regex.IsMatch(exe, ExePattern))
        {
            Sys.RegDelKey($@"{IFEO}\{exe}\PerfOptions");
            // drop the parent key too when we were the only ones using it
            if (Sys.RegValueNames($@"{IFEO}\{exe}").Length == 0) Sys.RegDelKey($@"{IFEO}\{exe}", tree: false);
        }
        AppState.Settings.Games.RemoveAll(g => g.Equals(exe, StringComparison.OrdinalIgnoreCase));
        AppState.Settings.Save();
        Sys.Log($"GAME PRIORITY  removed {exe}");
        ShowGames();
        W.Toast(L.F("{0} priority reset to normal.", exe), "info");
    }

    // ------------------------------------------------------------ "this is not a game"
    void NotGame_Click(object sender, RoutedEventArgs e)
    {
        if (Game.Active?.Game is not { } exe) return;
        if (!S.NotGames.Contains(exe, StringComparer.OrdinalIgnoreCase)) S.NotGames = [.. S.NotGames, exe];
        S.Save();   // the watcher sees the list on its next step and switches game mode off again
        W.Toast(L.F("{0} will no longer start game mode.", exe), "info");
        Refresh();
    }
}
