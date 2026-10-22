using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Atomic.Core;

namespace Atomic.Controls;

static class ChartUtil
{
    public static Brush Frozen(string hex, double opacity = 1)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    /// <summary>Smooth curve through the points (midpoint quadratic beziers).</summary>
    public static StreamGeometry Curve(IReadOnlyList<Point> pts, double? closeToY = null)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], closeToY != null, closeToY != null);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var mid = new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2);
                c.QuadraticBezierTo(pts[i], mid, true, true);
            }
            c.LineTo(pts[^1], true, true);
            if (closeToY is double y)
            {
                c.LineTo(new Point(pts[^1].X, y), false, false);
                c.LineTo(new Point(pts[0].X, y), false, false);
            }
        }
        g.Freeze();
        return g;
    }

    public static List<Point> Map(IReadOnlyList<double> v, int capacity, double x0, double w, double top, double h, double max, double min = 0)
    {
        var pts = new List<Point>(v.Count);
        double step = w / (capacity - 1);
        int offset = capacity - v.Count;   // newest sample sits at the right edge
        for (int i = 0; i < v.Count; i++)
            pts.Add(new Point(x0 + (offset + i) * step, top + h - Math.Clamp((v[i] - min) / (max - min), 0, 1) * h));
        return pts;
    }
}

/// <summary>Small filled sparkline with a dashed average line and a marker on the newest value.</summary>
public class Spark : FrameworkElement
{
    IReadOnlyList<double> _values = [];
    Color _color = Colors.White;
    Pen _pen = new(Brushes.White, 1.6), _dash = new(Brushes.Gray, 1);
    Brush _fill = Brushes.Transparent, _dot = Brushes.White;
    static readonly Brush DotRing = ChartUtil.Frozen("#151517");

    public int Capacity { get; set; } = 60;

    public string Stroke
    {
        get => _color.ToString();
        set
        {
            _color = (Color)ColorConverter.ConvertFromString(value);
            _dot = new SolidColorBrush(_color); _dot.Freeze();
            _pen = new Pen(_dot, 1.6) { LineJoin = PenLineJoin.Round }; _pen.Freeze();
            _dash = new Pen(new SolidColorBrush(_color) { Opacity = 0.45 }, 1) { DashStyle = new DashStyle([3, 4], 0) }; _dash.Freeze();
            var grad = new LinearGradientBrush(Color.FromArgb(70, _color.R, _color.G, _color.B), Color.FromArgb(0, _color.R, _color.G, _color.B), 90);
            grad.Freeze(); _fill = grad;
        }
    }

    public void Set(IReadOnlyList<double> values) { _values = values; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (_values.Count < 2 || w < 10 || h < 10) return;
        // scale to the visible range so small changes still show and the line sits mid-card
        double lo = _values.Min(), hi = _values.Max(), span = Math.Max(8, hi - lo);
        lo = Math.Max(0, lo - span * 0.9); hi += span * 0.5;
        var pts = ChartUtil.Map(_values, Capacity, 0, w, 6, h - 12, hi, lo);
        dc.DrawGeometry(_fill, null, ChartUtil.Curve(pts, h));
        double avgY = 6 + (h - 12) - Math.Clamp((_values.Average() - lo) / (hi - lo), 0, 1) * (h - 12);
        dc.DrawLine(_dash, new Point(0, avgY), new Point(w, avgY));
        dc.DrawGeometry(null, _pen, ChartUtil.Curve(pts));
        var last = pts[^1]; last.X -= 5;
        dc.DrawEllipse(DotRing, null, last, 5.5, 5.5);
        dc.DrawEllipse(_dot, null, last, 3.2, 3.2);
    }
}

/// <summary>Multi-series 0-100% line chart with a labelled grid.</summary>
public class MultiChart : FrameworkElement
{
    public record Series(string Name, string Color, Func<double[]> Values);

    readonly List<(Series S, Pen Pen)> _series = [];
    static readonly Pen Grid = MakePen("#FFFFFF", 0.06, 1);
    static readonly Brush Label = ChartUtil.Frozen("#8E8E96");
    static readonly Typeface Face = new("Segoe UI");
    public HashSet<string> Hidden { get; } = [];
    public int Capacity { get; set; } = 60;

    static Pen MakePen(string hex, double opacity, double thickness)
    {
        var p = new Pen(ChartUtil.Frozen(hex, opacity), thickness) { LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    public void Add(Series s) => _series.Add((s, MakePen(s.Color, 1, 1.5)));
    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60 || h < 40) return;
        const double left = 34, bottom = 22, top = 6;
        double ph = h - bottom - top, pw = w - left;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int i = 0; i <= 4; i++)
        {
            double y = top + ph - ph * i / 4;
            dc.DrawLine(Grid, new Point(left, y), new Point(w, y));
            var ft = new FormattedText((i * 25).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, Label, dpi);
            dc.DrawText(ft, new Point(0, y - ft.Height / 2));
        }
        for (int i = 0; i <= 6; i++)
        {
            int sec = Capacity - i * Capacity / 6;
            var ft = new FormattedText(sec == 0 ? L.T("now") : $"-{sec}s", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, Label, dpi);
            double x = left + pw * i / 6;
            dc.DrawText(ft, new Point(Math.Min(x, w - ft.Width), h - ft.Height));
        }
        foreach (var (s, pen) in _series)
        {
            if (Hidden.Contains(s.Name)) continue;
            var v = s.Values();
            if (v.Length < 2) continue;
            dc.DrawGeometry(null, pen, ChartUtil.Curve(ChartUtil.Map(v, Capacity, left, pw, top, ph, 100)));
        }
    }
}

/// <summary>
/// Half-circle gauge for a value from 0 to 100: the arc is red up to the value and grey beyond it, a thin needle points
/// at the value, and a base line carries the 0% and 100% marks.
/// </summary>
public class Gauge : FrameworkElement
{
    /// <summary>Room under the arc for the base line and its two marks.</summary>
    const double Foot = 20;
    static readonly Brush Done = Across(Palette.Mix(Palette.GradA, Colors.Black, 0.45), Palette.Accent, Palette.Accent2);
    static readonly Brush Rest = Across(Color.FromRgb(0x4A, 0x4A, 0x52), Color.FromRgb(0x8E, 0x8E, 0x98), Color.FromRgb(0xE4, 0xE4, 0xEA));
    static readonly Pen Needle = MakePen(Color.FromArgb(0x90, 255, 255, 255), 1.2), Base = MakePen(Color.FromArgb(0x38, 255, 255, 255), 1);

    static Brush Across(params Color[] colours)
    {
        var b = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        for (int i = 0; i < colours.Length; i++) b.GradientStops.Add(new(colours[i], i / (colours.Length - 1.0)));
        b.Freeze();
        return b;
    }

    static Pen MakePen(Color c, double thickness) { var p = new Pen(new SolidColorBrush(c), thickness); p.Freeze(); return p; }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Gauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get; set; } = 26;

    /// <summary>Sweeps the gauge to the new value.</summary>
    public void AnimateTo(double value) =>
        BeginAnimation(ValueProperty, new DoubleAnimation(Math.Clamp(value, 0, 100), TimeSpan.FromSeconds(1.1)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40) return;
        double pad = Thickness / 2 + 2;
        double r = Math.Min(w / 2 - pad, h - Foot - pad);
        if (r < 10) return;
        var c = new Point(w / 2, h - Foot);
        double fpos = Math.Clamp(Value, 0, 100) / 100;

        Point At(double f, double radius) { double a = Math.PI * (1 - f); return new Point(c.X + radius * Math.Cos(a), c.Y - radius * Math.Sin(a)); }

        void Arc(Brush brush, double from, double to)
        {
            if (to - from < 0.004) return;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(At(from, r), false, false);
                ctx.ArcTo(At(to, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(brush, Thickness), g);
        }

        // the two brushes run left to right across the whole gauge, so each part shows its own stretch of the gradient
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, c.Y)));
        Arc(Rest, Math.Min(1, fpos + 0.006), 1);
        Arc(Done, 0, fpos);
        dc.Pop();
        dc.DrawLine(Needle, At(fpos, r * 0.64), At(fpos, r - Thickness / 2 - 4));   // short, so it never crosses the number

        // base line with the ends of the scale under it
        dc.DrawLine(Base, new Point(c.X - r * 0.62, c.Y + 5), new Point(c.X + r * 0.62, c.Y + 5));
        var face = new Typeface((FontFamily)FindResource("UiFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Mark(string text) => new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 10, (Brush)FindResource("Muted"), dip);
        FormattedText zero = Mark("0%"), full = Mark("100%");
        dc.DrawText(zero, new Point(c.X - r - zero.Width / 2, c.Y + 4));
        dc.DrawText(full, new Point(c.X + r - full.Width / 2, c.Y + 4));
    }
}
