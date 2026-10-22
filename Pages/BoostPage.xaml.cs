using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Atomic.Controls;
using Atomic.Core;

namespace Atomic.Pages;

public record BoostAction(string Key, string Glyph, string Title, string Button, string Desc);

public partial class BoostPage : UserControl
{
    static readonly string[] BackgroundApps = ["OneDrive", "Teams", "ms-teams", "msedge", "PhoneExperienceHost", "YourPhone", "Widgets", "WidgetService", "Cortana", "SkypeApp", "GoogleDriveFS", "Dropbox", "Copilot"];

    static MainWindow W => MainWindow.I;

    public BoostPage()
    {
        InitializeComponent();
        var pc = AppState.Pc;
        Fx.GlowBorder(Hero);

        Actions.ItemsSource = new BoostAction[]
        {
            new("ram", "", "Free up RAM", "Free now", "Trims the memory held by idle apps so more RAM is available for the game you are about to start."),
            new("bgclose", "", "Close background apps", "Close apps", "Closes OneDrive, Teams, Edge, Phone Link, Widgets and similar apps. Shows the list and asks first."),
            new("dns", "", "Flush DNS cache", "Flush", "Clears cached DNS records. Fixes \"can't connect to server\" errors after a game changes servers."),
            new("explorer", "", "Restart Explorer", "Restart", "Restarts the taskbar and desktop shell - clears a laggy taskbar or Start menu without a reboot."),
            new("defrag", "", "Optimize drives", "Open", "Opens Optimize Drives - defragments hard drives and TRIMs SSDs. Run it on the drive your games live on."),
            new("gfx", "", "Windows graphics settings", "Open", pc.IsLaptop
                ? "Set each game to \"High performance\" here so it runs on the discrete GPU instead of the integrated one."
                : "Per-game GPU preference, variable refresh rate and windowed-game optimizations in Windows Settings."),
        }.Select(a => a with { Title = L.T(a.Title), Button = L.T(a.Button), Desc = L.T(a.Desc) }).ToList();

    }

    // ------------------------------------------------------------ quick actions
    async void Action_Click(object sender, RoutedEventArgs e)
    {
        switch ((string)((Button)sender).Tag)
        {
            case "ram":
                double freed = 0;
                await W.RunBusy(L.T("Freeing memory"), L.T("Trimming idle app memory..."), () => freed = Cleaner.FreeRam());
                W.Toast(L.F("Released {0} of RAM.", Sys.Bytes(freed)));
                break;
            case "bgclose":
                if (await CloseBackgroundApps() == 0) W.Toast(L.T("No common background apps are running."), "info");
                break;
            case "dns":
                Sys.Run("ipconfig.exe", "/flushdns", 15000);
                W.Toast(L.T("DNS cache flushed."));
                break;
            case "explorer":
                await W.RunBusy(L.T("Restarting Explorer"), L.T("The taskbar will blink for a moment..."), Sys.RestartExplorer);
                W.Toast(L.T("Explorer restarted."));
                break;
            case "defrag": Sys.Open(Path.Combine(Environment.SystemDirectory, "dfrgui.exe")); break;
            case "gfx": Sys.Open("ms-settings:display-advancedgraphics"); break;
        }
    }

    /// <summary>Returns how many apps were closed; -1 when the user cancelled.</summary>
    async Task<int> CloseBackgroundApps()
    {
        var names = BackgroundApps.Where(n => Process.GetProcessesByName(n).Length > 0).ToList();
        if (names.Count == 0) return 0;
        if (!await W.Ask(L.T("Close background apps"), L.F("Close these apps now?\n\n{0}\n\nSave any open work in them first.", string.Join(", ", names)), L.T("Close apps"))) return -1;
        foreach (var n in names)
            foreach (var p in Process.GetProcessesByName(n))
            {
                try { p.Kill(); } catch { } finally { p.Dispose(); }
            }
        Sys.Log("BOOST  closed: " + string.Join(", ", names));
        W.Toast(L.F("Closed: {0}", string.Join(", ", names)));
        return names.Count;
    }

    async void BoostNow_Click(object sender, RoutedEventArgs e)
    {
        int closed = await CloseBackgroundApps();
        double freed = 0;
        await W.RunBusy(L.T("Boosting"), L.T("Releasing memory and flushing DNS..."), () =>
        {
            freed = Cleaner.FreeRam();
            Sys.Run("ipconfig.exe", "/flushdns", 15000);
        });
        W.Toast(L.F("Boost done: {0} apps closed, {1} of RAM released, DNS flushed.", Math.Max(0, closed), Sys.Bytes(freed)));
    }
}
