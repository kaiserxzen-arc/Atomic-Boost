using System.Runtime.InteropServices;

namespace Atomic.Core;

public static class Native
{
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SpiGet(uint action, uint param, ref bool value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SpiSet(uint action, uint param, IntPtr value, uint winIni);

    /// <summary>
    /// Windows "Animation effects" (Settings > Accessibility > Visual effects). Browsers and every Chromium / WebView2 /
    /// Electron app turn it into prefers-reduced-motion, so switching it off stops animations in all of them.
    /// </summary>
    public static bool? ClientAreaAnimation { get { bool v = false; return SpiGet(0x1042, 0, ref v, 0) ? v : null; } }   // SPI_GETCLIENTAREAANIMATION

    /// <summary>Sets it, saves it and tells every running app straight away (no sign-out needed).</summary>
    public static bool SetClientAreaAnimation(bool on) => SpiSet(0x1043, 0, on ? 1 : 0, 0x01 | 0x02);   // SPI_SETCLIENTAREAANIMATION, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

    [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    public static (double Total, double Avail) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (m.ullTotalPhys, m.ullAvailPhys) : (0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetCompressedFileSizeW(string name, out uint high);

    /// <summary>Space a file really occupies. Sparse and compressed files (e.g. NVIDIA shader caches) report a far larger Length.</summary>
    public static long SizeOnDisk(FileInfo f)
    {
        if ((f.Attributes & (FileAttributes.SparseFile | FileAttributes.Compressed)) == 0) return f.Length;
        uint low = GetCompressedFileSizeW(f.FullName, out uint high);
        if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0) return f.Length;
        return ((long)high << 32) | low;
    }

    // ------------------------------------------------------------ processes
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, System.Text.StringBuilder name, ref uint size);

    /// <summary>Full path of a process's exe, or "" - only needs limited query access, so it also works for elevated and protected processes.</summary>
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returned);
    [StructLayout(LayoutKind.Sequential)] struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }

    /// <summary>Command line of a process ("" when it cannot be read).</summary>
    public static string CommandLine(int pid) => QueryProcess(pid, 60, (buf, _) =>   // ProcessCommandLineInformation
    {
        var s = Marshal.PtrToStructure<UnicodeString>(buf);
        return Marshal.PtrToStringUni(s.Buffer, s.Length / 2) ?? "";
    }) ?? "";

    /// <summary>Process that started this one (0 when unknown: default of int).</summary>
    public static int ParentId(int pid) => QueryProcess(pid, 0, (buf, size) => (int)Marshal.ReadIntPtr(buf, IntPtr.Size * 5));   // PROCESS_BASIC_INFORMATION.InheritedFromUniqueProcessId

    static T? QueryProcess<T>(int pid, int infoClass, Func<IntPtr, int, T> read)
    {
        var h = OpenProcess(0x1000, false, (uint)pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return default;
        try
        {
            int size = infoClass == 0 ? IntPtr.Size * 6 : 0;
            if (size == 0) NtQueryInformationProcess(h, infoClass, IntPtr.Zero, 0, out size);
            if (size <= 0) return default;
            var buf = Marshal.AllocHGlobal(size);
            try { return NtQueryInformationProcess(h, infoClass, buf, size, out _) == 0 ? read(buf, size) : default; }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { CloseHandle(h); }
    }

    public static string ProcessPath(int pid)
    {
        var h = OpenProcess(0x1000, false, (uint)pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

    /// <summary>The process that owns the window the user is working in.</summary>
    public static (int Pid, string Path)? ForegroundProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;
        string path = ProcessPath((int)pid);
        return path.Length == 0 ? null : ((int)pid, path);
    }

    // ------------------------------------------------------------ global hotkey

    // ------------------------------------------------------------ timer resolution / standby memory
    [DllImport("ntdll.dll")] static extern int NtSetTimerResolution(uint desired100ns, bool set, out uint current);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIVILEGES { public uint Count; public long Luid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returned);

    /// <summary>Asks Windows for a 0.5 ms system timer (hold = true) or drops that request again.</summary>
    public static bool TimerResolution(bool hold) => NtSetTimerResolution(5000, hold, out _) == 0;

    static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var token)) return false;   // TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY
        try
        {
            if (!LookupPrivilegeValueW(null, name, out long luid)) return false;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };   // SE_PRIVILEGE_ENABLED
            // also "succeeds" when the token does not hold the privilege; the last error tells the two apart
            return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
        }
        finally { CloseHandle(token); }
    }

    /// <summary>Drops the standby (cached file) memory list so the RAM counts as free again. Needs Administrator rights.</summary>
    public static bool PurgeStandbyList()
    {
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege")) return false;
        int command = 4;   // MemoryPurgeStandbyList
        return NtSetSystemInformation(80, ref command, sizeof(int)) == 0;   // SystemMemoryListInformation
    }
}

/// <summary>Power plan settings through powrprof.dll (no powercfg process per value).</summary>
public static class Power
{
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid overlay);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0 || p == IntPtr.Zero) return Guid.Empty;
        try { return Marshal.PtrToStructure<Guid>(p); } finally { LocalFree(p); }
    }

    public static void Activate(Guid scheme) => PowerSetActiveScheme(IntPtr.Zero, ref scheme);

    public static uint? Get(Guid scheme, string sub, string setting, bool ac)
    {
        Guid s = new(sub), t = new(setting);
        uint r = ac ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref s, ref t, out var v)
                    : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref s, ref t, out v);
        return r == 0 ? v : null;
    }

    public static void Set(Guid scheme, string sub, string setting, uint value, bool ac)
    {
        Guid s = new(sub), t = new(setting);
        if (ac) PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref s, ref t, value);
        else PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref s, ref t, value);
    }

    /// <summary>Windows "power mode" slider (overlay scheme). Null when the API is unavailable.</summary>
    public static Guid? GetOverlay()
    {
        try { return PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : null; } catch { return null; }
    }

    public static bool SetOverlay(Guid overlay)
    {
        try { return PowerSetActiveOverlayScheme(overlay) == 0; } catch { return false; }
    }
}
