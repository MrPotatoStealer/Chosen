using Godot;

namespace Chosen.Sim;

/// <summary>
/// Godot's Geometry2D tests the game and the renderer run every frame, step for step in single precision as Godot's
/// C++ has them: a call through Godot's interop cost more than the test (E3M5's sprite pass spent most of its time
/// there, the live sectors' shapes half of theirs).
/// </summary>
public static class Geo2
{
    const float Eps = 0.00001f;     // CMP_EPSILON

    /// <summary>Geometry2D.get_closest_point_to_segment.</summary>
    public static Vector2 ClosestPointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        float px = point.X - a.X, py = point.Y - a.Y;
        float nx = b.X - a.X, ny = b.Y - a.Y;
        float l2 = nx * nx + ny * ny;
        if (l2 < 1e-20f)
            return a;
        float d = (nx * px + ny * py) / l2;
        if (d <= 0.0f)
            return a;
        if (d >= 1.0f)
            return b;
        return new Vector2(a.X + nx * d, a.Y + ny * d);
    }

    /// <summary>Geometry2D.segment_intersects_segment.</summary>
    public static bool SegmentsCross(Vector2 fromA, Vector2 toA, Vector2 fromB, Vector2 toB, out Vector2 result)
    {
        result = default;
        float bx = toA.X - fromA.X, by = toA.Y - fromA.Y;
        float cx = fromB.X - fromA.X, cy = fromB.Y - fromA.Y;
        float dx = toB.X - fromA.X, dy = toB.Y - fromA.Y;
        float abLen = bx * bx + by * by;
        if (abLen <= 0)
            return false;
        float bnx = bx / abLen, bny = by / abLen;
        float c2x = cx * bnx + cy * bny, c2y = cy * bnx - cx * bny;
        float d2x = dx * bnx + dy * bny, d2y = dy * bnx - dx * bny;
        if ((c2y < -Eps && d2y < -Eps) || (c2y > Eps && d2y > Eps))
            return false;
        if (EqualApprox(c2y, d2y))
            return false;
        float abPos = d2x + (c2x - d2x) * d2y / (d2y - c2y);
        if (abPos < 0 || abPos > 1)
            return false;
        result = new Vector2(fromA.X + bx * abPos, fromA.Y + by * abPos);
        return true;
    }

    /// <summary>Geometry2D.is_point_in_polygon: crossings of a segment to a point far outside, true on an edge.</summary>
    public static bool IsPointInPolygon(Vector2 point, Vector2[] poly)
    {
        int c = poly.Length;
        if (c < 3)
            return false;
        float fx = -1e20f, fy = -1e20f, ox = 1e20f, oy = 1e20f;
        foreach (var q in poly)
        {
            fx = Mathf.Max(fx, q.X);
            fy = Mathf.Max(fy, q.Y);
            ox = Mathf.Min(ox, q.X);
            oy = Mathf.Min(oy, q.Y);
        }
        var far = new Vector2(fx + (fx - ox) * 1.221313f, fy + (fy - oy) * 1.512312f);
        int crossings = 0;
        for (int i = 0; i < c; i++)
        {
            if (SegmentsCross(poly[i], poly[(i + 1) % c], point, far, out var res))
            {
                crossings++;
                if (EqualApprox(res.X, point.X) && EqualApprox(res.Y, point.Y))
                    return true;
            }
        }
        return (crossings & 1) != 0;
    }

    /// <summary>Math::is_equal_approx.</summary>
    static bool EqualApprox(float a, float b)
    {
        if (a == b)
            return true;
        float tol = Eps * System.Math.Abs(a);
        if (tol < Eps)
            tol = Eps;
        return System.Math.Abs(a - b) < tol;
    }
}
