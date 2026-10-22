using System.Windows;
using System.Windows.Controls;
using Atomic.Controls;
using Atomic.Core;

namespace Atomic.Pages;

public partial class OptimizePage : UserControl
{
    string _cat = "", _filter = "all", _search = "";

    public OptimizePage()
    {
        InitializeComponent();
        var pc = AppState.Pc;
        SubTitle.Text = L.F("Tuned for {0}  -  {1}  -  {2}", pc.CpuName, pc.Gpu?.Name ?? "GPU", pc.DeviceKindText);

        foreach (var cat in new[] { "" }.Concat(Catalog.Categories))
        {
            var rb = new RadioButton { Style = (Style)FindResource("TabPill"), GroupName = "Cat", Content = L.T(cat.Length == 0 ? "All" : cat), Tag = cat, Margin = new Thickness(0, 0, 2, 0) };
            rb.Checked += (s, _) => { _cat = (string)((RadioButton)s).Tag; Apply(); Fx.Enter(TweakList, fromY: 12, ms: 260); };
            CatTabs.Children.Add(rb);
        }
        ((RadioButton)CatTabs.Children[0]).IsChecked = true;
        Ops.Changed += Apply;
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _search = SearchBox.Text.Trim();
        if (TweakList != null) Apply();
    }

    void Apply()
    {
        var q = AppState.Engine.All.Where(t =>
            (_cat.Length == 0 || t.Cat == _cat) &&
            (_filter switch { "rec" => t.Rec, "on" => t.IsOn && t.Available, "off" => !t.IsOn && t.Available, _ => true }) &&
            (_search.Length == 0 || t.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) || t.Desc.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                 || t.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase) || t.DisplayDesc.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                 || t.Cat.Contains(_search, StringComparison.OrdinalIgnoreCase) || t.Tags.Any(g => g.Contains(_search, StringComparison.OrdinalIgnoreCase)))).ToList();
        TweakList.ItemsSource = q;
        EmptyText.Visibility = q.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var all = AppState.Engine.All;
        CountText.Text = L.F("{0} shown  -  {1} of {2} active", q.Count, all.Count(t => t.IsOn && t.Available), all.Count);
    }

    void Filter_Checked(object sender, RoutedEventArgs e)
    {
        _filter = (string)((RadioButton)sender).Tag;
        if (TweakList == null) return;
        Apply();
        Fx.Enter(TweakList, fromY: 12, ms: 260);
    }

    /// <summary>A click on the filter that is already chosen takes it off again: the list shows everything.</summary>
    void Filter_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var rb = (RadioButton)sender;
        if (rb.IsChecked != true) return;
        rb.IsChecked = false;
        e.Handled = true;
        _filter = "all";
        Apply();
        Fx.Enter(TweakList, fromY: 12, ms: 260);
    }

    async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        if (cb.DataContext is Tweak t) await Ops.Toggle(t, cb.IsChecked == true);
    }

    async void ApplyRec_Click(object sender, RoutedEventArgs e) => await Ops.ApplyRecommended();
    async void RevertAll_Click(object sender, RoutedEventArgs e) => await Ops.RevertAll();
}
