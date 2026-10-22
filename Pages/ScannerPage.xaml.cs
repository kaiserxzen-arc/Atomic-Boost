using System.Windows;
using System.Windows.Controls;
using Atomic.Core;

namespace Atomic.Pages;

public record SpecCard(string Glyph, string Title, string Main, List<KeyValuePair<string, string>> Rows);
public record HealthRow(string Glyph, string Fg, string Bg, string Title, string Text);
public record VolRow(string Name, string Usage, GridLength UsedStar, GridLength FreeStar, string Bar);

public partial class ScannerPage : UserControl
{
    public ScannerPage()
    {
        InitializeComponent();
        Show();
    }

    static KeyValuePair<string, string> Kv(string k, string v) => new(L.T(k), v.Length == 0 ? "-" : v);

    void Show()
    {
        var pc = AppState.Pc;
        SubTitle.Text = $"{pc.Computer}  -  {pc.DeviceKindText}  -  {L.F("up {0}", pc.Uptime)}";

        var cards = new List<SpecCard>
        {
            new("", "PROCESSOR", pc.CpuName,
            [
                Kv("Cores / threads", pc.CpuCores > 0 ? $"{pc.CpuCores} / {pc.CpuThreads}" : L.F("{0} threads", pc.CpuThreads)),
                Kv("Base clock", pc.CpuClock > 0 ? $"{pc.CpuClock / 1000.0:N2} GHz" : ""),
                Kv("L3 cache", pc.CpuL3Kb > 0 ? $"{pc.CpuL3Kb / 1024} MB" : ""),
            ]),
            new("", "GRAPHICS", pc.Gpu?.Name ?? L.T("Unknown GPU"),
            [
                Kv("Video memory", pc.Gpu is { Vram: > 0 } ? Sys.Bytes(pc.Gpu.Vram) : ""),
                Kv("Driver", pc.Gpu?.Driver ?? ""),
                Kv("Driver date", pc.Gpu?.DriverDate?.ToString("yyyy-MM-dd") ?? ""),
                .. pc.Gpus.Where(g => g != pc.Gpu).Select(g => Kv("Also installed", g.Name)),
            ]),
            new("", "MEMORY", $"{Sys.Bytes(pc.RamBytes)} {pc.RamType}".Trim(),
            [
                Kv("Modules", pc.RamSticks > 0 ? pc.RamSlots > 0 ? L.F("{0} of {1} slots", pc.RamSticks, pc.RamSlots) : $"{pc.RamSticks}" : ""),
                Kv("Speed", pc.RamSpeed > 0 ? $"{pc.RamSpeed} MT/s" : ""),
                Kv("Manufacturer", pc.RamMaker),
            ]),
            new("", "STORAGE", pc.Disks.Count > 0 ? L.F("{0} total", Sys.Bytes(pc.Disks.Sum(d => d.Size))) : L.T("Unknown"),
            [
                .. pc.Disks.Select(d => Kv($"{d.Type}  {Sys.Bytes(d.Size)}", d.Name)),
                Kv("Windows is on", pc.SystemDiskType),
            ]),
            new("", "SYSTEM", $"{pc.OsName} {pc.OsVer}".Trim(),
            [
                Kv("Build", pc.Build.ToString()),
                Kv("Model", pc.Model),
                Kv("Motherboard", pc.Board),
                Kv("BIOS", pc.Bios),
            ]),
            new("", "POWER", pc.PowerPlan.Length > 0 ? pc.PowerPlan : L.T("Unknown plan"),
            [
                Kv("Device type", pc.DeviceKindText),
                Kv("Power source", L.T(SystemInfo.OnAcPower ? "Plugged in" : "Battery")),
                Kv("Battery", SystemInfo.BatteryPercent is int p ? $"{p}%" + (pc.BatteryHealth is int h ? "  " + L.F("(health {0}%)", h) : "") : L.T("None")),
                Kv("Memory Integrity", L.T(pc.HvciOn ? "On" : "Off")),
            ]),
        };
        Specs.ItemsSource = cards.Select(c => c with { Title = L.T(c.Title) }).ToList();

        var health = pc.Health(StartupManager.Load().Count(e => e.Enabled));
        HealthList.ItemsSource = health.OrderBy(h => h.Level == "warn" ? 0 : h.Level == "info" ? 1 : 2).Select(h => h.Level switch
        {
            "warn" => new HealthRow("", "#F5B544", "#26F5B544", h.Title, h.Text),
            "info" => new HealthRow("", "#B57BEE", "#26B57BEE", h.Title, h.Text),
            _ => new HealthRow("", "#3DDC97", "#263DDC97", h.Title, h.Text)
        }).ToList();
        int warns = health.Count(h => h.Level == "warn");
        HealthSummary.Text = warns == 0 ? L.T("No problems found") : L.F("{0} thing(s) to look at", warns);

        Volumes.ItemsSource = pc.Volumes.Where(v => v.Size > 0).Select(v =>
        {
            double used = (v.Size - v.Free) / v.Size;
            return new VolRow($"{v.Letter}  {v.Label}".Trim(), L.F("{0} free of {1}", Sys.Bytes(v.Free), Sys.Bytes(v.Size)),
                new GridLength(Math.Max(0.01, used), GridUnitType.Star), new GridLength(Math.Max(0.01, 1 - used), GridUnitType.Star),
                used > 0.9 ? "#FF5C5C" : used > 0.75 ? "#F5B544" : Palette.Accent2Hex);
        }).ToList();
    }

    async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        await MainWindow.I.RunBusy(L.T("Scanning your system"), L.T("Detecting hardware..."), () =>
        {
            var pc = SystemInfo.Scan();
            // keep the engine's view of the PC in sync with the fresh scan
            AppState.Pc = pc;
            AppState.Engine.Load(Catalog.Build(pc));
            AppState.Engine.RefreshAll();
        });
        Show();
        Ops.RaiseChanged();
        MainWindow.I.Toast(L.T("Scan complete."), "info");
    }
}
