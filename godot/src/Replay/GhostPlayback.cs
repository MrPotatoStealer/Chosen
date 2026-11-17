using Chosen.Sim;
using Godot;

namespace Chosen.Replay;

/// <summary>
/// Plays a recording's player path back as a camera, one recorded frame per game frame: the eye at the recorded
/// body position and view height (zView, absolute Build z), looking along the recorded view angles. Checks that a
/// level, its coordinates and the game clock line up with the DarkPlaces version before any game logic exists.
/// </summary>
public sealed class GhostPlayback
{
    readonly Recording _rec;
    readonly int _origin, _zView, _angles, _look;
    int _index;
    Vector3 _prevEye, _eye, _prevDir, _dir;

    public GhostPlayback(Recording rec)
    {
        _rec = rec;
        _origin = rec.FieldIndex("origin");
        _zView = rec.FieldIndex("zView");
        _angles = rec.FieldIndex("v_angle");
        _look = rec.FieldIndex("look");
        Sample(0);
        _prevEye = _eye;
        _prevDir = _dir;
    }

    public bool Finished => _index >= _rec.Frames.Count - 1;
    public int FrameNumber => _rec.Frames[_index].Number;

    /// <summary>One game frame on.</summary>
    public void Step()
    {
        if (Finished)
            return;
        _prevEye = _eye;
        _prevDir = _dir;
        Sample(++_index);
    }

    void Sample(int i)
    {
        var v = _rec.Frames[i].Values;
        var o = Recording.Vec(v[_origin]);
        var a = Recording.Vec(v[_angles]);
        float pitch = a.X;
        if (_zView >= 0)
            o.Z = Coords.BuildZToQuake(Recording.Float(v[_zView]));
        if (_look >= 0)
        {
            // Blood looks up and down by shearing the view (horiz = 100 tan(look)); the game draws it as the pitch
            // that keeps the aim centred, atan(horiz / 128), and raises the eye by horiz * 10 Build z units
            float horiz = 100f * Mathf.Tan(Recording.Float(v[_look]) * Mathf.Pi / 1024f);
            pitch = -Mathf.RadToDeg(Mathf.Atan2(horiz, 128f));
            o.Z += horiz * 10f / 256f;
        }
        _eye = Coords.QuakeToGodot(o);
        _dir = Coords.QuakeAnglesToGodotDir(pitch, a.Y);
    }

    /// <summary>Places the camera between the last two frames (lerp 0..1), like the game's interpolated view.</summary>
    public void Apply(Camera3D cam, float lerp)
    {
        var eye = _prevEye.Lerp(_eye, lerp);
        var dir = _prevDir.Lerp(_dir, lerp).Normalized();
        cam.GlobalPosition = eye;
        if (dir.LengthSquared() > 0.0001f)
            cam.LookAt(eye + dir, Vector3.Up);
    }
}
