using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Atomic.Core;

public record ProcRow(string Name, int Count, double Bytes, double Cpu)
{
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string Sub => Count > 1 ? L.F("{0} processes", Count) : L.T("1 process");
    public string MemText => Sys.Bytes(Bytes);
    public string CpuText => $"{Cpu:N1}%";
}

/// <summary>Samples CPU / RAM / GPU / disk / network once a second on a background thread.</summary>
public class LiveStats
{
    public const int Points = 60;
    public double[] Cpu = [], Ram = [], Gpu = [], Disk = [];
    public double CpuNow, RamNow, GpuNow, DiskNow, NetDown, NetUp, RamUsed, RamTotal;
    public bool GpuOk;
    public List<ProcRow> TopMem = [], TopCpu = [];
    public int ProcessCount;
    public event Action? Updated;
    public void ClearListeners() => Updated = null;

    readonly Queue<double> _cpu = new(), _ram = new(), _gpu = new(), _disk = new();
    long _idle, _kernel, _user, _rx, _tx;
    PerformanceCounter? _diskCounter;
    Dictionary<string, CounterSample> _gpuPrev = [];
    Dictionary<string, TimeSpan> _procPrev = [];
    DateTime _procTime;
    bool _started;

    /// <summary>Returns false when the sampler was already running.</summary>
    public bool Start()
    {
        if (_started) return false;
        _started = true;
        new Thread(Loop) { IsBackground = true, Name = "LiveStats", Priority = ThreadPriority.BelowNormal }.Start();
        return true;
    }

    void Loop()
    {
        Native.GetSystemTimes(out _idle, out _kernel, out _user);
        (_rx, _tx) = NetBytes();
        try { _diskCounter = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); _diskCounter.NextValue(); } catch { _diskCounter = null; }
        for (int tick = 0; ; tick++)
        {
            Thread.Sleep(1000);
            try
            {
                Sample();
                if (tick % 2 == 0) SampleGpu();
                if (tick % 3 == 0) SampleProcesses();
                Updated?.Invoke();
            }
            catch (Exception ex) { Sys.Log("stats: " + ex.Message); }
        }
    }

    static double[] Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > Points) q.Dequeue();
        return [.. q];
    }

    void Sample()
    {
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            // kernel time includes idle time
            double total = (kernel - _kernel) + (user - _user), busy = total - (idle - _idle);
            CpuNow = total > 0 ? Math.Clamp(100 * busy / total, 0, 100) : 0;
            (_idle, _kernel, _user) = (idle, kernel, user);
        }
        var (memTotal, memAvail) = Native.Memory();
        RamTotal = memTotal; RamUsed = memTotal - memAvail;
        RamNow = memTotal > 0 ? 100 * RamUsed / memTotal : 0;

        try { if (_diskCounter != null) DiskNow = Math.Clamp(100 - _diskCounter.NextValue(), 0, 100); } catch { _diskCounter = null; }

        var (rx, tx) = NetBytes();
        NetDown = Math.Max(0, rx - _rx); NetUp = Math.Max(0, tx - _tx);
        (_rx, _tx) = (rx, tx);

        Cpu = Push(_cpu, CpuNow); Ram = Push(_ram, RamNow); Disk = Push(_disk, DiskNow); Gpu = Push(_gpu, GpuNow);
    }

    static (long Rx, long Tx) NetBytes()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = n.GetIPStatistics();
                rx += s.BytesReceived; tx += s.BytesSent;
            }
        }
        catch { }
        return (rx, tx);
    }

    /// <summary>Busiest 3D engine across adapters, the same figure Task Manager shows.</summary>
    void SampleGpu()
    {
        try
        {
            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
            if (!data.Contains("Utilization Percentage")) return;
            var cur = new Dictionary<string, CounterSample>();
            var perAdapter = new Dictionary<string, double>();
            foreach (InstanceData d in data["Utilization Percentage"]!.Values)
            {
                var name = d.InstanceName;
                if (!name.Contains("engtype_3D")) continue;
                cur[name] = d.Sample;
                if (!_gpuPrev.TryGetValue(name, out var old)) continue;
                int i = name.IndexOf("luid_", StringComparison.Ordinal), j = name.IndexOf("_eng_", StringComparison.Ordinal);
                var adapter = i >= 0 && j > i ? name[i..j] : "gpu";
                perAdapter[adapter] = perAdapter.GetValueOrDefault(adapter) + CounterSample.Calculate(old, d.Sample);
            }
            _gpuPrev = cur;
            GpuNow = perAdapter.Count > 0 ? Math.Clamp(perAdapter.Values.Max(), 0, 100) : 0;
            GpuOk = true;
        }
        catch { GpuOk = false; }
    }

    void SampleProcesses()
    {
        var now = DateTime.UtcNow;
        double elapsed = (now - _procTime).TotalMilliseconds * Environment.ProcessorCount;
        var cpuNow = new Dictionary<string, TimeSpan>();
        var mem = new Dictionary<string, (int Count, double Bytes)>();
        int count = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                count++;
                if (p.Id is 0 or 4) continue;
                var name = p.ProcessName;
                var m = mem.GetValueOrDefault(name);
                mem[name] = (m.Count + 1, m.Bytes + p.WorkingSet64);
                try { cpuNow[name] = cpuNow.GetValueOrDefault(name) + p.TotalProcessorTime; } catch { }
            }
            catch { }
            finally { p.Dispose(); }
        }
        var rows = mem.Select(kv =>
        {
            double cpu = _procPrev.TryGetValue(kv.Key, out var old) && cpuNow.TryGetValue(kv.Key, out var cur) && elapsed > 0
                ? Math.Clamp(100 * (cur - old).TotalMilliseconds / elapsed, 0, 100) : 0;
            return new ProcRow(kv.Key, kv.Value.Count, kv.Value.Bytes, cpu);
        }).ToList();
        _procPrev = cpuNow; _procTime = now;
        ProcessCount = count;
        TopMem = rows.OrderByDescending(r => r.Bytes).Take(5).ToList();
        TopCpu = rows.OrderByDescending(r => r.Cpu).ThenByDescending(r => r.Bytes).Take(5).ToList();
    }
}
