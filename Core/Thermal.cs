using System.Diagnostics;

namespace Atomic.Core;

/// <summary>
/// Temperatures and throttling from what Windows and the NVIDIA driver already expose, so no kernel driver is needed.
/// The CPU figure is the hottest ACPI thermal zone, which on most laptops follows the processor.
/// </summary>
public static class Thermal
{
    /// <param name="CpuLimit">Percent of full speed Windows currently allows the CPU (100 = not limited).</param>
    public record Reading(double? CpuTemp, double? GpuTemp, double CpuLimit, bool Throttled);

    static readonly string Smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
    static List<PerformanceCounter> _temps = [], _passive = [];
    static PerformanceCounter? _limit;
    static bool _ready;

    static void Init()
    {
        _ready = true;
        try
        {
            foreach (var zone in new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames())
            {
                _temps.Add(new PerformanceCounter("Thermal Zone Information", "Temperature", zone));
                _passive.Add(new PerformanceCounter("Thermal Zone Information", "% Passive Limit", zone));
            }
        }
        catch { _temps = []; _passive = []; }
        try { _limit = new PerformanceCounter("Processor Information", "% Performance Limit", "_Total"); _limit.NextValue(); } catch { _limit = null; }
    }

    public static bool HasGpuTemp => File.Exists(Smi);

    public static Reading Read(bool gpu)
    {
        if (!_ready) Init();
        double? cpu = null, gpuTemp = null;
        double limit = 100;
        bool passive = false;
        try
        {
            // counters report Kelvin; zones stuck at a placeholder value (often 20 C) are ignored
            var c = _temps.Select(t => t.NextValue() - 273.15).Where(v => v is > 25 and < 125).ToList();
            if (c.Count > 0) cpu = c.Max();
            passive = _passive.Any(p => p.NextValue() < 100);
            if (_limit != null) limit = _limit.NextValue();
        }
        catch { }
        if (gpu && HasGpuTemp)
        {
            var first = Sys.Run(Smi, "--query-gpu=temperature.gpu --format=csv,noheader,nounits", 4000).Split('\n')[0].Trim();
            if (double.TryParse(first, out var t)) gpuTemp = t;
        }
        return new Reading(cpu, gpuTemp, limit, passive || limit < 99);
    }

    /// <summary>Free and standby (cached) memory in bytes, or null when the counters are unavailable.</summary>
    public static (double Free, double Standby)? MemoryLists()
    {
        try
        {
            _memory ??= MemoryCounters.Select(n => new PerformanceCounter("Memory", n)).ToArray();
            var v = _memory.Select(c => (double)c.NextValue()).ToArray();
            return (v[0], v[1] + v[2] + v[3]);
        }
        catch { return null; }
    }
    static readonly string[] MemoryCounters = ["Free & Zero Page List Bytes", "Standby Cache Normal Priority Bytes", "Standby Cache Reserve Bytes", "Standby Cache Core Bytes"];
    static PerformanceCounter[]? _memory;
}
