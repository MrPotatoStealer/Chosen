using Godot;

namespace Chosen.Sim;

/// <summary>
/// Coordinate systems. The game's world is in Quake units: x east, y north, z up. Blood's own units are
/// 16 per Quake unit horizontally and 256 per Quake unit vertically (z down). Godot space is func_godot's: Godot
/// (x, y, z) = Quake (y, z, x) / 32.
/// </summary>
public static class Coords
{
    public const float QuakeScale = 32f;          // Quake units per Godot unit (func_godot's inverse scale factor)

    public static Vector3 QuakeToGodot(Vector3 q) => new Vector3(q.Y, q.Z, q.X) / QuakeScale;
    public static Vector3 GodotToQuake(Vector3 g) => new Vector3(g.Z, g.X, g.Y) * QuakeScale;

    /// <summary>Quake view angles (pitch down positive, yaw counter-clockwise, degrees) to a Godot direction.</summary>
    public static Vector3 QuakeAnglesToGodotDir(float pitch, float yaw)
    {
        float p = Mathf.DegToRad(pitch), y = Mathf.DegToRad(yaw);
        var q = new Vector3(Mathf.Cos(y) * Mathf.Cos(p), Mathf.Sin(y) * Mathf.Cos(p), -Mathf.Sin(p));
        return new Vector3(q.Y, q.Z, q.X);
    }

    /// <summary>Build z (down, 256 per Quake unit) to Quake z.</summary>
    public static float BuildZToQuake(float bz) => -bz / 256f;
}
