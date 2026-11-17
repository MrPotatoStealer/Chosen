using System;

namespace Chosen.Sim;

/// <summary>
/// Blood's integer arithmetic with C semantics (floor for shifts and scales, truncation for integer division), as
/// Blood3D's QuakeC did it. Values stay in double: Blood's numbers fit, and the formulas match the original's.
/// </summary>
public static class Blood
{
    public const double XYScale = 16;     // Build xy units per Quake unit
    public const double ZScale = 256;     // Build z units per Quake unit
    public const int Ang180 = 1024;

    public static double Int(double a) => a < 0 ? -Math.Floor(-a) : Math.Floor(a);
    public static double MulScale8(double a, double b) => Math.Floor(a * b / 256);
    public static double MulScale14(double a, double b) => Math.Floor(a * b / 16384);
    public static double MulScale16(double a, double b) => Math.Floor(a * b / 65536);
    public static double MulScale16R(double a, double b) => Math.Floor((a * b + 32768) / 65536);
    public static double MulScale30(double a, double b) => Math.Floor(a * b / 1073741824);
    public static double MulScale30R(double a, double b) => Math.Floor((a * b + 536870912) / 1073741824);
    public static double DivScale16(double a, double b) => b == 0 ? 0 : Int(a * 65536 / b);
    public static double Scale3(double a, double b, double c) => c == 0 ? 0 : Int(a * b / c);
    public static double Sar(double a, int n) => Math.Floor(a / (1 << n));
    public static double ClipLow(double a, double b) => a < b ? b : a;
    public static double ClipHigh(double a, double b) => a >= b ? b : a;
    public static double ClipRange(double a, double b, double c) => a < b ? b : a > c ? c : a;
    public static double AngMask(double a) => a - 2048 * Math.Floor(a / 2048);

    /// <summary>Blood's costable: cos * 2^30.</summary>
    public static double Cos(double ang) => Math.Cos(AngMask(ang) * (Math.PI / 1024)) * 1073741824;
    public static double Sin(double ang) => Math.Sin(AngMask(ang) * (Math.PI / 1024)) * 1073741824;

    public static double ApproxDist(double dx, double dy)
    {
        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        if (dx > dy)
            dy = Math.Floor(3 * dy / 8);
        else
            dx = Math.Floor(3 * dx / 8);
        return dx + dy;
    }

    public static double Interpolate(double a, double b, double c) => a + MulScale16(b - a, c);

    public static double DMulScale30R(double a, double b, double c, double d) => Math.Floor((a * b + c * d + 536870912) / 1073741824);
    public static double Scale5(double a, double b, double c, double d, double e) => c == b ? d : d + Int((e - d) * (a - b) / (c - b));
    public static double KSqrt(double x, double y) => Math.Floor(Math.Sqrt(x * x + y * y));
    public static double Sgn(double a) => a < 0 ? -1 : a > 0 ? 1 : 0;

    public static double IncBy(double a, double b)
    {
        a += b;
        double q = a - b * Int(a / b);
        a -= q;
        if (q < 0)
            a -= b;
        return a;
    }

    public static double DecBy(double a, double b)
    {
        a -= 1;
        double q = a - b * Int(a / b);
        a -= q;
        if (q < 0)
            a -= b;
        return a;
    }

    /// <summary>Build's getangle(dx, dy): 0..2047.</summary>
    public static int GetAngle(double dx, double dy) =>
        dx == 0 && dy == 0 ? 0 : (int)AngMask(Rint(Math.Atan2(dy, dx) * (1024 / Math.PI)));

    /// <summary>RotateVector: (dx, dy) turned by a Build angle.</summary>
    public static (double x, double y) RotateVector(double dx, double dy, double ang)
    {
        double c = Cos(ang), s = Sin(ang);
        return (DMulScale30R(dx, c, -dy, s), DMulScale30R(dx, s, dy, c));
    }

    /// <summary>QuakeC's rint: halves round away from zero.</summary>
    public static double Rint(double a) => Math.Round(a, MidpointRounding.AwayFromZero);
    public static double MulScale10(double a, double b) => Math.Floor(a * b / 1024);
    public static double MulScale15(double a, double b) => Math.Floor(a * b / 32768);
    public static double DivScale10(double a, double b) => b == 0 ? 0 : Int(a * 1024 / b);
    public static double YawToAng(double yaw) => AngMask(Rint(-yaw * (2048.0 / 360.0)));
    public static double AngToYaw(double a) => -AngMask(a) * (360.0 / 2048.0);
}
