using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Atomic.Core;

/// <param name="Live">A folder other programs work in right now (temp): files changed in the last day are left alone,
/// and so is a whole Playwright folder (playwright_*, playwright-artifacts-*) while anything in it changed in the last day.
/// Folders left behind by a run that was stopped half way are cleaned like everything else.</param>
public record CleanPath(string Dir, string Pattern = "*", bool Recurse = true, bool Live = false);

public class CleanItem : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Desc { get; init; } = "";
    public string Glyph { get; init; } = "";
    public List<CleanPath> Paths { get; init; } = [];
    public bool RecycleBin { get; init; }

    bool _checked; long _size = -1; int _files;
    public bool Checked { get => _checked; set { _checked = value; Raise(nameof(Checked)); } }
    public long Size { get => _size; set { _size = value; Raise(nameof(Size)); Raise(nameof(SizeText)); } }
    public int Files { get => _files; set { _files = value; Raise(nameof(SizeText)); } }
    public string SizeText => Size < 0 ? L.T("Not scanned") : Size == 0 ? L.T("Clean") : L.F("{0}  -  {1:N0} items", Sys.Bytes(Size), Files);

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public static class Cleaner
{
    // never follow junctions/symlinks out of the folder being cleaned
    static readonly EnumerationOptions Deep = new() { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
    static readonly EnumerationOptions Flat = new() { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint };

    public static List<CleanItem> Build()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var browsers = new List<CleanPath>();
        foreach (var root in new[] { @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"BraveSoftware\Brave-Browser\User Data", @"Vivaldi\User Data" })
        {
            var dir = Path.Combine(local, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var profile in SafeDirs(dir).Where(d => File.Exists(Path.Combine(d, "Preferences"))))
                foreach (var cache in new[] { @"Cache\Cache_Data", "Code Cache", "GPUCache" })
                    browsers.Add(new(Path.Combine(profile, cache)));
        }
        var ff = Path.Combine(local, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ff)) foreach (var p in SafeDirs(ff)) browsers.Add(new(Path.Combine(p, "cache2")));

        return
        [
            new() { Name = L.T("Temporary files"), Glyph = "", Checked = true, Desc = L.T("Leftovers from installers and apps in your user and Windows temp folders. Files in use, files changed in the last day and Playwright folders of a run that is going on are skipped."),
                    Paths = [new(Path.GetTempPath().TrimEnd('\\'), Live: true), new(Path.Combine(win, "Temp"), Live: true)] },
            new() { Name = L.T("Windows Update cache"), Glyph = "", Checked = true, Desc = L.T("Already-installed update downloads and Delivery Optimization files. Windows re-downloads anything it still needs."),
                    Paths = [new(Path.Combine(win, @"SoftwareDistribution\Download")), new(Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"))] },
            new() { Name = L.T("Crash dumps & error reports"), Glyph = "", Checked = true, Desc = L.T("Memory dumps and queued Windows Error Reporting data from past crashes."),
                    Paths = [new(Path.Combine(local, "CrashDumps")), new(Path.Combine(pd, @"Microsoft\Windows\WER\ReportQueue")), new(Path.Combine(pd, @"Microsoft\Windows\WER\ReportArchive")),
                             new(Path.Combine(win, "Minidump")), new(win, "MEMORY.DMP", false)] },
            new() { Name = L.T("Windows log files"), Glyph = "", Checked = true, Desc = L.T("Old servicing (CBS / DISM) logs that grow to hundreds of megabytes over time."),
                    Paths = [new(Path.Combine(win, @"Logs\CBS")), new(Path.Combine(win, @"Logs\DISM"))] },
            new() { Name = L.T("Thumbnail & icon cache"), Glyph = "", Checked = true, Desc = L.T("Explorer's picture and icon previews. They are rebuilt automatically the next time you open a folder."),
                    Paths = [new(Path.Combine(local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", false), new(Path.Combine(local, @"Microsoft\Windows\Explorer"), "iconcache_*.db", false)] },
            new() { Name = L.T("Browser caches"), Glyph = "", Checked = true, Desc = L.T("Cached web files from Chrome, Edge, Brave, Vivaldi and Firefox. History, passwords and cookies are NOT touched. Close your browser first."),
                    Paths = browsers },
            new() { Name = L.T("GPU shader caches"), Glyph = "", Checked = false, Desc = L.T("DirectX, NVIDIA, AMD and Intel shader caches. Clear after a driver update if games stutter - shaders rebuild on first launch, so expect a few minutes of hitching."),
                    Paths = [new(Path.Combine(local, "D3DSCache")), new(Path.Combine(local, @"NVIDIA\DXCache")), new(Path.Combine(local, @"NVIDIA\GLCache")), new(Path.Combine(pd, @"NVIDIA Corporation\NV_Cache")),
                             new(Path.Combine(local, @"AMD\DxCache")), new(Path.Combine(local, @"AMD\DxcCache")), new(Path.Combine(local, @"AMD\GLCache")), new(Path.Combine(local, @"Intel\ShaderCache"))] },
            new() { Name = L.T("Recycle Bin"), Glyph = "", Checked = false, RecycleBin = true, Desc = L.T("Permanently deletes everything in the Recycle Bin on all drives. This cannot be undone.") },
        ];
    }

    static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch { return []; }
    }

    static IEnumerable<FileInfo> Files(CleanPath p, List<string>? inUse = null)
    {
        // only ever touch absolute, non-root folders
        if (!Path.IsPathFullyQualified(p.Dir) || p.Dir.Length <= 3 || !Directory.Exists(p.Dir)) return [];
        try
        {
            var files = new DirectoryInfo(p.Dir).EnumerateFiles(p.Pattern, p.Recurse ? Deep : Flat);
            if (!p.Live) return files;
            var dayAgo = DateTime.UtcNow.AddDays(-1);
            inUse ??= BusyBotFolders(p.Dir, dayAgo);
            return files.Where(f => f.LastWriteTimeUtc < dayAgo && !InAny(f.FullName, inUse));
        }
        catch { return []; }
    }

    /// <summary>Playwright folders directly in dir with a file changed since the given time: a run that is going on now.</summary>
    static List<string> BusyBotFolders(string dir, DateTime since)
    {
        var busy = new List<string>();
        foreach (var d in new DirectoryInfo(dir).EnumerateDirectories("playwright*", Flat))
        {
            if (!d.Name.StartsWith("playwright_", StringComparison.OrdinalIgnoreCase) && !d.Name.StartsWith("playwright-artifacts-", StringComparison.OrdinalIgnoreCase)) continue;
            // a file changed in the last day means a run uses it; a folder without any file is only "in use" for the few
            // minutes after it was made (a cleaned left-over gets today's date too, and must not look busy for a day)
            try
            {
                bool any = false, recent = false;
                foreach (var f in d.EnumerateFiles("*", Deep)) { any = true; if (f.LastWriteTimeUtc >= since) { recent = true; break; } }
                if (recent || (!any && d.LastWriteTimeUtc >= DateTime.UtcNow.AddMinutes(-10))) busy.Add(d.FullName + @"\");
            }
            catch { busy.Add(d.FullName + @"\"); }   // cannot look inside: leave it
        }
        return busy;
    }

    static bool InAny(string path, List<string> folders) => folders.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase) || (path + @"\").Equals(f, StringComparison.OrdinalIgnoreCase));

    public static void Scan(CleanItem item)
    {
        long size = 0; int files = 0;
        if (item.RecycleBin)
        {
            var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
            if (Native.SHQueryRecycleBin(null, ref info) == 0) { size = info.i64Size; files = (int)info.i64NumItems; }
        }
        else
        {
            foreach (var p in item.Paths)
                foreach (var f in Files(p))
                {
                    try { size += Native.SizeOnDisk(f); files++; } catch { }
                }
        }
        item.Files = files;
        item.Size = size;
    }

    /// <summary>Deletes what Scan found and returns the bytes actually freed.</summary>
    public static long Clean(CleanItem item)
    {
        long freed = 0;
        if (item.RecycleBin)
        {
            Scan(item);
            freed = item.Size;
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
            if (freed > 0 && Native.SHEmptyRecycleBin(IntPtr.Zero, null, 7) != 0) freed = 0;
        }
        else
        {
            foreach (var p in item.Paths)
            {
                var keep = p.Live && Directory.Exists(p.Dir) ? BusyBotFolders(p.Dir, DateTime.UtcNow.AddDays(-1)) : [];
                foreach (var f in Files(p, keep).ToList())
                {
                    try { long len = Native.SizeOnDisk(f); f.Attributes = FileAttributes.Normal; f.Delete(); freed += len; } catch { }
                }
                if (!p.Recurse || !Directory.Exists(p.Dir)) continue;
                try
                {
                    // deepest folders first; Delete(false) only removes folders that are now empty
                    foreach (var d in new DirectoryInfo(p.Dir).EnumerateDirectories("*", Deep).Where(d => !InAny(d.FullName, keep)).OrderByDescending(d => d.FullName.Length).ToList())
                    {
                        try { d.Delete(false); } catch { }
                    }
                }
                catch { }
            }
        }
        Sys.Log($"CLEAN  {item.Name}: freed {Sys.Bytes(freed)}");
        Scan(item);
        return freed;
    }

    /// <summary>Trims the working set of every process we can open. Returns bytes of RAM made available.</summary>
    /// <param name="skipPid">Process to leave alone: trimming a game while it loads would only slow it down.</param>
    public static double FreeRam(int skipPid = 0)
    {
        double before = Native.Memory().Avail;
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.Id != skipPid) Native.EmptyWorkingSet(p.Handle); } catch { } finally { p.Dispose(); }
        }
        Thread.Sleep(600);
        return Math.Max(0, Native.Memory().Avail - before);
    }
}

// ---------------------------------------------------------------- startup apps
public class StartupEntry : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";
    public string ApprovedKey { get; init; } = "";
    public string ApprovedName { get; init; } = "";

    bool _enabled;
    public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled))); } }
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Startup entries, switched on/off the same way Task Manager does (StartupApproved), so nothing is deleted.</summary>
public static class StartupManager
{
    const string Cv = @"\Microsoft\Windows\CurrentVersion";
    const string Approved = Cv + @"\Explorer\StartupApproved\";

    public static List<StartupEntry> Load()
    {
        var list = new List<StartupEntry>();
        (string Run, string Appr, string Label)[] keys =
        [
            (@"HKCU\Software" + Cv + @"\Run", @"HKCU\Software" + Approved + "Run", "Current user"),
            (@"HKLM\SOFTWARE" + Cv + @"\Run", @"HKLM\SOFTWARE" + Approved + "Run", "All users"),
            (@"HKLM\SOFTWARE\WOW6432Node" + Cv + @"\Run", @"HKLM\SOFTWARE" + Approved + "Run32", "All users (32-bit)"),
        ];
        foreach (var (run, appr, label) in keys)
            foreach (var name in Sys.RegValueNames(run))
            {
                if (name.Length == 0) continue;
                list.Add(new StartupEntry { Name = name, Command = Sys.RegStr(run, name), Source = L.T(label), ApprovedKey = appr, ApprovedName = name, Enabled = IsEnabled(appr, name) });
            }

        (Environment.SpecialFolder Folder, string Appr, string Label)[] folders =
        [
            (Environment.SpecialFolder.Startup, @"HKCU\Software" + Approved + "StartupFolder", "Startup folder"),
            (Environment.SpecialFolder.CommonStartup, @"HKLM\SOFTWARE" + Approved + "StartupFolder", "Startup folder (all users)"),
        ];
        foreach (var (folder, appr, label) in folders)
        {
            var dir = Environment.GetFolderPath(folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir))
            {
                var file = Path.GetFileName(f);
                if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry { Name = Path.GetFileNameWithoutExtension(f), Command = f, Source = L.T(label), ApprovedKey = appr, ApprovedName = file, Enabled = IsEnabled(appr, file) });
            }
        }
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // first byte: even = enabled, odd = disabled; missing value = enabled
    static bool IsEnabled(string key, string name) => Sys.RegGet(key, name).Value is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;

    public static void SetEnabled(StartupEntry e, bool enabled)
    {
        var data = new byte[12];
        data[0] = (byte)(enabled ? 2 : 3);
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        Sys.RegSet(e.ApprovedKey, e.ApprovedName, RegistryValueKind.Binary, data);
        Sys.Log($"STARTUP {(enabled ? "enable" : "disable")}  {e.Name}  ({e.Source})");
        e.Enabled = enabled;
    }
}
