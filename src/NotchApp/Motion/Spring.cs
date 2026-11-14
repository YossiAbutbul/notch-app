namespace NotchApp.Motion;

/// <summary>
/// Apple-style spring parameters: damping ratio (1 = no overshoot) and response (seconds, not a duration).
/// stiffness = (2π / response)², damping = 4πζ / response, mass 1 — the same model as the motion spec.
/// </summary>
public readonly record struct SpringParams(double DampingRatio, double Response)
{
    public double Stiffness => Math.Pow(2 * Math.PI / Response, 2);
    public double Damping => 4 * Math.PI * DampingRatio / Response;

    public static readonly SpringParams Press = new(1.0, 0.16);
}

/// <summary>Named "feels" from Settings → Appearance & motion.</summary>
public static class Feels
{
    public static (SpringParams Everyday, SpringParams Momentum) Get(string feel) => feel switch
    {
        "snappy" => (new(1.0, 0.26), new(0.84, 0.30)),
        "playful" => (new(0.84, 0.42), new(0.62, 0.50)),
        "calm" => (new(1.0, 0.52), new(0.95, 0.55)),
        _ => (new(1.0, 0.38), new(0.78, 0.42)), // fluid default
    };
}

/// <summary>
/// An interruptible spring: retargeting keeps the current value and velocity, so motion never jumps.
/// </summary>
public sealed class Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public SpringParams Params;
    /// <summary>Distance and speed under which the spring counts as at rest.</summary>
    public double Epsilon;
    /// <summary>When set, the spring falls back to these parameters once it settles (momentum → everyday).</summary>
    public SpringParams? RestoreTo;

    public Spring(double value, SpringParams p, double epsilon = 0.25)
    {
        Value = Target = value;
        Params = p;
        Epsilon = epsilon;
    }

    public bool IsSettled => Math.Abs(Velocity) < Epsilon * 4 && Math.Abs(Value - Target) < Epsilon;

    public void Snap(double v)
    {
        Value = Target = v;
        Velocity = 0;
    }

    /// <summary>Advance by dt seconds using fixed 1/240 s sub-steps (stable for snappy springs).</summary>
    public void Step(double dt)
    {
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
            if (RestoreTo is { } r) { Params = r; RestoreTo = null; }
            return;
        }
        int n = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 240)));
        double h = dt / n, k = Params.Stiffness, c = Params.Damping;
        for (int i = 0; i < n; i++)
        {
            double f = -k * (Value - Target) - c * Velocity;
            Velocity += f * h;
            Value += Velocity * h;
        }
    }

    /// <summary>Use the momentum spring until this motion settles.</summary>
    public void Kick(SpringParams momentum, SpringParams everyday)
    {
        Params = momentum;
        RestoreTo = everyday;
    }
}

public static class Gesture
{
    /// <summary>Where a flick would come to rest (Apple's projection, scroll-style deceleration).</summary>
    public static double Project(double velocity, double decelerationRate = 0.998)
        => velocity / 1000.0 * decelerationRate / (1 - decelerationRate);

    /// <summary>Progressive resistance past an edge.</summary>
    public static double RubberBand(double overshoot, double dimension, double constant = 0.55)
        => overshoot * dimension * constant / (dimension + constant * Math.Abs(overshoot));
}
