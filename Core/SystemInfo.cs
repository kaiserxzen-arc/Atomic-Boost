using System.Globalization;
using System.Text.Json;

namespace Atomic.Core;

public record GpuInfo(string Name, string Vendor, string Driver, DateTime? DriverDate, double Vram, int ResX, int ResY, int Hz, string Pnp);
public record DiskInfo(string Number, string Name, string Type, double Size, string Bus, string Health);
public record VolInfo(string Letter, string Label, double Size, double Free);
public record HealthCheck(string Level, string Title, string Text);   // Level: ok | warn | info

/// <summary>Hardware scan plus the slow-to-query bits of system state (Store apps, memory compression, NIC power).</summary>
public class SystemInfo
{
    public string Computer = Environment.MachineName, OsName = "Windows", OsVer = "", Model = "", Board = "", Bios = "";
    public int Build;
    public DateTime? BootTime;

    public string CpuName = "Unknown CPU", CpuVendor = "";
    public int CpuCores, CpuThreads = Environment.ProcessorCount, CpuClock, CpuL3Kb;

    public List<GpuInfo> Gpus = [];
    public GpuInfo? Gpu;

    public double RamBytes;
    public int RamSticks, RamSlots, RamSpeed;
    public string RamType = "", RamMaker = "";

    public List<DiskInfo> Disks = [];
    public List<VolInfo> Volumes = [];
    public string SystemDiskType = "Unknown", SystemDiskName = "";
    public bool SystemIsSSD => SystemDiskType.Contains("SSD");
    public bool SystemIsHDD => SystemDiskType == "HDD";
    public bool HasHDD => Disks.Any(d => d.Type == "HDD");

    public bool IsLaptop, HasBattery, HvciOn;
    public int? BatteryHealth;
    public string PowerPlan = "";

    // state snapshot
    public HashSet<string> AppxNames = new(StringComparer.OrdinalIgnoreCase);
    public bool? MemCompression;
    public List<(string Name, string Pm)> Nics = [];
    public string Bcd = "";

    public string GpuVendor => Gpu?.Vendor ?? "";
    public string DeviceKind => IsLaptop ? "Laptop" : "Desktop";
    public string DeviceKindText => L.T(DeviceKind);
    public static bool OnAcPower => !Native.GetSystemPowerStatus(out var s) || s.ACLineStatus != 0;
    public static int? BatteryPercent => Native.GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;

    const string ScanScript = """
        $ErrorActionPreference='SilentlyContinue'
        $o=[ordered]@{}
        $cpu=Get-CimInstance Win32_Processor|Select-Object -First 1
        $o.Cpu=@{Name="$($cpu.Name)";Cores=[int]$cpu.NumberOfCores;Threads=[int]$cpu.NumberOfLogicalProcessors;Clock=[int]$cpu.MaxClockSpeed;L3=[int]$cpu.L3CacheSize;Maker="$($cpu.Manufacturer)"}
        $o.Gpus=@(Get-CimInstance Win32_VideoController|Where-Object{$_.Name -notmatch 'Basic Display|Remote|Virtual|Meta|Parsec|DisplayLink|IddCx'}|ForEach-Object{@{Name="$($_.Name)";Driver="$($_.DriverVersion)";Date=$(if($_.DriverDate){$_.DriverDate.ToString('s')}else{''});Ram=[double]$_.AdapterRAM;ResX=[int]$_.CurrentHorizontalResolution;ResY=[int]$_.CurrentVerticalResolution;Hz=[int]$_.CurrentRefreshRate;Pnp="$($_.PNPDeviceID)"}})
        $o.Mem=@(Get-CimInstance Win32_PhysicalMemory|ForEach-Object{@{Cap=[double]$_.Capacity;Speed=[int]$_.ConfiguredClockSpeed;Speed2=[int]$_.Speed;Type=[int]$_.SMBIOSMemoryType;Maker="$($_.Manufacturer)".Trim()}})
        $o.Slots=[int](Get-CimInstance Win32_PhysicalMemoryArray|Measure-Object -Property MemoryDevices -Sum).Sum
        $o.Disks=@(Get-PhysicalDisk|ForEach-Object{@{Num="$($_.DeviceId)";Name="$($_.FriendlyName)".Trim();Media="$($_.MediaType)";Bus="$($_.BusType)";Spindle=[double]$_.SpindleSpeed;Size=[double]$_.Size;Health="$($_.HealthStatus)"}})
        $o.SysDisk="$((Get-Partition -DriveLetter $env:SystemDrive.TrimEnd(':')|Select-Object -First 1).DiskNumber)"
        $bb=Get-CimInstance Win32_BaseBoard;$bios=Get-CimInstance Win32_BIOS;$cs=Get-CimInstance Win32_ComputerSystem;$os=Get-CimInstance Win32_OperatingSystem
        $o.Board=("$($bb.Manufacturer) $($bb.Product)").Trim()
        $o.Bios="$($bios.SMBIOSBIOSVersion)"
        $o.Model=("$($cs.Manufacturer) $($cs.Model)").Trim()
        $o.OsName="$($os.Caption)"
        $o.Boot=$(if($os.LastBootUpTime){$os.LastBootUpTime.ToString('s')}else{''})
        $o.Chassis=@((Get-CimInstance Win32_SystemEnclosure).ChassisTypes|ForEach-Object{[int]$_})
        $o.HasBattery=[bool](Get-CimInstance Win32_Battery)
        $o.BatFull=[double](Get-CimInstance -Namespace root\wmi -ClassName BatteryFullChargedCapacity|Select-Object -First 1).FullChargedCapacity
        $o.BatDesign=[double](Get-CimInstance -Namespace root\wmi -ClassName BatteryStaticData|Select-Object -First 1).DesignedCapacity
        $dg=Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard
        $o.Hvci=(@($dg.SecurityServicesRunning) -contains 2)

        """ + StateScript;

    const string StateScript = """
        $o.Appx=@(Get-AppxPackage|ForEach-Object{$_.Name})
        $o.MemComp="$((Get-MMAgent).MemoryCompression)"
        $o.Nics=@(Get-NetAdapter -Physical|ForEach-Object{$pm=Get-NetAdapterPowerManagement -Name $_.Name;@{Name="$($_.Name)";Pm="$($pm.AllowComputerToTurnOffDevice)"}})
        $o|ConvertTo-Json -Depth 5 -Compress
        """;

    // ------------------------------------------------------------ json helpers (PowerShell output is loosely typed)
    static string S(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v.ToString() : "";
    static double D(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    static bool B(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static IEnumerable<JsonElement> A(JsonElement e, string n)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(n, out var v)) yield break;
        if (v.ValueKind == JsonValueKind.Array) { foreach (var x in v.EnumerateArray()) yield return x; }
        else if (v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) yield return v;
    }
    static JsonElement? Parse(string output)
    {
        int i = output.IndexOf('{');
        if (i < 0) return null;
        try { return JsonDocument.Parse(output[i..]).RootElement; } catch (Exception ex) { Sys.Log("scan parse fail: " + ex.Message); return null; }
    }
    static DateTime? Date(string s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // ------------------------------------------------------------ scan
    public static SystemInfo Scan()
    {
        var i = new SystemInfo();
        const string cv = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        int.TryParse(Sys.RegStr(cv, "CurrentBuildNumber"), out i.Build);
        i.OsVer = Sys.RegStr(cv, "DisplayVersion");
        i.OsName = Sys.RegStr(cv, "ProductName");
        i.CpuName = Sys.RegStr(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString").Trim();
        i.RamBytes = Native.Memory().Total;
        i.Volumes = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new VolInfo(d.Name.TrimEnd('\\'), d.VolumeLabel, d.TotalSize, d.TotalFreeSpace)).ToList();

        var root = Parse(Sys.PS(ScanScript));
        if (root is JsonElement o)
        {
            if (o.TryGetProperty("Cpu", out var cpu))
            {
                var name = System.Text.RegularExpressions.Regex.Replace(S(cpu, "Name"), @"\s+", " ").Trim();
                if (name.Length > 0) i.CpuName = name;
                i.CpuCores = (int)D(cpu, "Cores");
                if (D(cpu, "Threads") > 0) i.CpuThreads = (int)D(cpu, "Threads");
                i.CpuClock = (int)D(cpu, "Clock");
                i.CpuL3Kb = (int)D(cpu, "L3");
                var maker = S(cpu, "Maker");
                i.CpuVendor = maker.Contains("Intel") ? "Intel" : maker.Contains("AMD") ? "AMD" : maker;
            }

            foreach (var g in A(o, "Gpus"))
            {
                var name = S(g, "Name");
                var vendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                           : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                           : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Other";
                double vram = RegistryVram(name);
                if (vram <= 0) vram = D(g, "Ram");
                // NVIDIA's public version is the last five digits of the Windows one: 32.0.15.6109 -> 561.09
                var driver = S(g, "Driver");
                var digits = driver.Replace(".", "");
                if (vendor == "NVIDIA" && digits.Length >= 5) driver = $"{digits[^5..^2]}.{digits[^2..]}  ({driver})";
                i.Gpus.Add(new GpuInfo(name, vendor, driver, Date(S(g, "Date")), vram, (int)D(g, "ResX"), (int)D(g, "ResY"), (int)D(g, "Hz"), S(g, "Pnp")));
            }
            // discrete cards first, then the one with the most video memory
            i.Gpu = i.Gpus.OrderBy(g => g.Vendor == "NVIDIA" || g.Name.Contains(" RX ") || g.Name.Contains("Arc") ? 0 : 1).ThenByDescending(g => g.Vram).FirstOrDefault();

            var mods = A(o, "Mem").ToList();
            i.RamSticks = mods.Count;
            i.RamSlots = (int)D(o, "Slots");
            if (mods.Count > 0)
            {
                var m = mods[0];
                i.RamSpeed = (int)(D(m, "Speed") > 0 ? D(m, "Speed") : D(m, "Speed2"));
                i.RamType = (int)D(m, "Type") switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 30 => "LPDDR4", 35 => "LPDDR5", _ => "" };
                i.RamMaker = S(m, "Maker");
            }

            foreach (var d in A(o, "Disks"))
            {
                string media = S(d, "Media"), bus = S(d, "Bus");
                string type = bus == "NVMe" ? "NVMe SSD"
                            : media is "SSD" or "HDD" ? media
                            : D(d, "Spindle") == 0 ? "SSD" : "Disk";
                i.Disks.Add(new DiskInfo(S(d, "Num"), S(d, "Name"), type, D(d, "Size"), bus, S(d, "Health")));
            }
            var sys = i.Disks.FirstOrDefault(d => d.Number == S(o, "SysDisk"));
            if (sys != null) { i.SystemDiskType = sys.Type; i.SystemDiskName = sys.Name; }

            i.Board = S(o, "Board"); i.Bios = S(o, "Bios"); i.Model = S(o, "Model");
            var os = S(o, "OsName").Replace("Microsoft ", "").Trim();
            if (os.Length > 0) i.OsName = os;
            i.BootTime = Date(S(o, "Boot"));

            var chassis = A(o, "Chassis").Where(c => c.ValueKind == JsonValueKind.Number).Select(c => c.GetInt32()).ToList();
            i.HasBattery = B(o, "HasBattery");
            int[] laptop = [8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32], desktop = [3, 4, 5, 6, 7, 13, 15, 16, 35];
            i.IsLaptop = chassis.Any(laptop.Contains) || (i.HasBattery && !chassis.Any(desktop.Contains));
            if (D(o, "BatDesign") > 0 && D(o, "BatFull") > 0) i.BatteryHealth = (int)Math.Min(100, Math.Round(100 * D(o, "BatFull") / D(o, "BatDesign")));
            i.HvciOn = B(o, "Hvci");
            i.ReadState(o);
        }
        else
        {
            // PowerShell unavailable: fall back to what the Win32 API can tell us
            i.HasBattery = Native.GetSystemPowerStatus(out var ps) && ps.BatteryFlag != 128 && ps.BatteryFlag != 255;
            i.IsLaptop = i.HasBattery;
        }
        if (i.Build >= 22000) i.OsName = i.OsName.Replace("Windows 10", "Windows 11");
        i.RefreshLight();
        return i;
    }

    void ReadState(JsonElement o)
    {
        AppxNames = new HashSet<string>(A(o, "Appx").Select(a => a.ToString()), StringComparer.OrdinalIgnoreCase);
        MemCompression = S(o, "MemComp") switch { "True" => true, "False" => false, _ => null };
        Nics = A(o, "Nics").Select(n => (S(n, "Name"), S(n, "Pm"))).ToList();
    }

    /// <summary>Re-reads Store apps, memory compression and NIC power state (one PowerShell call).</summary>
    public void RefreshState()
    {
        if (Parse(Sys.PS("$ErrorActionPreference='SilentlyContinue';$o=[ordered]@{}\n" + StateScript)) is JsonElement o) ReadState(o);
    }

    /// <summary>Cheap state that is re-read on every refresh.</summary>
    public void RefreshLight()
    {
        Bcd = Sys.Run("bcdedit.exe", "/enum {current}", 10000);
        var m = System.Text.RegularExpressions.Regex.Match(Sys.Run("powercfg.exe", "/getactivescheme", 10000), @"\((.+)\)");
        if (m.Success) PowerPlan = m.Groups[1].Value;
    }

    static double RegistryVram(string gpuName)
    {
        const string cls = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        foreach (var sub in Sys.RegSubKeys(cls))
        {
            if (sub.Length != 4 || Sys.RegStr($@"{cls}\{sub}", "DriverDesc") != gpuName) continue;
            var q = Sys.RegGet($@"{cls}\{sub}", "HardwareInformation.qwMemorySize").Value;
            if (q is long l && l > 0) return l;
            var v = Sys.RegGet($@"{cls}\{sub}", "HardwareInformation.MemorySize").Value;
            if (v is byte[] b && b.Length >= 4) return BitConverter.ToUInt32(b, 0);
            if (v is int n && n > 0) return (uint)n;
        }
        return 0;
    }

    public string Uptime
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return ts.Days > 0 ? $"{ts.Days}d {ts.Hours}h {ts.Minutes}m" : $"{ts.Hours}h {ts.Minutes}m";
        }
    }

    // ------------------------------------------------------------ health checks
    public List<HealthCheck> Health(int startupCount)
    {
        var c = new List<HealthCheck>();
        void Add(string level, string title, string text) => c.Add(new(level, title, text));

        if (SystemIsHDD) Add("warn", L.T("Windows is installed on a hard drive"), L.T("An SSD is the single biggest upgrade for boot time, load times and stutter. Even a budget SATA SSD makes a huge difference."));
        else if (SystemIsSSD) Add("ok", L.F("Windows is on an {0}", SystemDiskType), L.T("Fast system drive - good for load times and stutter-free asset streaming."));

        foreach (var d in Disks.Where(d => d.Health.Length > 0 && d.Health != "Healthy"))
            Add("warn", L.F("Drive health: {0}", d.Health), L.F("{0} reports a problem. Back up your data and check the drive with the manufacturer's tool.", d.Name));

        double gb = RamBytes / 1073741824d;
        if (gb is > 0 and < 7.5) Add("warn", L.F("Only {0:N0} GB of RAM", gb), L.T("Windows and a browser already use most of this. 16 GB is the comfortable minimum for modern games."));
        else if (RamSticks == 1 && !IsLaptop) Add("warn", L.T("RAM is running in single channel"), L.T("One memory stick halves memory bandwidth. Two matched sticks (dual channel) noticeably improve minimum FPS."));
        else if (RamSticks > 0) Add("ok", L.F("{0} {1} memory", Sys.Bytes(RamBytes), RamType), RamSpeed > 0 ? L.F("{0} module(s) running at {1} MT/s.", RamSticks, RamSpeed) : L.F("{0} module(s) installed.", RamSticks));

        if (PowerPlan.Length > 0)
        {
            bool fast = System.Text.RegularExpressions.Regex.IsMatch(PowerPlan, "Ultimate|High|OptiForge|Vortex|Atomic|Performance", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string plan = L.F("Power plan: {0}", PowerPlan);
            if (fast) Add("ok", plan, L.T("The CPU is allowed to run at full speed."));
            else if (IsLaptop) Add("info", plan, L.T("Normal for a laptop. Turn on \"Best Performance Power Mode\" in Windows Tweaks and play plugged in for full speed."));
            else Add("warn", plan, L.T("Balanced and power-saver plans down-clock the CPU between frames. Apply the Atomic Ultimate Performance plan."));
        }

        if (Gpu?.DriverDate is DateTime dt)
        {
            int months = (int)((DateTime.Now - dt).TotalDays / 30);
            string site = Gpu.Vendor switch { "NVIDIA" => "nvidia.com", "AMD" => "amd.com/support", "Intel" => "intel.com/support", _ => L.T("the manufacturer's website") };
            if (months > 12) Add("warn", L.F("GPU driver is {0} months old", months), L.F("Download the latest {0} driver from {1}.", Gpu.Vendor, site));
            else Add("ok", L.T("GPU driver is recent"), L.F("{0} - driver {1}", Gpu.Name, Gpu.Driver));
        }

        if (startupCount > 8) Add("warn", L.F("{0} apps launch at startup", startupCount), L.F("Every startup app competes for your {0} CPU threads and RAM. Disable the ones you do not need on the Startup Apps page.", CpuThreads));
        else Add("ok", L.F("{0} startup apps", startupCount), L.T("Startup is lean."));

        var sysVol = Volumes.FirstOrDefault(v => v.Letter.Equals(Environment.GetEnvironmentVariable("SystemDrive"), StringComparison.OrdinalIgnoreCase));
        if (sysVol is { Size: > 0 })
        {
            int pct = (int)Math.Round(100 * sysVol.Free / sysVol.Size);
            if (pct < 15) Add("warn", L.F("System drive only {0}% free", pct), L.T("Windows, updates and shader caches need free space. Run the Cleaner or move games to another drive."));
            else Add("ok", L.F("System drive {0}% free", pct), L.F("{0} free on {1}", Sys.Bytes(sysVol.Free), sysVol.Letter));
        }

        if (HvciOn) Add("info", L.T("Memory Integrity (HVCI) is ON"), L.T("A security feature that costs some performance, most on older CPUs. Disabling it is an Advanced trade-off - see Windows Tweaks > Advanced."));

        if (IsLaptop)
        {
            if (!OnAcPower) Add("warn", L.T("Running on battery"), L.T("Laptops limit CPU and GPU power on battery. Plug in the charger for maximum performance."));
            if (BatteryHealth is int h) Add(h < 60 ? "warn" : "ok", L.F("Battery health {0}%", h), L.T(h < 60 ? "The battery holds much less charge than when new - consider replacing it." : "Battery capacity is in good shape."));
        }

        if (Gpu is { Hz: > 0 }) Add("info", L.F("Display: {0} x {1} @ {2} Hz", Gpu.ResX, Gpu.ResY, Gpu.Hz), L.T(Gpu.Hz <= 60 ? "If your monitor supports a higher refresh rate, select it in Settings > Display > Advanced display." : "High refresh rate is active."));
        return c;
    }
}
