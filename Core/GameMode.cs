using System.Diagnostics;
using Microsoft.Win32;

namespace Atomic.Core;

/// <summary>An app on the "close when a game starts" list: process name without .exe, and whether it is switched on.</summary>
public record CloseApp(string Exe, bool On);

/// <summary>The game that game mode is on for right now. Nothing about a session is recorded or kept.</summary>
public class GameSession
{
    public string Game { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime Start { get; set; }
}

/// <summary>
/// Auto Game Mode: notices a game in the foreground, prepares the PC while it runs and puts everything back afterwards.
/// It never guesses which apps to close: only entries of Settings.GmApps that are switched on are touched.
/// While a game runs it does as little as it can: a check every 3 s that the game is still there, plus the
/// temperature and memory readings that Heat Guard and the standby clearing need - and only when those are on.
/// </summary>
public class GameMode
{
    /// <summary>Never closed, whatever the close list says: Windows itself, this app and game launchers (closing those takes the game down).</summary>
    public static readonly string[] Protected =
    [
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer", "fontdrvhost",
        "sihost", "ctfmon", "audiodg", "conhost", "RuntimeBroker", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "taskhostw",
        "MsMpEng", "SecurityHealthService", "spoolsv", "OptiForge", "Vortex", "Atomic",
        "steam", "steamwebhelper", "steamservice", "EpicGamesLauncher", "EpicWebHelper", "RiotClientServices", "RiotClientUx", "vgc", "vgtray",
        "LeagueClient", "LeagueClientUx", "LeagueClientUxRender", "Battle.net", "EADesktop", "EABackgroundService", "upc", "UbisoftConnect",
        "GalaxyClient", "XboxPcApp", "GameBar", "EasyAntiCheat", "BEService",
        // launchers and editors that live in a game folder but are not the game: "Riot Client.exe" has a space in its name
        "Riot Client", "RobloxStudioBeta",
        // a bot that may be running in the background: its Node scripts, its desktop app with WebView2, and its CLI
        "node", "app", "msedgewebview2", "agy",
    ];

    // an exe inside one of these folders is a game unless its name says launcher / helper
    static readonly string[] Libraries =
        [@"\steamapps\common\", @"\Epic Games\", @"\Riot Games\", @"\XboxGames\", @"\Ubisoft Game Launcher\games\", @"\EA Games\", @"\Origin Games\", @"\GOG Games\", @"\Rockstar Games\", @"\Roblox\Versions\"];
    static readonly string[] HelperWords = ["launcher", "crash", "helper", "updater", "installer", "setup", "redist", "report", "service", "overlay", "webview", "anticheat"];

    const string GpuKey = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-cf48a8ea3d2b", BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    const int GraceSeconds = 12;

    /// <summary>Where the foreground process comes from - replaceable for tests.</summary>
    public Func<(int Pid, string Path)?> Foreground = Native.ForegroundProcess;

    public GameSession? Active { get; private set; }
    /// <summary>What the mode did for the running game, for the status card.</summary>
    public List<string> Done { get; private set; } = [];
    /// <summary>Latest temperatures while Heat Guard is watching (null = no reading).</summary>
    public double? CpuTempNow { get; private set; }
    public double? GpuTempNow { get; private set; }

    public event Action? Changed;
    public event Action<string, string>? Notice;
    /// <summary>Heat Guard changed power settings, so tweak states and recommendations moved.</summary>
    public event Action? TweaksChanged;
    public void ClearListeners() { Changed = null; Notice = null; TweaksChanged = null; }

    static Settings S => AppState.Settings;
    readonly object _gate = new();
    Thread? _thread;
    volatile bool _stop;
    int _pid, _tick, _hotTicks;
    DateTime _goneAt, _lastPurge, _lastHeatStep;
    Guid? _prevOverlay, _prevPlan;
    bool _timer, _heatWarned, _heatOn;
    List<string> _reopen = [];

    public void Start()
    {
        if (_thread != null) return;
        // a turbo limit that outlived its game (crash, power cut): outside a game the CPU gets its full speed back
        if (S.HeatSaved.Count > 0) { HeatGuard.Restore(); S.Save(); }
        _thread = new Thread(() =>
        {
            while (!_stop)
            {
                try { Tick(); } catch (Exception ex) { Sys.Log("game mode: " + ex.Message); }
                Thread.Sleep(3000);
            }
        }) { IsBackground = true, Name = "GameMode", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>App is closing: undo whatever game mode changed.</summary>
    public void Stop()
    {
        _stop = true;
        lock (_gate) { if (Active != null) End(); }
    }

    // ------------------------------------------------------------ detection
    static bool Has(IEnumerable<string> list, string name) => list.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsProtected(string processName) => Has(Protected, processName);

    /// <summary>A game is an exe the user listed, or one that lives in a game library folder and is not a launcher or helper.</summary>
    public static bool IsGame(string path)
    {
        string exe = Path.GetFileName(path), name = Path.GetFileNameWithoutExtension(path);
        if (exe.Length == 0 || Has(S.NotGames, exe)) return false;
        if (Has(S.Games, exe)) return true;
        if (IsProtected(name) || HelperWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) return false;
        return Libraries.Any(l => path.Contains(l, StringComparison.OrdinalIgnoreCase));
    }

    static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; }
    }

    /// <summary>One step of the watcher (every 3 s from the background thread).</summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (Active == null)
            {
                if (S.GameMode && Foreground() is { } fg && IsGame(fg.Path)) Begin(fg.Pid, fg.Path);
            }
            else if (!S.GameMode || Has(S.NotGames, Active.Game)) End();   // switched off, or the user said "not a game"
            else if (Alive(_pid)) { _goneAt = default; Watch(); }
            else if (Foreground() is { } next && IsGame(next.Path))
            {
                // a launcher handed over to the real game: same game mode, new process
                _pid = next.Pid; _goneAt = default;
                Active.Game = Path.GetFileName(next.Path); Active.Path = next.Path;
                Prepare(next.Pid, next.Path);
                Changed?.Invoke();
            }
            else if (_goneAt == default) _goneAt = DateTime.Now;
            else if ((DateTime.Now - _goneAt).TotalSeconds >= GraceSeconds) End();
        }
    }

    // ------------------------------------------------------------ a game starts
    void Begin(int pid, string path)
    {
        var s = new GameSession { Game = Path.GetFileName(path), Path = path, Start = DateTime.Now };
        _pid = pid; _tick = _hotTicks = 0; _goneAt = default; _lastPurge = default; _lastHeatStep = DateTime.Now; _heatWarned = false;
        CpuTempNow = GpuTempNow = null;
        Done = [];
        Active = s;
        Sys.Log($"GAME MODE  start  {s.Game} (pid {pid})");
        Changed?.Invoke();

        Prepare(pid, path);
        // this PC is known to run too hot: start with the step it needed last time instead of waiting for it to heat up again
        if (S.GmHeat && S.HeatLevel > 0 && HeatGuard.Apply(S.HeatLevel))
        {
            S.Save();
            _heatOn = true;
            Done.Add(L.T("Heat Guard") + ": " + HeatGuard.Describe());
        }
        if (S.GmPower) BoostPower();
        if (S.GmTimer && (_timer = Native.TimerResolution(true))) Done.Add(L.T("0.5 ms timer held"));
        if (S.GmClose)
        {
            var closed = CloseApps();
            if (closed.Count > 0) Done.Add(L.F("Closed: {0}", string.Join(", ", closed)));
        }
        if (S.GmFreeRam) Done.Add(L.F("Released {0} of RAM.", Sys.Bytes(Cleaner.FreeRam(pid))).TrimEnd('.'));
        Sys.Log("GAME MODE  " + (Done.Count > 0 ? string.Join(" | ", Done) : "nothing to do"));
        Notice?.Invoke(L.F("Game mode on: {0}", s.Game), "ok");
        Changed?.Invoke();
    }

    /// <summary>Per-process steps, repeated when a launcher hands over to the game itself.</summary>
    void Prepare(int pid, string path)
    {
        if (S.GmPriority)
        {
            // anti-cheat protected games refuse this; they are left as they are
            try { using var p = Process.GetProcessById(pid); p.PriorityClass = ProcessPriorityClass.High; Add(L.T("High CPU priority")); } catch { }
        }
        // only matters with integrated + discrete graphics, and never overrides a choice that is already there
        if (S.GmGpu && AppState.Pc.Gpus.Count > 1 && !Sys.RegStr(GpuKey, path).Contains("GpuPreference="))
        {
            try
            {
                Sys.RegSet(GpuKey, path, RegistryValueKind.String, "GpuPreference=2;");
                Sys.Log($"GAME MODE  GPU preference = high performance for {path}");
                Add(L.T("High-performance GPU from the next launch"));
            }
            catch (Exception ex) { Sys.Log("  gpu preference: " + ex.Message); }
        }
        void Add(string what) { if (!Done.Contains(what)) Done.Add(what); }
    }

    void BoostPower()
    {
        if (AppState.Pc.IsLaptop)
        {
            var now = Power.GetOverlay();
            if (now == null || now == Power.OverlayBestPerformance || !Power.SetOverlay(Power.OverlayBestPerformance)) return;
            _prevOverlay = now;
        }
        else
        {
            var now = Power.ActiveScheme();
            if (now != new Guid(BalancedPlan)) return;   // already on a performance plan (or a custom one we should not touch)
            Power.Activate(new Guid(HighPerformancePlan));
            if (Power.ActiveScheme() == now) return;     // this PC has no High performance plan
            _prevPlan = now;
        }
        Done.Add(L.T("Best performance power mode"));
    }

    /// <summary>
    /// Closes the listed apps the polite way first. An app that still shows a window afterwards is asking the user something
    /// (unsaved work), so it is left completely alone; only windowless leftovers of apps that did close are ended.
    /// </summary>
    List<string> CloseApps()
    {
        var closed = new List<string>();
        _reopen = [];
        foreach (var app in S.GmApps.Where(a => a.On && !IsProtected(a.Exe)).ToList())
        {
            var procs = Process.GetProcessesByName(app.Exe).Where(p => p.Id != _pid).ToList();
            var automated = Automated(procs);
            if (automated.Count > 0)
            {
                Sys.Log($"GAME MODE  {app.Exe}: {automated.Count} process(es) of a browser driven by Playwright left alone");
                var left = procs.Where(p => automated.Contains(p.Id)).ToList();
                procs = procs.Except(left).ToList();
                foreach (var p in left) p.Dispose();
            }
            if (procs.Count == 0) continue;
            string path = procs.Select(p => Native.ProcessPath(p.Id)).FirstOrDefault(x => x.Length > 0) ?? "";
            try
            {
                foreach (var p in procs) { try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { } }
                for (int i = 0; i < 12 && procs.Any(p => !Gone(p) && HasWindow(p)); i++) Thread.Sleep(250);
                if (procs.Any(p => !Gone(p) && HasWindow(p)))
                {
                    Sys.Log($"GAME MODE  {app.Exe} kept open - it did not close when asked (unsaved work?)");
                    continue;
                }
                foreach (var p in procs) { try { if (!Gone(p)) p.Kill(); } catch { } }
                closed.Add(app.Exe);
                // Store apps come back on their own when needed and cannot be started by path
                if (path.Length > 0 && !path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) && !path.Contains(@"\SystemApps\", StringComparison.OrdinalIgnoreCase))
                    _reopen.Add(path);
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        return closed;

        static bool Gone(Process p) { try { p.Refresh(); return p.HasExited; } catch { return true; } }

        // a browser started by Playwright (a bot or a test) has its own profile in temp and talks over a pipe;
        // its helper processes are its children, so they stay too
        static HashSet<int> Automated(List<Process> procs)
        {
            var ids = procs.Select(p => p.Id).ToHashSet();
            var roots = ids.Where(id => Native.CommandLine(id) is var c
                                        && (c.Contains("playwright_chromiumdev_profile", StringComparison.OrdinalIgnoreCase) || c.Contains("--remote-debugging-pipe", StringComparison.OrdinalIgnoreCase))).ToHashSet();
            if (roots.Count == 0) return roots;
            var found = new HashSet<int>(roots);
            foreach (var id in ids)
                for (int up = Native.ParentId(id), depth = 0; up != 0 && ids.Contains(up) && depth < 8; up = Native.ParentId(up), depth++)
                    if (roots.Contains(up)) { found.Add(id); break; }
            return found;
        }
        static bool HasWindow(Process p) { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } }
    }

    // ------------------------------------------------------------ while it runs
    /// <summary>Nothing is measured for its own sake: only what a switched-on helper needs is read.</summary>
    void Watch()
    {
        _tick++;
        if (S.GmHeat)
        {
            var heat = Thermal.Read(gpu: _tick % 3 == 1);   // the GPU reading starts a process, so only every 9 s
            if (heat.CpuTemp is double c) CpuTempNow = c;
            if (heat.GpuTemp is double g) GpuTempNow = g;
            WatchHeat();
        }
        // cached files fill the RAM while a game runs for hours; hand it back when little is left free
        if (S.GmStandby && (DateTime.Now - _lastPurge).TotalSeconds > 60 && Thermal.MemoryLists() is var (free, standby)
            && free < 1024 * 1048576d && standby > 1024 * 1048576d && Native.PurgeStandbyList())
        {
            _lastPurge = DateTime.Now;
            Sys.Log($"GAME MODE  standby memory cleared ({Sys.Bytes(standby)} cached, {Sys.Bytes(free)} was free)");
        }
    }

    /// <summary>
    /// Too hot for 30 seconds in a row: turn turbo down one step, then give the cooler setting a minute before judging again.
    /// The step is remembered for this PC; the power values themselves go back when the game closes.
    /// </summary>
    void WatchHeat()
    {
        bool hot = CpuTempNow >= S.HeatCpu || GpuTempNow >= S.HeatGpu;
        _hotTicks = hot ? _hotTicks + 1 : 0;
        if (_hotTicks < 10 || (DateTime.Now - _lastHeatStep).TotalSeconds < 60) return;
        _hotTicks = 0;
        _lastHeatStep = DateTime.Now;
        string temps = $"CPU {CpuTempNow:N0} C, GPU {GpuTempNow:N0} C";
        if (S.HeatLevel >= HeatGuard.MaxLevel)
        {
            if (_heatWarned) return;
            _heatWarned = true;
            Sys.Log($"HEAT GUARD  still hot at the last step ({temps})");
            Notice?.Invoke(L.T("Still running hot with turbo off - the cooling needs attention (clean the vents, raise the back of the laptop, cooling pad)."), "warn");
            return;
        }
        Sys.Log($"HEAT GUARD  too hot for 30 s ({temps})");
        if (!HeatGuard.Apply(S.HeatLevel + 1)) return;
        S.Save();
        _heatOn = true;
        HeatGuard.MarkTweaks(AppState.Engine.All);
        AppState.Engine.RefreshAll();
        Done.Add(L.T("Heat Guard") + ": " + HeatGuard.Describe());
        Notice?.Invoke(L.F("Heat Guard: {0} ({1})", HeatGuard.Describe(), temps), "warn");
        TweaksChanged?.Invoke();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ the game is gone
    void End()
    {
        var s = Active!;
        if (_prevOverlay is Guid overlay) Power.SetOverlay(overlay);
        if (_prevPlan is Guid plan) Power.Activate(plan);
        if (_timer) Native.TimerResolution(false);
        _prevOverlay = _prevPlan = null; _timer = false;
        if (S.HeatSaved.Count > 0) { HeatGuard.Restore(); S.Save(); }
        bool heatWasOn = _heatOn;
        _heatOn = false;
        if (S.GmReopen) foreach (var path in _reopen) Sys.Open("explorer.exe", $"\"{path}\"");   // through Explorer so the app starts without admin rights
        _reopen = [];

        Sys.Log($"GAME MODE  end    {s.Game}  {(DateTime.Now - s.Start).TotalMinutes:N1} min");
        Active = null;
        Done = [];
        CpuTempNow = GpuTempNow = null;
        Notice?.Invoke(L.F("Game mode off: {0} - everything is back as it was.", s.Game), "info");
        if (heatWasOn) { AppState.Engine.RefreshAll(); TweaksChanged?.Invoke(); }
        Changed?.Invoke();
    }
}
