using Godot;

namespace Chosen.Sim;

/// <summary>Build's angle and tile helpers (2048 angle units a turn; angle 0 = +x, 512 = Build +y = Quake -y).</summary>
public static class BuildMath
{
    public static int AngMask(int a) => a & 2047;
    public static float AngToRad(float a) => a * Mathf.Pi / 1024f;

    /// <summary>Quake yaw (degrees, counter-clockwise) to a Build angle.</summary>
    public static int YawToAng(float yaw) => AngMask((int)System.Math.Round(-yaw * 2048.0 / 360.0, System.MidpointRounding.AwayFromZero));

    /// <summary>Blood's GetOctant (OctantTable): which of 8 rotations a sprite shows for a view vector.</summary>
    public static int GetOctant(float x, float y)
    {
        float vc = Mathf.Abs(x) - Mathf.Abs(y);
        int idx = 7;
        if (x < 0) idx -= 1;
        if (y < 0) idx -= 2;
        if (vc < 0) idx -= 4;
        return idx switch { 0 => 5, 1 => 6, 2 => 2, 3 => 1, 4 => 4, 5 => 7, 6 => 3, _ => 0 };
    }

    /// <summary>RotateVector: (dx, dy) turned by a Build angle.</summary>
    public static Vector2 RotateVector(float dx, float dy, float ang)
    {
        float r = AngToRad(ang), c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(dx * c - dy * s, dx * s + dy * c);
    }

    /// <summary>Build's animateoffs: a tile's animation frame offset at a 120 Hz clock.</summary>
    public static int AnimateOffs(int tile, double clock)
    {
        int num = Data.Tiles.AnimNum(tile);
        if (num <= 0)
            return 0;
        int type = Data.Tiles.AnimType(tile);
        long i = (long)System.Math.Floor(clock / (1 << Data.Tiles.AnimSpeed(tile)));
        switch (type)
        {
            case 1:
            {
                long k = i % (num * 2);
                return (int)(k < num ? k : num * 2 - k);
            }
            case 2:
                return (int)(i % (num + 1));
            case 3:
                return -(int)(i % (num + 1));
        }
        return 0;
    }
}
