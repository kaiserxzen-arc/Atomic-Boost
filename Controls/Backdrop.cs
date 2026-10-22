using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Atomic.Controls;

/// <summary>
/// Window backdrop: a soft, molten picture - a ribbon of red-hot light flowing across black, with dark shapes pushing
/// into it. It is computed once as a small bitmap (everything in it is blurred anyway) and stretched over the window,
/// so nothing behind the UI moves or costs CPU while the app is open.
/// </summary>
public class Backdrop : FrameworkElement
{
    const int W = 384, H = 256;
    /// <summary>Colour for a heat from 0 (black) to 1 (the white-hot middle of the ribbon), as (heat, R, G, B).</summary>
    static readonly (double At, byte R, byte G, byte B)[] Ramp =
    [
        (0.00, 5, 3, 4), (0.22, 44, 4, 10), (0.46, 150, 9, 26), (0.68, 236, 30, 52), (0.84, 255, 112, 104), (1.00, 255, 222, 208),
    ];
    static readonly ImageSource Picture = Paint();

    public Backdrop()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawImage(Picture, new Rect(0, 0, ActualWidth, ActualHeight));

    /// <summary>Heat at a point of the picture (both coordinates 0..1): the ribbon plus a glow from the top, minus the dark shapes.</summary>
    static double Heat(double x, double y)
    {
        // bend the plane a little so no edge in the picture is straight
        double u = x + 0.05 * Math.Sin(6.1 * y + 0.8), v = y + 0.045 * Math.Sin(5.2 * x + 0.3);
        double centre = 0.22 + 0.36 * u + 0.17 * Math.Sin(5.0 * u + 0.6), width = 0.105 + 0.05 * Math.Sin(3.1 * u + 1.2);
        double ribbon = Math.Exp(-Math.Pow((v - centre) / width, 2));
        double above = 0.40 * Math.Clamp(1 - v * 1.35, 0, 1) * (0.55 + 0.45 * (1 - u));
        static double Blob(double u, double v, double cx, double cy, double rx, double ry) => Math.Exp(-(Math.Pow((u - cx) / rx, 2) + Math.Pow((v - cy) / ry, 2)));
        double dark = 1.0 * Blob(u, v, 0.74, 0.60, 0.20, 0.24) + 0.9 * Blob(u, v, 0.16, 1.02, 0.30, 0.30) + 0.8 * Blob(u, v, 0.98, 0.05, 0.22, 0.26);
        return Math.Clamp(above + 0.92 * ribbon - dark, 0, 1);
    }

    static BitmapSource Paint()
    {
        var px = new byte[W * H * 4];
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                double heat = Heat((i + 0.5) / W, (j + 0.5) / H);
                int k = 1;
                while (k < Ramp.Length - 1 && heat > Ramp[k].At) k++;
                var (a, b) = (Ramp[k - 1], Ramp[k]);
                double t = (heat - a.At) / (b.At - a.At);
                int o = (j * W + i) * 4;
                px[o] = (byte)(a.B + (b.B - a.B) * t);
                px[o + 1] = (byte)(a.G + (b.G - a.G) * t);
                px[o + 2] = (byte)(a.R + (b.R - a.R) * t);
                px[o + 3] = 255;
            }
        var picture = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
        picture.Freeze();
        return picture;
    }
}
