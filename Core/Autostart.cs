namespace Atomic.Core;

/// <summary>
/// "Start with Windows": a scheduled task that runs at sign-in with the highest privileges, so Atomic starts elevated
/// without a permission prompt, hidden next to the clock (--tray). A Run key or the Startup folder would ask for
/// Administrator rights at every sign-in.
/// </summary>
public static class Autostart
{
    const string TaskName = "Atomic Boost";

    /// <summary>The task exists (it is checked, not remembered, so deleting it in Task Scheduler is also seen).</summary>
    public static bool IsOn => Sys.Run("schtasks.exe", $"/Query /TN \"{TaskName}\"", 15000).Contains(TaskName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates or removes the task; true when it is now as asked. Needs Administrator rights.</summary>
    public static bool Set(bool on)
    {
        string exe = Environment.ProcessPath!.Replace("'", "''");
        var r = Sys.PS(on
            ? $$"""
                $ErrorActionPreference='Stop'
                try {
                    $a = New-ScheduledTaskAction -Execute '{{exe}}' -Argument '--tray'
                    $t = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
                    $t.Delay = 'PT15S'   # let Windows finish signing in first
                    $p = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
                    # no time limit (the default stops a task after 3 days), and laptops on battery start it too
                    $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
                    Register-ScheduledTask -TaskName '{{TaskName}}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null
                    'OK'
                } catch { 'FAIL ' + $_.Exception.Message }
                """
            : $$"""
                try { Unregister-ScheduledTask -TaskName '{{TaskName}}' -Confirm:$false -ErrorAction Stop } catch { }
                'OK'
                """, 60000).Trim();
        Sys.Log($"AUTOSTART  {(on ? "on" : "off")}  {r}");
        return IsOn == on;
    }

    /// <summary>The app moved (or was renamed) since the task was made: point the task at this copy again.</summary>
    public static void Repair()
    {
        if (!Sys.IsAdmin) return;
        var xml = Sys.Run("schtasks.exe", $"/Query /TN \"{TaskName}\" /XML", 15000);
        if (!xml.Contains("<Command>", StringComparison.Ordinal)) return;   // no task
        if (!xml.Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase)) Set(true);
    }
}
