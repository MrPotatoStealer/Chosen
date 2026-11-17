using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// A Blood sector made a brush entity (blood_sector): either a volume carrying the sector's x-data (triggers on
/// enter/exit, damage, wind, water) or a mover (z-motion, sliding, rotating, stepping or path sector) that moves
/// between its off position (as built) and its on position (origin + moveOn, yaw + rotOn).
/// </summary>
public sealed class Sector : XObject
{
    public override int ObjType => ObjSector;

    // XSECTOR
    [MapKey] public int BusyTimeA, BusyTimeB, WaitTimeA, WaitTimeB, BusyWaveA, BusyWaveB;
    [MapKey] public int ReTriggerA, ReTriggerB, StopOn, StopOff;
    [MapKey] public int SCrush, SData, SEnter, SExit, SWallpush, SUnderwater, SDepth;
    [MapKey] public string SOpen = "";        // a z-motion sector: its open z range at OFF ("lo hi")
    [MapKey] public int WindVel, WindAng, WindAlways, PanVel, PanAngle, PanAlways, DamageType;
    [MapKey] public int PanPushRot;           // a relative floor's push turn: its first wall's angle + 512 (Build angle)
    [MapKey] public int SndOpenStart, SndOpenEnd, SndCloseStart, SndCloseEnd;
    [MapKey] public int BSector;              // Build sector index + 1 (outline lookup)
    [MapKey] public int PartSign = 1;         // a marked slide/turn's part: with the transform (1) or against it (-1)
    // light effect (XSECTOR amplitude, freq, phase, wave, shadeAlways; which parts take it; a sky ceiling)
    [MapKey] public int FxAmp, FxFreq, FxPhase, FxWave, FxAlways, FxFloor, FxCeil, FxWalls, FxSky;
    [MapKey] public int FxShadeWalls;         // Blood's shadeWalls: its walls take the effect (World.Live draws them)
    public int FxShade, FxSprite, FxWeapon;   // this frame's shade offset: of the surfaces, the sprites, the weapon
    [MapKey] public string Pushwall = "";     // blood_wall whose x-wall the mover's faces carry
    // a sliding sector's floor / ceiling picture stays world-aligned as it moves (Build's, unless relative)
    [MapKey] public int WorldFloor, WorldCeil;
    [MapKey] public int LiveRigid;            // a whole mover passing through a live region (the converter's live_rigid)
    [MapKey] public int CoverLink;            // a whole mover over a neighbour's link plane: no link where it is (MoverLinks)
    [MapKey] public int BySectors;            // r_sectors draws its faces from Build's walls (LiveRenderer's dynamic groups)
    public Map.MapEntity Entity;              // its brush entity (the renderer reads its faces' texture axes)
    public Wall PushWallEnt;
    public XObject SMarker1, SDataEnt, SBasePath;

    // mover
    [MapKey] public Vector3 MoveOn;           // off -> on translation (Quake units)
    [MapKey] public Vector3 ClipMin, ClipMax;
    [MapKey] public string ZPart = "";        // a z-motion sector's mover: "floor" or "ceiling" (both have its name) // the mover is drawn only inside this box (Quake units; both zero: none)
    [MapKey] public double RotOn;             // off -> on yaw (degrees)
    public System.Collections.Generic.List<Sector> Lockstep;             // the movers sliding or turning with it as one (World.LockstepOf)
    [MapKey] public int SDrag;                // carries what stands on its floor while sliding/rotating
    [MapKey] public int VsFloorDZ, VsCeilDZ;  // z-motion sprite sectors: how far flagged sprites move (Build z)
    // ProcessMotion: the floor or ceiling bobs (bobTheta advances by bobSpeed a frame, always or while busy;
    // bobZRange: its height, in 256ths of a Build z unit a step)
    [MapKey] public int BobSpeed, BobZRange, BobAlways, BobFloor, BobCeiling, BobTheta0;   // bobTheta0: its phase at start
    [MapKey] public int BobOpen;              // the bob scales with how far open the sector is (no area at OFF)
    public Sector BobOpenEnt;                 // whose busy says that: the sector's sliding part
    // AlignSlopes: the plane is turned about the line of the sector's first wall (alignHinge "x0 y0 x1 y1") to meet
    // the neighbour's plane at the middle of its filler wall (alignTo "x y z", z as built; alignTarget "name part":
    // that plane's mover, if it moves); alignBase "zHinge zPoint": this plane's heights there as built
    [MapKey] public string AlignHinge = "", AlignBase = "", AlignTo = "", AlignTarget = "";
    // the filler wall's vertices slide with another sector's walls (alignMove "dx dy": the point's travel OFF to ON;
    // alignMoveWith: that sector's sliding part): the point moves with it (E2M6's ice floe shrinks as the water opens)
    [MapKey] public string AlignMoveWith = "";
    [MapKey] public Vector3 AlignMove;
    // a skin of another sector's moving plane ("name part"): it slides with its own sector and rises and tilts with
    // that plane (the floe's floor over the water strip as it opens)
    [MapKey] public string PlaneWith = "";
    public int BobTheta, PlaceF;
    public float BobZ, BobRaw;                // the bob shown, and Build's (BobOpen holds the shown one while shut)
    public float PlaneZ;                      // PlaneWith: that plane's rise
    public Sector AlignEnt, AlignMoveEnt, PlaneWithEnt;
    public Vector2 AlignPoint0;               // the point at OFF
    public float AlignSlope0;                 // the plane's slope along ShearDir as built
    public float Shear, PrevShear;            // the plane's slope change: z += Shear * ShearDir.(xy - ShearAt)
    public Vector2 ShearDir, ShearAt;
    public float AlignZHinge, AlignZPoint, AlignZTo, AlignDist;
    public Vector2 AlignPoint;
    public bool IsMover;
    public Sector Twin;                       // the other part of a z-motion sector moving both planes
    public Vector3 Origin, BaseOrg, PathBase;
    public double Yaw, BaseYaw;
    public Vector3 PrevOrigin;                // at the previous frame (the renderer interpolates)
    public double PrevYaw;
    public bool BusyActive;
    public bool MotionPaused;                 // a modern map paused its motion (kCmdSectorMotionPause: XSECTOR unused1)
    public int BusyDelta, BusyVal, BusyProc;
    public double StepData, StepOfs;          // kSectorRotateStep: accumulated angle, angle at busy 0 of this step

    // a level loaded from its Build data (World.Native): the Build sector (0-based) and its x-sector's heights (OFF and ON
    // floor and ceiling, Build z; a path sector's are set as it goes)
    public int B = -1;
    public int OffFloorZ, OnFloorZ, OffCeilZ, OnCeilZ;

    public Aabb Bounds;                       // world-space bounds of its brushes (at the off position)

    /// <summary>Current world bounds (absmin/absmax): moved with the mover; turned movers get the turned box's
    /// axis-aligned bounds about the pivot.</summary>
    public Aabb AbsBounds
    {
        get
        {
            var b = Bounds;
            var d = Origin - BaseOrg;
            double rot = Yaw - BaseYaw;
            if (rot != 0)
            {
                // the farthest a corner can be from the pivot, all round
                float r = 0;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.GetEndpoint(i) - BaseOrg;
                    r = Mathf.Max(r, new Vector2(c.X, c.Y).Length());
                }
                return new Aabb(new Vector3(BaseOrg.X - r, BaseOrg.Y - r, b.Position.Z) + d, new Vector3(2 * r, 2 * r, b.Size.Z));
            }
            return new Aabb(b.Position + d, b.Size);
        }
    }

    /// <summary>sectorTypeIsMover: z-motion sprite sectors (602) are not; Blood moves only their flagged sprites.</summary>
    public static bool TypeIsMover(int t) =>
        t == Data.K.kSectorZMotion || t == Data.K.kSectorRotateStep || t == Data.K.kSectorSlideMarked
        || t == Data.K.kSectorRotateMarked || t == Data.K.kSectorSlide || t == Data.K.kSectorRotate || t == Data.K.kSectorPath;
}

/// <summary>A Blood wall with x-data made a brush entity (blood_wall): switches pushed, walls shot, gib walls.</summary>
public sealed class Wall : XObject
{
    public override int ObjType => ObjWall;

    [MapKey] public int GibType;
    [MapKey] public int BWall = -1;           // its Build wall (a converted level's; World.BuildClip's hits)
    public bool Broken;
    public Aabb Bounds;
}
