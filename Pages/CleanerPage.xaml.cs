using System.Windows;
using System.Windows.Controls;
using Atomic.Core;

namespace Atomic.Pages;

public partial class CleanerPage : UserControl
{
    readonly List<CleanItem> _items = Cleaner.Build();
    bool _scanned;

    public CleanerPage()
    {
        InitializeComponent();
        Items.ItemsSource = _items;
        Loaded += async (_, _) => { if (!_scanned) { _scanned = true; await Scan(); } };
    }

    async Task Scan()
    {
        await MainWindow.I.RunBusy(L.T("Scanning"), L.T("Measuring junk files..."), () =>
        {
            foreach (var item in _items) { MainWindow.I.BusyStatus(item.Name); Cleaner.Scan(item); }
        });
        UpdateTotals();
    }

    void UpdateTotals()
    {
        SelText.Text = Sys.Bytes(_items.Where(i => i.Checked).Sum(i => Math.Max(0, i.Size)));
        TotalText.Text = Sys.Bytes(_items.Sum(i => Math.Max(0, i.Size)));
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            FreeText.Text = $"{Sys.Bytes(d.TotalFreeSpace)} ({100.0 * d.TotalFreeSpace / d.TotalSize:N0}%)";
        }
        catch { }
    }

    void Tick_Click(object sender, RoutedEventArgs e) => UpdateTotals();
    async void Scan_Click(object sender, RoutedEventArgs e) => await Scan();

    async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var todo = _items.Where(i => i.Checked && i.Size > 0).ToList();
        if (todo.Count == 0) { MainWindow.I.Toast(L.T("Nothing selected to clean."), "info"); return; }
        string extra = todo.Any(i => i.RecycleBin) ? "\n\n" + L.T("The Recycle Bin will be emptied permanently.") : "";
        if (!await MainWindow.I.Ask(L.T("Clean selected items"), L.F("{0} in {1} categories will be deleted:", Sys.Bytes(todo.Sum(i => i.Size)), todo.Count)
                + $"\n\n{string.Join("\n", todo.Select(i => "  -  " + i.Name))}{extra}", L.T("Clean now"))) return;
        long freed = 0;
        await MainWindow.I.RunBusy(L.T("Cleaning"), L.T("Deleting files..."), () =>
        {
            foreach (var item in todo) { MainWindow.I.BusyStatus(item.Name); freed += Cleaner.Clean(item); }
        });
        UpdateTotals();
        MainWindow.I.Toast(L.F("Freed {0}.", Sys.Bytes(freed)));
    }
}
