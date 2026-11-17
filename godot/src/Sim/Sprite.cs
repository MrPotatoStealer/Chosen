using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>Blood sprite cstat bits.</summary>
public static class Cstat
{
    public const int Block = 1, Trans = 2, XFlip = 4, YFlip = 8, Wall = 16, Floor = 32, OneSided = 64,
        YCenter = 128, Hitscan = 256, Trans2 = 512, Pushable = 4096, MoveForward = 8192, MoveReverse = 16384,
        Invisible = 32768;
    public const int AlignMask = 48;
    /// <summary>A floor sprite with a slope (the engine's CSTAT_SPRITE_ALIGNMENT_SLOPE: both alignment bits).</summary>
    public const int Slope = 48;
}

/// <summary>
/// A Blood sprite (Build's spritetype plus its x-sprite): decorations, items, monsters, things, switches, markers,
/// and the players' dudes. Positions are Quake units (x east, y north, z up); angles are Build angles; velocities are
/// Blood units (65536 = 1 Quake unit per game frame, z positive down).
/// </summary>
public class Sprite : XObject
{
    // World.Live: a sprite placed where its sector has no area yet (E1M1's start, in the grave the level opens): Build
    // keeps it in that sector on its floor; held here until a live sector opens around it
    public bool LiveChecked, LiveHeld;
    public Godot.Vector3 LiveHeldAt;

    public override int ObjType => ObjSprite;

    public int InitType;
    public int StatNum;              // Blood status list (decoration, item, thing, dude, marker...)
    public Vector3 Origin;           // Build z: the sprite's z (its picture is centred around it, plus offsets)
    public Vector3 PrevOrigin;       // at the previous game frame (the renderer interpolates)
    public Vector3 DrawnAt;          // where the renderer last placed its picture
    public int Ang, PrevAng;
    [MapKey] public int Picnum, Pal, Shade, XRepeat = 64, YRepeat = 64, XOffset, YOffset, Cstat;
    [MapKey] public int ClipDist = 32;
    [MapKey] public int NoX;                  // 1: Blood's map sprite has no x-sprite (a torch without one burns)
    [MapKey] public int BSector;              // the Build sector it starts in + 1 (0: unknown; Render.StackedRooms)
    [MapKey] public int LightId;              // the map's light entity it gives (lightOf; Render.DynamicLights), 0: none
    // Refreshed Supply's fixes for its voxel model (defs/levelHacks, applied by the converter): a turn (Build angle
    // units), an offset (Build units: x y as the map's, z as Build's) and no model at all (the flat sprite instead)
    [MapKey] public int VoxAngOff, VoxXOff, VoxYOff, VoxZOff, NoVoxel;
    public int HitTag;               // physics and sequence flags (kPhys*, kHitag*)
    public double XVel, YVel, ZVel;
    public double XHealth;
    public int XScale;               // sequence repeat scale (0: none)
    public int Medium;               // 0 normal, 1 water, 2 goo
    public double XHeight;           // height above the floor (Build z / 256)
    public int Hit, FlorHit, CeilHit;        // last move's hit, floor and ceiling hit codes
    public object HitEnt, FlorHitEnt, CeilHitEnt;
    public int SprIndex;             // spawn counter (gSpriteCounter): a stable id
    public int GoalAng;              // AI goal angle; a flare stuck in flesh keeps its angle here
    public int StateTimer;           // AI state time left
    // DUDEEXTRA (ai.cpp)
    public int DudeClock, TeslaHit, SfxPriority, ThinkTime, BirthCounter, DudeActive, DodgeDir;
    public double CumulDamage;
    public double DudeSlope;         // gDudeSlope: the aim slope to its target
    public int RespawnPending;       // 1..3: waiting to respawn (kStatRespawn)
    public int RespawnStat;          // the status list it returns to
    public Vector3 BaseOrigin;       // where it respawns (baseSprite)
    public int PostStat;             // actPostSprite: the status it changes to at the end of the frame
    public bool Posted;
    public int FlorSurf;             // surface type of the floor it stands on
    public double TargetX, TargetY, TargetZ;   // AI target position; things use TargetX as a time
    public AiState AiState;          // the dude's AI state
    public int BurnTime;
    public Sprite BurnSource;
    public Sprite XTarget;           // the dude's target
    public Sprite Owner;             // who spawned it (missiles, thrown things)
    public int Anim;                 // view type 0 tiles: animation frame chosen by the game (switch state...)
    public int SecShade = int.MinValue;   // the shade of the sector it starts in (int.MinValue: unknown)
    public bool Visible = true;
    // Build draws a sprite only inside its own sector: the converter's open range (z) of the sector a sprite starts
    // outside of (one waiting under its floor until it is needed); equal: none
    [MapKey] public double HomeLo, HomeHi;

    /// <summary>spriteOutsideHome (net.qc): wholly below or above its sector's open range, so not drawn.</summary>
    public bool OutsideHome => HomeLo != HomeHi && (Origin.Z + Maxs.Z < HomeLo || Origin.Z + Mins.Z > HomeHi);

    // carried by a sliding/rotating sector (cstat 8192/16384): moveWith names it
    [MapKey] public string MoveWith = "";
    [MapKey] public int MoveSign;
    [MapKey] public string MoveZPart = "";    // carried by that part of a z-motion sector (Sector.ZPart)
    public Sector MoveWithEnt;
    public Vector3 MoveBase;
    public int MoveBaseAng;
    public int MoverCarried;         // frame+1 when a mover carried it this frame
    public Sector MoverCarrier;      // the mover that did

    // SEQ instance (seq.qc)
    public bool SeqPlaying;
    public int SeqId = -1, SeqFrame, SeqTime;
    public System.Action<Sprite> SeqCallback;

    public Vector3 Mins, Maxs;       // its box around Origin (spriteUpdateBox)
    public float BoxYaw;             // the box turns by this (wall/floor sprites at odd angles)
    public Collision.Body Body;      // its collision body (null or disabled: not solid)
    public bool LinkPrevSet;         // CheckLink: origin z at the previous check (crossings are found between two)
    public double LinkPrevZ;
    // Build's sector of it (World.SectorTrack: sectnum, kept as clipmove keeps it), where it was when last updated
    public int Sect = -1;
    public bool SectKnown;
    public Vector3 SectAt;
    public bool Solid => Body != null && Body.Enabled;
    public bool SolidBlock => Solid && (Body.Contents & (Collision.Contents.Body | Collision.Contents.Blocker)) != 0;     // SOLID_SLIDEBOX

    // NoOne's modern features (World.Modern): the x-sprite's bits vanilla Blood leaves unused (unused1: more dude flags,
    // unused3: the Screen and Aim triggers), its dude flags (patrol), and what the game keeps on it out of the editor's
    // reach (sysData1..4, the debris physics attributes)
    public int Unused1, Unused2, Unused3, Unused4;
    public int DudeDeaf, DudeAmbush, DudeGuard, DudeFlag4;
    public int SysData1, SysData2, SysData3, SysData4, PhysAttr;
    public int Slope;                // a sloped floor sprite's slope (spriteSetSlope: Build keeps it in the offsets)
    public Modern.CustomDude Custom; // a custom dude's descriptor and state (kDudeModernCustom)
    public Modern.CondState Cond;    // a condition's serials and what it tracks (kModernCondition)
    public Modern.SpriteMass Mass;   // a debris sprite's mass (getSpriteMassBySize)
    public Modern.PatrolState Patrol;    // a patrolling dude's (aipatrol)
    public Player Linked;            // a player linker's player (kModernPlayerControl with the link command)
    public Sprite FollowTarg;        // who a custom dude's missile or thrown thing follows (NBlood keeps it in sysData1)

    public bool IsFace => (Cstat & Sim.Cstat.AlignMask) == 0;

    /// <summary>A sloped floor sprite's slope (0: none; 4096 rises 45 degrees the way the sprite faces).</summary>
    public int SlopeNow => (Cstat & Sim.Cstat.AlignMask) == Sim.Cstat.Slope ? Slope : 0;

    /// <summary>spriteGetZOfSlope: the Build z of a (sloped) floor sprite's plane at Build x, y.</summary>
    public double ZOfSlope(double x, double y)
    {
        int heinum = SlopeNow;
        if (heinum == 0)
            return BZ;
        double a = Ang * (System.Math.PI / 1024);
        double j = System.Math.Floor((-System.Math.Sin(a) * 16384 * (y - BY) - System.Math.Cos(a) * 16384 * (x - BX)) / 16);
        return BZ + System.Math.Floor(heinum * j / 262144);
    }

    public double BZ => -Origin.Z * Blood.ZScale;
    public double BX => Origin.X * Blood.XYScale;
    public double BY => -Origin.Y * Blood.XYScale;
    public void SetBZ(double z) => Origin.Z = (float)(-z / Blood.ZScale);
}
