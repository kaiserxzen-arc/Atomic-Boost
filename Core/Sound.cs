using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Atomic.Core;

/// <summary>
/// Short sounds for the buttons that do something: the boost button, the red main buttons and the switches. Menus,
/// tabs, small buttons and icons stay silent. The sounds are synthesised when the app starts (no sound files).
/// </summary>
public static class Sound
{
    const int Rate = 44100;
    static SoundPlayer? _click, _on, _off, _boost;

    /// <summary>Call once at startup, after the styles are loaded.</summary>
    public static void Hook()
    {
        _click = Make(0.045, t => Tone(t, 1150, 60) * 0.20);
        // a switch: two quick notes, rising for on and falling for off
        _on = Make(0.11, t => (t < 0.05 ? Tone(t, 700, 55) : Tone(t - 0.05, 1050, 45)) * 0.18);
        _off = Make(0.11, t => (t < 0.05 ? Tone(t, 1050, 55) : Tone(t - 0.05, 700, 45)) * 0.18);
        // boost: a sweep upwards with an octave on top
        _boost = Make(0.34, t =>
        {
            double phase = 2 * Math.PI * (320 * t + 1500 * t * t);   // 320 Hz rising to about 1340 Hz
            double fade = Math.Min(1, t / 0.015) * Math.Exp(-5.5 * t);
            return (Math.Sin(phase) + 0.35 * Math.Sin(2 * phase)) * fade * 0.24;
        });
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), true);
    }

    static void OnClick(object sender, RoutedEventArgs e)
    {
        if (!AppState.Settings.Sounds || !ReferenceEquals(sender, e.OriginalSource)) return;
        var player = sender switch
        {
            CheckBox c when Is(c, "Toggle") => c.IsChecked == true ? _on : _off,
            Button b when Is(b, "BoostBtn") => _boost,
            Button b when Is(b, "AccentBtn") => _click,
            _ => null
        };
        try { player?.Play(); } catch { }   // no sound device: stay silent
    }

    static bool Is(FrameworkElement el, string style) => ReferenceEquals(el.Style, Application.Current.TryFindResource(style));

    /// <summary>One note that dies away: a sine with a fast attack and an exponential fall.</summary>
    static double Tone(double t, double hz, double decay) => Math.Sin(2 * Math.PI * hz * t) * Math.Min(1, t / 0.002) * Math.Exp(-decay * t);

    /// <summary>Renders a wave (value -1..1 for each time in seconds) into a 16-bit mono WAV held in memory.</summary>
    static SoundPlayer Make(double seconds, Func<double, double> wave)
    {
        int n = (int)(seconds * Rate);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
        {
            w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(n * 2);
            for (int i = 0; i < n; i++)
            {
                double tail = Math.Min(1, (n - i) / (Rate * 0.004));   // no click at the very end
                w.Write((short)(Math.Clamp(wave(i / (double)Rate) * tail, -1, 1) * short.MaxValue));
            }
        }
        ms.Position = 0;
        var player = new SoundPlayer(ms);
        player.Load();
        return player;
    }
}
