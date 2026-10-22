using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Atomic.Core;

namespace Atomic.Controls;

/// <summary>Animation helpers shared by the window and the pages.</summary>
public static class Fx
{
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction Back = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
    static readonly IEasingFunction InOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static Duration Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    /// <summary>Fades and slides an element into place.</summary>
    public static void Enter(FrameworkElement el, double fromX = 0, double fromY = 16, int delayMs = 0, int ms = 340)
    {
        var move = new TranslateTransform(fromX, fromY);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        el.RenderTransform = move;
        el.Opacity = 0;   // stays hidden until its turn; the animation then holds it at 1
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(ms)) { BeginTime = begin, EasingFunction = EaseOut });
        if (fromX != 0) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, Ms(ms)) { BeginTime = begin, EasingFunction = EaseOut });
        if (fromY != 0) move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, Ms(ms)) { BeginTime = begin, EasingFunction = EaseOut });
    }

    /// <summary>
    /// Page entrance: the whole page fades and rises a little, once. While it does, the page is one cached picture,
    /// so the animation costs the same however much is on the page.
    /// </summary>
    public static void PageIn(FrameworkElement page)
    {
        var move = new TranslateTransform(0, 10);
        var fade = new DoubleAnimation(0, 1, Ms(170)) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => page.CacheMode = null;
        page.RenderTransform = move;
        page.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(page).DpiScaleX);
        page.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, Ms(200)) { EasingFunction = EaseOut });
    }

    /// <summary>Shows an overlay with its card popping in.</summary>
    public static void PopIn(FrameworkElement overlay, FrameworkElement card)
    {
        var scale = new ScaleTransform(0.94, 0.94);
        var grow = new DoubleAnimation(0.94, 1, Ms(280)) { EasingFunction = Back };
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = scale;
        overlay.Visibility = Visibility.Visible;
        overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(160)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    public static void FadeOut(FrameworkElement el, Action done, int ms = 260)
    {
        var fade = new DoubleAnimation(0, Ms(ms));
        fade.Completed += (_, _) => done();
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // ------------------------------------------------------------ animated number
    static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached("Number", typeof(double), typeof(Fx),
        new PropertyMetadata(0d, (d, e) => ((TextBlock)d).Text = ((double)e.NewValue).ToString("N0")));

    /// <summary>Counts the number shown in a TextBlock up (or down) to the new value.</summary>
    public static void CountTo(TextBlock text, double value, int ms = 900) =>
        text.BeginAnimation(NumberProperty, new DoubleAnimation(value, Ms(ms)) { EasingFunction = EaseOut });

    /// <summary>
    /// Accent lights on a border. They travel once around it when the card appears and when the pointer enters it:
    /// nothing in the window animates by itself.
    /// </summary>
    public static void GlowBorder(Border border)
    {
        Color a = Palette.Accent, b = Palette.Accent2, dim = Color.FromArgb(0x20, 255, 255, 255);
        var spin = new RotateTransform(0, 0.5, 0.5);
        border.BorderBrush = new LinearGradientBrush(new GradientStopCollection { new(a, 0), new(dim, 0.34), new(dim, 0.66), new(b, 1) }, new Point(0, 0), new Point(1, 1)) { RelativeTransform = spin };
        void Sweep() => spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4)) { EasingFunction = InOut });
        border.MouseEnter += (_, _) => Sweep();
        border.IsVisibleChanged += (_, _) => { if (border.IsVisible) Sweep(); };
    }
}
