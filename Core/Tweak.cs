using System.ComponentModel;
using Microsoft.Win32;

namespace Atomic.Core;

public record RegV(string Path, string Name, RegistryValueKind Kind, object Value, object? Default = null);
public record SvcV(string Name, string Start, string Default);
/// <summary>Power setting: subgroup GUID, setting GUID, target value, Windows default.</summary>
public record PowV(string Sub, string Setting, uint Value, uint? Default = null);
public record Chip(string Text, string Color);

public class Tweak : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Cat { get; init; } = "";
    public string Name { get; init; } = "";
    public string Desc { get; set; } = "";
    public string Impact { get; init; } = "Medium";      // High | Medium | Low
    public string Risk { get; init; } = "Safe";          // Safe | Moderate | Risky
    public bool Rec { get; set; }
    public string[] Tags { get; set; } = [];
    public string? Kind { get; init; }
    public string? Note { get; init; }
    /// <summary>Thai texts from CatalogTh; Name and Desc stay English (they are also what the log uses).</summary>
    public string? NameTh { get; set; }
    public string? DescTh { get; set; }
    public string DisplayName => L.Thai && NameTh != null ? NameTh : Name;
    public string DisplayDesc => L.Thai && DescTh != null ? DescTh : Desc;

    public RegV[] Reg { get; init; } = [];
    public Func<IEnumerable<RegV>>? RegFn { get; init; }
    public SvcV[] Svc { get; init; } = [];
    public PowV[] Pow { get; init; } = [];
    public string[] Tasks { get; init; } = [];
    public string[] Appx { get; init; } = [];
    /// <summary>true = applied, false = not applied, null = not available on this PC.</summary>
    public Func<bool?>? Test { get; init; }
    /// <summary>Custom apply step; the returned text is stored in the backup and handed to Revert.</summary>
    public Func<string?>? Apply { get; init; }
    public Action<string?>? Revert { get; init; }

    bool _on, _available = true;
    public bool IsOn { get => _on; set { _on = value; Raise(nameof(IsOn)); } }
    public bool Available { get => _available; set { _available = value; Raise(nameof(Available)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public IEnumerable<RegV> ResolveReg() => RegFn != null ? RegFn() : Reg;
    public bool NeedsSnapshot => Appx.Length > 0 || Id is "mem_compression" or "net_nicpower";
    public bool NeedsReboot => Tags.Contains("Reboot");
    public int Weight => Impact switch { "High" => 3, "Medium" => 2, _ => 1 };

    public string KindText => Kind ?? (Appx.Length > 0 ? "APPS" : Pow.Length > 0 ? "POWER"
        : Svc.Length > 0 && (Reg.Length > 0 || RegFn != null) ? "SERVICE + REG" : Svc.Length > 0 ? "SERVICE"
        : Tasks.Length > 0 ? "TASKS" : Reg.Length > 0 || RegFn != null ? "REGISTRY" : "SYSTEM");

    public string Glyph => Cat switch
    {
        "FPS & Latency" => "", "GPU & Display" => "", "Power & CPU" => "", "Registry" => "",
        "Memory & Storage" => "", "Network & Ping" => "", "Debloat" => "", "Privacy" => "",
        "Quality of Life" => "", "Advanced" => "", _ => ""
    };

    public List<Chip> Chips
    {
        get
        {
            var l = new List<Chip>();
            if (Rec) l.Add(new(L.T("Recommended"), "#3DDC97"));
            l.Add(new(L.T($"{Impact} impact"), Impact == "High" ? "#FF7A3D" : Impact == "Medium" ? "#F5B544" : "#9A9AA3"));
            foreach (var t in Tags)
                l.Add(new(L.T(t), t switch
                {
                    "Security Risk" or "Not Reversible" => "#FF5C5C",
                    "Feature Breaking" => "#B57BEE",
                    "Reboot" or "Sign-out" => "#6FB7FF",
                    "Power Hungry" => "#F5B544",
                    _ => "#9A9AA3"
                }));
            return l;
        }
    }

    /// <summary>Exactly what this tweak changes - shown as the card tooltip.</summary>
    public string ChangeText
    {
        get
        {
            var lines = new List<string> { KindText };
            foreach (var r in ResolveReg())
                lines.Add($"REG  {r.Path}\\{r.Name} = {(r.Value is byte[] b ? BitConverter.ToString(b).Replace('-', ' ') : r.Value)}");
            foreach (var s in Svc) lines.Add($"SERVICE  {s.Name} -> {s.Start}");
            foreach (var p in Pow) lines.Add($"POWER  {p.Setting} -> {p.Value}  (active plan{(AppState.Pc.IsLaptop ? ", plugged in only" : "")})");
            foreach (var k in Tasks) lines.Add($"TASK  disable {k}");
            foreach (var a in Appx) lines.Add($"APP  remove {a}");
            if (Note != null) lines.Add(Note);
            return string.Join("\n", lines);
        }
    }
}

// ---------------------------------------------------------------- backup model (backup.json)
public class RegBk
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Exists { get; set; }
    public RegistryValueKind Kind { get; set; }
    public string Value { get; set; } = "";
    /// <summary>Keys that had to be created for this value, deepest first - removed again on revert if still empty.</summary>
    public List<string> CreatedKeys { get; set; } = [];
}
public class SvcBk
{
    public string Name { get; set; } = "";
    public string Start { get; set; } = "";
    /// <summary>Whether the service was running before it was disabled, so revert only restarts what it stopped.</summary>
    public bool Running { get; set; }
}
public class PowBk { public string Sub { get; set; } = ""; public string Setting { get; set; } = ""; public uint? Ac { get; set; } public uint? Dc { get; set; } public string Scheme { get; set; } = ""; }
public class TaskBk { public string Path { get; set; } = ""; public bool Enabled { get; set; } }
public class TweakBackup
{
    public List<RegBk> Reg { get; set; } = [];
    public List<SvcBk> Svc { get; set; } = [];
    public List<PowBk> Pow { get; set; } = [];
    public List<TaskBk> Tasks { get; set; } = [];
    public string? Custom { get; set; }
    public string Time { get; set; } = "";
}

/// <summary>Applies, reverts and tests tweaks. Original values are saved before the first change.</summary>
public class TweakEngine
{
    public static readonly string BackupFile = Path.Combine(Sys.DataDir, "backup.json");
    public List<Tweak> All { get; private set; } = [];
    Dictionary<string, TweakBackup> _backup = [];

    public int BackupCount => _backup.Count;
    public bool HasBackup(string id) => _backup.ContainsKey(id);

    public void Load(List<Tweak> catalog)
    {
        All = catalog;
        _backup = Sys.LoadJson<Dictionary<string, TweakBackup>>(BackupFile);
    }

    void SaveBackup() => Sys.SaveJson(BackupFile, _backup);

    public bool? TestState(Tweak t)
    {
        try
        {
            if (t.Test != null) return t.Test();
            int checkedCount = 0;
            foreach (var r in t.ResolveReg())
            {
                checkedCount++;
                var c = Sys.RegGet(r.Path, r.Name);
                if (!c.Exists || !Sys.Same(c.Value, r.Value)) return false;
            }
            foreach (var s in t.Svc)
            {
                var cur = Sys.SvcGet(s.Name);
                if (cur == null) continue;
                checkedCount++;
                if (cur != s.Start) return false;
            }
            if (t.Pow.Length > 0)
            {
                var scheme = Power.ActiveScheme();
                foreach (var p in t.Pow)
                {
                    var cur = Power.Get(scheme, p.Sub, p.Setting, true);
                    if (cur == null) continue;
                    checkedCount++;
                    if (cur != p.Value) return false;
                }
            }
            foreach (var pattern in t.Tasks)
                foreach (var (_, task) in SchedTasks.Find(pattern))
                {
                    checkedCount++;
                    if ((bool)task.Enabled) return false;
                }
            if (t.Appx.Length > 0)
            {
                checkedCount++;
                foreach (var a in t.Appx)
                {
                    var rx = Sys.Wildcard(a);
                    if (AppState.Pc.AppxNames.Any(n => rx.IsMatch(n))) return false;
                }
            }
            return checkedCount == 0 ? null : true;
        }
        catch (Exception ex) { Sys.Log($"Test failed for {t.Id}: {ex.Message}"); return false; }
    }

    /// <summary>Applies the tweak and returns what could not be changed (empty when everything went through).</summary>
    public List<string> Apply(Tweak t)
    {
        Sys.Log($"APPLY  {t.Id}  ({t.Name})");
        var errors = new List<string>();
        void Fail(string what, Exception ex) { errors.Add($"{what}: {ex.Message}"); Sys.Log($"  fail {what}: {ex.Message}"); }
        bool hadBackup = _backup.ContainsKey(t.Id);
        var bk = new TweakBackup { Time = DateTime.Now.ToString("s") };

        foreach (var r in t.ResolveReg())
        {
            var c = Sys.RegGet(r.Path, r.Name);
            var created = Sys.RegMissingKeys(r.Path);
            bk.Reg.Add(new RegBk { Path = r.Path, Name = r.Name, Exists = c.Exists, Kind = c.Kind, Value = c.Exists ? Sys.Encode(c.Value!) : "", CreatedKeys = created });
            try { Sys.RegSet(r.Path, r.Name, r.Kind, r.Value); }
            catch (Exception ex)
            {
                // Windows write-protects some values (UCPD); do not leave the empty key behind
                Fail($"registry {r.Path}\\{r.Name}", ex);
                foreach (var key in created) Sys.RegDelKeyIfEmpty(key);
            }
        }
        foreach (var s in t.Svc)
        {
            var cur = Sys.SvcGet(s.Name);
            if (cur == null) continue;
            bk.Svc.Add(new SvcBk { Name = s.Name, Start = cur, Running = Sys.SvcRunning(s.Name) });
            Sys.SvcSet(s.Name, s.Start);
        }
        if (t.Pow.Length > 0)
        {
            // laptops keep their battery (DC) behaviour: performance settings only change while plugged in
            var scheme = Power.ActiveScheme();
            foreach (var p in t.Pow)
            {
                bk.Pow.Add(new PowBk { Sub = p.Sub, Setting = p.Setting, Ac = Power.Get(scheme, p.Sub, p.Setting, true), Dc = Power.Get(scheme, p.Sub, p.Setting, false), Scheme = scheme.ToString() });
                Power.Set(scheme, p.Sub, p.Setting, p.Value, true);
                if (!AppState.Pc.IsLaptop) Power.Set(scheme, p.Sub, p.Setting, p.Value, false);
            }
            Power.Activate(scheme);
        }
        foreach (var pattern in t.Tasks)
            foreach (var (path, task) in SchedTasks.Find(pattern))
            {
                try
                {
                    bk.Tasks.Add(new TaskBk { Path = path, Enabled = (bool)task.Enabled });
                    task.Enabled = false;
                }
                catch (Exception ex) { Fail($"task {path}", ex); }
            }
        if (t.Appx.Length > 0) RemoveAppx(t.Appx);
        if (t.Apply != null)
        {
            try { bk.Custom = t.Apply(); } catch (Exception ex) { Fail("custom step", ex); }
        }
        if (!hadBackup) { _backup[t.Id] = bk; SaveBackup(); }
        return errors;
    }

    /// <summary>Original values saved for a tweak, if it was applied by this app.</summary>
    public TweakBackup? BackupOf(string id) => _backup.GetValueOrDefault(id);

    public void Revert(Tweak t)
    {
        Sys.Log($"REVERT {t.Id}  ({t.Name})");
        _backup.TryGetValue(t.Id, out var bk);

        if (bk is { Reg.Count: > 0 })
        {
            foreach (var e in bk.Reg)
            {
                try { if (e.Exists) Sys.RegSet(e.Path, e.Name, e.Kind, Sys.Decode(e.Kind, e.Value)); else Sys.RegDel(e.Path, e.Name); }
                catch (Exception ex) { Sys.Log($"  reg restore fail {e.Path}\\{e.Name}: {ex.Message}"); }
            }
            // second pass, once every value is gone: drop the keys we created ourselves
            foreach (var key in bk.Reg.SelectMany(e => e.CreatedKeys)) Sys.RegDelKeyIfEmpty(key);
        }
        else
        {
            foreach (var r in t.ResolveReg())
            {
                try { if (r.Default == null) Sys.RegDel(r.Path, r.Name); else Sys.RegSet(r.Path, r.Name, r.Kind, r.Default); } catch { }
            }
        }

        if (bk is { Svc.Count: > 0 })
        {
            foreach (var e in bk.Svc) { Sys.SvcSet(e.Name, e.Start); if (e.Running) Sys.SvcStart(e.Name); }
        }
        else
        {
            foreach (var s in t.Svc) if (Sys.SvcGet(s.Name) != null) Sys.SvcSet(s.Name, s.Default);
        }

        if (bk is { Pow.Count: > 0 })
        {
            foreach (var e in bk.Pow)
            {
                var scheme = Guid.TryParse(e.Scheme, out var g) ? g : Power.ActiveScheme();
                if (e.Ac is uint ac) Power.Set(scheme, e.Sub, e.Setting, ac, true);
                if (e.Dc is uint dc) Power.Set(scheme, e.Sub, e.Setting, dc, false);
            }
            Power.Activate(Power.ActiveScheme());
        }
        else if (t.Pow.Length > 0)
        {
            var scheme = Power.ActiveScheme();
            foreach (var p in t.Pow)
            {
                if (p.Default is not uint d) continue;
                Power.Set(scheme, p.Sub, p.Setting, d, true);
                if (!AppState.Pc.IsLaptop) Power.Set(scheme, p.Sub, p.Setting, d, false);
            }
            Power.Activate(scheme);
        }

        if (t.Tasks.Length > 0)
        {
            // without a backup we cannot know which tasks were already disabled, so re-enable them all
            var targets = bk is { Tasks.Count: > 0 } ? bk.Tasks.Where(e => e.Enabled).Select(e => e.Path) : t.Tasks;
            foreach (var pattern in targets)
                foreach (var (path, task) in SchedTasks.Find(pattern))
                {
                    try { task.Enabled = true; } catch (Exception ex) { Sys.Log($"  task restore fail {path}: {ex.Message}"); }
                }
        }

        if (t.Revert != null)
        {
            try { t.Revert(bk?.Custom); } catch (Exception ex) { Sys.Log($"  custom revert fail: {ex.Message}"); }
        }
        if (_backup.Remove(t.Id)) SaveBackup();
    }

    static void RemoveAppx(string[] patterns)
    {
        var list = string.Join(",", patterns.Select(p => $"'{p.Replace("'", "''")}'"));
        Sys.PS($$"""
            $ErrorActionPreference='SilentlyContinue'
            foreach ($a in @({{list}})) {
                Get-AppxPackage -AllUsers -Name $a | ForEach-Object {
                    try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop } catch { Remove-AppxPackage -Package $_.PackageFullName }
                }
                Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like $a } | ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName | Out-Null }
            }
            """, 600000);
    }

    /// <summary>Re-tests every tweak. Pass snapshot=true after changes that need the PowerShell state snapshot.</summary>
    public void RefreshAll(bool snapshot = false)
    {
        if (snapshot) AppState.Pc.RefreshState();
        AppState.Pc.RefreshLight();
        foreach (var t in All)
        {
            var st = TestState(t);
            t.Available = st != null;
            t.IsOn = st == true;
        }
    }

    public (int Score, int On, int Total) Score()
    {
        var rec = All.Where(t => t.Rec && t.Available).ToList();
        int total = rec.Sum(t => t.Weight), on = rec.Where(t => t.IsOn).Sum(t => t.Weight);
        return (total == 0 ? 100 : (int)Math.Round(100.0 * on / total), rec.Count(t => t.IsOn), rec.Count);
    }
}
