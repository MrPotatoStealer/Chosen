using Chosen.Core;
using Chosen.Sim;
using Godot;

namespace Chosen.Client;

/// <summary>
/// The local player's controls (Blood3D's default bindings): mouse look, WASD, Space jump, C/Ctrl crouch, E use,
/// mouse buttons fire, 1-0 and the wheel pick weapons, Enter/[ ]/B/J/K/M items, Backspace turns around, Shift walks
/// (always run is on). The view angles live here, client side, like Blood3D's; the server turning the player
/// (FixAngle: movers, spin, teleports) sets them.
/// </summary>
public sealed class PlayerControls
{
    public float Yaw, Pitch;                 // Quake degrees: pitch down positive
    int _pendingImpulse;
    public static int WalkFrames;            // the walk command (testing): run forward for this many game frames
    public static int WalkDelay;             // after this many game frames
    public static int UseFrames;             // the use command (testing): press use for this many game frames

    static readonly Cvar Sensitivity = Cvars.Register("sensitivity", "3", "mouse sensitivity", saved: true);
    static readonly Cvar InvertMouse = Cvars.Register("m_invert", "0", "1: mouse up looks down", saved: true);
    static readonly Cvar AlwaysRun = Cvars.Register("cl_alwaysrun", "1", "1: run unless Shift is held", saved: true);

    public void MouseMotion(Vector2 rel)
    {
        float s = Sensitivity.Float * 0.022f;
        Yaw -= rel.X * s;
        Pitch += rel.Y * s * (InvertMouse.Bool ? -1 : 1);
        // Blood's look range (+289 up / -347 down Build angle units, about 51 and 61 degrees), as the view and the
        // player clamp it: kept past it, the pitch would have to come back before the view moves again
        Pitch = Mathf.Clamp(Pitch, -289f * 360f / 2048f, 347f * 360f / 2048f);
        Yaw = Mathf.PosMod(Yaw, 360f);
    }

    /// <summary>Keys that pick a weapon or an item (impulses): latched until the next game frame.</summary>
    public bool KeyPressed(InputEventKey k)
    {
        if (!k.Pressed || k.Echo)
            return false;
        int imp = k.Keycode switch
        {
            >= Key.Key1 and <= Key.Key9 => (int)(k.Keycode - Key.Key1) + 1,
            Key.Key0 => 10,
            Key.Enter => 20,
            Key.Bracketright => 21,
            Key.Bracketleft => 22,
            Key.M => 23,
            Key.K => 24,
            Key.B => 25,
            Key.J => 26,
            Key.Backspace => 27,
            Key.Q => 13,
            Key.X => 14,
            _ => 0,
        };
        if (imp == 0)
            return false;
        _pendingImpulse = imp;
        return true;
    }

    public void Wheel(bool up) => _pendingImpulse = up ? 12 : 11;

    /// <summary>The input for the next game frame.</summary>
    public PlayerInput Build(bool active)
    {
        var inp = new PlayerInput { Pitch = Pitch < 0 ? Pitch + 360 : Pitch, Yaw = Yaw };
        if (WalkDelay > 0)
            WalkDelay--;
        else if (WalkFrames > 0)
        {
            WalkFrames--;
            inp.Forward = 2048;
        }
        if (UseFrames > 0)
        {
            UseFrames--;
            inp.Use = true;
        }
        if (!active)
            return inp;
        float speed = AlwaysRun.Bool != Input.IsKeyPressed(Key.Shift) ? 2048 : 1024;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) inp.Forward += speed;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) inp.Forward -= speed;
        if (Input.IsKeyPressed(Key.A)) inp.Strafe += speed;
        if (Input.IsKeyPressed(Key.D)) inp.Strafe -= speed;
        inp.Jump = Input.IsKeyPressed(Key.Space);
        inp.Crouch = Input.IsKeyPressed(Key.C) || Input.IsKeyPressed(Key.Ctrl);
        inp.Use |= Input.IsKeyPressed(Key.E);
        inp.Fire = Input.IsMouseButtonPressed(MouseButton.Left);
        inp.AltFire = Input.IsMouseButtonPressed(MouseButton.Right);
        inp.Impulse = _pendingImpulse;
        _pendingImpulse = 0;
        return inp;
    }

    float _turnLeft, _turnRate;      // a mover's turn of the view still to come, and its pace (degrees a second)

    /// <summary>The server turned the player: the view follows (a mover's turn over the next game frame).</summary>
    public void Sync(Player p)
    {
        if (p.MoverTurn != 0)
        {
            _turnLeft += p.MoverTurn;
            _turnRate = Mathf.Abs(_turnLeft) * GameClock.FramesPerSecond;
            p.MoverTurn = 0;
        }
        if (!p.FixAngle)
            return;
        p.FixAngle = false;
        _turnLeft = 0;
        Yaw = Mathf.PosMod((float)Blood.AngToYaw(p.Ang), 360f);
    }

    /// <summary>Each drawn frame: the part of a mover's turn due by now (E2M5's revolving bookcase).</summary>
    public void Turn(double delta)
    {
        if (_turnLeft == 0)
            return;
        float step = Mathf.Min(Mathf.Abs(_turnLeft), _turnRate * (float)delta) * Mathf.Sign(_turnLeft);
        _turnLeft -= step;
        Yaw = Mathf.PosMod(Yaw + step, 360f);
    }
}
