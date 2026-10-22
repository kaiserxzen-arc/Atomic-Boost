namespace Atomic.Core;

/// <summary>
/// Turns the CPU's turbo behaviour down in steps when a game session runs too hot and remembers the step this PC needs
/// (Settings.HeatLevel). The step is only in force while a game runs: the game mode applies it when a session starts
/// and restores the original values when it ends, so everything else keeps the full CPU speed.
/// </summary>
public static class HeatGuard
{
    const string Processor = "54533251-82be-4824-96c1-47b60b740d00";
    const string BoostMode = "be337238-0d82-4146-a960-4f3749d470c7";      // 0 = turbo off, 2 = aggressive
    const string MaxFrequency = "75b0ae3f-bce0-45a7-8c89-c9611c25e100";   // MHz, 0 = no cap
    public const int MaxLevel = 2;
    /// <summary>Tweaks that ask for more turbo - not recommended any more once the guard had to step in.</summary>
    public static readonly string[] HeatTweaks = ["pwr_boost", "pwr_epp"];

    static Settings S => AppState.Settings;

    /// <summary>The plan in use and, on top of it, the Windows power mode: while a power mode is active its own values win.</summary>
    static List<Guid> Schemes()
    {
        var list = new List<Guid> { Power.ActiveScheme() };
        if (Power.GetOverlay() is Guid overlay && overlay != Guid.Empty) list.Add(overlay);
        return list;
    }

    /// <summary>Step 1 keeps part of the turbo range (about 15% above the base clock), step 2 switches turbo off.</summary>
    static List<(string Setting, uint Value)> Level(int level)
    {
        var l = new List<(string, uint)>();
        int baseMhz = AppState.Pc.CpuClock;
        if (level >= 1 && baseMhz > 0) l.Add((MaxFrequency, (uint)(Math.Round(baseMhz * 1.15 / 100) * 100)));
        if (level >= 2 || baseMhz <= 0) l.Add((BoostMode, 0));
        return l;
    }

    /// <summary>Makes Windows take the changed values into use now.</summary>
    static void Reapply()
    {
        if (Power.GetOverlay() is Guid overlay && overlay != Guid.Empty) Power.SetOverlay(overlay);
        Power.Activate(Power.ActiveScheme());
    }

    /// <summary>Applies a step (plugged-in values) and remembers what each setting was before the first change.</summary>
    public static bool Apply(int level)
    {
        try
        {
            foreach (var scheme in Schemes())
                foreach (var (setting, value) in Level(level))
                {
                    string key = $"{scheme}|{setting}";
                    if (!S.HeatSaved.ContainsKey(key) && Power.Get(scheme, Processor, setting, true) is uint was) S.HeatSaved[key] = was;
                    Power.Set(scheme, Processor, setting, value, true);
                }
            Reapply();
            S.HeatLevel = level;
            Sys.Log($"HEAT GUARD  step {level}: " + string.Join(", ", Level(level).Select(x => x.Setting == BoostMode ? "turbo boost off" : $"CPU capped at {x.Value} MHz")));
            return true;
        }
        catch (Exception ex) { Sys.Log("heat guard: " + ex.Message); return false; }
    }

    /// <summary>Puts every setting the guard changed back to its saved value; the step this PC needs stays remembered.</summary>
    public static void Restore()
    {
        if (S.HeatSaved.Count == 0) return;
        foreach (var (key, value) in S.HeatSaved)
        {
            var part = key.Split('|');
            if (part.Length == 2 && Guid.TryParse(part[0], out var scheme)) Power.Set(scheme, Processor, part[1], value, true);
        }
        Reapply();
        S.HeatSaved = [];
        Sys.Log("HEAT GUARD  original CPU power values restored");
    }

    /// <summary>Restores the values and forgets the step: this PC starts again without a turbo limit.</summary>
    public static void Reset()
    {
        Restore();
        S.HeatLevel = 0;
        Sys.Log("HEAT GUARD  reset");
    }

    /// <summary>What the current step means, for the UI.</summary>
    public static string Describe() => S.HeatLevel switch
    {
        0 => "",
        1 when AppState.Pc.CpuClock > 0 => L.F("CPU turbo limited to {0:N1} GHz", Level(1)[0].Value / 1000.0),
        _ => L.T("CPU turbo boost switched off")
    };

    /// <summary>Once the guard stepped in, the tweaks that raise turbo are no longer recommended on this PC.</summary>
    public static void MarkTweaks(IEnumerable<Tweak> tweaks)
    {
        if (S.HeatLevel == 0) return;
        const string note = " Not recommended on this PC any more: Heat Guard had to turn turbo down because it ran too hot in games.";
        const string noteTh = " ไม่แนะนำบนเครื่องนี้แล้ว เพราะ Heat Guard ต้องลด turbo ลงเนื่องจากเครื่องร้อนเกินไปตอนเล่นเกม";
        foreach (var t in tweaks.Where(t => HeatTweaks.Contains(t.Id)))
        {
            t.Rec = false;
            if (!t.Desc.EndsWith(note)) t.Desc += note;
            if (t.DescTh != null && !t.DescTh.EndsWith(noteTh)) t.DescTh += noteTh;
        }
    }
}
