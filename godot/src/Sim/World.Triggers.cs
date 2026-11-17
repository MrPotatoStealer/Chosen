using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Collision;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// Blood's x-object triggers (triggers.cpp, via Blood3D's server/triggers.qc): state changes and what they send,
/// operating sprites, walls and sectors, links (kCmdLink), and the movers (doors, lifts, sliding and rotating
/// sectors, path sectors) with what they carry and push.
/// </summary>
public sealed partial class World
{
    /// <summary>A z-motion sector whose open range the converter gave (sOpen), not its second part.</summary>
    static bool IsZMotionVolume(Sector s) =>
        s.ZPart != "" && s.SOpen != "" && s.MoveOn.X == 0 && s.MoveOn.Y == 0 && s.RotOn == 0
        && !(s.Twin != null && s.ZPart == "floor");

    /// <summary>trTriggerSector for a sector and, of a z-motion sector moving both planes, its other part (the same
    /// Build sector: both move; only the first sends events).</summary>
    public void TrTriggerSectorParts(Sector s, int command, XObject causer, bool enter)
    {
        TrTriggerSector(s, command, causer);
        var t = s.Twin;
        if (t != null && t.HasX && (enter ? t.SEnter : t.SExit) != 0)
            TrTriggerSector(t, command, causer);
    }

    /// <summary>trTriggerSector for a sector pushed or used: of a z-motion sector moving both planes, both parts (one
    /// Build sector, one xsector: pushing E1M3's lift moved only the part hit, its ceiling rose alone). A decoupled
    /// sector's event goes out once.</summary>
    public void TrTriggerSectorBoth(Sector s, int command, XObject causer)
    {
        TrTriggerSector(s, command, causer);
        var t = s?.Twin;
        if (t != null && t.HasX && !(s.Decoupled != 0 && s.TxID > 0))
            TrTriggerSector(t, command, causer);
    }

    /// <summary>A sprite's sector x-object (Blood's xsector of its sectnum; World.SectorTrack keeps the sector), or null
    /// (no level).</summary>
    public Sector SpriteSector(Sprite e) => Map != null ? XSectorOf(SpriteBSector(e)) : null;

    public Sector SpriteSectorUpdate(Sprite e) => SpriteSector(e);

    /// <summary>A sprite moved where no move leads (a teleport): its sector is looked up afresh.</summary>
    public void SpriteSectorForget(Sprite e)
    {
        e.SectKnown = false;
        e.BSector = 0;
    }

    public static bool StandingOnMover(Sprite e) => e.FlorHitEnt is Sector s && s.IsMover && s.BusyActive;

    // ---------------------------------------------------------------- wave / state
    public static int GetWaveValue(int nPhase, int nType) => nType switch
    {
        0 => (int)(32768 - Blood.Sar(Blood.Cos(Blood.Sar(nPhase * 1024.0, 16)), 15)),
        1 => nPhase,
        2 => (int)(65536 - Blood.Sar(Blood.Cos(Blood.Sar(nPhase * 512.0, 16)), 14)),
        3 => (int)Blood.Sar(Blood.Sin(Blood.Sar(nPhase * 512.0, 16)), 14),
        _ => nPhase,
    };

    static int Tics(int tenths) => (int)Blood.Int(tenths * 120 / 10.0);

    public bool SetSpriteState(Sprite e, int nState, XObject causer)
    {
        if ((e.XBusy & 65535) == 0 && e.XState == nState)
            return false;
        e.XBusy = nState * 65536;
        e.XState = nState;
        EvKillBy(e, causer);
        if ((e.HitTag & K.kHitagRespawn) != 0 && Info.IsDude(e.InitType))
        {
            e.RespawnPending = 3;
            EvPostCallback(e, MonsterRespawnTime, K.kCallbackRespawn);
            return true;
        }
        if (e.RestState != nState && e.WaitTime > 0)
            EvPost(e, Tics(e.WaitTime), e.RestState != 0 ? K.kCmdOn : K.kCmdOff, causer);
        if (e.TxID != 0)
        {
            if (e.XCommand != K.kCmdLink && e.TriggerOn != 0 && e.XState != 0)
                EvSend(e, e.TxID, e.XCommand, causer);
            if (e.XCommand != K.kCmdLink && e.TriggerOff != 0 && e.XState == 0)
                EvSend(e, e.TxID, e.XCommand, causer);
        }
        return true;
    }

    public bool SetWallState(Wall w, int nState, XObject causer)
    {
        if ((w.XBusy & 65535) == 0 && w.XState == nState)
            return false;
        w.XBusy = nState * 65536;
        w.XState = nState;
        EvKillBy(w, causer);
        if (w.RestState != nState && w.WaitTime > 0)
            EvPost(w, Tics(w.WaitTime), w.RestState != 0 ? K.kCmdOn : K.kCmdOff, causer);
        if (w.TxID != 0)
        {
            if (w.XCommand != K.kCmdLink && w.TriggerOn != 0 && w.XState != 0)
                EvSend(w, w.TxID, w.XCommand, causer);
            if (w.XCommand != K.kCmdLink && w.TriggerOff != 0 && w.XState == 0)
                EvSend(w, w.TxID, w.XCommand, causer);
        }
        WallUpdateVisual(w);
        return true;
    }

    public bool SetSectorState(Sector s, int nState, XObject causer)
    {
        if ((s.XBusy & 65535) == 0 && s.XState == nState)
            return false;
        s.XBusy = nState * 65536;
        s.XState = nState;
        EvKillBy(s, causer);
        if (nState == 1)
        {
            if (s.XCommand != K.kCmdLink && s.TriggerOn != 0 && s.TxID != 0)
                EvSend(s, s.TxID, s.XCommand, causer);
            if (s.StopOn != 0)
            {
                s.StopOn = 0;
                s.StopOff = 0;
            }
            else if (s.ReTriggerA != 0)
                EvPost(s, Tics(s.WaitTimeA), K.kCmdOff, causer);
        }
        else
        {
            if (s.XCommand != K.kCmdLink && s.TriggerOff != 0 && s.TxID != 0)
                EvSend(s, s.TxID, s.XCommand, causer);
            if (s.StopOff != 0)
            {
                s.StopOn = 0;
                s.StopOff = 0;
            }
            else if (s.ReTriggerB != 0)
                EvPost(s, Tics(s.WaitTimeB), K.kCmdOn, causer);
        }
        return true;
    }

    static void AddBusy(Sector s, int proc, int nDelta)
    {
        if (!s.BusyActive)
        {
            s.BusyActive = true;
            s.BusyProc = proc;
            s.BusyVal = s.XBusy;   // continue from the current position
        }
        s.BusyDelta = nDelta;
    }

    static void ReverseBusy(Sector s)
    {
        if (s.BusyActive)
            s.BusyDelta = -s.BusyDelta;
    }

    static int GetSourceBusy(XObject src) => src?.XBusy ?? 0;

    // ---------------------------------------------------------------- sector sounds
    IEnumerable<Sprite> SoundSectorSprites(Sector s)
    {
        foreach (var e in Sprites)
            if (e.Type == K.kSoundSector && e.StatNum == K.kStatDecoration && e.HasX && !e.Removed
                && (e.SMarker0 == s || SpriteSector(e) == s))
                yield return e;
    }

    void SectorStartSound(Sector s, int nState)
    {
        if (nState != 0) { if (s.SndCloseStart != 0) Sound3D(s, s.SndCloseStart); }
        else if (s.SndOpenStart != 0) Sound3D(s, s.SndOpenStart);
        foreach (var e in SoundSectorSprites(s))
        {
            if (nState != 0) { if (e.Data3 != 0) Sound3D(e, e.Data3); }
            else if (e.Data1 != 0) Sound3D(e, e.Data1);
        }
    }

    void SectorEndSound(Sector s, int nState)
    {
        if (nState != 0) { if (s.SndOpenEnd != 0) Sound3D(s, s.SndOpenEnd); }
        else if (s.SndCloseEnd != 0) Sound3D(s, s.SndCloseEnd);
        foreach (var e in SoundSectorSprites(s))
        {
            if (nState != 0) { if (e.Data2 != 0) Sound3D(e, e.Data2); }
            else if (e.Data4 != 0) Sound3D(e, e.Data4);
        }
    }

    void PathSound(Sector s, int nSound)
    {
        Sound3D(s, nSound);
        foreach (var e in SoundSectorSprites(s))
            Sound3D(e, nSound);
    }

    // ---------------------------------------------------------------- mover placement

    /// <summary>findradius over sprites: those whose box centre is within r of c.</summary>
    // the sprites by a coarse grid of their middles, made at the first SpritesNear of a game frame and again when the
    // list changes: a mover asked for every sprite near it two or three times a frame, each a scan of all of them (E1M8's
    // 17 bobbing planes, about 4 ms a game frame). A sprite moving during the frame is found from where it was made,
    // GridPad around the asked circle; the answer is tested where it is now, in list order as the scan gave it.
    const float GridCell = 256, GridPad = 128;
    readonly Dictionary<long, List<Sprite>> _spriteGrid = new();
    int _spriteGridFrame = -1, _spriteGridCount = -1;
    Sprite _spriteGridFirst;

    static long GridKey(int x, int y) => ((long)x << 32) ^ (uint)y;

    void EnsureSpriteGrid()
    {
        var first = Sprites.Count > 0 ? Sprites[0] : null;
        if (_spriteGridFrame == Frame && _spriteGridCount == Sprites.Count && _spriteGridFirst == first)
            return;
        foreach (var l in _spriteGrid.Values)
            l.Clear();
        foreach (var e in Sprites)
        {
            var mid = e.Origin + (e.Mins + e.Maxs) * 0.5f;
            var key = GridKey((int)Mathf.Floor(mid.X / GridCell), (int)Mathf.Floor(mid.Y / GridCell));
            if (!_spriteGrid.TryGetValue(key, out var l))
                _spriteGrid[key] = l = new List<Sprite>();
            l.Add(e);
        }
        _spriteGridFrame = Frame;
        _spriteGridCount = Sprites.Count;
        _spriteGridFirst = first;
    }

    List<Sprite> SpritesNear(Vector3 c, float r)
    {
        EnsureSpriteGrid();
        var list = new List<Sprite>();
        float reach = r + GridPad;
        int x0 = (int)Mathf.Floor((c.X - reach) / GridCell), x1 = (int)Mathf.Floor((c.X + reach) / GridCell);
        int y0 = (int)Mathf.Floor((c.Y - reach) / GridCell), y1 = (int)Mathf.Floor((c.Y + reach) / GridCell);
        float rr = r * r;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                if (!_spriteGrid.TryGetValue(GridKey(x, y), out var cell))
                    continue;
                foreach (var e in cell)
                {
                    if (e.Removed)
                        continue;
                    float mx = e.Origin.X + (e.Mins.X + e.Maxs.X) * 0.5f - c.X;
                    float my = e.Origin.Y + (e.Mins.Y + e.Maxs.Y) * 0.5f - c.Y;
                    float mz = e.Origin.Z + (e.Mins.Z + e.Maxs.Z) * 0.5f - c.Z;
                    if (mx * mx + my * my + mz * mz <= rr)
                        list.Add(e);
                }
            }
        if (list.Count > 1)
            list.Sort((a, b) => a.Index.CompareTo(b.Index));
        return list;
    }

    // placing the movers at their starting state (trInit): from where they were built, carrying only their own sprites
    // (E1M3's lava sliders 84, 88 and 92 start 2400 units on, over the vent: Drag must not take its grate along)

    /// <summary>A point taken from where a sliding or turning sector is now back to where Build stores it (ON, its
    /// outline's place): RigidPoint's inverse.</summary>
    /// <summary>How far a sliding or turning sector at yaw is turned from where Build stores it (degrees), and its
    /// origin there. A step-turning one (613) is stored where it starts, its yaw counting every step since; the others
    /// at ON.</summary>
    public static (double turn, Vector3 at) FromStored(Sector m, double yaw) => m.Type == K.kSectorRotateStep
        ? (yaw - m.BaseYaw, m.BaseOrg)
        : (yaw - m.BaseYaw - m.StepOfs - m.RotOn, m.BaseOrg + m.MoveOn);

    public static Vector3 ToStored(Sector m, Vector3 p)
    {
        var (turn, at) = FromStored(m, m.Yaw);
        double c = Math.Cos(-turn * Math.PI / 180), s = Math.Sin(-turn * Math.PI / 180);
        var rel = p - m.Origin;
        return at + new Vector3((float)(rel.X * c - rel.Y * s), (float)(rel.X * s + rel.Y * c), rel.Z);
    }

    readonly List<Sector> _wholeMovers = new();
    readonly HashSet<int> _wholeMoverSectors = new();

    /// <summary>Is Build sector s (0-based) one that moves whole?</summary>
    public bool IsWholeMover(int s) => _wholeMoverSectors.Contains(s);

    /// <summary>The whole moving sector (0-based) holding p where it is now, by holds (its sector and p taken back to its
    /// stored place: the renderer's outlines and planes), else -1.</summary>
    public int WholeMoverAt(Vector3 p, Func<int, Vector3, bool> holds)
    {
        foreach (var m in _wholeMovers)
        {
            var b = m.AbsBounds;
            if (p.X < b.Position.X - 1 || p.Y < b.Position.Y - 1 || p.X > b.End.X + 1 || p.Y > b.End.Y + 1)
                continue;
            if (holds(m.BSector - 1, ToStored(m, p)))
                return m.BSector - 1;
        }
        return -1;
    }

    int GenSectorBusy(Sector s, int a2, XObject causer)
    {
        s.XBusy = a2;
        if (s.XCommand == K.kCmdLink && s.TxID != 0)
            EvSend(s, s.TxID, K.kCmdLink, causer);
        if ((a2 & 65535) == 0)
        {
            SetSectorState(s, a2 >> 16, causer);
            SectorEndSound(s, a2 >> 16);
            return 3;
        }
        return 0;
    }

    // path sector: move between path markers (marker0 -> marker1)

    int RunBusyProc(Sector s, int a2)
    {
        return NativeRunBusyProc(s, a2);
    }

    // the hitch log's counter per busy sector type (r_hitchlog: busy616 ms/calls)
    static readonly Dictionary<int, string> _busyKey = new();

    void TrProcessBusy()
    {
        System.Array.Clear(VelFloor);
        System.Array.Clear(VelCeil);
        foreach (var s in Sectors)
        {
            if (!s.BusyActive)
                continue;
            int oldBusy = s.BusyVal;
            s.BusyVal = (int)Blood.ClipRange(oldBusy + s.BusyDelta * 4, 0, 65536);
            long t0 = Core.Perf.Start();
            // (a modern map's paused sector: its motion is dropped until a command continues it)
            int nStatus = s.MotionPaused ? 3 : RunBusyProc(s, s.BusyVal);
            if (t0 != 0)
            {
                if (!_busyKey.TryGetValue(s.Type, out var key))
                    _busyKey[s.Type] = key = "busy" + s.Type;
                Core.Perf.Add(key, t0);
            }
            if (nStatus == 1)
                s.BusyVal = oldBusy;
            else if (nStatus == 2)
            {
                s.BusyVal = oldBusy;
                s.BusyDelta = -s.BusyDelta;
            }
            else if (nStatus == 3)
                s.BusyActive = false;
        }
    }

    // ---------------------------------------------------------------- operate
    /// <summary>LifeLeechOperate: a dropped life leech. Its owner takes it back by using it; otherwise it fires at the
    /// dude its proximity picked.</summary>
    void LifeLeechOperate(Sprite e, int cmd, XObject causer)
    {
        if (cmd == K.kCmdSpritePush)
        {
            var p = Players.Find(pl => pl.PlayerIndex == e.Data4 && pl.Ready);
            if (p != null && p.XHealth > 0)
            {
                EvKill(e);
                p.Ammo[8] = Blood.ClipHigh(p.Ammo[8] + e.Data3, AmmoMax(8));
                p.HasWeapon[K.kWeaponLifeLeech] = 1;
                if (p.CurWeapon != K.kWeaponLifeLeech && !CheckLitSprayOrTNT(p))
                {
                    p.WeaponState = 0;
                    p.NextWeapon = K.kWeaponLifeLeech;
                }
            }
            ActPostSprite(e, K.kStatFree);
            return;
        }
        if (cmd == K.kCmdSpriteProximity)
        {
            var t = e.XTarget;
            if (t != null && e.StateTimer == 0 && t.StatNum == K.kStatDude && (t.HitTag & 32) == 0 && t.HasX)
            {
                var (top, _) = Extents(e);
                double z1 = (top - e.BZ) - 256;
                double x = t.BX, y = t.BY, z = t.BZ;
                double nDist = Blood.ApproxDist(x - e.BX, y - e.BY);
                if (nDist != 0 && CanSee(e.BX, e.BY, top, x, y, z, e, t))
                {
                    double tt = Blood.Int(nDist * 4096 / 1747626);
                    x += Blood.Sar(t.XVel * tt, 12);
                    y += Blood.Sar(t.YVel * tt, 12);
                    int angBak = e.Ang;
                    e.Ang = Blood.GetAngle(x - e.BX, y - e.BY);
                    double dx = Blood.Sar(Blood.Cos(e.Ang), 16), dy = Blood.Sar(Blood.Sin(e.Ang), 16);
                    double tz = t.BZ - t.YRepeat * Info.Dude(t.Type, K.DI_aimHeight) * 4;
                    double dz = Blood.DivScale10(tz - top - 256, nDist);
                    int nMissileType = e.Data3 != 0 ? K.kMissileLifeLeechAltSmall : K.kMissileLifeLeechAltNormal;
                    int t2 = e.Data3 == 0 ? 12 : 36;
                    var m = ActFireMissile(e, 0, z1, dx, dy, dz, nMissileType);
                    if (m != null)
                    {
                        m.Owner = e.Owner;
                        e.StateTimer = 1;
                        EvPostCallback(e, t2, K.kCallbackLeechStateTimer);
                        e.Data3 = (int)Blood.ClipLow(e.Data3 - 1, 0);
                    }
                    e.Ang = angBak;
                }
            }
            return;
        }
        ActPostSprite(e, K.kStatFree);
    }

    void OperateSprite(Sprite e, int cmd, XObject causer)
    {
        if (IsModern && ModernTypeOperateSprite(e, cmd, causer, _evSource))
            return;
        if (cmd == K.kCmdLock) { e.XLocked = 1; return; }
        if (cmd == K.kCmdUnlock) { e.XLocked = 0; return; }
        if (cmd == K.kCmdToggleLock) { e.XLocked ^= 1; return; }
        if (e.StatNum == K.kStatDude && Info.IsDude(e.Type))
        {
            if (cmd == K.kCmdOff)
                SetSpriteState(e, 0, causer);
            else if (cmd == K.kCmdSpriteProximity || cmd == K.kCmdOn || cmd == K.kCmdSpritePush || cmd == K.kCmdSpriteTouch)
            {
                if (cmd == K.kCmdSpriteProximity && e.XState != 0)
                    return;
                if (e.XState == 0)
                    SetSpriteState(e, 1, causer);
                AiActivateDude(e);
            }
            return;
        }
        int t = e.Type;
        if (t == K.kTrapMachinegun)
        {
            if (e.XHealth <= 0) return;
            if (cmd == K.kCmdOff)
            {
                if (!SetSpriteState(e, 0, causer)) return;
                Seq.Spawn(this, 40, e);
            }
            else if (cmd == K.kCmdOn)
            {
                if (!SetSpriteState(e, 1, causer)) return;
                Seq.Spawn(this, 38, e, MGunOpenSeqCallback);
                if (e.Data1 > 0)
                    e.Data2 = e.Data1;
            }
        }
        else if (t == K.kThingFallingRock)
        {
            if (SetSpriteState(e, 1, causer))
                e.HitTag |= 7;
        }
        else if (t == K.kThingWallCrack || t == K.kThingCrateFace)
        {
            if (SetSpriteState(e, 0, causer))
                ActPostSprite(e, K.kStatFree);
        }
        else if (t == K.kTrapZapSwitchable)
        {
            if (cmd == K.kCmdOff)
            {
                e.XState = 0;
                e.Cstat |= Cstat.Invisible;
                e.Cstat &= ~Cstat.Block;
            }
            else if (cmd == K.kCmdOn)
            {
                e.XState = 1;
                e.Cstat &= ~Cstat.Invisible;
                e.Cstat |= Cstat.Block;
            }
            else if (cmd == K.kCmdToggle)
            {
                e.XState ^= 1;
                e.Cstat ^= Cstat.Invisible;
                e.Cstat ^= Cstat.Block;
            }
            e.Visible = (e.Cstat & Cstat.Invisible) == 0;
            UpdateBox(e);
        }
        else if (t == K.kTrapFlame)
        {
            if (cmd == K.kCmdOff)
            {
                if (!SetSpriteState(e, 0, causer)) return;
                Seq.Spawn(this, 40, e);
                Kill3DSound(e);
            }
            else if (cmd == K.kCmdOn)
            {
                if (!SetSpriteState(e, 1, causer)) return;
                Seq.Spawn(this, 38, e);
                Sound3D(e, 441);
            }
        }
        else if (t == K.kSwitchPadlock)
        {
            if (cmd == K.kCmdOff)
                SetSpriteState(e, 0, causer);
            else if (cmd == K.kCmdOn)
            {
                if (!SetSpriteState(e, 1, causer)) return;
                Seq.Spawn(this, 37, e);
            }
            else
            {
                SetSpriteState(e, e.XState ^ 1, causer);
                if (e.XState != 0) Seq.Spawn(this, 37, e);
            }
        }
        else if (t == K.kSwitchToggle)
        {
            if (cmd == K.kCmdOff)
            {
                if (!SetSpriteState(e, 0, causer)) return;
                Sound3D(e, e.Data2);
            }
            else if (cmd == K.kCmdOn)
            {
                if (!SetSpriteState(e, 1, causer)) return;
                Sound3D(e, e.Data1);
            }
            else
            {
                if (!SetSpriteState(e, e.XState ^ 1, causer)) return;
                Sound3D(e, e.XState != 0 ? e.Data1 : e.Data2);
            }
        }
        else if (t == K.kSwitchOneWay)
        {
            if (cmd == K.kCmdOff)
            {
                if (!SetSpriteState(e, 0, causer)) return;
                Sound3D(e, e.Data2);
            }
            else if (cmd == K.kCmdOn)
            {
                if (!SetSpriteState(e, 1, causer)) return;
                Sound3D(e, e.Data1);
            }
            else
            {
                if (!SetSpriteState(e, e.RestState ^ 1, causer)) return;
                Sound3D(e, e.XState != 0 ? e.Data1 : e.Data2);
            }
        }
        else if (t == K.kSwitchCombo)
        {
            if (cmd == K.kCmdOff)
            {
                e.Data1--;
                if (e.Data1 < 0)
                    e.Data1 += e.Data3;
            }
            else
            {
                e.Data1++;
                if (e.Data1 >= e.Data3)
                    e.Data1 -= e.Data3;
            }
            Sound3D(e, e.Data4, -1);
            if (e.XCommand == K.kCmdLink && e.TxID > 0)
                EvSend(e, e.TxID, K.kCmdLink, causer);
            SetSpriteState(e, e.Data1 == e.Data2 ? 1 : 0, causer);
        }
        else if (t == K.kMarkerDudeSpawn)
        {
            if (Monsters && Info.IsDude(e.Data1))
            {
                var spawn = ActSpawnDude(e, e.Data1, -1, 0);
                if (spawn != null)
                {
                    KillMgrAddCountSprite(spawn);
                    int d = e.Data1;
                    if (d == K.kDudeBurningInnocent || d == K.kDudeBurningCultist || d == K.kDudeBurningZombieAxe
                        || d == K.kDudeBurningZombieButcher || d == K.kDudeBurningTinyCaleb || d == K.kDudeBurningBeast)
                    {
                        spawn.XHealth = Info.Dude(d, Info.DI_startHealth) * 16;
                        spawn.BurnTime = 10;
                        spawn.XTarget = null;
                        AiActivateDude(spawn);
                    }
                }
            }
        }
        else if (t == K.kMarkerEarthQuake)
        {
            e.TriggerOn = 0;
            e.IsTriggered = 1;
            SetSpriteState(e, 1, causer);
            foreach (var p in Players)
            {
                double dx = Blood.Sar(e.BX - p.BX, 4), dy = Blood.Sar(e.BY - p.BY, 4), dz = Blood.Sar(e.BZ - p.BZ, 8);
                double nDist = dx * dx + dy * dy + dz * dz + 262144;
                p.QuakeEffect = Blood.DivScale16(e.Data1, nDist);
            }
        }
        else if (t == K.kThingTNTBarrel || t == K.kThingArmedTNTStick || t == K.kThingArmedTNTBundle || t == K.kThingArmedSpray)
        {
            if (t == K.kThingTNTBarrel && (e.HitTag & K.kHitagRespawn) != 0)
                return;
            ActExplodeSprite(e);
        }
        else if (t == K.kTrapExploder)
        {
            if (cmd == K.kCmdOn)
                SetSpriteState(e, 1, causer);
            else
            {
                e.Cstat &= ~Cstat.Invisible;
                ActExplodeSprite(e);
            }
        }
        else if (t == K.kThingArmedRemoteBomb)
        {
            if (e.StatNum != K.kStatRespawn)
            {
                if (cmd != K.kCmdOn)
                    ActExplodeSprite(e);
                else
                {
                    Sound3D(e, 454);
                    EvPost(e, 18, K.kCmdOff, causer);
                }
            }
        }
        else if (t == K.kThingArmedProxBomb)
        {
            if (e.StatNum != K.kStatRespawn)
            {
                if (cmd == K.kCmdSpriteProximity)
                {
                    if (e.XState != 0) return;
                    Sound3D(e, 452);
                    EvPost(e, 30, K.kCmdOff, causer);
                    e.XState = 1;
                    Sound3D(e, 451);
                    e.XProximity = 1;
                }
                else if (cmd == K.kCmdOn)
                {
                    Sound3D(e, 451);
                    e.XProximity = 1;
                }
                else
                    ActExplodeSprite(e);
            }
        }
        else if (t == K.kThingDroppedLifeLeech)
            LifeLeechOperate(e, cmd, causer);
        else if (t >= K.kGenTrigger && t <= K.kGenSound)
        {
            if (cmd == K.kCmdOff)
                SetSpriteState(e, 0, causer);
            else if (cmd == K.kCmdRepeat)
            {
                if (t != K.kGenTrigger)
                    ActivateGenerator(e);
                if (e.TxID != 0)
                    EvSend(e, e.TxID, e.XCommand, causer);
                if (e.BusyTime > 0)
                {
                    int nRand = Random2(e.Data1);
                    EvPost(e, Tics(nRand + e.BusyTime), K.kCmdRepeat, causer);
                }
            }
            else if (e.XState == 0)
            {
                SetSpriteState(e, 1, causer);
                EvPost(e, 0, K.kCmdRepeat, causer);
            }
        }
        else if (t == K.kSoundPlayer)
        {
            var me = Me;
            if (me != null)
            {
                if (me.XHealth <= 0)
                    return;
                me.RestTime = 0;
                SoundLocal(me, e.Data1);
            }
        }
        else if (t == K.kThingObjectGib || t == K.kThingObjectExplode || t == K.kThingBloodBits || t == K.kThingBloodChunks || t == K.kThingZombieHead)
        {
            int ns = cmd == K.kCmdOff ? 0 : cmd == K.kCmdOn ? 1 : e.XState ^ 1;
            if (!SetSpriteState(e, ns, causer)) return;
            ActActivateGibObject(e);
        }
        else
        {
            SetSpriteState(e, cmd == K.kCmdOff ? 0 : cmd == K.kCmdOn ? 1 : e.XState ^ 1, causer);
        }
    }

    // gib walls vanish when broken; wall switches only affect logic (sprite switches carry the visual)
    void WallUpdateVisual(Wall w)
    {
        if (w.Type != K.kWallGib)
            return;
        w.Broken = w.XState != 0;
        WallVisibilityChanged?.Invoke(w);
    }

    /// <summary>SetupGibWallState: a broken gib wall stops blocking (Build's collision reads the flags), whole again
    /// it blocks from its own side and hitscans when it takes vector triggers, masked on both sides.</summary>
    void SetupGibWallState(Wall w)
    {
        if (Map == null || !Map.HasClipData || w.BWall < 0 || w.BWall >= Map.WallCount)
            return;
        int bw = w.BWall, nw = Map.WallNextWall(bw);
        int c = Map.WallCstat(bw), c2 = nw >= 0 ? Map.WallCstat(nw) : 0;
        if (w.XState != 0)
        {
            c &= ~65;
            if (nw >= 0)
            {
                c2 &= ~65;
                c &= ~16;
                c2 &= ~16;
            }
        }
        else
        {
            bool vector = w.XVector != 0;
            c |= 1;
            if (vector)
                c |= 64;
            if (nw >= 0)
            {
                c2 &= ~1;
                if (vector)
                    c2 |= 64;
                c |= 16;
                c2 |= 16;
            }
        }
        Map.SetWallCstat(bw, c);
        if (nw >= 0)
            Map.SetWallCstat(nw, c2);
        // a level from its Build data: the board's walls too (the native world draws them, and the lookups take their
        // flags again when the sector reshapes: E1M3's train windows stayed drawn, unblocking, once shot)
        if (Board != null)
        {
            Board.Walls[bw].Cstat = c;
            if (nw >= 0)
                Board.Walls[nw].Cstat = c2;
        }
    }

    /// <summary>A wall appeared or vanished (gib walls); the renderer hides its brushes.</summary>
    public event Action<Wall> WallVisibilityChanged;

    void OperateWall(Wall w, int cmd, XObject causer)
    {
        if (IsModern && ModernTypeOperateWall(w, cmd, causer))
            return;
        if (cmd == K.kCmdLock) { w.XLocked = 1; return; }
        if (cmd == K.kCmdUnlock) { w.XLocked = 0; return; }
        if (cmd == K.kCmdToggleLock) { w.XLocked ^= 1; return; }
        if (w.Type == K.kWallGib)
        {
            bool bStatus;
            if (cmd == K.kCmdOn || cmd == K.kCmdWallImpact)
                bStatus = SetWallState(w, 1, causer);
            else if (cmd == K.kCmdOff)
                bStatus = SetWallState(w, 0, causer);
            else
                bStatus = SetWallState(w, w.XState ^ 1, causer);
            if (bStatus)
                SetupGibWallState(w);
            if (bStatus && w.XState != 0)
            {
                int nType = (int)Blood.ClipRange(w.GibType, 0, 31);
                if (nType > 0)
                    GibBrush(w, nType);
            }
            return;
        }
        SetWallState(w, cmd == K.kCmdOff ? 0 : cmd == K.kCmdOn ? 1 : w.XState ^ 1, causer);
    }

    void OperateDoor(Sector s, int cmd, int busyWave)
    {
        if (cmd == K.kCmdOff)
        {
            if (s.XBusy == 0) return;
            AddBusy(s, busyWave, -(int)Blood.Int(65536 / Blood.ClipLow(Tics(s.BusyTimeB), 1)));
            SectorStartSound(s, 1);
        }
        else if (cmd == K.kCmdOn)
        {
            if (s.XBusy == 65536) return;
            AddBusy(s, busyWave, (int)Blood.Int(65536 / Blood.ClipLow(Tics(s.BusyTimeA), 1)));
            SectorStartSound(s, 0);
        }
        else
        {
            if ((s.XBusy & 65535) != 0)
            {
                if (s.Interruptable != 0)
                {
                    ReverseBusy(s);
                    s.XState = s.XState != 0 ? 0 : 1;
                }
            }
            else
            {
                bool t = s.XState == 0;
                int nDelta = t ? (int)Blood.Int(65536 / Blood.ClipLow(Tics(s.BusyTimeA), 1))
                    : -(int)Blood.Int(65536 / Blood.ClipLow(Tics(s.BusyTimeB), 1));
                AddBusy(s, busyWave, nDelta);
                SectorStartSound(s, s.XState);
            }
        }
    }

    Sprite FindPathMarker(int nId)
    {
        foreach (var e in Sprites)
            if (e.Type == K.kMarkerPath && e.HasX && e.Data1 == nId && !e.Removed)
                return e;
        return null;
    }

    void OperateSector(Sector s, int cmd, XObject causer)
    {
        if (IsModern && ModernTypeOperateSector(s, cmd, causer))
            return;
        if (cmd == K.kCmdLock) { s.XLocked = 1; return; }
        if (cmd == K.kCmdUnlock) { s.XLocked = 0; return; }
        if (cmd == K.kCmdToggleLock) { s.XLocked ^= 1; return; }
        if (cmd == K.kCmdStopOff) { s.StopOn = 0; s.StopOff = 1; return; }
        if (cmd == K.kCmdStopOn) { s.StopOn = 1; s.StopOff = 0; return; }
        if (cmd == K.kCmdStopNext) { s.StopOn = 1; s.StopOff = 1; return; }
        if (IsModern && s.MotionPaused)
            return;
        int t = s.Type;
        if (t == K.kSectorZMotionSprite)
            OperateDoor(s, cmd, K.BUSYID_1);
        else if (t == K.kSectorZMotion)
            OperateDoor(s, cmd, K.BUSYID_2);
        else if (t == K.kSectorSlideMarked || t == K.kSectorSlide)
            OperateDoor(s, cmd, K.BUSYID_3);
        else if (t == K.kSectorRotateMarked || t == K.kSectorRotate)
            OperateDoor(s, cmd, K.BUSYID_4);
        else if (t == K.kSectorRotateStep)
        {
            if (cmd == K.kCmdOn)
            {
                s.XState = 0;
                s.XBusy = 0;
                s.BusyActive = false;
                AddBusy(s, K.BUSYID_5, (int)Blood.Int(65536 / Blood.ClipLow(Tics(s.BusyTimeA), 1)));
                s.BusyVal = 0;
                SectorStartSound(s, 0);
            }
            else if (cmd == K.kCmdOff)
            {
                s.XState = 1;
                s.XBusy = 65536;
                s.BusyActive = false;
                AddBusy(s, K.BUSYID_5, -(int)Blood.Int(65536 / Blood.ClipLow(Tics(s.BusyTimeB), 1)));
                s.BusyVal = 65536;
                SectorStartSound(s, 1);
            }
        }
        else if (t == K.kSectorTeleport)
        {
            NativeOperateTeleport(s);
        }
        else if (t == K.kSectorPath)
        {
            NativeOperatePath(s, cmd, causer);
        }
        else if (s.BusyTimeA == 0 && s.BusyTimeB == 0)
            SetSectorState(s, cmd == K.kCmdOff ? 0 : cmd == K.kCmdOn ? 1 : s.XState ^ 1, causer);
        else
            OperateDoor(s, cmd, K.BUSYID_6);
    }

    void LinkSector(Sector s, XObject src)
    {
        NativeLinkSector(s, src);
    }

    void LinkSprite(Sprite e, XObject src)
    {
        int nBusy = GetSourceBusy(src);
        if (e.Type == K.kSwitchCombo)
        {
            if (src is Sprite ss)
            {
                e.Data1 = ss.Data1;
                SetSpriteState(e, e.Data1 == e.Data2 ? 1 : 0, null);
            }
        }
        else
        {
            e.XBusy = nBusy;
            if ((e.XBusy & 65535) == 0)
                SetSpriteState(e, nBusy >> 16, null);
        }
    }

    void LinkWall(Wall w, XObject src)
    {
        int nBusy = GetSourceBusy(src);
        w.XBusy = nBusy;
        if ((w.XBusy & 65535) == 0)
            SetWallState(w, nBusy >> 16, null);
    }

    public void TrTriggerSector(Sector s, int command, XObject causer)
    {
        if (s == null || !s.HasX)
            return;
        if (s.XLocked == 0 && s.IsTriggered == 0)
        {
            if (s.TriggerOnce != 0)
                s.IsTriggered = 1;
            if (s.Decoupled != 0 && s.TxID > 0)
                EvSend(s, s.TxID, s.XCommand, causer);
            else
                OperateSector(s, command, causer);
        }
    }

    public void TrTriggerWall(Wall w, int command, XObject causer)
    {
        if (w == null || !w.HasX)
            return;
        if (w.XLocked == 0 && w.IsTriggered == 0)
        {
            if (w.TriggerOnce != 0)
                w.IsTriggered = 1;
            if (w.Decoupled != 0 && w.TxID > 0)
                EvSend(w, w.TxID, w.XCommand, causer);
            else
                OperateWall(w, command, causer);
        }
    }

    public void TrTriggerSprite(Sprite e, int command, XObject causer)
    {
        if (e == null || !e.HasX)
            return;
        if (e.XLocked == 0 && e.IsTriggered == 0)
        {
            if (e.TriggerOnce != 0)
                e.IsTriggered = 1;
            if (e.Decoupled != 0 && e.TxID > 0)
                EvSend(e, e.TxID, e.XCommand, causer);
            else
                OperateSprite(e, command, causer);
        }
    }

    /// <summary>trMessage: a command arriving at an x-object (from a channel or a posted event).</summary>
    public void TrMessage(XObject target, int cmd, XObject causer)
    {
        if (target == null || target.Removed || !target.HasX)
            return;
        if (target is Sprite sp && sp.StatNum == K.kStatFree)
            return;
        if (target.XLocked != 0 && cmd != K.kCmdUnlock && cmd != K.kCmdToggleLock)
            return;
        if (cmd == K.kCmdLink)
        {
            switch (target)
            {
                case Sector s: LinkSector(s, _linkSource); break;
                case Wall w: LinkWall(w, _linkSource); break;
                case Sprite e: LinkSprite(e, _linkSource); break;
            }
            return;
        }
        if (cmd == KM.kCmdModernUse && IsModern)
        {
            ModernTypeTrigger(target, _evSource, causer);
            return;
        }
        switch (target)
        {
            case Sector s: OperateSector(s, cmd, causer); break;
            case Wall w: OperateWall(w, cmd, causer); break;
            case Sprite e: OperateSprite(e, cmd, causer); break;
        }
    }

    /// <summary>Explosions trigger impact-sensitive walls within range (gAffectedXWalls).</summary>
    public void TrExplosionImpact(Sprite ex, double radius, XObject owner)
    {
        foreach (var w in Walls)
        {
            if (!w.HasX || w.XVector == 0 || w.Broken)
                continue;
            var b = w.Bounds;
            // nearest point of the brush box to the explosion
            var c = ex.Origin.Clamp(b.Position, b.End);
            if ((c - ex.Origin).Length() > radius / Blood.XYScale)
                continue;
            var tr = Collision.Trace(ex.Origin, Vector3.Zero, Vector3.Zero, c, Contents.MaskMove & ~Contents.Sprites, ex);
            if (tr.Fraction < 1 && tr.Entity != w)
                continue;
            TrTriggerWall(w, K.kCmdWallImpact, owner ?? ex);
        }
    }

    // ---------------------------------------------------------------- generators / traps
    void FireballTrapSeqCallback(Sprite e)
    {
        if ((e.Cstat & 32) != 0)
            ActFireMissile(e, 0, 0, 0, 0, (e.Cstat & 8) != 0 ? 16384 : -16384, K.kMissileFireball);
        else
            ActFireMissile(e, 0, 0, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), 0, K.kMissileFireball);
    }

    void MGunFireSeqCallback(Sprite e)
    {
        if (e.Data2 > 0 || e.Data1 == 0)
        {
            if (e.Data2 > 0)
            {
                e.Data2--;
                if (e.Data2 == 0)
                    EvPost(e, 1, K.kCmdOff, e);
            }
            double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random2(1000);
            double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random2(1000);
            double dz = Random2(1000);
            ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorBullet);
            Sound3D(e, 359, -1);
        }
    }

    void MGunOpenSeqCallback(Sprite e) => Seq.Spawn(this, 39, e, MGunFireSeqCallback);

    void InitGenerator(Sprite e)
    {
        if (e.Type == K.kGenTrigger)
        {
            e.Cstat &= ~Cstat.Block;
            e.Cstat |= Cstat.Invisible;
        }
        if (e.XState != e.RestState && e.BusyTime > 0)
            EvPost(e, Tics(e.BusyTime + Random2(e.Data1)), K.kCmdRepeat, e);
    }

    void ActivateGenerator(Sprite e)
    {
        int t = e.Type;
        if (t == K.kGenDripWater || t == K.kGenDripBlood)
        {
            var (_, bottom) = Physics.SpriteExtents(e.BZ, e.Picnum, e.YRepeat, e.Cstat);
            ActSpawnThing(e.BX, e.BY, bottom, t == K.kGenDripWater ? K.kThingDripWater : K.kThingDripBlood);
        }
        else if (t == K.kGenSound)
            Sound3D(e, e.Data2, -1);
        else if (t == K.kGenMissileFireball)
        {
            if (e.Data2 == 0)
                FireballTrapSeqCallback(e);
            else if (e.Data2 == 1)
                Seq.Spawn(this, 35, e, FireballTrapSeqCallback);
            else if (e.Data2 == 2)
                Seq.Spawn(this, 36, e, FireballTrapSeqCallback);
        }
        else if (t == K.kGenBubble || t == K.kGenBubbleMulti)
        {
            var (top, _) = Physics.SpriteExtents(e.BZ, e.Picnum, e.YRepeat, e.Cstat);
            FxSpawn(t == K.kGenBubble ? K.FX_23 : K.FX_26, e.BX, e.BY, top, 0);
        }
    }

    // ---------------------------------------------------------------- init (trInit)
}
