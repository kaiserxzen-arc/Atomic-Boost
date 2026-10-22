using System.Windows;
using System.Windows.Controls;
using Atomic.Core;

namespace Atomic.Pages;

public partial class StartupPage : UserControl
{
    List<StartupEntry> _entries = [];

    public StartupPage()
    {
        InitializeComponent();
        Reload();
    }

    void Reload()
    {
        _entries = StartupManager.Load();
        Items.ItemsSource = _entries;
        EmptyText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSub();
    }

    void UpdateSub() => SubTitle.Text = L.F("{0} of {1} apps start with Windows - switch off the ones you do not need", _entries.Count(e => e.Enabled), _entries.Count);

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        if (cb.DataContext is not StartupEntry entry) return;
        bool want = cb.IsChecked == true;
        if (!Ops.RequireAdmin()) { entry.Enabled = entry.Enabled; return; }
        try
        {
            StartupManager.SetEnabled(entry, want);
            MainWindow.I.Toast(L.F(want ? "{0} will start with Windows again." : "{0} will no longer start with Windows.", entry.Name));
        }
        catch (Exception ex)
        {
            entry.Enabled = entry.Enabled;
            MainWindow.I.Toast(L.F("Could not change {0}: {1}", entry.Name, ex.Message), "error");
        }
        UpdateSub();
    }

    void Reload_Click(object sender, RoutedEventArgs e) => Reload();
    void TaskMgr_Click(object sender, RoutedEventArgs e) => Sys.Open("taskmgr.exe", "/0 /startup");
}
