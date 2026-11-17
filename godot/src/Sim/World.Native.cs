using System.Collections.Generic;
using Chosen.Data;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// A level loaded from its Blood .MAP (Sim.Build): the game's sectors, walls and sprites made straight from the Build
/// data as Blood's dbLoadMap and actInit have them (an x-object per x-sector, x-wall and sprite), the Build sectors and
/// walls themselves the Board the game changes as Blood does (World.NativeSectors: TranslateSector, ZTranslateSector),
/// Build's collision and lookups on it as it is now (SectorMap.FromBoard).
/// </summary>
public sealed partial class World
{
    /// <summary>The level's Build data as it plays (null: a converted level).</summary>
    public Build.Board Board { get; private set; }

    /// <summary>The level's name (its map file's, lower case).</summary>
    public string MapName = "";

    /// <summary>The SEQ animations playing on walls and sectors (SurfSeq).</summary>
    public readonly List<SurfSeq.Inst> SurfSeqs = new();

    /// <summary>A wall's (its Build index) or sector's look was changed by an animation or a changer.</summary>
    public void SurfaceChanged(int index, bool wall)
    {
        if (wall)
            WallLookChanged(index);
        else
            LookChanged(index);
    }

    /// <summary>A surface's animation plays its sound: at the wall's middle or the sector's.</summary>
    public void SurfaceSound(int kind, int index, int soundId)
    {
        if (kind == SurfSeq.Wall || kind == SurfSeq.Masked)
        {
            var (x1, y1, x2, y2) = Board.WallEnds(index);
            int s = Board.Walls[index].Sector;
            double z = s >= 0 ? (Board.Sectors[s].FloorZ + Board.Sectors[s].CeilingZ) / 2.0 : 0;
            Sound3DAt(B2Q((x1 + x2) / 2.0, (y1 + y2) / 2.0, z), soundId);
        }
        else
        {
            var sec = Board.Sectors[index];
            var w = Board.Walls[sec.WallPtr];
            Sound3DAt(B2Q(w.X, w.Y, kind == SurfSeq.Floor ? sec.FloorZ : sec.CeilingZ), soundId);
        }
    }

    /// <summary>Is the level its Build data (not a converted one)?</summary>
    public bool Native => Board != null;

    // the game sprite made from each Build sprite of the map (null: none, or filtered out)
    Sprite[] _spriteOfB = System.Array.Empty<Sprite>();

    /// <summary>The game sprite made from Build sprite i of the map (markers, teleport destinations), or null.</summary>
    public Sprite SpriteOfB(int i) => i >= 0 && i < _spriteOfB.Length ? _spriteOfB[i] : null;

    static Dictionary<int, string> _classOfType;

    /// <summary>A sprite type's class name (the map entity names of the converted levels, K.SpawnType).</summary>
    static string ClassOfType(int type)
    {
        if (_classOfType == null)
        {
            _classOfType = new Dictionary<int, string>();
            foreach (var (name, t) in K.SpawnType)
                _classOfType.TryAdd(t, name);
        }
        return type == 1 ? "info_player_start" : type == 2 ? "info_player_deathmatch"
            : _classOfType.TryGetValue(type, out var n) ? n : "blood_sprite";
    }

    /// <summary>Spawns a level from its Build data as Blood does (dbLoadMap, actInit, trInit).</summary>
    public void LoadNative(Build.Board b, string mapName, SectorMap map = null)
    {
        Tiles.Load();
        SeqData.Load();
        QavData.Load();
        Board = b;
        MapName = mapName ?? "";
        ModernMap = ModernRevision(b.File);
        ModernReset();
        SurfSeqs.Clear();
        GlobalVisibility = -1;
        _levelStartPending = false;
        Sprites.Clear();
        Sectors.Clear();
        Walls.Clear();
        Players.Clear();
        EvKillAll();
        _fxSectors = null;
        _waterMovers = null;
        FrameClock = Frame = LevelTime = 0;
        Kills = TotalKills = SecretsTotal = SecretsFound = SuperSecretsFound = 0;
        _spriteCounter = 0;
        LevelEnded = false;
        Collision = new Collision.CollisionWorld();
        _wholeMovers.Clear();
        _wholeMoverSectors.Clear();
        Map = map ?? SectorMap.FromBoard(b);
        _levelMessages.Clear();
        for (int n = 1; n < 256; n++)
            if (LevelInfo.Message(mapName, n) is string msg && msg.Trim() != "")
                _levelMessages[n] = msg;
        NextMap = LevelInfo.NextMap(mapName, false) ?? "";
        SecretMap = LevelInfo.NextMap(mapName, true) ?? "";
        Edicts.Reset();
        Edicts.Loading = true;
        _edictTime = -100;
        _tempSlots.Clear();
        _spawned.Clear();
        for (int i = 0; i < AiStateEntities; i++)
            Edicts.Alloc(_edictTime);
        Collision.Links.Clear();
        StartSpots.Clear();
        // the sectors' and walls' x-objects
        for (int i = 0; i < b.SectorCount; i++)
            if (b.Sectors[i].Xs != null)
                SpawnNativeSector(i);
        for (int i = 0; i < b.WallCount; i++)
            if (b.Walls[i].Xw != null)
                SpawnNativeWall(i);
        // the sprites (bloodSpawnSprite), in map order; the filter's still take a number and a slot first
        _spriteOfB = new Sprite[b.File.Sprites.Count];
        var filtered = new List<int>();
        foreach (var bs in b.File.Sprites)
        {
            if (bs.StatNum >= 1024 || bs.SectNum < 0 || bs.SectNum >= b.SectorCount)
                continue;
            if (bs.Type == 1 || bs.Type == 2)
            {
                // player starts (dbLoadMap: kMarkerSPStart / kMarkerMPStart: their place, angle and data1)
                StartSpots.Add(new StartSpot(B2Q(bs.X, bs.Y, bs.Z), bs.Ang, bs.Xsp?.Data1 ?? 0, bs.Type == 2, bs.SectNum));
                continue;
            }
            int slot = Edicts.Alloc(_edictTime);
            int sprIndex = ++_spriteCounter;
            if (NativeFilteredOut(bs))
            {
                filtered.Add(slot);
                continue;
            }
            var s = SpawnNativeSprite(bs);
            s.SprIndex = sprIndex;
            s.Edict = slot;
            _spriteOfB[bs.Index] = s;
        }
        Edicts.Loading = false;
        _edictTime = -10;
        foreach (int slot in filtered)
            Edicts.Free(slot, _edictTime);
        foreach (var s in Sprites)
            InitByType(s);
        if (IsModern)
        {
            Core.GameConsole.Print($"modern map (revision {ModernMap})");
            NnExtInitModernStuff();
        }
        // markers and teleport destinations by sprite (the x-sectors' marker0 / marker1, PropagateMarkerReferences'd)
        foreach (var s in Sectors)
        {
            var x = b.Sectors[s.B].Xs;
            s.SMarker0 = SpriteOfB(x.Marker0);
            s.SMarker1 = SpriteOfB(x.Marker1);
        }
        IndexMap();
        NativeWarpInit();
        // monsters off: drop their keys and remove them (actInit)
        if (!Monsters)
        {
            foreach (var e in Sprites.ToArray())
            {
                if (e.StatNum != K.kStatDude || e is Player)
                    continue;
                if (e.XKey > 0)
                    ActDropObject(e, K.kItemKeyBase + e.XKey - 1);
                Delete(e);
            }
        }
        else
        {
            KillMgrCountTotalKills();
            foreach (var e in Sprites)
                if (e.StatNum == K.kStatDude && e is not Player)
                    AiInitSprite(e);
        }
        Sprites.RemoveAll(s => s.Removed);
        for (int i = 0; i < Sprites.Count; i++)
            Sprites[i].Index = i;
        foreach (var s in Sprites)
        {
            UpdateBox(s);
            s.PrevOrigin = s.Origin;
            s.PrevAng = s.Ang;
        }
        IndexReceivers();
        // the sectors sharing ground, as the map stores them (before trInit: a marked slide's walls drag their neighbours'
        // corners over the next room, DWE3M1's panel 240 took 281's edge over 856 and the walk hid 281's step), not
        // those moving whole or by marked walls (a door panel stored open lies over the next room: E1M1's chapel door)
        Map.FindOverlaps(s => XSectorOf(s) is { } xs && xs.Type >= K.kSectorPath && xs.Type <= K.kSectorRotate);
        NativeTrInit();
        b.BeginFrame();
        Map.Refresh();
        foreach (var s in Sprites)
        {
            s.PrevOrigin = s.Origin;
            s.PrevAng = s.Ang;
        }
        _edictTime = -5;
    }


    /// <summary>Removed for the difficulty or game type (dbLoadMap's filter: the x-sprite's lSkill, lS, lB, lT, lC).</summary>
    bool NativeFilteredOut(BSprite bs)
    {
        var x = bs.Xsp;
        if (x == null)
            return false;
        if ((x.LSkill & (1 << Skill)) != 0)
            return true;
        if (GameType == K.kGameTypeSinglePlayer && x.LS != 0) return true;
        if (GameType == K.kGameTypeBloodBath && x.LB != 0) return true;
        if (GameType == K.kGameTypeTeams && x.LT != 0) return true;
        if (GameType == K.kGameTypeCoop && x.LC != 0) return true;
        return false;
    }

    Sector SpawnNativeSector(int i)
    {
        var bs = Board.Sectors[i];
        var x = bs.Xs;
        var s = new Sector
        {
            ClassName = "blood_sector", Index = Sectors.Count, B = i, BSector = i + 1, Type = bs.Type,
            RxID = x.RxID, TxID = x.TxID, XState = x.State, RestState = x.RestState, XCommand = x.Command,
            TriggerOn = x.TriggerOn, TriggerOff = x.TriggerOff, Decoupled = x.Decoupled, TriggerOnce = x.TriggerOnce,
            XLocked = x.Locked, Interruptable = x.Interruptable, DudeLockout = x.DudeLockout,
            BusyTimeA = x.BusyTimeA, BusyTimeB = x.BusyTimeB, WaitTimeA = x.WaitTimeA, WaitTimeB = x.WaitTimeB,
            ReTriggerA = x.ReTriggerA, ReTriggerB = x.ReTriggerB, BusyWaveA = x.BusyWaveA, BusyWaveB = x.BusyWaveB,
            StopOn = x.StopOn, StopOff = x.StopOff,
            SCrush = x.Crush, SData = x.Data, SEnter = x.Enter, SExit = x.Exit, SWallpush = x.Wallpush,
            SUnderwater = x.Underwater, SDepth = x.Depth, DamageType = x.DamageType,
            WindVel = x.WindVel, WindAng = x.WindAng, WindAlways = x.WindAlways,
            PanVel = x.PanVel, PanAngle = x.PanAngle, PanAlways = x.PanAlways, SDrag = x.Drag,
            XKey = x.Key, XPush = x.Push, XVector = x.Vector,
            FxAmp = x.Amplitude, FxFreq = x.Freq, FxPhase = x.Phase, FxWave = x.Wave, FxAlways = x.ShadeAlways,
            FxFloor = x.ShadeFloor, FxCeil = x.ShadeCeiling, FxShadeWalls = x.ShadeWalls,
            FxSky = (bs.CeilingStat & 1) != 0 && (bs.FloorStat & 32768) == 0 ? 1 : 0,
            BobSpeed = x.BobSpeed, BobZRange = x.BobZRange, BobAlways = x.BobAlways, BobFloor = x.BobFloor,
            BobCeiling = x.BobCeiling, BobTheta0 = x.BobTheta, BobTheta = x.BobTheta,
            OffFloorZ = x.OffFloorZ, OnFloorZ = x.OnFloorZ, OffCeilZ = x.OffCeilZ, OnCeilZ = x.OnCeilZ,
        };
        // a relative floor pushes along panAngle turned by its first wall's angle + 512 (actProcessSprites)
        if (x.PanVel != 0 && (bs.FloorStat & 64) != 0)
        {
            var w = Board.Walls[bs.WallPtr];
            var w2 = Board.Walls[w.Point2];
            s.PanPushRot = (Blood.GetAngle(w2.X - w.X, w2.Y - w.Y) + 512) & 2047;
        }
        s.IsMover = Sector.TypeIsMover(s.Type);
        s.Keys = new Dictionary<string, string>();
        s.Edict = Edicts.Alloc(_edictTime);
        Sectors.Add(s);
        return s;
    }

    Wall SpawnNativeWall(int i)
    {
        var bw = Board.Walls[i];
        var x = bw.Xw;
        var w = new Wall
        {
            ClassName = "blood_wall", Index = Walls.Count, BWall = i, Type = bw.Type,
            RxID = x.RxID, TxID = x.TxID, XState = x.State, RestState = x.RestState, XCommand = x.Command,
            TriggerOn = x.TriggerOn, TriggerOff = x.TriggerOff, Decoupled = x.Decoupled, TriggerOnce = x.TriggerOnce,
            XLocked = x.Locked, Interruptable = x.Interruptable, DudeLockout = x.DudeLockout,
            BusyTime = x.BusyTime, WaitTime = x.WaitTime, Data1 = x.Data, XKey = x.Key,
            XPush = x.TriggerPush, XVector = x.TriggerVector, XTouch = x.TriggerTouch,
        };
        if (bw.Type == K.kWallGib)
            w.GibType = x.Data != 0 ? x.Data : 13;
        w.Keys = new Dictionary<string, string>();
        w.Edict = Edicts.Alloc(_edictTime);
        Walls.Add(w);
        return w;
    }

    /// <summary>bloodSpawnSprite for a Build sprite: its type, picture, place (Blood's z, the picture centred on it by cstat 128),
    /// its x-sprite's fields; the sector's shade a sprite in it takes.</summary>
    Sprite SpawnNativeSprite(BSprite bs)
    {
        int type = bs.Type;
        var s = new Sprite { Index = Sprites.Count, ClassName = ClassOfType(type) };
        s.Type = s.InitType = type;
        s.Keys = new Dictionary<string, string>();
        s.Picnum = bs.Picnum;
        s.Pal = bs.Pal;
        s.Shade = bs.Shade;
        s.XRepeat = bs.XRepeat;
        s.YRepeat = bs.YRepeat;
        s.XOffset = bs.XOffset;
        s.YOffset = bs.YOffset;
        if ((bs.Cstat & Cstat.AlignMask) == Cstat.Slope)
        {
            // a sloped floor sprite: its offsets are its slope (low byte, high byte)
            s.Slope = (short)((bs.XOffset & 255) | ((bs.YOffset & 255) << 8));
            s.XOffset = s.YOffset = 0;
        }
        // (as stored: Blood's own maps have every sprite centred on its z, cstat 128; a mod's can have one standing on it)
        s.Cstat = bs.Cstat;
        s.ClipDist = bs.ClipDist;
        s.HitTag = bs.Flags;
        s.Ang = bs.Ang;       // signed as stored: a marker's -512 turns a door the short way (TranslateSector interpolates it)
        s.Origin = B2Q(bs.X, bs.Y, bs.Z);
        s.BaseOrigin = s.Origin;
        s.BSector = bs.SectNum + 1;
        s.SecShade = Map.SpriteShade(bs.SectNum);
        if (bs.Patch != null)
        {
            foreach (var (k, v) in bs.Patch)
                s.Keys[k] = v;
            s.ReadKeys(s.Keys);
        }
        var x = bs.Xsp;
        s.NoX = x == null ? 1 : 0;
        if (x != null)
        {
            s.RxID = x.RxID; s.TxID = x.TxID; s.XState = x.State; s.RestState = x.RestState; s.XCommand = x.Command;
            s.TriggerOn = x.TriggerOn; s.TriggerOff = x.TriggerOff; s.BusyTime = x.BusyTime; s.WaitTime = x.WaitTime;
            s.XWave = x.Wave; s.Data1 = x.Data1; s.Data2 = x.Data2; s.Data3 = x.Data3; s.Data4 = x.Data4;
            s.XKey = x.Key; s.XPush = x.Push; s.XVector = x.Vector; s.XImpact = x.Impact; s.XPickup = x.Pickup;
            s.XTouch = x.Touch; s.XSight = x.Sight; s.XProximity = x.Proximity; s.Decoupled = x.Decoupled;
            s.TriggerOnce = x.TriggerOnce; s.XLocked = x.Locked; s.Interruptable = x.Interrutable;
            s.DudeLockout = x.DudeLockout; s.LockMsg = x.LockMsg; s.DropMsg = x.DropMsg; s.XRespawn = x.Respawn;
            if (IsModern)
                ReadModernFields(s, x);
        }
        Sprites.Add(s);
        SetSpriteBSector(s, bs.SectNum);
        return s;
    }
}
