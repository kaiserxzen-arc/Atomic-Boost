using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Atomic.Core;

/// <summary>Process, registry, service and logging helpers shared by the whole app.</summary>
public static class Sys
{
    // the app was called OptiForge, then Vortex, before it became Atomic: the folder keeps its name so saved backups and settings stay where they are
    public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OptiForge");
    public static readonly string LogFile = Path.Combine(DataDir, "optiforge.log");
    static readonly object LogLock = new();

    public static bool IsAdmin { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static Sys() { try { Directory.CreateDirectory(DataDir); } catch { } }

    public static void Log(string msg)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}";
        try { lock (LogLock) File.AppendAllText(LogFile, line); }
        catch
        {
            // read-only mode may not write to the log an elevated run created: keep its messages in the temp folder
            try { lock (LogLock) File.AppendAllText(Path.Combine(Path.GetTempPath(), "optiforge.log"), line); } catch { }
        }
    }

    // ------------------------------------------------------------ processes
    public static string Run(string exe, string args, int timeoutMs = 60000, Encoding? encoding = null)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (encoding != null) psi.StandardOutputEncoding = encoding;
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } }
            return stdout.Result;
        }
        catch (Exception ex) { Log($"run fail {exe}: {ex.Message}"); return ""; }
    }

    /// <summary>Runs a Windows PowerShell 5.1 script and returns its stdout (UTF-8).</summary>
    public static string PS(string script, int timeoutMs = 180000)
    {
        var full = "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script;
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}", timeoutMs, Encoding.UTF8);
    }

    public static void Open(string file, string args = "")
    {
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
        catch (Exception ex) { Log($"open fail {file}: {ex.Message}"); }
    }

    public static void RestartExplorer()
    {
        Run("taskkill.exe", "/f /im explorer.exe", 10000);
        for (int i = 0; i < 12; i++)
        {
            Thread.Sleep(250);
            if (Process.GetProcessesByName("explorer").Length > 0) return;
        }
        Open("explorer.exe");
    }

    // ------------------------------------------------------------ registry ("HKLM\..." / "HKCU\...")
    static (RegistryKey Root, string Sub) Split(string path)
    {
        int i = path.IndexOf('\\');
        return (path[..i].Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? Registry.CurrentUser : Registry.LocalMachine, path[(i + 1)..]);
    }

    public static (bool Exists, RegistryValueKind Kind, object? Value) RegGet(string path, string name)
    {
        try
        {
            var (root, sub) = Split(path);
            using var k = root.OpenSubKey(sub);
            var v = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return v == null ? (false, default, null) : (true, k!.GetValueKind(name), v);
        }
        catch { return (false, default, null); }
    }

    public static void RegSet(string path, string name, RegistryValueKind kind, object value)
    {
        var (root, sub) = Split(path);
        using var k = root.CreateSubKey(sub, true);
        k.SetValue(name, value, kind);
    }

    public static void RegDel(string path, string name)
    {
        try { var (root, sub) = Split(path); using var k = root.OpenSubKey(sub, true); k?.DeleteValue(name, false); } catch { }
    }

    public static bool RegKeyExists(string path)
    {
        try { var (root, sub) = Split(path); using var k = root.OpenSubKey(sub); return k != null; } catch { return false; }
    }

    /// <summary>tree=false only removes the key when it has no subkeys.</summary>
    public static void RegDelKey(string path, bool tree = true)
    {
        try
        {
            var (root, sub) = Split(path);
            if (tree) root.DeleteSubKeyTree(sub, false); else root.DeleteSubKey(sub, false);
        }
        catch { }
    }

    /// <summary>Keys on the way to <paramref name="path"/> that do not exist yet, deepest first.</summary>
    public static List<string> RegMissingKeys(string path)
    {
        var res = new List<string>();
        while (path.Count(c => c == '\\') > 1 && !RegKeyExists(path))
        {
            res.Add(path);
            path = path[..path.LastIndexOf('\\')];
        }
        return res;
    }

    public static void RegDelKeyIfEmpty(string path)
    {
        try
        {
            var (root, sub) = Split(path);
            using (var k = root.OpenSubKey(sub))
            {
                if (k == null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
            }
            root.DeleteSubKey(sub, false);
        }
        catch { }
    }

    public static string[] RegSubKeys(string path)
    {
        try { var (root, sub) = Split(path); using var k = root.OpenSubKey(sub); return k?.GetSubKeyNames() ?? []; } catch { return []; }
    }

    public static string[] RegValueNames(string path)
    {
        try { var (root, sub) = Split(path); using var k = root.OpenSubKey(sub); return k?.GetValueNames() ?? []; } catch { return []; }
    }

    public static int? RegInt(string path, string name)
    {
        var v = RegGet(path, name).Value;
        try { return v == null ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return null; }
    }

    public static string RegStr(string path, string name) => RegGet(path, name).Value switch
    {
        null => "",
        string[] a => string.Join(" ", a),
        var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };

    // registry values are stored in backups as text
    public static string Encode(object v) => v switch
    {
        byte[] b => Convert.ToHexString(b),
        string[] a => string.Join("\n", a),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };

    public static object Decode(RegistryValueKind kind, string s) => kind switch
    {
        RegistryValueKind.DWord => int.Parse(s, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(s, CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromHexString(s),
        RegistryValueKind.MultiString => s.Split('\n'),
        _ => s
    };

    public static bool Same(object? cur, object want) => cur != null && Encode(cur) == Encode(want);

    // ------------------------------------------------------------ services
    const string SvcRoot = @"HKLM\SYSTEM\CurrentControlSet\Services\";

    /// <summary>auto | delayed-auto | demand | disabled, or null when the service does not exist.</summary>
    public static string? SvcGet(string name)
    {
        var start = RegInt(SvcRoot + name, "Start");
        if (start == null || !RegGet(SvcRoot + name, "ImagePath").Exists) return null;
        return start switch
        {
            2 => RegInt(SvcRoot + name, "DelayedAutostart") == 1 ? "delayed-auto" : "auto",
            3 => "demand",
            4 => "disabled",
            _ => start.ToString()
        };
    }

    public static void SvcSet(string name, string start)
    {
        if (start is not ("auto" or "delayed-auto" or "demand" or "disabled")) return;
        Run("sc.exe", $"config \"{name}\" start= {start}", 15000);
        if (start == "disabled") Run("sc.exe", $"stop \"{name}\"", 15000);
    }

    public static bool SvcRunning(string name)
    {
        var state = Run("sc.exe", $"query \"{name}\"", 15000);
        return state.Contains("RUNNING") || state.Contains("START_PENDING");
    }

    public static void SvcStart(string name)
    {
        // a service that was just told to stop refuses to start until it has finished stopping
        for (int i = 0; i < 20; i++)
        {
            var state = Run("sc.exe", $"query \"{name}\"", 15000);
            if (state.Contains("RUNNING") || state.Contains("START_PENDING")) return;
            if (!state.Contains("STOP_PENDING")) Run("sc.exe", $"start \"{name}\"", 15000);
            Thread.Sleep(500);
        }
        Log($"  service {name} did not start");
    }

    // ------------------------------------------------------------ misc
    public static string Bytes(double b)
    {
        if (b >= 1099511627776d) return $"{b / 1099511627776d:N2} TB";
        if (b >= 1073741824d) return $"{b / 1073741824d:N1} GB";
        if (b >= 1048576d) return $"{b / 1048576d:N0} MB";
        return $"{b / 1024d:N0} KB";
    }

    public static Regex Wildcard(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static T LoadJson<T>(string file) where T : new()
    {
        try { if (File.Exists(file)) return JsonSerializer.Deserialize<T>(File.ReadAllText(file)) ?? new T(); }
        catch (Exception ex) { Log($"json load fail {file}: {ex.Message}"); }
        return new T();
    }

    public static void SaveJson<T>(string file, T value)
    {
        try { File.WriteAllText(file, JsonSerializer.Serialize(value, Json)); }
        catch (Exception ex) { Log($"json save fail {file}: {ex.Message}"); }
    }
}

/// <summary>Scheduled tasks through the Task Scheduler COM API (supports * in the task name).</summary>
public static class SchedTasks
{
    public static List<(string Path, dynamic Task)> Find(string pattern)
    {
        var res = new List<(string, dynamic)>();
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) return res;
            dynamic svc = Activator.CreateInstance(type)!;
            svc.Connect();
            int i = pattern.LastIndexOf('\\');
            string folder = i <= 0 ? "\\" : pattern[..i];
            var rx = Sys.Wildcard(pattern[(i + 1)..]);
            dynamic f = svc.GetFolder(folder);
            foreach (dynamic t in f.GetTasks(1))
                if (rx.IsMatch((string)t.Name)) res.Add(((string)t.Path, t));
        }
        catch { }
        return res;
    }
}

public class Settings
{
    /// <summary>Where settings are saved - redirected by the test harness.</summary>
    public static string FilePath = Path.Combine(Sys.DataDir, "settings.json");
    public bool AutoRestorePoint { get; set; } = true;
    public bool ConfirmRisky { get; set; } = true;
    /// <summary>Short sounds when buttons, tabs and switches are pressed.</summary>
    public bool Sounds { get; set; } = true;
    public List<string> Games { get; set; } = [];
    /// <summary>"en", "th" or empty to follow Windows.</summary>
    public string Language { get; set; } = "";
    /// <summary>What the window's X does: "" = ask, "tray" = keep running in the background, "exit" = quit.</summary>
    public string CloseAction { get; set; } = "";

    // ---- Auto Game Mode (see Core/GameMode.cs); Games above doubles as the list of known game exes
    public bool GameMode { get; set; }
    /// <summary>Closing apps is opt-in on top of the mode itself, and only ever touches GmApps entries that are switched on.</summary>
    public bool GmClose { get; set; }
    public bool GmReopen { get; set; } = true;
    public bool GmFreeRam { get; set; } = true;
    public bool GmPower { get; set; } = true;
    public bool GmPriority { get; set; } = true;
    public bool GmGpu { get; set; } = true;
    public bool GmStandby { get; set; }
    public bool GmTimer { get; set; }
    public bool GmHeat { get; set; } = true;
    /// <summary>Heat Guard limits in degrees C, the step it has reached on this PC (0 = never needed) and the power values it replaced.</summary>
    public int HeatCpu { get; set; } = 95;
    public int HeatGpu { get; set; } = 87;
    public int HeatLevel { get; set; }
    public Dictionary<string, uint> HeatSaved { get; set; } = [];
    public List<CloseApp> GmApps { get; set; } = DefaultCloseApps();
    /// <summary>Exe names that looked like games but are not (never start game mode).</summary>
    public List<string> NotGames { get; set; } = [];

    static List<CloseApp> DefaultCloseApps() =>
    [
        new("OneDrive", true), new("ms-teams", true), new("Teams", true), new("PhoneExperienceHost", true), new("Widgets", true),
        new("WidgetService", true), new("Cortana", true), new("SkypeApp", true), new("Copilot", true),
        // browsers and sync tools may hold work in progress: listed, but off until the user decides
        new("msedge", false), new("chrome", false), new("GoogleDriveFS", false), new("Dropbox", false), new("Discord", false),
    ];

    public static Settings Load() => Sys.LoadJson<Settings>(FilePath);
    public void Save() => Sys.SaveJson(FilePath, this);
}

public static class AppState
{
    public const string Name = "Atomic";
    public const string Version = "1.0.0";
    public static SystemInfo Pc = new();
    public static TweakEngine Engine = new();
    public static readonly LiveStats Stats = new();
    public static readonly Settings Settings = Settings.Load();
    public static readonly GameMode Game = new();
    public static bool RestorePointDone, Scanned, RebootNeeded;
}
