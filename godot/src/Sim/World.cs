using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Map;
using Chosen.Sim.Collision;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The game's state: everything Blood simulates at 30 frames per second (Blood3D's QuakeC server, ported). It runs
/// without Godot nodes (renderers read it), so the same state can later be run on a server and sent to clients.
/// </summary>
public sealed partial class World
{
    public readonly List<Sprite> Sprites = new();
    public readonly List<Sector> Sectors = new();
    public readonly List<Wall> Walls = new();
    public readonly List<Player> Players = new();
    public CollisionWorld Collision = new();

    // game options (levelReadOptions)
    public int Skill = 2;                 // 0 Still Kicking .. 4 Extra Crispy
    public bool WeaponsV10x;              // gGameOptions.weaponsV10x (b3d_weaponsv10x): NBlood's v1.0x weapons balance
    public bool FixBlood;                 // b3d_fixblood: Blood's own bugs fixed (MoveDude: a floor leaving a dude standing still)
    public int GameType;                  // kGameType*
    public bool Monsters = true;          // b3d_monsters
    public int MonsterRespawnTime = 3600, WeaponRespawnTime = 1800, ItemRespawnTime = 1800, SpecialRespawnTime = 7200;

    // time
    public int FrameClock;                // gFrameClock: Build's 120 Hz totalclock
    public int Frame;                     // gFrame
    public int LevelTime;
    public double Clock => FrameClock;

    // level progress
    public int Kills, TotalKills, SecretsTotal, SecretsFound, SuperSecretsFound;
    public bool LevelEnded;
    public int ExitKind = -1;             // 0 normal, 1 secret

    public Player Me => Players.Count > 0 ? Players[0] : null;
    int _spriteCounter;                   // gSpriteCounter

    /// <summary>DarkPlaces' entity slots, so entities go in Blood3D's order (see Edicts).</summary>
    public readonly Edicts Edicts = new();
    int _edictTime;                       // the frame entities spawn and go at (negative while the level starts)
    readonly List<Sprite> _spawned = new();    // spawned during this game frame (their PrevAng is set at its end)
    readonly List<(int slot, int frame)> _tempSlots = new();   // sfx_temp emitters (sfxPlay3DSoundXYZ): gone after 10 s
    static readonly int AiStateEntities = System.Linq.Enumerable.Count(typeof(World).GetFields(), f => f.FieldType == typeof(AiState));

    public World() => AiInitStates();

    /// <summary>A sound a game object plays (object, sound id, channel, sfxPlay3DSound's flags); the client side
    /// listens.</summary>
    public event Action<XObject, int, int, int> SoundPlayed;
    /// <summary>A message for the player (level messages, pickups); the client shows it.</summary>
    public event Action<string> MessageShown;

    /// <summary>Every x-object in entity slot order (find(world, ...) order).</summary>
    public IEnumerable<XObject> AllObjects()
    {
        var all = new List<XObject>(Sectors.Count + Walls.Count + Sprites.Count);
        all.AddRange(Sectors);
        all.AddRange(Walls);
        all.AddRange(Sprites);
        all.Sort((a, b) => a.Edict.CompareTo(b.Edict));
        return all;
    }

    // ---------------------------------------------------------------- random (Blood's wrand, seeded)
    // Two Lehmer generators whose products stay below 2^24 (75 mod 65537, 17 mod 65521), as Blood3D's QuakeC did it.
    double _seed1 = 1, _seed2 = 1;

    public void Seed(double s)
    {
        s = Math.Floor(Math.Abs(s));
        _seed1 = 1 + (s - 65536 * Math.Floor(s / 65536));
        s = Math.Floor(s / 65536) * 251 + s;
        _seed2 = 1 + (s - 65520 * Math.Floor(s / 65520));
    }

    public string SeedState => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1}", _seed1, _seed2);

    public long RandCalls;               // how many draws so far (comparing with the DarkPlaces version)

    public System.Text.StringBuilder RandTrace;   // the callers of each draw (comparing with the DarkPlaces version)

    public int WRand()
    {
        RandCalls++;
        if (RandTrace != null)
        {
            var st = new System.Diagnostics.StackTrace(1);
            RandTrace.Append("  ");
            for (int k = 0; k < 4 && k < st.FrameCount; k++)
                RandTrace.Append(st.GetFrame(k).GetMethod()?.Name).Append(k < 3 ? " < " : "");
            RandTrace.Append('\n');
        }
        _seed1 *= 75;
        _seed1 -= 65537 * Math.Floor(_seed1 / 65537);
        _seed2 *= 17;
        _seed2 -= 65521 * Math.Floor(_seed2 / 65521);
        return ((int)_seed1 + (int)_seed2) & 32767;
    }

    // Blood3D's QuakeC is compiled without short-circuit logic: both sides of && and || are evaluated, so a roll on
    // the right side of a condition draws even when the left side already decides it. The port writes those
    // conditions as (pure part) & Chance(..) / | Chance(..) to keep the random sequence the same.
    public bool Chance(int a1) => WRand() < a1 / 2;
    public int Random(int a1) => (int)Blood.MulScale15(WRand(), a1);
    public int Random2(int a1) => (int)Blood.MulScale14(WRand(), a1) - a1;
    public int Random3(int a1) => (int)Blood.MulScale15(WRand() + WRand(), a1) - a1;

    // ---------------------------------------------------------------- per-type initialisation (world.qc)
    void InitByType(Sprite e)
    {
        int t = e.Type;
        if (Info.IsDude(t))
            InitDude(e);
        else if (Info.IsThing(t) || t == K.kTrapMachinegun)
            InitThing(e);
        else if ((t >= K.kItemWeaponBase && t < K.kItemWeaponMax) || (t >= K.kItemAmmoBase && t < K.kItemAmmoMax) || (t >= K.kItemBase && t < K.kItemMax))
            InitItem(e);
        else if (t == K.kTrapFlame || t == K.kTrapSawCircular || t == K.kTrapZapSwitchable || t == K.kTrapExploder)
        {
            e.StatNum = K.kStatTraps;
            if (t == K.kTrapExploder)
            {
                e.Cstat &= ~Cstat.Block;
                e.Cstat |= Cstat.Invisible;
                e.WaitTime = (int)Blood.ClipLow(e.WaitTime, 1);
                e.XState = 0;
            }
        }
        else if (t >= K.kMarkerSPStart && t <= K.kMarkerEarthQuake)
        {
            e.StatNum = t == K.kMarkerPath ? K.kStatPathMarker : K.kStatMarker;
            e.Cstat |= Cstat.Invisible;
            e.Cstat &= ~(Cstat.Block | Cstat.Hitscan);
        }
        else if (t == K.kSoundAmbient)
        {
            e.StatNum = K.kStatAmbience;
            e.Cstat |= Cstat.Invisible;
        }
        else if (t >= K.kGenTrigger && t <= K.kSoundPlayer)
        {
            e.StatNum = K.kStatDecoration;
            e.Cstat |= Cstat.Invisible;
            e.Cstat &= ~(Cstat.Block | Cstat.Hitscan);
        }
        else
            e.StatNum = K.kStatDecoration;
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
    }

    void InitItem(Sprite e)
    {
        if (e.Type == K.kItemWeaponVoodooDoll)
        {
            e.Type = K.kItemAmmoVoodooDoll;
            e.InitType = e.Type;
        }
        int t = e.Type;
        e.StatNum = K.kStatItem;
        Func<int, int, int> data = t >= K.kItemWeaponBase && t < K.kItemWeaponMax ? Info.WeaponItem
            : t >= K.kItemAmmoBase && t < K.kItemAmmoMax ? Info.AmmoItem : Info.Item;
        if (e.Picnum == 0) e.Picnum = data(t, Info.ID_picnum);
        if (e.XRepeat == 64 && data(t, Info.ID_xrepeat) != 0) e.XRepeat = data(t, Info.ID_xrepeat);
        if (e.YRepeat == 64 && data(t, Info.ID_yrepeat) != 0) e.YRepeat = data(t, Info.ID_yrepeat);
    }

    void InitThing(Sprite e)
    {
        int t = e.Type;
        e.StatNum = K.kStatThing;
        if (e.Picnum == 0) e.Picnum = Info.Thing(t, Info.TI_picnum);
        if (e.XRepeat == 64 && Info.Thing(t, Info.TI_xrepeat) != 0) e.XRepeat = Info.Thing(t, Info.TI_xrepeat);
        if (e.YRepeat == 64 && Info.Thing(t, Info.TI_yrepeat) != 0) e.YRepeat = Info.Thing(t, Info.TI_yrepeat);
        e.Cstat |= Info.Thing(t, Info.TI_cstat);
        e.XHealth = Info.Thing(t, Info.TI_startHealth) * 16;
        // (a modern map's things keep the clipdist the map gives them: actInit)
        if (!IsModern)
            e.ClipDist = Info.Thing(t, Info.TI_clipdist);
        e.HitTag = Info.Thing(t, Info.TI_flags);
        if ((e.HitTag & K.kPhysGravity) != 0)
            e.HitTag |= K.kPhysFalling;
        e.XVel = e.YVel = e.ZVel = 0;
        if (t == K.kThingArmedProxBomb || t == K.kTrapMachinegun || (IsModern && t == KM.kModernThingTNTProx))
            e.XState = 0;
        else if (t != K.kThingBloodChunks)
            e.XState = 1;
    }

    void InitDude(Sprite e)
    {
        int t = e.Type;
        e.StatNum = K.kStatDude;
        e.XVel = e.YVel = e.ZVel = 0;
        if (IsModern)
        {
            // a modern map's dude may have its own start health (data4); a custom dude and the mother pod keep the
            // map's clipdist, flags and picture (actInit)
            e.XHealth = NnExtDudeStartHealth(e, e.Data4);
            if (t == KM.kDudeModernCustom || t == K.kDudePodMother)
            {
                e.SysData1 = e.Data3;       // the sounds' first id, out of the game's way
                e.Data3 = 0;
                return;
            }
            e.ClipDist = Info.Dude(t, Info.DI_clipdist);
            e.Cstat |= 4096 | Cstat.Hitscan | Cstat.Block;
            if (SeqData.Exists(Info.Dude(t, Info.DI_seqStartID)))
                Seq.Spawn(this, Info.Dude(t, Info.DI_seqStartID), e);
            return;
        }
        e.XHealth = Info.Dude(t, Info.DI_startHealth) * 16;
        e.ClipDist = Info.Dude(t, Info.DI_clipdist);
        e.Cstat |= 4096 | Cstat.Hitscan | Cstat.Block;
        // actInit: only when the RFF has it (the gargoyle statues have none: they stand as placed)
        int seq = Info.Dude(t, Info.DI_seqStartID);
        if (SeqData.Exists(seq))
            Seq.Spawn(this, seq, e);
    }

    // ---------------------------------------------------------------- placement (world.qc)

    // ---------------------------------------------------------------- collision box (engine.qc spriteUpdateBox)
    /// <summary>
    /// The sprite's box from its clipdist and current picture: a clipdist square for face sprites, the picture's line
    /// or rectangle for wall and floor sprites. Blocking sprites are bodies, hitscan-only ones corpses.
    /// </summary>
    public void UpdateBox(Sprite e)
    {
        if (e.Removed)
            return;
        double z = e.BZ;
        var (top, bottom) = Physics.SpriteExtents(z, e.Picnum, e.YRepeat, e.Cstat);
        double r = Math.Max(e.ClipDist / 4.0, 1);
        Vector3 mn = new((float)-r, (float)-r, 0), mx = new((float)r, (float)r, 0);
        float yaw = 0;
        if ((e.Cstat & Cstat.AlignMask) != 0 && e is not Player && Tiles.W(e.Picnum) > 0)
        {
            var (bmin, bmax, boxYaw) = FlatBox(e);
            double q = Blood.Rint(boxYaw / 90);
            if (Math.Abs(boxYaw - q * 90) < 0.01)
            {
                // axis aligned: rotate the box here
                int qi = (int)(q - 4 * Math.Floor(q / 4));
                if (qi == 0) { mn = bmin; mx = bmax; }
                else if (qi == 1) { mn.X = -bmax.Y; mx.X = -bmin.Y; mn.Y = bmin.X; mx.Y = bmax.X; }
                else if (qi == 2) { mn.X = -bmax.X; mx.X = -bmin.X; mn.Y = -bmax.Y; mx.Y = -bmin.Y; }
                else { mn.X = bmin.Y; mx.X = bmax.Y; mn.Y = -bmax.X; mx.Y = -bmin.X; }
            }
            else
            {
                mn = bmin;
                mx = bmax;
                yaw = (float)boxYaw;
            }
        }
        mn.Z = (float)(-(bottom - z) / Blood.ZScale);
        mx.Z = (float)((z - top) / Blood.ZScale);
        if (mx.Z - mn.Z < 1)
        {
            mn.Z -= 0.5f;
            mx.Z += 0.5f;
        }
        e.Mins = mn;
        e.Maxs = mx;
        e.BoxYaw = yaw;
        int contents = e is Player ? (e.XHealth > 0 ? Contents.Body : Contents.Corpse)
            : (e.Cstat & Cstat.Block) != 0 ? ((e.Cstat & Cstat.Hitscan) != 0 ? Contents.Body : Contents.Blocker)
            : (e.Cstat & Cstat.Hitscan) != 0 ? Contents.Corpse : 0;
        if (contents == 0)
        {
            if (e.Body != null)
                e.Body.Enabled = false;
            return;
        }
        if (e.Body == null)
        {
            e.Body = new Body { Owner = e, Sprite = e };
            Collision.Bodies.Add(e.Body);
        }
        e.Body.Enabled = true;
        e.Body.Yaw = yaw;
        e.Body.Contents = contents;
    }

    /// <summary>spriteFlatBox: a wall or floor sprite's box in its own frame (yaw in degrees).</summary>
    static (Vector3 min, Vector3 max, double yaw) FlatBox(Sprite e)
    {
        double w = Tiles.W(e.Picnum) * e.XRepeat / 64.0;
        double h = Tiles.H(e.Picnum) * e.YRepeat / 64.0;
        double xo = (Tiles.XOfs(e.Picnum) + e.XOffset) * e.XRepeat / 64.0;
        if ((e.Cstat & Cstat.XFlip) != 0)
            xo = -xo;
        if ((e.Cstat & Cstat.AlignMask) == Cstat.Wall)
        {
            // local x runs along the sprite (angle - 90), thin across it
            return (new Vector3((float)(-xo - w / 2), -0.5f, 0), new Vector3((float)(-xo + w / 2), 0.5f, 0),
                Blood.AngToYaw(e.Ang - 512));
        }
        double yo = (Tiles.YOfs(e.Picnum) + e.YOffset) * e.YRepeat / 64.0;
        return (new Vector3((float)(-yo - h / 2), (float)(-xo - w / 2), 0), new Vector3((float)(-yo + h / 2), (float)(-xo + w / 2), 0),
            Blood.AngToYaw(e.Ang));
    }

    // ---------------------------------------------------------------- lifetime
    /// <summary>DeleteSprite: the sprite stops existing (dropped from the list at the end of the frame).</summary>
    public void Delete(Sprite e)
    {
        if (e.Removed)
            return;
        e.Removed = true;
        Edicts.Free(e.Edict, _edictTime);
        e.Visible = false;
        e.SeqPlaying = false;
        e.StatNum = K.kStatFree;
        if (e.Body != null)
        {
            Collision.Bodies.Remove(e.Body);
            e.Body = null;
        }
        EvKill(e);
        if (e.RxID > 0 && _receivers.TryGetValue(e.RxID, out var l))
            l.Remove(e);
    }

    /// <summary>A sequence with the remove flag ended (seqProcessOne).</summary>
    public void SeqRemove(Sprite e)
    {
        EvKill(e);
        if ((e.HitTag & K.kHitagRespawn) != 0 && Info.IsDude(e.InitType))
            SpriteRespawnPending(e);
        else
            Delete(e);
    }

    /// <summary>Adds a sprite spawned during play (actSpawnSprite).</summary>
    public Sprite AddSprite(Sprite s)
    {
        if (s.Edict < 0)
            s.Edict = s is Player p ? 1 + p.PlayerIndex : Edicts.Alloc(_edictTime);
        // the list stays in slot order
        int lo = 0, hi = Sprites.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Sprites[mid].Edict <= s.Edict)
                lo = mid + 1;
            else
                hi = mid;
        }
        Sprites.Insert(lo, s);
        for (int i = lo; i < Sprites.Count; i++)
            Sprites[i].Index = i;
        s.PrevOrigin = s.Origin;
        s.PrevAng = s.Ang;
        _spawned.Add(s);
        AddReceiver(s);
        return s;
    }

    public void Sound3D(XObject e, int soundId, int channel = 0, int flags = 0)
    {
        if (soundId != 0)
            SoundPlayed?.Invoke(e, soundId, channel, flags);
        if (IsModern && e is Sprite s)
            PatrolNoteSound(s, s.BX, s.BY, SpriteBSector(s), soundId, 0);
    }

    public void ShowMessage(string text) => MessageShown?.Invoke(text);

    // ---------------------------------------------------------------- the game frame (main.qc bloodFrame)
    /// <summary>One Blood game frame: players, busy sectors, events, sector effects, sequences, actors.</summary>
    public void Tick()
    {
        _edictTime = Frame;
        _spawned.Clear();
        foreach (var s in Sprites)
        {
            s.PrevOrigin = s.Origin;
            s.PrevAng = s.Ang;
        }
        foreach (var s in Sectors)
        {
            s.PrevOrigin = s.Origin;
            s.PrevYaw = s.Yaw;
            s.PrevShear = s.Shear;
        }
        foreach (var p in Players)
        {
            p.PrevZView = p.ZView;
            p.PrevZWeapon = p.ZWeapon;
            p.PrevBZ = p.BZ;
            p.PrevBobHeight = p.BobHeight;
            p.PrevBobWidth = p.BobWidth;
            p.PrevSwayHeight = p.SwayHeight;
            p.PrevSwayWidth = p.SwayWidth;
        }
        // the end-of-level screen stops the game (StartFrame returns while gEndGame is set)
        if (LevelEnded)
            return;
        Board?.BeginFrame();
        if (_levelStartPending && Players.Exists(pl => pl.Ready))
        {
            _levelStartPending = false;
            SendLevelStart();
        }
        foreach (var p in Players)
        {
            if (!p.Ready || p.Removed)
                continue;
            if (!LevelEnded)
                PlayerLatchInput(p, p.Cmd);
            PlayerProcess(p);
            // a hand on the player chokes it each second (ProcessFrame's gChokeCounter, gChoke.Process): the time it
            // takes to shake off with the use key grows, and past a point the view darkens
            if (p.Hand == 1)
            {
                p.ChokeCounter += GameClock.TicsPerFrame * 2;
                while (p.ChokeCounter >= 120)
                {
                    PlayerHandChoke(p);
                    p.ChokeCounter -= 120;
                }
            }
        }
        Core.Perf.Mark("t.players");
        TrProcessBusy();
        Core.Perf.Mark("t.trbusy");
        // trProcessBusy's end: ProcessMotion and AlignSlopes; then the lookups take the level as it is now
        NativeProcessMotion();
        NativeAlignSlopes();
        Map.Refresh();
        NativeUpdateLinks();
        Core.Perf.Mark("t.motion");
        EvProcess();
        Core.Perf.Mark("t.events");
        SectorFxProcess();
        Core.Perf.Mark("t.fx");
        Seq.Process(this, GameClock.TicsPerFrame);
        if (SurfSeqs.Count > 0)
            SurfSeq.Process(this, GameClock.TicsPerFrame);
        NativeSectorPanning();
        Core.Perf.Mark("t.seq");
        ActProcessSprites();
        Core.Perf.Mark("t.actors");
        ActPostProcess();
        Core.Perf.Mark("t.post");
        for (int i = _tempSlots.Count - 1; i >= 0; i--)
            if (_tempSlots[i].frame <= Frame)
            {
                Edicts.Free(_tempSlots[i].slot, Frame);
                _tempSlots.RemoveAt(i);
            }
        // a sprite spawned this frame gets its angle after AddSprite (actFireMissile sets it): no turn from 0 on
        // its first drawn frame (NBlood interpolates only sprites backed up before they moved)
        foreach (var s in _spawned)
            s.PrevAng = s.Ang;
        _spawned.Clear();
        LevelTime++;
        Frame++;
        FrameClock += GameClock.TicsPerFrame;
        if (Sprites.RemoveAll(s => s.Removed) > 0)
            for (int i = 0; i < Sprites.Count; i++)
                Sprites[i].Index = i;
    }
}
