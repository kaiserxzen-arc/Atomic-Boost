using System.Windows;
using System.Windows.Media;

namespace Atomic.Core;

/// <summary>The one look of the app - black glass lit in red - as the colour resources the styles in Themes/Theme.xaml are made of.</summary>
public static class Palette
{
    public const string AccentHex = "#FF2338", Accent2Hex = "#FF5F6B";
    public static readonly Color Bg = C("#060506"), Accent = C(AccentHex), Accent2 = C(Accent2Hex), GradA = C("#D10D27"), GradB = C("#FF3D48"), Glow = C("#C20A22");
    /// <summary>Chart lines: CPU, memory, GPU, disk.</summary>
    public static readonly string[] Series = ["#FF3D48", "#F1F1F4", "#FFB45A", "#6FD0C4"];
    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static T Fz<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static Brush B(Color c) => Fz(new SolidColorBrush(c));
    static Brush Grad(Point from, Point to, params (Color C, double At)[] stops) =>
        Fz(new LinearGradientBrush(new GradientStopCollection(stops.Select(s => new GradientStop(s.C, s.At))), from, to));

    /// <summary>Fills the application resources with the palette plus the shared styles.</summary>
    public static void Apply()
    {
        Color white = Colors.White, black = Colors.Black;
        // neutrals pick up a little red so the dark is not a flat grey
        Color Tint(string hex, double amount = 0.05) => Mix(C(hex), Accent, amount);
        Point tl = new(0, 0), br = new(1, 1), top = new(0, 0), bottom = new(0, 1);

        var d = new ResourceDictionary
        {
            ["Bg"] = B(Bg),
            ["Accent"] = B(Accent),
            ["Accent2"] = B(Accent2),
            ["AccentColor"] = Accent,
            ["AccentGrad"] = Grad(tl, br, (GradA, 0), (GradB, 1)),
            ["OnAccent"] = B(white),
            ["AccentOnWhite"] = B(GradA),
            ["AccentSoft"] = B(A(Accent2, 0x1F)),
            ["AccentSoft2"] = B(A(Accent2, 0x2C)),
            ["AccentLine"] = B(A(Accent2, 0x55)),
            ["AccentEdgeColor"] = A(Accent2, 0xB8),
            ["NavSel"] = Grad(tl, new(1, 0), (A(Accent, 0x78), 0), (A(Accent, 0x30), 1)),
            // the main card of a page: black at the top, red rising from the bottom
            ["HeroBg"] = Grad(top, bottom, (A(C("#0E0E10"), 0xCC), 0), (A(Mix(C("#101012"), Glow, 0.16), 0xCC), 0.42), (A(Mix(GradA, black, 0.30), 0xDC), 1)),

            ["Text"] = B(C("#F6F6F7")),
            ["Soft"] = B(Tint("#CBCBD0", 0.04)),
            ["Muted"] = B(Tint("#9C9CA3")),
            ["Dim"] = B(Tint("#6C6C73")),
            // glass: see-through, with a little of the red coming up from below
            ["CardBg"] = Grad(top, bottom, (A(Tint("#151517"), 0xBA), 0), (A(Tint("#131315"), 0xBA), 0.55), (A(Mix(C("#141416"), Glow, 0.26), 0xC2), 1)),
            ["CardEdgeColor"] = A(white, 0x48),
            ["CardBorder"] = Grad(tl, br, (A(white, 0x48), 0), (A(white, 0x1E), 0.5), (A(white, 0x34), 1)),
            ["Pill"] = B(A(Tint("#1C1C1F"), 0xD0)),
            ["PillHover"] = B(Tint("#2B2B2F", 0.08)),
            ["Line"] = B(A(white, 0x16)),
            ["TopPill"] = B(A(Tint("#141416"), 0xB0)),
            ["TopPill2"] = B(A(Tint("#101012"), 0xD0)),
            ["Panel"] = B(A(Tint("#17171A"), 0xF6)),
            ["Overlay"] = B(C("#CC060607")),

            ["Green"] = B(C("#3DDC97")),
            ["Red"] = B(C("#FF5C5C")),
            ["Purple"] = B(C("#B57BEE")),
            ["Yellow"] = B(C("#F5B544")),
        };
        for (int i = 0; i < Series.Length; i++) d[$"Series{i + 1}"] = B(C(Series[i]));

        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(d);
        merged.Add(new ResourceDictionary { Source = new Uri("/Atomic;component/Themes/Theme.xaml", UriKind.Relative) });
    }
}
