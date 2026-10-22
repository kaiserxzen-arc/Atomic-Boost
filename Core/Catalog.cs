using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Win32;

namespace Atomic.Core;

/// <summary>Every tweak the app knows. Built after the hardware scan so text and recommendations fit this PC.</summary>
public static class Catalog
{
    const string SUB_PROC = "54533251-82be-4824-96c1-47b60b740d00";
    const string SUB_PCIE = "501a4d13-42af-4429-9fd1-a8218c268e20";
    const string SUB_USB = "2a737441-1930-4402-8d77-b2bebba308a3";
    const string SUB_DISK = "0012ee47-9041-4b5d-9b77-535fba8b1442";
    const string SUB_WIFI = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    const string OF_PLAN = "0f7a1c0d-3f05-4e2b-9a6b-1c2d3e4f5a6b";
    const string CDM = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    const string ADV = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string MMCSS = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    const string MEMMGMT = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    const string FS = @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem";
    const string GCS = @"HKCU\System\GameConfigStore";
    const string CLASSIC_MENU = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    public static readonly string[] Categories =
        ["FPS & Latency", "GPU & Display", "Power & CPU", "Registry", "Memory & Storage", "Network & Ping", "Debloat", "Privacy", "Quality of Life", "Advanced"];

    static RegV D(string path, string name, int value, int? def = null) => new(path, name, RegistryValueKind.DWord, value, def);
    static RegV S(string path, string name, string value, string? def = null) => new(path, name, RegistryValueKind.String, value, def);

    static bool BcdHas(string key) => AppState.Pc.Bcd.Contains(key, StringComparison.OrdinalIgnoreCase);
    static void Powercfg(string args) => Sys.Run("powercfg.exe", args, 20000);
    static void Bcdedit(string args) => Sys.Run("bcdedit.exe", args, 20000);

    static IEnumerable<NetworkInterface> ActiveNics() => NetworkInterface.GetAllNetworkInterfaces().Where(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet);

    /// <summary>
    /// UserPreferencesMask for "best performance": menu, list and tooltip animations off. Bit 0x02 of byte 4 is the
    /// Windows "Animation effects" switch, which browsers and Chromium / WebView2 apps read as prefers-reduced-motion;
    /// it keeps the value from before the tweak (from the backup once the tweak is applied), and is never switched off here.
    /// </summary>
    static byte[] VisualFxMask()
    {
        byte[] mask = [0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00];
        var saved = AppState.Engine.BackupOf("gpu_visualfx")?.Reg.FirstOrDefault(r => r.Name == "UserPreferencesMask");
        bool animations = saved is { Exists: true } ? Sys.Decode(RegistryValueKind.Binary, saved.Value) is byte[] b && b.Length > 4 && (b[4] & 0x02) != 0
                        : Native.ClientAreaAnimation ?? true;
        if (animations) mask[4] |= 0x02;
        return mask;
    }

    public static List<Tweak> Build(SystemInfo pc)
    {
        bool laptop = pc.IsLaptop, win11 = pc.Build >= 22000;
        string gpu = pc.Gpu?.Name ?? "graphics card";
        string acNote = laptop ? " On this laptop it only changes behaviour while plugged in - battery life is untouched." : "";
        double ramGb = pc.RamBytes / 1073741824d;
        bool plentyOfRam = ramGb >= 24;   // 32 GB class: memory compression is no longer worth its CPU cost
        var l = new List<Tweak>();

        // ------------------------------------------------------------ POWER & CPU
        l.Add(new Tweak
        {
            Id = "pwr_plan", Cat = "Power & CPU", Name = "Atomic Ultimate Performance Plan", Impact = "High", Rec = !laptop, Tags = ["Power Hungry"],
            Desc = "Creates and activates a dedicated Ultimate Performance power plan so the CPU never waits to ramp up its clocks. Apply this first so the power tweaks below land on this plan."
                 + (laptop ? " Not recommended on a laptop: it also applies on battery and raises heat. Use Best Performance Power Mode instead." : ""),
            Kind = "POWER", Note = "POWER  duplicate Ultimate Performance as \"Atomic Ultimate Performance\" and activate it",
            Test = () => Power.ActiveScheme() == new Guid(OF_PLAN),
            Apply = () =>
            {
                var prev = Power.ActiveScheme();
                if (!Sys.Run("powercfg.exe", "/list").Contains(OF_PLAN, StringComparison.OrdinalIgnoreCase))
                {
                    Powercfg($"/duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 {OF_PLAN}");
                    if (!Sys.Run("powercfg.exe", "/list").Contains(OF_PLAN, StringComparison.OrdinalIgnoreCase))
                        Powercfg($"/duplicatescheme 8c5e7fda-e8bf-4a96-9a85-cf48a8ea3d2b {OF_PLAN}");
                    Powercfg($"/changename {OF_PLAN} \"Atomic Ultimate Performance\" \"Maximum performance plan created by Atomic\"");
                }
                Powercfg($"/setactive {OF_PLAN}");
                return prev.ToString();
            },
            Revert = c =>
            {
                var prev = Guid.TryParse(c, out var g) && g != new Guid(OF_PLAN) && g != Guid.Empty ? g.ToString() : "381b4222-f694-41f0-9685-ff5bb260df2e";
                Powercfg($"/setactive {prev}");
                Powercfg($"/delete {OF_PLAN}");
            }
        });

        if (laptop) l.Add(new Tweak
        {
            Id = "lap_powermode", Cat = "Power & CPU", Name = "Best Performance Power Mode", Impact = "High", Rec = true, Tags = ["Laptop", "Power Hungry"],
            Desc = "Sets the Windows power mode slider to Best Performance, lifting the CPU and GPU power limits laptops apply in Balanced mode. The biggest single gain on a laptop - play plugged in.",
            Kind = "POWER", Note = "POWER  power mode overlay = Best Performance",
            Test = () => Power.GetOverlay() is Guid g ? g == Power.OverlayBestPerformance : null,
            Apply = () => { var prev = Power.GetOverlay(); Power.SetOverlay(Power.OverlayBestPerformance); return prev?.ToString(); },
            Revert = c => Power.SetOverlay(Guid.TryParse(c, out var g) && g != Power.OverlayBestPerformance ? g : Guid.Empty)
        });

        l.Add(new Tweak
        {
            Id = "pwr_coreparking", Cat = "Power & CPU", Name = "Disable CPU Core Parking", Impact = "Medium", Rec = true, Tags = ["Power Hungry"],
            Desc = $"Keeps all {Math.Max(pc.CpuCores, 1)} cores awake. A parked core that has to wake up in the middle of a frame causes micro-stutter.{acNote}",
            Pow = [new(SUB_PROC, "0cc5b647-c1df-4637-891a-dec35c318583", 100, 10)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_minstate", Cat = "Power & CPU", Name = "Minimum Processor State 100%", Impact = "Medium", Rec = !laptop, Tags = ["Power Hungry"],
            Desc = "Prevents Windows from down-clocking the CPU between frames, which smooths frame times in CPU-heavy games."
                 + (laptop ? " Optional on a laptop: it raises idle temperature and fan noise while plugged in." : ""),
            Pow = [new(SUB_PROC, "893dee8e-2bef-41e0-89c6-b55d0929964c", 100, 5)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_boost", Cat = "Power & CPU", Name = "Aggressive Turbo Boost", Impact = "Low", Rec = true, Tags = ["Power Hungry"],
            Desc = $"Sets the processor boost mode to Aggressive so the CPU jumps to its turbo frequency immediately instead of easing into it.{acNote}",
            Pow = [new(SUB_PROC, "be337238-0d82-4146-a960-4f3749d470c7", 2, 1)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_epp", Cat = "Power & CPU", Name = "Performance-Biased Energy Preference (EPP)", Impact = "Medium", Rec = true, Tags = ["Power Hungry"],
            Desc = $"Tells modern CPUs (Intel Speed Shift, AMD CPPC) to favour speed over power saving when choosing a frequency, so clocks rise faster under load.{acNote}",
            Pow = [new(SUB_PROC, "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0, 33)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_cooling", Cat = "Power & CPU", Name = "Active Cooling Policy", Impact = "Low", Rec = true,
            Desc = $"Makes Windows speed up the fans before it slows the processor down when things get hot, instead of throttling first.{acNote}",
            Pow = [new(SUB_PROC, "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 1, 1)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_usb", Cat = "Power & CPU", Name = "Disable USB Selective Suspend", Impact = "Low", Rec = true,
            Desc = $"Stops Windows from putting USB ports to sleep. Prevents mouse, keyboard and headset dropouts or first-input lag after they idle.{acNote}",
            Pow = [new(SUB_USB, "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_aspm", Cat = "Power & CPU", Name = "Disable PCIe Link State Power Saving", Impact = "Medium", Rec = true, Tags = ["Power Hungry"],
            Desc = $"Keeps the PCIe link to the {gpu} and NVMe drives at full power. Power-saving link states add wake-up latency each time a device idles for a moment.{acNote}",
            Pow = [new(SUB_PCIE, "ee12f906-d277-404b-b6da-e5fa1a576df5", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_diskidle", Cat = "Power & CPU", Name = "Never Spin Down Hard Drives", Impact = "Medium", Rec = pc.HasHDD && !laptop, Tags = ["HDD"],
            Desc = pc.HasHDD ? "A hard drive was detected. When a sleeping HDD spins back up you get a 2-5 second freeze while a game loads assets. This keeps drives spinning."
                             : "Prevents Windows from spinning down hard drives. No hard drive was detected in this PC, so this is optional.",
            Pow = [new(SUB_DISK, "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0, 1200)]
        });
        l.Add(new Tweak
        {
            Id = "pwr_hibernate", Cat = "Power & CPU", Name = "Disable Hibernation & Fast Startup", Impact = "Medium", Rec = !laptop, Tags = ["Reboot"],
            Desc = $"Deletes hiberfil.sys (about {Math.Max(1, ramGb * 0.4):N0} GB on this PC) and turns off Fast Startup, which keeps drivers in a half-saved state between boots and is a common cause of stutter and driver glitches."
                 + (laptop ? " Not recommended on a laptop: hibernation protects your work when the battery runs out." : ""),
            Kind = "SYSTEM", Note = "CMD  powercfg /hibernate off",
            Test = () => Sys.RegInt(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") == 0,
            Apply = () => { Powercfg("/hibernate off"); return null; },
            Revert = _ => Powercfg("/hibernate on")
        });
        l.Add(new Tweak
        {
            Id = "fps_powerthrottle", Cat = "Power & CPU", Name = "Disable Windows Power Throttling", Impact = "Medium", Rec = !laptop, Tags = ["Power Hungry", "Reboot"],
            Desc = "Power Throttling slows background processes to save energy - but Windows sometimes classifies game helpers, voice chat or overlays as background. This turns it off system-wide."
                 + (laptop ? " Optional on a laptop: it shortens battery life." : ""),
            Reg = [D(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1)]
        });

        // ------------------------------------------------------------ FPS & LATENCY
        l.Add(new Tweak
        {
            Id = "fps_gamemode", Cat = "FPS & Latency", Name = "Enable Windows Game Mode", Impact = "High", Rec = true,
            Desc = "Game Mode stops Windows Update from installing drivers and showing restart prompts while you play, and gives the game priority over background work.",
            Reg = [D(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1), D(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1)],
            // Windows treats missing values as "on", so only an explicit 0 means Game Mode is off
            Test = () => Sys.RegInt(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled") != 0 && Sys.RegInt(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode") != 0
        });
        l.Add(new Tweak
        {
            Id = "fps_gamedvr", Cat = "FPS & Latency", Name = "Disable Game DVR Background Recording", Impact = "High", Rec = true,
            Desc = "Background recording constantly encodes your gameplay, which costs real FPS - most of all on entry-level GPUs and CPUs with few cores.",
            Reg = [D(GCS, "GameDVR_Enabled", 0, 1), D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0)]
        });
        l.Add(new Tweak
        {
            Id = "fps_gamebar", Cat = "FPS & Latency", Name = "Disable Xbox Game Bar Overlay", Impact = "Medium", Rec = true, Tags = ["Feature Breaking"],
            Desc = "Stops the Win+G overlay from hooking into every game. Saves RAM and removes an overlay layer. You lose the Game Bar widgets (use your GPU vendor's or Steam's overlay instead).",
            Reg = [D(@"HKCU\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0), D(@"HKCU\Software\Microsoft\GameBar", "ShowStartupPanel", 0)]
        });
        l.Add(new Tweak
        {
            Id = "fps_hpet", Cat = "FPS & Latency", Name = "Ensure HPET Is Not Forced", Impact = "Medium", Rec = true, Tags = ["Reboot"],
            Desc = "Some \"optimizers\" force the slow HPET timer as the only clock source, which adds overhead to every timer call. This removes that override so Windows uses the faster TSC timer.",
            Kind = "BOOT", Note = "CMD  bcdedit /deletevalue useplatformclock",
            Test = () => !BcdHas("useplatformclock"),
            Apply = () => { bool was = BcdHas("useplatformclock"); Bcdedit("/deletevalue useplatformclock"); return was ? "1" : "0"; },
            Revert = c => { if (c == "1") Bcdedit("/set useplatformclock true"); }
        });
        l.Add(new Tweak
        {
            Id = "fps_dyntick", Cat = "FPS & Latency", Name = "Disable Dynamic Tick", Impact = "Low", Risk = "Moderate", Tags = ["Power Hungry", "Reboot"],
            Desc = "Keeps the system timer ticking at a constant rate instead of pausing it to save power. Can make input and frame pacing slightly more consistent."
                 + (laptop ? " Avoid on a laptop: it prevents deep idle states and drains the battery." : " Raises idle power slightly."),
            Kind = "BOOT", Note = "CMD  bcdedit /set disabledynamictick yes",
            Test = () => BcdHas("disabledynamictick"),
            Apply = () => { Bcdedit("/set disabledynamictick yes"); return null; },
            Revert = _ => Bcdedit("/deletevalue disabledynamictick")
        });
        l.Add(new Tweak
        {
            Id = "fps_fso", Cat = "FPS & Latency", Name = "Disable Fullscreen Optimizations (Global)", Impact = "Low", Risk = "Moderate",
            Desc = "Forces classic exclusive fullscreen in older DirectX 9/11 games, which can lower input latency. On Windows 11 the default (flip model) is often just as good - test with your games.",
            Reg = [D(GCS, "GameDVR_FSEBehaviorMode", 2, 2), D(GCS, "GameDVR_HonorUserFSEBehaviorMode", 1, 0), D(GCS, "GameDVR_FSEBehavior", 2), D(GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 1, 0)]
        });
        l.Add(new Tweak
        {
            Id = "fps_mouse", Cat = "FPS & Latency", Name = "Disable Mouse Acceleration", Impact = "Medium", Rec = true, Tags = ["Sign-out"],
            Desc = "Turns off \"Enhance pointer precision\" so mouse movement is 1:1. Your aim becomes consistent because the same hand movement always turns the same distance.",
            Reg = [S(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0", "1"), S(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0", "6"), S(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0", "10")]
        });
        l.Add(new Tweak
        {
            Id = "fps_keyboard", Cat = "FPS & Latency", Name = "Fastest Keyboard Repeat Response", Impact = "Low", Rec = true, Tags = ["Sign-out"],
            Desc = "Sets the shortest key-repeat delay and the fastest repeat rate so held keys respond immediately.",
            Reg = [S(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "0", "1"), S(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31", "31")]
        });
        l.Add(new Tweak
        {
            Id = "fps_bgapps", Cat = "FPS & Latency", Name = "Stop Background Apps", Impact = "Medium", Rec = true, Tags = ["Feature Breaking"],
            Desc = $"Prevents Store apps (Weather, Mail, Phone Link, etc.) from running in the background and stealing CPU time from your {pc.CpuThreads} threads. Some of those apps will stop sending live notifications.",
            Reg = [D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2)]
        });
        l.Add(new Tweak
        {
            Id = "fps_edge", Cat = "FPS & Latency", Name = "Stop Edge Startup Boost & Background Mode", Impact = "Medium", Rec = true,
            Desc = "Microsoft Edge preloads itself at boot and keeps running after you close it. This stops both, freeing RAM and CPU. (Edge will show \"managed by your organization\" - that is normal for this policy.)",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0), D(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0)]
        });
        l.Add(new Tweak
        {
            Id = "fps_noreboot", Cat = "FPS & Latency", Name = "No Forced Update Restarts While Signed In", Impact = "Low", Rec = true,
            Desc = "Stops Windows Update from restarting the PC automatically while you are logged in - no more surprise restarts in the middle of a match.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1)]
        });

        // ------------------------------------------------------------ GPU & DISPLAY
        l.Add(new Tweak
        {
            Id = "gpu_hags", Cat = "GPU & Display", Name = "Hardware-Accelerated GPU Scheduling", Impact = "Medium", Rec = true, Tags = ["Reboot"],
            Desc = $"Lets the {gpu} manage its own video memory scheduling instead of the CPU. Frees CPU time and can lower latency; also required for frame generation. Needs a recent GPU driver.",
            Reg = [D(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1)]
        });
        l.Add(new Tweak
        {
            Id = "gpu_mpo", Cat = "GPU & Display", Name = "Disable Multiplane Overlay (MPO)", Impact = "Medium", Rec = pc.GpuVendor == "NVIDIA", Tags = ["Reboot"],
            Desc = "MPO is a known cause of flickering, black screens and stutter in borderless/windowed games, especially on NVIDIA cards. NVIDIA itself has published this fix. Safe to undo.",
            Reg = [D(@"HKLM\SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5)]
        });
        if (win11) l.Add(new Tweak
        {
            Id = "gpu_winopt", Cat = "GPU & Display", Name = "Optimizations for Windowed Games", Impact = "Medium", Rec = true, Tags = ["Win11"],
            Desc = "Upgrades older DirectX 10/11 games running in borderless or windowed mode to the modern flip presentation model - lower latency, close to exclusive fullscreen.",
            Reg = [S(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;")]
        });
        var msiGpus = pc.Gpus.Where(g => g.Vendor is "NVIDIA" or "AMD" && g.Pnp.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)).ToList();
        if (msiGpus.Count > 0) l.Add(new Tweak
        {
            Id = "gpu_msi", Cat = "GPU & Display", Name = "Enable MSI Mode for the GPU", Impact = "Low", Risk = "Moderate", Tags = ["Reboot"],
            Desc = $"Switches the {msiGpus[0].Name} from legacy line-based interrupts to Message Signaled Interrupts, which lowers interrupt latency and avoids IRQ sharing. Re-apply after a clean driver install.",
            RegFn = () => msiGpus.Select(g => D($@"HKLM\SYSTEM\CurrentControlSet\Enum\{g.Pnp}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", "MSISupported", 1, 0))
        });
        l.Add(new Tweak
        {
            Id = "gpu_transparency", Cat = "GPU & Display", Name = "Disable Transparency Effects", Impact = "Medium", Rec = true,
            Desc = "Removes the blur/acrylic effects from the taskbar, Start and windows. Less work for the Desktop Window Manager, especially in borderless games and on integrated graphics.",
            Reg = [D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "gpu_visualfx", Cat = "GPU & Display", Name = "Visual Effects: Best Performance", Impact = "Medium", Rec = true, Tags = ["Sign-out"],
            Desc = "Turns off window animations, fades, shadows and Aero Peek while keeping smooth fonts and thumbnails. Windows feels snappier and the GPU does less desktop work. The Windows \"Animation effects\" switch is left as you had it, so browsers and other apps keep their own animations.",
            RegFn = () =>
            [
                D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3),
                new(@"HKCU\Control Panel\Desktop", "UserPreferencesMask", RegistryValueKind.Binary, VisualFxMask(), new byte[] { 0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00 }),
                S(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1"),
                D(ADV, "TaskbarAnimations", 0, 1), D(ADV, "ListviewAlphaSelect", 0, 1), D(ADV, "ListviewShadow", 0, 1),
                D(@"HKCU\Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0, 1)
            ],
            // the mask also holds "Animation effects": make Windows and the running apps see the kept value now, not after a sign-out
            Apply = () => { Native.SetClientAreaAnimation((VisualFxMask()[4] & 0x02) != 0); return null; },
            Revert = _ =>
            {
                if (Sys.RegGet(@"HKCU\Control Panel\Desktop", "UserPreferencesMask").Value is byte[] m && m.Length > 4)
                    Native.SetClientAreaAnimation((m[4] & 0x02) != 0);
            }
        });
        if (pc.Gpus.Any(g => g.Vendor == "NVIDIA")) l.Add(new Tweak
        {
            Id = "gpu_nvtelemetry", Cat = "GPU & Display", Name = "Disable NVIDIA Telemetry", Impact = "Low", Rec = true,
            Desc = "Stops the NVIDIA telemetry container and its scheduled reporting tasks. Your driver, control panel and game profiles keep working normally.",
            Svc = [new("NvTelemetryContainer", "disabled", "auto")],
            Tasks = [@"\NvTmRep*", @"\NvTmMon*"]
        });
        l.Add(new Tweak
        {
            Id = "gpu_wudrivers", Cat = "GPU & Display", Name = "Stop Windows Update Replacing Drivers", Impact = "Low", Rec = true,
            Desc = "Prevents Windows Update from silently swapping your GPU driver for an older generic one (a common cause of sudden FPS drops). Update drivers from the manufacturer instead.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1)]
        });

        // ------------------------------------------------------------ REGISTRY (scheduler / kernel)
        l.Add(new Tweak
        {
            Id = "reg_mmcss_games", Cat = "Registry", Name = "Prioritize Games in the Multimedia Scheduler", Impact = "High", Rec = true,
            Desc = "Raises the MMCSS \"Games\" task profile to High scheduling category, CPU priority 6 and GPU priority 8, so game threads registered with MMCSS win against background work.",
            Reg = [D($@"{MMCSS}\Tasks\Games", "GPU Priority", 8, 8), D($@"{MMCSS}\Tasks\Games", "Priority", 6, 2), S($@"{MMCSS}\Tasks\Games", "Scheduling Category", "High", "Medium"), S($@"{MMCSS}\Tasks\Games", "SFIO Priority", "High", "Normal")]
        });
        l.Add(new Tweak
        {
            Id = "reg_sysresp", Cat = "Registry", Name = "System Responsiveness for Gaming", Impact = "Medium", Rec = true,
            Desc = "Lowers the CPU share Windows reserves for background tasks during multimedia/gaming from 20% to the minimum of 10%, giving more of the CPU to the game.",
            Reg = [D(MMCSS, "SystemResponsiveness", 10, 20)]
        });
        l.Add(new Tweak
        {
            Id = "reg_priosep", Cat = "Registry", Name = "Foreground Priority Boost (Win32PrioritySeparation)", Impact = "Medium", Rec = true,
            Desc = "Sets short, variable CPU time slices with maximum boost for the focused window (value 0x26). The game you are playing gets the CPU first.",
            Reg = [D(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, 2)]
        });
        l.Add(new Tweak
        {
            Id = "reg_timerres", Cat = "Registry", Name = "Global Timer Resolution Requests", Impact = "Medium", Rec = !laptop, Tags = ["Reboot"],
            Desc = "Windows 11 ignores high-resolution timer requests from minimized or background processes. This restores the classic global behaviour so games and frame limiters get precise 0.5-1 ms timers."
                 + (laptop ? " Optional on a laptop: a constantly raised timer costs battery life." : ""),
            Reg = [D(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1)]
        });
        l.Add(new Tweak
        {
            Id = "reg_pagingexec", Cat = "Registry", Name = "Keep Kernel & Drivers in RAM", Impact = "Low", Rec = ramGb >= 15, Tags = ["Reboot"],
            Desc = $"With {ramGb:N0} GB of RAM {(ramGb >= 15 ? "there is no reason to page kernel code and drivers to disk" : "this is optional - it uses a little more memory")}. Setting DisablePagingExecutive keeps them in memory, which helps most when Windows is on a hard drive.",
            Reg = [D(MEMMGMT, "DisablePagingExecutive", 1, 0)]
        });
        l.Add(new Tweak
        {
            Id = "reg_svchost", Cat = "Registry", Name = "Group Service Host Processes", Impact = "Low", Rec = ramGb >= 15, Tags = ["Reboot"],
            Desc = "Sets SvcHostSplitThresholdInKB to your installed RAM so Windows groups services into fewer svchost.exe processes - dozens fewer processes and less scheduler overhead.",
            RegFn = () => [D(@"HKLM\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", (int)Math.Min(int.MaxValue, Math.Max(3670016, pc.RamBytes / 1024)), 3670016)]
        });
        l.Add(new Tweak
        {
            Id = "reg_menudelay", Cat = "Registry", Name = "Instant Menus", Impact = "Low", Rec = true,
            Desc = "Removes the 400 ms delay before context sub-menus open (MenuShowDelay = 0). The desktop feels instantly responsive.",
            Reg = [S(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "0", "400")]
        });
        l.Add(new Tweak
        {
            Id = "reg_startupdelay", Cat = "Registry", Name = "Remove Startup App Delay", Impact = "Low", Rec = true,
            Desc = "Windows deliberately waits before launching startup apps. This removes that artificial delay so the desktop is usable sooner after boot.",
            Reg = [D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0)]
        });
        l.Add(new Tweak
        {
            Id = "reg_shutdown", Cat = "Registry", Name = "Faster Shutdown & Hung-App Detection", Impact = "Low", Rec = true,
            Desc = "Shortens how long Windows waits for frozen apps and services when shutting down or restarting. Apps still get a chance to save.",
            Reg = [S(@"HKCU\Control Panel\Desktop", "HungAppTimeout", "2000"), S(@"HKCU\Control Panel\Desktop", "WaitToKillAppTimeout", "4000"), S(@"HKLM\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", "5000")]
        });

        // ------------------------------------------------------------ MEMORY & STORAGE
        l.Add(new Tweak
        {
            Id = "mem_compression", Cat = "Memory & Storage", Name = "Disable Memory Compression", Impact = "Medium", Rec = plentyOfRam, Tags = ["Reboot"],
            Desc = plentyOfRam ? $"Memory compression trades CPU time for RAM. With {ramGb:N0} GB you rarely need it - turning it off returns those CPU cycles to your games."
                               : $"Memory compression trades CPU time for RAM. With {ramGb:N0} GB it keeps you out of the much slower page file when memory fills up, so leave it ON.",
            Kind = "SYSTEM", Note = "CMD  Disable-MMAgent -MemoryCompression",
            Test = () => pc.MemCompression is bool on ? !on : null,
            Apply = () => { Sys.PS("Disable-MMAgent -MemoryCompression"); return null; },
            Revert = _ => Sys.PS("Enable-MMAgent -MemoryCompression")
        });
        l.Add(new Tweak
        {
            Id = "mem_sysmain", Cat = "Memory & Storage", Name = "Disable SysMain (Superfetch)", Impact = "Medium", Rec = pc.SystemIsSSD && plentyOfRam, Tags = ["SSD"],
            Desc = "SysMain preloads apps into RAM. On an SSD it mostly causes background disk and CPU activity. It also runs memory compression, so disabling it switches that off too. "
                 + (pc.SystemIsHDD ? "Detected: Windows is on a hard drive - keep SysMain ON."
                    : !plentyOfRam ? $"With {ramGb:N0} GB of RAM keep SysMain ON so memory compression stays available."
                    : pc.SystemIsSSD ? "Detected: Windows is on an SSD with plenty of RAM - recommended." : "Keep it ON if Windows is on a hard drive."),
            Svc = [new("SysMain", "disabled", "auto")]
        });
        l.Add(new Tweak
        {
            Id = "mem_search", Cat = "Memory & Storage", Name = "Disable Search Indexing", Impact = "Medium", Tags = ["Feature Breaking"],
            Desc = "The indexer crawls your files in the background, causing disk thrashing on hard drives. Start-menu app search still works, but searching file contents becomes slower.",
            Svc = [new("WSearch", "disabled", "delayed-auto")]
        });
        l.Add(new Tweak
        {
            Id = "sto_lastaccess", Cat = "Memory & Storage", Name = "Disable NTFS Last-Access Timestamps", Impact = "Low", Rec = true,
            Desc = "Stops NTFS from writing a timestamp every time a file is read. Games read thousands of files - this removes those extra disk writes.",
            Reg = [D(FS, "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001), unchecked((int)0x80000002))],
            Test = () => Sys.RegInt(FS, "NtfsDisableLastAccessUpdate") is int v && (v & 1) == 1
        });
        l.Add(new Tweak
        {
            Id = "sto_8dot3", Cat = "Memory & Storage", Name = "Disable 8.3 Short File Names", Impact = "Low", Rec = true,
            Desc = "Stops NTFS creating legacy DOS-style \"PROGRA~1\" names for every new file, which speeds up file creation in folders with many files (game installs, shader caches).",
            Reg = [D(FS, "NtfsDisable8dot3NameCreation", 1, 2)]
        });

        // ------------------------------------------------------------ NETWORK & PING
        l.Add(new Tweak
        {
            Id = "net_nagle", Cat = "Network & Ping", Name = "Disable Nagle's Algorithm", Impact = "Medium", Rec = true,
            Desc = "Sends small TCP packets immediately instead of bundling them. Lowers latency in TCP-based online games (MMOs, League, many shooters' lobbies).",
            RegFn = () =>
            {
                const string ifs = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
                var res = new List<RegV>();
                foreach (var id in Sys.RegSubKeys(ifs))
                {
                    var ip = (Sys.RegStr($@"{ifs}\{id}", "DhcpIPAddress") + " " + Sys.RegStr($@"{ifs}\{id}", "IPAddress")).Trim();
                    if (ip.Length == 0 || ip.Replace("0.0.0.0", "").Trim().Length == 0) continue;
                    res.Add(D($@"{ifs}\{id}", "TcpAckFrequency", 1));
                    res.Add(D($@"{ifs}\{id}", "TCPNoDelay", 1));
                }
                return res;
            }
        });
        l.Add(new Tweak
        {
            Id = "net_throttle", Cat = "Network & Ping", Name = "Disable Network Throttling", Impact = "Low", Rec = true,
            Desc = "Windows throttles non-multimedia network traffic while media plays. Setting NetworkThrottlingIndex to off removes that cap.",
            Reg = [D(MMCSS, "NetworkThrottlingIndex", -1, 10)]
        });
        l.Add(new Tweak
        {
            Id = "net_nicpower", Cat = "Network & Ping", Name = "Disable Network Adapter Power Saving", Impact = "Low", Rec = !laptop,
            Desc = "Stops Windows from turning off your network adapter to save power, which can cause lag spikes and brief disconnects."
                 + (laptop ? " Optional on a laptop: it slightly shortens standby battery life." : ""),
            Kind = "SYSTEM", Note = "NET  \"Allow the computer to turn off this device\" = off on physical adapters",
            Test = () =>
            {
                var sup = pc.Nics.Where(n => n.Pm is "Enabled" or "Disabled").ToList();
                return sup.Count == 0 ? null : sup.All(n => n.Pm == "Disabled");
            },
            Apply = () => { Sys.PS(NicPowerScript("Disabled")); return null; },
            Revert = _ => Sys.PS(NicPowerScript("Enabled"))
        });
        l.Add(new Tweak
        {
            Id = "net_wifi", Cat = "Network & Ping", Name = "Wi-Fi Adapter Maximum Performance", Impact = "Medium", Rec = true,
            Desc = $"Stops the wireless adapter from napping between packets (802.11 power save), which adds random latency spikes on Wi-Fi. No effect on wired connections.{acNote}",
            Pow = [new(SUB_WIFI, "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0, 0)]
        });
        l.Add(new Tweak
        {
            Id = "net_dosvc", Cat = "Network & Ping", Name = "Stop Peer-to-Peer Update Uploads", Impact = "Medium", Rec = true,
            Desc = "Delivery Optimization uploads Windows updates to other PCs over your internet connection in the background, which can spike your ping. Updates still download normally.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)]
        });
        l.Add(new Tweak
        {
            Id = "net_dns", Cat = "Network & Ping", Name = "Use Cloudflare DNS (1.1.1.1)", Impact = "Low",
            Desc = "Switches active adapters to Cloudflare's fast DNS resolvers. Speeds up server lookups and launcher logins; does not change in-game ping. Reverting restores your previous DNS settings.",
            Kind = "NETWORK", Note = "NET  IPv4 DNS = 1.1.1.1, 1.0.0.1 on connected adapters",
            Test = () => ActiveNics().Any(n => n.GetIPProperties().DnsAddresses.Any(a => a.ToString() == "1.1.1.1")),
            Apply = () =>
            {
                // remember each adapter's static DNS ("" = automatic) so revert can put it back
                var saved = ActiveNics().ToDictionary(n => n.Id, n => Sys.RegStr($@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{n.Id}", "NameServer"));
                Sys.PS("Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses '1.1.1.1','1.0.0.1' }; Clear-DnsClientCache");
                return JsonSerializer.Serialize(saved);
            },
            Revert = c =>
            {
                Dictionary<string, string>? saved = null;
                try { if (c != null) saved = JsonSerializer.Deserialize<Dictionary<string, string>>(c); } catch { }
                var sb = new System.Text.StringBuilder();
                if (saved is { Count: > 0 })
                    foreach (var (id, dns) in saved)
                    {
                        var servers = string.Join(",", dns.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(s => $"'{s}'"));
                        sb.AppendLine(servers.Length == 0
                            ? $"Get-NetAdapter | Where-Object InterfaceGuid -eq '{id}' | Set-DnsClientServerAddress -ResetServerAddresses"
                            : $"Get-NetAdapter | Where-Object InterfaceGuid -eq '{id}' | Set-DnsClientServerAddress -ServerAddresses {servers}");
                    }
                else sb.AppendLine("Get-NetAdapter -Physical | Set-DnsClientServerAddress -ResetServerAddresses");
                sb.AppendLine("Clear-DnsClientCache");
                Sys.PS(sb.ToString());
            }
        });

        // ------------------------------------------------------------ DEBLOAT
        l.Add(new Tweak
        {
            Id = "dbl_copilot", Cat = "Debloat", Name = "Disable Copilot", Impact = "Low", Rec = true,
            Desc = "Turns off Windows Copilot and hides its taskbar button so it never loads in the background.",
            Reg = [D(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1), D(ADV, "ShowCopilotButton", 0)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_widgets", Cat = "Debloat", Name = "Disable Widgets / News & Interests", Impact = "Medium", Rec = true,
            Desc = "Removes the Widgets board (Windows 11) and News & Interests (Windows 10). They run a hidden Edge WebView in the background that uses 100-300 MB of RAM and CPU. "
                 + "Recent Windows 11 builds write-protect this policy; if Windows blocks it, switch Widgets off in Settings > Personalization > Taskbar instead.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_bing", Cat = "Debloat", Name = "Remove Bing Web Results from Start Search", Impact = "Low", Rec = true,
            Desc = "Start-menu search stays local - faster results and no web queries sent to Bing every time you type.",
            Reg = [D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0), D(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_autoinstall", Cat = "Debloat", Name = "Block Auto-Installed Sponsored Apps", Impact = "Medium", Rec = true,
            Desc = "Stops Windows from silently installing promoted apps and games (Candy Crush, TikTok, etc.) after updates.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1), D(CDM, "SilentInstalledAppsEnabled", 0, 1), D(CDM, "PreInstalledAppsEnabled", 0, 1), D(CDM, "OemPreInstalledAppsEnabled", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_startads", Cat = "Debloat", Name = "Remove Start Menu Suggestions & Tips", Impact = "Low", Rec = true,
            Desc = "Removes promoted apps in Start, \"tips & tricks\" pop-ups and the post-update welcome experience.",
            Reg = [D(CDM, "SystemPaneSuggestionsEnabled", 0, 1), D(CDM, "SubscribedContent-338388Enabled", 0, 1), D(CDM, "SubscribedContent-338389Enabled", 0, 1), D(CDM, "SubscribedContent-310093Enabled", 0, 1), D(CDM, "SoftLandingEnabled", 0, 1), D(ADV, "Start_IrisRecommendations", 0)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_settingsads", Cat = "Debloat", Name = "Remove Windows Settings Ads", Impact = "Low", Rec = true,
            Desc = "Removes promotional suggestions and ads from the Windows Settings app.",
            Reg = [D(CDM, "SubscribedContent-338393Enabled", 0, 1), D(CDM, "SubscribedContent-353694Enabled", 0, 1), D(CDM, "SubscribedContent-353696Enabled", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_lockads", Cat = "Debloat", Name = "Remove Lock Screen Ads & Fun Facts", Impact = "Low", Rec = true,
            Desc = "Removes \"fun facts\", tips and promotions printed on top of the lock-screen picture.",
            Reg = [D(CDM, "RotatingLockScreenOverlayEnabled", 0, 1), D(CDM, "SubscribedContent-338387Enabled", 0, 1)]
        });
        if (win11) l.Add(new Tweak
        {
            Id = "dbl_recall", Cat = "Debloat", Name = "Disable Recall Snapshots", Impact = "Low", Rec = true, Tags = ["Win11"],
            Desc = "Blocks the Windows Recall feature from taking screenshots of your activity (where present).",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1), D(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_onedrive", Cat = "Debloat", Name = "Prevent OneDrive from Running", Impact = "Medium", Tags = ["Feature Breaking"],
            Desc = "Stops OneDrive from starting and syncing in the background (it can saturate your disk and upload bandwidth). Only use this if you don't rely on OneDrive.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1)]
        });
        l.Add(new Tweak
        {
            Id = "dbl_apps_news", Cat = "Debloat", Name = "Remove News, Weather, Maps & Tips Apps", Impact = "Low", Rec = true, Tags = ["Not Reversible"],
            Desc = "Uninstalls Microsoft News, Weather, Maps, Get Started/Tips, Get Help and Feedback Hub. Any of them can be reinstalled from the Microsoft Store.",
            Appx = ["Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.WindowsMaps", "Microsoft.Getstarted", "Microsoft.GetHelp", "Microsoft.WindowsFeedbackHub"]
        });
        l.Add(new Tweak
        {
            Id = "dbl_apps_office", Cat = "Debloat", Name = "Remove Office Hub, To Do, Clipchamp & Teams", Impact = "Low", Tags = ["Not Reversible"],
            Desc = "Uninstalls the Microsoft 365 hub, To Do, Power Automate, Clipchamp, Teams (personal), Skype, People and the Copilot app. Reinstall from the Store if needed. Skip this if you use any of them.",
            Appx = ["Microsoft.MicrosoftOfficeHub", "Microsoft.Todos", "Microsoft.PowerAutomateDesktop", "Clipchamp.Clipchamp", "MicrosoftTeams", "MSTeams", "Microsoft.SkypeApp", "Microsoft.People", "Microsoft.Copilot"]
        });
        l.Add(new Tweak
        {
            Id = "dbl_apps_sponsored", Cat = "Debloat", Name = "Remove Sponsored Games & Apps", Impact = "Low", Rec = true, Tags = ["Not Reversible"],
            Desc = "Uninstalls pre-installed promos such as Candy Crush, Solitaire Collection, TikTok, Facebook, Instagram, Disney+ and Prime Video. Your own Steam / Xbox games are not touched.",
            Appx = ["king.com.*", "*CandyCrush*", "Microsoft.MicrosoftSolitaireCollection", "BytedancePte.Ltd.TikTok", "Facebook.*", "Disney.*", "AmazonVideo.PrimeVideo"]
        });
        l.Add(new Tweak
        {
            Id = "dbl_apps_legacy", Cat = "Debloat", Name = "Remove Cortana, 3D Viewer & Mixed Reality", Impact = "Low", Rec = true, Tags = ["Not Reversible"],
            Desc = "Uninstalls Cortana, 3D Viewer, Mixed Reality Portal and Microsoft Wallet - leftovers almost nobody uses.",
            Appx = ["Microsoft.549981C3F5F10", "Microsoft.Microsoft3DViewer", "Microsoft.MixedReality.Portal", "Microsoft.Wallet"]
        });
        l.Add(new Tweak
        {
            Id = "dbl_services", Cat = "Debloat", Name = "Disable Unused Services", Impact = "Low", Rec = true,
            Desc = "Disables Fax, Retail Demo, Downloaded Maps Manager, Remote Registry, Windows Media Player network sharing and the Insider service - none are needed on a home PC.",
            Svc = [new("Fax", "disabled", "demand"), new("RetailDemo", "disabled", "demand"), new("MapsBroker", "disabled", "delayed-auto"), new("RemoteRegistry", "disabled", "disabled"), new("WMPNetworkSvc", "disabled", "demand"), new("wisvc", "disabled", "demand")]
        });
        l.Add(new Tweak
        {
            Id = "dbl_xbox", Cat = "Debloat", Name = "Disable Xbox Live Services", Impact = "Low", Tags = ["Feature Breaking"],
            Desc = "Turns off Xbox Live auth, cloud saves and networking services. Only apply this if you do NOT use the Xbox app, Game Pass or Microsoft Store games. Controllers keep working.",
            Svc = [new("XblAuthManager", "disabled", "demand"), new("XblGameSave", "disabled", "demand"), new("XboxNetApiSvc", "disabled", "demand")]
        });
        l.Add(new Tweak
        {
            Id = "dbl_spooler", Cat = "Debloat", Name = "Disable Print Spooler", Impact = "Low", Tags = ["Feature Breaking"],
            Desc = "If you have no printer, the Print Spooler is wasted RAM and a known attack surface. Printing (including \"Print to PDF\") stops working until reverted.",
            Svc = [new("Spooler", "disabled", "auto")]
        });

        // ------------------------------------------------------------ PRIVACY & TELEMETRY
        l.Add(new Tweak
        {
            Id = "prv_telemetry", Cat = "Privacy", Name = "Minimize Windows Telemetry", Impact = "Medium", Rec = true,
            Desc = "Sets diagnostic data to the lowest level your edition allows and stops feedback prompts. Less background data collection and uploading.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1), D(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0)]
        });
        l.Add(new Tweak
        {
            Id = "prv_diagtrack", Cat = "Privacy", Name = "Disable Telemetry Services (DiagTrack)", Impact = "Medium", Rec = true,
            Desc = "Disables the Connected User Experiences and Telemetry service and the WAP push service that feeds it. These periodically wake up, scan the system and upload data.",
            Svc = [new("DiagTrack", "disabled", "auto"), new("dmwappushservice", "disabled", "demand")]
        });
        l.Add(new Tweak
        {
            Id = "prv_tasks", Cat = "Privacy", Name = "Disable Telemetry Scheduled Tasks", Impact = "Medium", Rec = true,
            Desc = "Disables the Compatibility Appraiser, Customer Experience Improvement Program and disk-diagnostic tasks. The Appraiser in particular can spike CPU and disk for minutes - sometimes mid-game.",
            Tasks =
            [
                @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                @"\Microsoft\Windows\Autochk\Proxy",
                @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
                @"\Microsoft\Windows\Feedback\Siuf\DmClient",
                @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"
            ]
        });
        l.Add(new Tweak
        {
            Id = "prv_adid", Cat = "Privacy", Name = "Disable Advertising ID", Impact = "Low", Rec = true,
            Desc = "Stops apps from using your advertising ID to track you across apps for personalized ads.",
            Reg = [D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1)]
        });
        l.Add(new Tweak
        {
            Id = "prv_tailored", Cat = "Privacy", Name = "Disable Tailored Experiences", Impact = "Low", Rec = true,
            Desc = "Stops Microsoft using your diagnostic data to show personalized tips, ads and recommendations.",
            Reg = [D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0), D(@"HKCU\Software\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1)]
        });
        l.Add(new Tweak
        {
            Id = "prv_activity", Cat = "Privacy", Name = "Disable Activity History", Impact = "Low", Rec = true,
            Desc = "Stops Windows from recording and uploading a timeline of the apps and files you open.",
            Reg = [D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0), D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)]
        });
        l.Add(new Tweak
        {
            Id = "prv_wer", Cat = "Privacy", Name = "Disable Windows Error Reporting", Impact = "Low", Rec = true,
            Desc = "Stops crash-dump collection and upload after an app crashes - which otherwise eats CPU and disk right after a game crash.",
            Reg = [D(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)],
            Svc = [new("WerSvc", "disabled", "demand")]
        });
        l.Add(new Tweak
        {
            Id = "prv_inking", Cat = "Privacy", Name = "Disable Typing & Inking Data Collection", Impact = "Low", Rec = true,
            Desc = "Stops Windows collecting your typing and handwriting patterns and contacts to \"improve\" the keyboard dictionary.",
            Reg = [D(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, 0), D(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, 0), D(@"HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, 1), D(@"HKCU\Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "prv_remote", Cat = "Privacy", Name = "Disable Remote Assistance", Impact = "Low", Rec = true,
            Desc = "Closes the Remote Assistance invitation feature - a favourite of tech-support scammers. Remote Desktop and Quick Assist are not affected.",
            Reg = [D(@"HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "prv_location", Cat = "Privacy", Name = "Disable Location Services", Impact = "Low", Tags = ["Feature Breaking"],
            Desc = "Turns off location access for all apps. Weather, Maps and \"Find my device\" will no longer know where you are.",
            Reg = [S(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", "Allow")]
        });

        // ------------------------------------------------------------ QUALITY OF LIFE
        l.Add(new Tweak
        {
            Id = "qol_stickykeys", Cat = "Quality of Life", Name = "Disable Sticky / Filter / Toggle Keys Pop-ups", Impact = "Low", Rec = true,
            Desc = "Pressing Shift five times in a game will no longer minimize it to ask about Sticky Keys. The accessibility features themselves still work from Settings.",
            Reg = [S(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "506", "510"), S(@"HKCU\Control Panel\Accessibility\ToggleKeys", "Flags", "58", "62"), S(@"HKCU\Control Panel\Accessibility\Keyboard Response", "Flags", "122", "126")]
        });
        if (win11) l.Add(new Tweak
        {
            Id = "qol_endtask", Cat = "Quality of Life", Name = "\"End Task\" in Taskbar Right-Click", Impact = "Low", Rec = true, Tags = ["Win11"],
            Desc = "Adds End Task to the taskbar right-click menu so you can kill a frozen game without opening Task Manager.",
            Reg = [D($@"{ADV}\TaskbarDeveloperSettings", "TaskbarEndTask", 1)]
        });
        if (win11) l.Add(new Tweak
        {
            Id = "qol_classicmenu", Cat = "Quality of Life", Name = "Classic Right-Click Menu", Impact = "Low", Tags = ["Win11"],
            Desc = "Brings back the full Windows 10 style context menu, which also opens faster than the new one. Explorer restarts to apply.",
            Kind = "REGISTRY", Note = $"REG  {CLASSIC_MENU}\\InprocServer32 (Default) = \"\"",
            Test = () => Sys.RegKeyExists($@"{CLASSIC_MENU}\InprocServer32"),
            Apply = () => { Sys.RegSet($@"{CLASSIC_MENU}\InprocServer32", "", RegistryValueKind.String, ""); Sys.RestartExplorer(); return null; },
            Revert = _ => { Sys.RegDelKey(CLASSIC_MENU); Sys.RestartExplorer(); }
        });
        l.Add(new Tweak
        {
            Id = "qol_fileext", Cat = "Quality of Life", Name = "Show File Extensions", Impact = "Low", Rec = true,
            Desc = "Shows \".exe\", \".zip\" etc. in File Explorer - useful for modding games and spotting disguised malware.",
            Reg = [D(ADV, "HideFileExt", 0, 1)]
        });
        l.Add(new Tweak
        {
            Id = "qol_thispc", Cat = "Quality of Life", Name = "Open File Explorer to This PC", Impact = "Low",
            Desc = "File Explorer opens straight to your drives instead of Home / Quick Access, which is faster when you have several large drives.",
            Reg = [D(ADV, "LaunchTo", 1)]
        });
        l.Add(new Tweak
        {
            Id = "qol_verbose", Cat = "Quality of Life", Name = "Show Detailed Boot & Shutdown Status", Impact = "Low",
            Desc = "Displays what Windows is doing during startup and shutdown instead of a spinning circle. Helps spot slow drivers or services.",
            Reg = [D(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1)]
        });

        // ------------------------------------------------------------ ADVANCED (security trade-offs)
        l.Add(new Tweak
        {
            Id = "adv_vbs", Cat = "Advanced", Name = "Disable Memory Integrity (VBS / HVCI)", Impact = "High", Risk = "Risky", Tags = ["Security Risk", "Reboot"],
            Desc = "Memory Integrity runs the kernel under a hypervisor. It can cost 5-15% FPS, most on older CPUs that lack MBEC (Intel before 7th gen, AMD before Zen 2). Disabling it is a real gain but lowers protection against kernel-level malware."
                 + (pc.HvciOn ? " Currently running on this PC." : " Currently not running on this PC."),
            Reg = [D(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0), D(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0)]
        });
        l.Add(new Tweak
        {
            Id = "adv_spectre", Cat = "Advanced", Name = "Disable Spectre & Meltdown Mitigations", Impact = "High", Risk = "Risky", Tags = ["Security Risk", "Reboot"],
            Desc = "Older CPUs pay a high price for these CPU-bug patches (system calls, disk and network I/O). Turning them off can noticeably help frame times, but exposes the PC to those attacks (e.g. via malicious websites). Little to gain on recent CPUs.",
            Reg = [D(MEMMGMT, "FeatureSettingsOverride", 3), D(MEMMGMT, "FeatureSettingsOverrideMask", 3)]
        });

        CatalogTh.Fill(l, pc);
        HeatGuard.MarkTweaks(l);
        return l;
    }

    static string NicPowerScript(string state) =>
        "Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object { $pm = Get-NetAdapterPowerManagement -Name $_.Name -ErrorAction SilentlyContinue; " +
        "if ($pm -and \"$($pm.AllowComputerToTurnOffDevice)\" -ne 'Unsupported') { $pm.AllowComputerToTurnOffDevice = '" + state + "'; $pm | Set-NetAdapterPowerManagement -ErrorAction SilentlyContinue } }";
}
