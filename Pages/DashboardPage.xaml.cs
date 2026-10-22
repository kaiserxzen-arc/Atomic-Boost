using System.Windows;
using System.Windows.Controls;
using Atomic.Controls;
using Atomic.Core;

namespace Atomic.Pages;

public partial class DashboardPage : UserControl
{
    readonly LiveStats _stats = AppState.Stats;

    public DashboardPage()
    {
        InitializeComponent();
        var pc = AppState.Pc;
        Title.Text = (pc.Model.Length > 0 && !pc.Model.Contains("System Product Name") ? pc.Model : pc.Computer).ToUpperInvariant();
        SubTitle.Text = $"{L.T("Live monitor")}  -  {pc.OsName} {pc.OsVer}  -  {pc.DeviceKindText}".ToUpperInvariant();
        CpuName.Text = ShortCpu(pc.CpuName);
        CpuSub.Text = pc.CpuCores > 0 ? L.F("{0} cores / {1} threads", pc.CpuCores, pc.CpuThreads) : L.F("{0} threads", pc.CpuThreads);
        RamName.Text = $"{Sys.Bytes(pc.RamBytes)} {pc.RamType}".Trim();

        var colors = Palette.Series;
        History.Add(new MultiChart.Series("CPU", colors[0], () => _stats.Cpu));
        History.Add(new MultiChart.Series("Memory", colors[1], () => _stats.Ram));
        History.Add(new MultiChart.Series("GPU", colors[2], () => _stats.Gpu));
        History.Add(new MultiChart.Series("Disk", colors[3], () => _stats.Disk));

        Fx.GlowBorder(ScoreCard);
        BoostInfo.ToolTip = L.F("Apply every tweak recommended for this {0} and switch on Auto Game Mode", pc.DeviceKindText.ToLowerInvariant());
        _stats.Updated += () => Dispatcher.BeginInvoke(() => UpdateLive());
        Ops.Changed += UpdateScore;
        UpdateScore();
        UpdateLive(true);
    }

    static string ShortCpu(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\(R\)|\(TM\)|CPU|Processor|@.*$|\d+-Core| with .*$", "").Replace("  ", " ").Trim();

    void UpdateLive(bool force = false)
    {
        if (!IsVisible && !force) return;
        CpuVal.Text = $"{_stats.CpuNow:N0}%";
        CpuPeak.Text = _stats.Cpu.Length > 0 ? L.F("peak {0:N0}%", _stats.Cpu.Max()) : "";
        CpuSpark.Set(_stats.Cpu);

        RamVal.Text = $"{_stats.RamNow:N0}%";
        RamSub.Text = L.F("{0} of {1} in use", Sys.Bytes(_stats.RamUsed), Sys.Bytes(_stats.RamTotal));
        RamFree.Text = L.F("{0} free", Sys.Bytes(_stats.RamTotal - _stats.RamUsed));
        RamSpark.Set(_stats.Ram);

        LegCpu.Text = $"CPU  {_stats.CpuNow:N0}%";
        LegRam.Text = $"{L.T("Memory")}  {_stats.RamNow:N0}%";
        LegGpu.Text = _stats.GpuOk ? $"GPU  {_stats.GpuNow:N0}%" : "GPU  n/a";
        LegDisk.Text = $"{L.T("Disk")}  {_stats.DiskNow:N0}%";
        NetText.Text = L.F("{0}/s down   {1}/s up", Sys.Bytes(_stats.NetDown), Sys.Bytes(_stats.NetUp));
        History.Refresh();

        ProcList.ItemsSource = TabMem.IsChecked == true ? _stats.TopMem : _stats.TopCpu;

        if (SystemInfo.BatteryPercent is int pct)
        {
            BatteryPill.Visibility = Visibility.Visible;
            BatteryText.Text = $"{pct}%  {L.T(SystemInfo.OnAcPower ? "plugged in" : "on battery")}";
        }
    }

    void UpdateScore()
    {
        var (score, on, total) = AppState.Engine.Score();
        Fx.CountTo(ScoreVal, score);
        ScoreGauge.AnimateTo(score);
        ScoreSub.Text = L.F("{0} of {1} active", on, total);
        int left = total - on;
        GaugeLine1.Text = left == 0 ? L.T("Fully optimized") : L.F("{0} tweaks", left);
        GaugeLine2.Text = L.T(left == 0 ? "for this PC" : "still available");
    }

    void HistTab_Checked(object sender, RoutedEventArgs e)
    {
        if (History == null) return;
        var only = (string)((RadioButton)sender).Tag;
        History.Hidden.Clear();
        if (only.Length > 0)
            foreach (var n in new[] { "CPU", "Memory", "GPU", "Disk" })
                if (n != only) History.Hidden.Add(n);
        History.Refresh();
    }

    void ProcTab_Checked(object sender, RoutedEventArgs e)
    {
        if (ProcList != null) ProcList.ItemsSource = TabMem.IsChecked == true ? _stats.TopMem : _stats.TopCpu;
    }

    async void BtnOptimize_Click(object sender, RoutedEventArgs e) => await Ops.ApplyRecommended();

    async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        await MainWindow.I.RunBusy(L.T("Re-checking"), L.T("Reading current Windows settings..."), () => AppState.Engine.RefreshAll(true));
        Ops.RaiseChanged();
        MainWindow.I.Toast(L.T("Settings re-checked."), "info");
    }

    void SeeScanner_Click(object sender, RoutedEventArgs e) => MainWindow.I.Navigate("scanner");
    void TaskManager_Click(object sender, RoutedEventArgs e) => Sys.Open("taskmgr.exe");
}
