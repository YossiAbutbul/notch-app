using System.Windows.Media;
using NotchApp.Settings;

namespace NotchApp.Services;

public enum Phase { Focus, Break, LongBreak }

/// <summary>Focus → break → focus … → long break after the last session of a round.</summary>
public sealed class FocusTimer
{
    private readonly TimerSettings _cfg;
    public Phase Phase { get; private set; } = Phase.Focus;
    public int Session { get; private set; } = 1;
    public double Total { get; private set; }
    public double Left { get; private set; }
    public bool Running { get; private set; }
    /// <summary>Started and not reset: the compact pill shows while this is true.</summary>
    public bool Active => Running || Left < Total - 0.5;

    public event Action? Changed;
    /// <summary>(phase that ended, phase that starts)</summary>
    public event Action<Phase, Phase>? PhaseEnded;

    public FocusTimer(TimerSettings cfg)
    {
        _cfg = cfg;
        Total = Left = Minutes(Phase) * 60;
    }

    public int Minutes(Phase p) => p switch { Phase.Focus => _cfg.Focus, Phase.Break => _cfg.ShortBreak, _ => _cfg.LongBreak };
    public int Sessions => _cfg.Sessions;
    public DateTime EndsAt => DateTime.Now.AddSeconds(Left);

    public static string Label(Phase p) => p switch { Phase.Focus => "Focus", Phase.Break => "Break", _ => "Long break" };
    public static Color ColorOf(Phase p) => p switch
    {
        Phase.Focus => Color.FromRgb(0xFF, 0xB5, 0x47),
        Phase.Break => Color.FromRgb(0x34, 0xC7, 0x6F),
        _ => Color.FromRgb(0x3D, 0x8B, 0xFF),
    };

    public void Tick(double seconds)
    {
        if (!Running) return;
        Left -= seconds;
        if (Left <= 0) Advance();
    }

    public void Toggle() { Running = !Running; Changed?.Invoke(); }
    public void Start() { Running = true; Changed?.Invoke(); }
    public void AddMinutes(int m) { Left += m * 60; Total += m * 60; Changed?.Invoke(); }
    public void RestartPhase() { Total = Left = Minutes(Phase) * 60; Changed?.Invoke(); }

    /// <summary>Settings changed: a new length for the running phase restarts it at that length.</summary>
    public void SettingsChanged()
    {
        if (Session > _cfg.Sessions) Session = _cfg.Sessions;
        if (Math.Abs(Total - Minutes(Phase) * 60) > 0.5 && Left >= Total - 0.5) Total = Left = Minutes(Phase) * 60;
        Changed?.Invoke();
    }

    /// <summary>A length was edited: if it's the phase that's running, restart it at the new length (running or not).</summary>
    public void LengthChanged(Phase p)
    {
        if (p == Phase) Total = Left = Minutes(p) * 60;
        Changed?.Invoke();
    }

    private void Advance()
    {
        var was = Phase;
        if (Phase == Phase.Focus) Phase = Session >= _cfg.Sessions ? Phase.LongBreak : Phase.Break;
        else { Session = Phase == Phase.LongBreak ? 1 : Session + 1; Phase = Phase.Focus; }
        Total = Left = Minutes(Phase) * 60;
        Running = _cfg.AutoStart;
        PhaseEnded?.Invoke(was, Phase);
        Changed?.Invoke();
    }
}
