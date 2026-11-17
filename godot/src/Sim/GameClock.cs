namespace Chosen.Sim;

/// <summary>
/// Blood's fixed game rate: 30 frames per second (kFrameTime). The renderer runs at any rate and interpolates
/// between the last two game frames; <see cref="Lerp"/> is how far between them it is.
/// </summary>
public sealed class GameClock
{
    public const int FramesPerSecond = 30;
    public const double FrameTime = 1.0 / FramesPerSecond;
    public const int TicsPerFrame = 4;           // Blood's 120 Hz totalclock advances 4 per game frame

    public int Frame { get; private set; }
    public double Speed { get; set; } = 1.0;     // > 1: faster than real time (replays); every frame still runs
    double _accum;

    /// <summary>lerphold (debugging): when 0..1, no game frames run and everything is drawn that far between the last
    /// two (a glitch of one blended frame, held to look at); below 0 off.</summary>
    public float Hold = -1;

    /// <summary>Real seconds passed; returns how many game frames are due now (capped, so a hitch doesn't snowball).</summary>
    public int Advance(double delta)
    {
        if (Hold >= 0)
            return 0;
        _accum += delta * Speed;
        double cap = 0.25 * System.Math.Max(1.0, Speed);
        if (_accum > cap)
            _accum = cap;
        int n = 0;
        while (_accum >= FrameTime)
        {
            _accum -= FrameTime;
            n++;
        }
        return n;
    }

    public void Step() => Frame++;

    public void Reset(int frame = 0)
    {
        Frame = frame;
        _accum = 0;
    }

    public float Lerp => Hold >= 0 ? Hold : (float)(_accum / FrameTime);

    /// <summary>Drawn at the last game frame itself (Lerp 1), not between it and the one before (a held shot).</summary>
    public void AtFrame() => _accum = FrameTime;
}
