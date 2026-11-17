using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// NoOne's modern features (NBlood's nnexts.cpp, "xxsystem": what xmapedit's maps of version 7.1 and 7.2 use, Eviction's
/// among them), as NBlood has them when gModernMap is set: the level's set-up (nnExtInitModernStuff: the modern sprite
/// types' status lists, the sprites that take Proximity, Sight and Impact triggers though they are no dudes or things),
/// the channels and commands added to the event system (the players' channels, the event causer's, kCmdModernUse), and
/// the helpers the modern types share. The types themselves are in World.ModernTypes.cs, the conditions in
/// World.ModernCond.cs, custom dudes in Modern/CustomDude*.cs.
/// </summary>
public sealed partial class World
{
    /// <summary>gModernMap: 0 a vanilla map, else the modern features' revision the map asks for (1 or 2).</summary>
    public int ModernMap;
    public bool IsModern => ModernMap != 0;

    // the sprites whose Proximity, Sight (Screen, Aim) and Impact triggers work though no dude or thing, the debris
    // sprites (physAttr), and the missiles and things following a target (the id lists of nnexts.cpp)
    readonly List<Sprite> _proxySprites = new(), _sightSprites = new(), _impactSprites = new(), _physSprites = new(),
        _followSprites = new();
    bool _eventRedirectsUsed;
    // the objects of the trigger sequences that end in the event causer's channel (gEvCauser: their pending events are
    // killed causer by causer)
    readonly HashSet<XObject> _evCauserPart = new();
    /// <summary>Where the event being delivered came from (EVENT's index and type): the sender of a sent command, the
    /// object itself for a posted one.</summary>
    XObject _evSource;

    /// <summary>A modern feature the port doesn't have yet, said once in the console.</summary>
    readonly HashSet<string> _modernMissing = new();

    void ModernMissing(string what)
    {
        if (_modernMissing.Add(what))
            Core.GameConsole.Print("modern features: not supported yet: " + what);
    }

    /// <summary>The map's modern revision: its version's minor number (xmapedit's maps), else an object receiving and
    /// sending on channel 60 or 61 with the command 100 (maps of other editors).</summary>
    static int ModernRevision(BloodMapFile f)
    {
        if (f.V7)
        {
            int minor = f.Version & 0xff;
            if (minor == 1 || minor == 2)
                return minor;
        }
        foreach (var s in f.Sprites)
            if (s.Xsp is { } x && x.RxID == x.TxID && x.Command == KM.kCmdModernFeaturesEnable)
            {
                if (x.TxID == KM.kChannelMapModernRev1)
                    return 1;
                if (x.TxID == KM.kChannelMapModernRev2)
                    return 2;
            }
        return 0;
    }

    void ModernReset()
    {
        _proxySprites.Clear();
        _sightSprites.Clear();
        _impactSprites.Clear();
        _physSprites.Clear();
        _followSprites.Clear();
        _evCauserPart.Clear();
        _eventRedirectsUsed = false;
        _modernMissing.Clear();
        _evSource = null;
        CondReset();
        PlayerCtrlReset();
        CdReset();
        PatrolReset();
    }

    // ---------------------------------------------------------------- small helpers (nnexts.cpp)
    public static bool IsDudeSprite(Sprite e) => e != null && Info.IsDude(e.Type);
    public static bool IsPlayerSprite(Sprite e) => e is Player;
    public static bool IsThingSprite(Sprite e) => e != null && Info.IsThing(e.Type);
    public static bool IsCustomDude(Sprite e) => e != null && e.Type == KM.kDudeModernCustom;
    static bool ValueIsBetween(int val, int min, int max) => val > min && val < max;
    static bool RngOk(int val, int lo, int hi) => val >= lo && val < hi;

    /// <summary>xsprIsFine: a sprite with x-data that is neither freed nor waiting to respawn.</summary>
    public static bool XSprIsFine(Sprite e)
    {
        if (e == null || e.Removed || !e.HasX || (e.HitTag & K.kHitagFree) != 0)
            return false;
        return (e.HitTag & K.kHitagRespawn) == 0 || (e.StatNum != K.kStatThing && e.StatNum != K.kStatDude);
    }

    static bool DudeIsAlive(Sprite e) => e != null && e.StatNum == K.kStatDude && XSprIsFine(e) && e.XHealth > 0;

    static bool IsOnRespawn(Sprite e) =>
        (e.HitTag & K.kHitagRespawn) != 0 && (e.StatNum == K.kStatDude || e.StatNum == K.kStatThing || e.StatNum == K.kStatItem);

    /// <summary>getPlayerById: 1..8 the players in order, 231..238 by the dude type.</summary>
    public Player GetPlayerById(int id)
    {
        if (id >= 1 && id <= KM.kMaxPlayers)
        {
            foreach (var p in Players)
                if (p.PlayerIndex == id - 1 && p.Ready && !p.Removed)
                    return p;
        }
        else if (id >= K.kDudePlayer1 && id <= K.kDudePlayer8)
        {
            foreach (var p in Players)
                if (p.Type == id && p.Ready && !p.Removed)
                    return p;
        }
        return null;
    }

    /// <summary>nnExtRandom: a to b, both counted in. (NBlood draws these from the C++ library's generator in single
    /// player; the game's own, so a recording replays.)</summary>
    int NnExtRandom(int a, int b) => Random((b + 1) - a) + a;

    static int GetDataVal(Sprite e, int data) => data switch { 0 => e.Data1, 1 => e.Data2, 2 => e.Data3, 3 => e.Data4, _ => -1 };

    /// <summary>The objects receiving on a channel, in delivery order (the rx bucket).</summary>
    List<XObject> Receivers(int rx) => rx > 0 && _receivers.TryGetValue(rx, out var l) ? l : _noReceivers;

    static readonly List<XObject> _noReceivers = new();

    /// <summary>An object's receive channel changed (a player linked to a channel).</summary>
    void SetRxID(XObject o, int rx)
    {
        if (o.RxID > 0 && _receivers.TryGetValue(o.RxID, out var l))
            l.Remove(o);
        o.RxID = rx;
        AddReceiver(o);
    }

    /// <summary>evKill(index, type, causer): in a modern map an object of a causer sequence loses only that causer's
    /// pending events, any other all of them.</summary>
    void EvKillBy(XObject target, XObject causer)
    {
        if (IsModern && _evCauserPart.Contains(target))
            _events.RemoveAll(e => e.Target == target && e.Cmd != K.kCmdCallback && e.Causer == causer && FreeEvent(e));
        else
            EvKill(target);
    }

    bool IsPartOfCauserScript(XObject o) => _evCauserPart.Contains(o);

    /// <summary>A sprite's Build sector's planes at its place (getflorzofslope, getceilzofslope).</summary>
    double FloorZOf(Sprite e)
    {
        int s = SpriteBSector(e);
        return s >= 0 ? Board.FloorZAt(s, e.BX, e.BY) : e.BZ;
    }

    double CeilZOf(Sprite e)
    {
        int s = SpriteBSector(e);
        return s >= 0 ? Board.CeilZAt(s, e.BX, e.BY) : e.BZ;
    }

    /// <summary>A sprite put at Build x, y, z.</summary>
    void SetBuildPos(Sprite e, double x, double y, double z) => e.Origin = B2Q(x, y, z);

    /// <summary>clampSprite: down to the floor (1) and/or up to the ceiling (2) of its sector.</summary>
    void ClampSprite(Sprite e, int which = 3)
    {
        int s = SpriteBSector(e);
        if (s < 0)
            return;
        var (top, bottom) = Extents(e);
        if ((which & 1) != 0)
            e.SetBZ(e.BZ + Blood.ClipHigh(Board.FloorZAt(s, e.BX, e.BY) - bottom, 0));
        if ((which & 2) != 0)
            e.SetBZ(e.BZ + Blood.ClipLow(Board.CeilZAt(s, e.BX, e.BY) - top, 0));
    }

    /// <summary>changeSpriteAngle: a sprite's angle set; a player's view, a dude's goal angle with it.</summary>
    void ChangeSpriteAngle(Sprite e, int nAng)
    {
        if (!IsDudeSprite(e))
            e.Ang = nAng;
        else if (e is Player p)
            PlayerSetViewYaw(p, nAng & 2047);
        else
        {
            e.Ang = nAng;
            if (XSprIsFine(e))
                e.GoalAng = e.Ang;
        }
    }

    static int GetVelocityAngle(Sprite e) => Blood.GetAngle(Blood.Sar(e.XVel, 12), Blood.Sar(e.YVel, 12));

    /// <summary>RotatePoint on a velocity (the angle's turn about the origin).</summary>
    static void RotateVel(Sprite e, int ang)
    {
        double c = Blood.Cos(ang), s = Blood.Sin(ang);
        double x = e.XVel, y = e.YVel;
        e.XVel = Blood.DMulScale30R(x, c, -y, s);
        e.YVel = Blood.DMulScale30R(x, s, y, c);
    }

    /// <summary>nnExtOffsetPos: a point moved by an offset in a sprite's frame (x right, y forward, z down).</summary>
    static void NnExtOffsetPos(double oX, double oY, double oZ, int nAng, ref double x, ref double y, ref double z)
    {
        if (oX != 0)
        {
            x -= Blood.MulScale30(oX, Blood.Cos(nAng + 512));
            y -= Blood.MulScale30(oX, Blood.Sin(nAng + 512));
        }
        if (oY != 0)
        {
            x += Blood.MulScale30R(Blood.Cos(nAng), oY);
            y += Blood.MulScale30R(Blood.Sin(nAng), oY);
        }
        z += oZ;
    }

    /// <summary>nnExtDudeStartHealth: the dude's own start health (its data4, kept in sysData2), else its kind's.</summary>
    static int NnExtDudeStartHealth(Sprite e, int nHealth) =>
        nHealth != 0 ? (int)Blood.ClipRange(nHealth << 4, 0, 65535) : Info.Dude(e.Type, Info.DI_startHealth) << 4;

    /// <summary>spriteIsUnderwater: in an underwater sector (oldWay: or its medium water or goo).</summary>
    bool SpriteIsUnderwater(Sprite e, bool oldWay = false) =>
        (SpriteSector(e) is { } xs && xs.SUnderwater != 0) || (oldWay && (e.Medium == K.kMediumWater || e.Medium == K.kMediumGoo));

    /// <summary>Blood's x-sector of a Build sector, made when it has none (dbInsertXSector).</summary>
    Sector EnsureXSector(int b)
    {
        if (XSectorOf(b) is { } had)
            return had;
        if (b < 0 || b >= Board.SectorCount)
            return null;
        Board.Sectors[b].Xs ??= new XSector();
        var s = SpawnNativeSector(b);
        s.XBusy = s.XState != 0 ? 65536 : 0;
        _xsector[b] = s;
        _fxSectors = null;
        return s;
    }

    // ---------------------------------------------------------------- set-up (nnExtInitModernStuff)

    /// <summary>The x-sprite's fields a modern map uses that vanilla Blood ignores.</summary>
    static void ReadModernFields(Sprite s, XSprite x)
    {
        s.Unused1 = x.Unused1;
        s.Unused2 = x.Unused2;
        s.Unused3 = x.Unused3;
        s.Unused4 = x.Unused4;
        s.DudeDeaf = x.DudeDeaf;
        s.DudeAmbush = x.DudeAmbush;
        s.DudeGuard = x.DudeGuard;
        s.DudeFlag4 = x.DudeFlag4;
    }

    /// <summary>nnExtInitSprite for a new level: a modern type's status list and settings, and the lists the sprite
    /// joins for its trigger flags.</summary>
    void NnExtInitSprite(Sprite e)
    {
        if ((e.HitTag & K.kHitagFree) != 0 || !e.HasX || e.NoX != 0)
            return;
        if ((e.Type == KM.kModernRandomTX || e.Type == KM.kModernSequentialTX) && e.XCommand == K.kCmdLink)
            _eventRedirectsUsed = true;
        // going On and going Off both, when neither is set
        if (e.TxID != 0 && e.TriggerOn == 0 && e.TriggerOff == 0)
            e.TriggerOn = e.TriggerOff = 1;
        // a dude's own start health, kept from the blood chunks it becomes
        if (IsDudeSprite(e))
            e.SysData2 = e.Data4;
        switch (e.Type)
        {
            case K.kGenBubble:
            case K.kGenBubbleMulti:
            {
                // an effect generator instead
                int fx = e.Type == K.kGenBubble ? K.FX_23 : K.FX_26;
                e.Type = KM.kModernEffectSpawner;
                e.HitTag = 0;
                e.Data2 = fx;
                e.Data3 = 0;
                e.Data4 = 0;
                break;
            }
            case K.kThingObjectExplode:
            case K.kThingObjectGib:
                // the flags kept, so 32 can be used
                e.SysData1 = e.HitTag;
                e.HitTag = 0;
                break;
            case KM.kModernRandomTX:
            case KM.kModernSequentialTX:
                if (e.XCommand == K.kCmdLink)
                    e.StatNum = KM.kStatModernEventRedirector;
                break;
            case KM.kModernWindGenerator:
                e.Cstat &= ~Cstat.Block;
                break;
            case KM.kModernDudeTargetChanger:
            case KM.kModernObjDataAccumulator:
            case KM.kModernRandom:
            case KM.kModernRandom2:
            case KM.kModernPatrolRegion:
                e.Cstat &= ~Cstat.Block;
                e.Cstat |= Cstat.Invisible;
                switch (e.Type)
                {
                    case KM.kModernPatrolRegion:
                        e.StatNum = KM.kStatModernPatrolRegion;
                        break;
                    case KM.kModernDudeTargetChanger:
                        e.StatNum = KM.kStatModernDudeTargetChanger;
                        if (e.BusyTime <= 0)
                            e.BusyTime = 5;
                        e.XCommand = K.kCmdLink;
                        break;
                    case KM.kModernRandom:
                    case KM.kModernRandom2:
                        e.StatNum = K.kStatDecoration;
                        e.SysData1 = e.XCommand;      // the item it drops inherits the command
                        e.XCommand = K.kCmdLink;      // the generator itself sends none
                        break;
                }
                break;
            case KM.kModernThingTNTProx:
                e.XProximity = 1;
                break;
            case K.kDudePodMother:
            case K.kDudeTentacleMother:
                e.XState = 1;
                break;
            case KM.kModernPlayerControl:
                switch (e.XCommand)
                {
                    case K.kCmdLink:
                        e.SysData1 = -1;
                        e.Cstat &= ~Cstat.Block;
                        e.StatNum = KM.kStatModernPlayerLinker;
                        break;
                    case 67:    // plays a QAV scene
                        if (e.TxID >= K.kChannelUser && e.WaitTime == 0)
                            e.WaitTime = 1;
                        e.StatNum = KM.kStatModernQavScene;
                        break;
                }
                break;
            case KM.kModernCondition:
            case KM.kModernConditionFalse:
                CondInitSprite(e);
                break;
        }
        // trigger flags that make no sense together
        if ((e.XTouch != 0 && (e.XProximity != 0 || e.XSight != 0) && e.DudeLockout != 0)
            || (e.XTouch != 0 && e.XProximity != 0 && e.XSight == 0))
            e.XTouch = 0;
        if (e.XProximity != 0 && e.XSight != 0 && e.DudeLockout != 0)
            e.XProximity = 0;
        // a floor sprite with Touch lying right in its sector's floor or ceiling: off it by one
        if (e.XTouch != 0 && (e.Cstat & Cstat.AlignMask) == Cstat.Floor && SpriteBSector(e) is int bs && bs >= 0)
        {
            var sec = Board.Sectors[bs];
            if (e.BZ == sec.FloorZ)
                e.SetBZ(e.BZ - 1);
            else if (e.BZ == sec.CeilingZ)
                e.SetBZ(e.BZ + 1);
        }
        NnExtListSprite(e);
    }

    static bool SuperListStat(int stat, bool thingsAndDudes) => stat switch
    {
        K.kStatFX or K.kStatExplosion or K.kStatItem or K.kStatPurge or K.kStatSpares or K.kStatFlare or K.kStatInactive
            or K.kStatFree or K.kStatMarker or KM.kStatModernPlayerLinker => false,
        K.kStatThing or K.kStatDude => thingsAndDudes,
        _ => true,
    };

    /// <summary>The lists a sprite joins for its Proximity, Sight (Screen, Aim) and Impact flags.</summary>
    void NnExtListSprite(Sprite e)
    {
        if (e.XProximity != 0 && SuperListStat(e.StatNum, false) && !_proxySprites.Contains(e))
            _proxySprites.Add(e);
        if ((e.XSight != 0 || e.Unused3 != 0) && SuperListStat(e.StatNum, true) && !_sightSprites.Contains(e))
            _sightSprites.Add(e);
        if (e.XImpact != 0 && SuperListStat(e.StatNum, true) && !_impactSprites.Contains(e))
            _impactSprites.Add(e);
    }

    /// <summary>nnExtInitModernStuff: every sprite set up, the causer sequences found, the conditions made ready.</summary>
    void NnExtInitModernStuff()
    {
        foreach (var e in Sprites.ToArray())
            if (!e.Removed)
                NnExtInitSprite(e);
        NnExtInitCauserTable();
        CondInit();
    }

    // an object's channels: its own, and a random or sequential TX switch's data fields
    bool IsMultiTx(XObject o)
    {
        if (o is not Sprite e || e.Removed || (e.Type != KM.kModernRandomTX && e.Type != KM.kModernSequentialTX))
            return false;
        for (int i = 0; i < 4; i++)
            if (RngOk(GetDataVal(e, i), 1, K.kChannelUserMax))
                return true;
        return false;
    }

    bool MultiTxPointsRx(int rx, Sprite e)
    {
        if (RngOk(e.Data1, 1, K.kChannelUserMax) && e.Data2 == 0 && e.Data3 == 0 && RngOk(e.Data4, 1, K.kChannelUserMax))
        {
            if (e.Data1 > e.Data4)
                (e.Data1, e.Data4) = (e.Data4, e.Data1);
            return RngOk(rx, e.Data1, e.Data4 + 1);
        }
        for (int j = 0; j < 4; j++)
            if (rx == GetDataVal(e, j))
                return true;
        return false;
    }

    /// <summary>collectObjectsByChannel: the objects receiving (rx) or sending on a channel.</summary>
    List<XObject> CollectObjectsByChannel(int channel, bool rx)
    {
        var o = new List<XObject>();
        foreach (var x in AllObjects())
        {
            if (!x.HasX || x.Removed)
                continue;
            if (rx ? x.RxID == channel : (x.TxID == channel || (IsMultiTx(x) && MultiTxPointsRx(channel, (Sprite)x))))
                o.Add(x);
        }
        return o;
    }

    void CollectBranchByChannel(int channel, bool rx, HashSet<XObject> seen)
    {
        foreach (var o in CollectObjectsByChannel(channel, rx))
            if (seen.Add(o) && o.RxID > 0)
                CollectBranchByChannel(o.RxID, false, seen);
    }

    /// <summary>nnExtInitCauserTable: the objects of the trigger sequences leading to the event causer's channel.</summary>
    void NnExtInitCauserTable()
    {
        _evCauserPart.Clear();
        var seen = new HashSet<XObject>();
        CollectBranchByChannel(K.kChannelEventCauser, false, seen);
        foreach (var o in seen)
            if (o.TxID > 0)
                _evCauserPart.Add(o);
    }

    /// <summary>trInit's modern part: the generators that start on.</summary>
    void ModernTrInitSprite(Sprite e)
    {
        switch (e.Type)
        {
            case KM.kModernRandom:
            case KM.kModernRandom2:
                if (e.XState == e.RestState)
                    break;
                EvPost(e, 120 * e.BusyTime / 10, K.kCmdRepeat, e);
                if (e.WaitTime > 0)
                    EvPost(e, e.WaitTime * 120 / 10, e.RestState != 0 ? K.kCmdOn : K.kCmdOff, e);
                break;
            case KM.kModernSeqSpawner:
            case KM.kModernObjDataAccumulator:
            case KM.kModernDudeTargetChanger:
            case KM.kModernEffectSpawner:
            case KM.kModernWindGenerator:
                if (e.XState == e.RestState)
                    break;
                EvPost(e, 0, K.kCmdRepeat, e);
                if (e.WaitTime > 0)
                    EvPost(e, e.WaitTime * 120 / 10, e.RestState != 0 ? K.kCmdOn : K.kCmdOff, e);
                break;
        }
    }

    // ---------------------------------------------------------------- events (eventq.cpp's modern part)

    /// <summary>evSend's channels of a modern map: a player's, all players', the event causer's; and the command that
    /// kills the receivers' pending events. True: handled.</summary>
    bool ModernEvSend(XObject source, int rxId, int command, XObject causer)
    {
        if (rxId >= K.kChannelPlayer0 && rxId < KM.kChannelPlayer7)
        {
            if (GetPlayerById(rxId - KM.kChannelPlayer7 + KM.kMaxPlayers) is { } p)
            {
                if (command == KM.kCmdEventKillFull)
                    EvKill(p);
                else
                    Deliver(p, source, command, causer);
            }
            return true;
        }
        if (rxId == K.kChannelAllPlayers)
        {
            foreach (var p in Players.ToArray())
            {
                if (!p.Ready || p.Removed)
                    continue;
                if (command == KM.kCmdEventKillFull)
                    EvKill(p);
                else
                    Deliver(p, source, command, causer);
            }
            return true;
        }
        if (rxId == K.kChannelEventCauser && causer is Sprite cs)
        {
            if (!cs.Removed && (cs.HitTag & K.kHitagFree) == 0 && (cs.HitTag & K.kHitagRespawn) == 0)
            {
                if (command == KM.kCmdEventKillFull)
                    EvKill(cs);
                else
                    Deliver(cs, source, command, causer);
            }
            return true;
        }
        if (command == KM.kCmdEventKillFull)
        {
            foreach (var o in Receivers(rxId).ToArray())
                EvKill(o);
            return true;
        }
        return false;
    }

    /// <summary>A sent command given to one object (trMessage with the event's source known).</summary>
    void Deliver(XObject target, XObject source, int command, XObject causer)
    {
        var oldLink = _linkSource;
        var oldSource = _evSource;
        _linkSource = source;
        _evSource = source;
        TrMessage(target, command, causer);
        _linkSource = oldLink;
        _evSource = oldSource;
    }

    /// <summary>levelEndLevelCustom: the level ends and the given level of the episode (0-based) is next.</summary>
    void LevelEndLevelCustom(int nLevel)
    {
        string next = LevelInfo.MapOfEpisode(MapName, nLevel);
        if (next != null)
            NextMap = next;
        EndLevel(0);
    }

    // ---------------------------------------------------------------- the lists, each game frame (nnExtProcessSuperSprites)

    void NnExtProcessSuperSprites()
    {
        CondTrackingProcess();
        ProcessList(_proxySprites, ProcessProxySprite);
        ProcessList(_sightSprites, ProcessSightSprite);
        ProcessList(_physSprites, ProcessPhysSprite);
        ProcessList(_followSprites, ProcessFollowSprite);
    }

    const int ListSkip = 0, ListOk = 1, ListRemove = 2;

    static void ProcessList(List<Sprite> list, Func<Sprite, int> f)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e.Removed || f(e) == ListRemove)
            {
                // (the callback may have changed the list)
                int at = list.IndexOf(e);
                if (at >= 0)
                {
                    list.RemoveAt(at);
                    if (at <= i)
                        i--;
                }
            }
        }
    }

    int ProcessProxySprite(Sprite e)
    {
        if (!e.HasX)
            return ListRemove;
        if (IsOnRespawn(e) || e.XLocked != 0)
            return ListSkip;
        if (e.IsTriggered != 0 || e.XProximity == 0)
            return ListRemove;
        if (e.Interruptable == 0 && e.XState != e.RestState)
            return ListSkip;
        double okDist = e.StatNum == K.kStatDude ? 96 : Blood.ClipLow(e.ClipDist * 3, 32);
        bool causerPart = IsPartOfCauserScript(e);
        if (e.DudeLockout != 0)
        {
            // players only
            foreach (var p in Players.ToArray())
            {
                if (!p.Ready || !XSprIsFine(p) || p.XHealth <= 0)
                    continue;
                if (CheckProximity(p, e.BX, e.BY, e.BZ, okDist))
                {
                    TrTriggerSprite(e, K.kCmdSpriteProximity, p);
                    if (!causerPart)
                        break;
                }
            }
        }
        else
        {
            foreach (var d in new List<Sprite>(StatList(K.kStatDude)))
            {
                if (!XSprIsFine(d) || d.XHealth <= 0)
                    continue;
                if (CheckProximity(d, e.BX, e.BY, e.BZ, okDist))
                {
                    TrTriggerSprite(e, K.kCmdSpriteProximity, d);
                    if (!causerPart)
                        break;
                }
            }
        }
        return ListOk;
    }

    /// <summary>The sprites drawn for the player lately (NBlood's show2dsprite, what the Screen trigger asks): the
    /// renderer marks them.</summary>
    public readonly HashSet<Sprite> SeenOnScreen = new();

    /// <summary>The sprites waiting for their Screen trigger (the client marks those on the screen in SeenOnScreen).</summary>
    public IEnumerable<Sprite> ScreenTriggerSprites()
    {
        foreach (var e in _sightSprites)
            if (!e.Removed && (e.Unused3 & KM.kTriggerSpriteScreen) != 0 && e.IsTriggered == 0)
                yield return e;
    }

    int ProcessSightSprite(Sprite e)
    {
        if (!e.HasX)
            return ListRemove;
        if (IsOnRespawn(e) || e.XLocked != 0)
            return ListSkip;
        if (e.IsTriggered != 0 || (e.XSight == 0 && e.Unused3 == 0))
            return ListRemove;
        if (e.Interruptable == 0 && e.XState != e.RestState)
            return ListSkip;
        // drawn for the player
        if ((e.Unused3 & KM.kTriggerSpriteScreen) != 0 && GameType == K.kGameTypeSinglePlayer && SeenOnScreen.Contains(e))
        {
            var me = Me;
            if (me != null && XSprIsFine(me) && me.XHealth > 0)
            {
                TrTriggerSprite(e, K.kCmdSpriteSight, me);
                SeenOnScreen.Remove(e);
            }
            return ListOk;
        }
        bool causerPart = IsPartOfCauserScript(e);
        foreach (var p in Players.ToArray())
        {
            if (!p.Ready || !XSprIsFine(p) || p.XHealth <= 0)
                continue;
            var (top, bottom) = Extents(p);
            double[] z = { p.BZ, top, bottom };
            int j;
            for (j = 0; j < 3; j++)
            {
                if (!CanSee(e.BX, e.BY, e.BZ, p.BX, p.BY, z[j], e, p))
                    continue;
                if (e.XSight != 0)
                    TrTriggerSprite(e, K.kCmdSpriteSight, p);
                else if ((e.Unused3 & KM.kTriggerSpriteAim) != 0)
                {
                    bool vector = (e.Cstat & Cstat.Hitscan) != 0;
                    if (!vector)
                    {
                        e.Cstat |= Cstat.Hitscan;
                        UpdateBox(e);
                    }
                    HitScan(p, p.ZWeapon, p.AimDx, p.AimDy, p.AimDz, 0);
                    if (HitType == 3 && HitEnt == e)
                        TrTriggerSprite(e, K.kCmdSpriteSight, p);
                    if (!vector && !e.Removed)
                    {
                        e.Cstat &= ~Cstat.Hitscan;
                        UpdateBox(e);
                    }
                }
                break;
            }
            if (j < 3 && !causerPart)
                break;
        }
        return ListOk;
    }

    // followTarget: a missile or thing turned towards its target
    void FollowTarget(Sprite e, Sprite targ, int nMaxAng)
    {
        if (!CanSee(e.BX, e.BY, e.BZ, targ.BX, targ.BY, targ.BZ, e, targ))
            return;
        double dx = targ.BX - e.BX, dy = targ.BY - e.BY;
        int nAng = Blood.GetAngle(dx, dy);
        int nDAng = Math.Abs(((nAng + 1024 - e.Ang) & 2047) - 1024);
        if (nMaxAng != 0 && nDAng >= nMaxAng)
            return;
        var (_, zb) = Extents(e);
        double dz = targ.BZ - zb;
        double zv = e.ZVel;
        int vAng = GetVelocityAngle(e);
        RotateVel(e, (nAng - vAng) & 2047);
        e.Ang = nAng;
        if ((e.Cstat & Cstat.AlignMask) == Cstat.Wall)
            e.Ang = (e.Ang + 512) & 2047;
        if ((e.HitTag & K.kPhysGravity) == 0)
        {
            if ((zv > 0 && targ.BZ < zb) || (zv < 0 && targ.BZ > e.BZ))
                e.ZVel = 0;
        }
        e.ZVel += dz;
    }

    int ProcessFollowSprite(Sprite e)
    {
        if (!e.HasX || e.Owner == null || !DudeIsAlive(e.Owner))
            return ListRemove;
        if (e.FollowTarg is { } targ && e.GoalAng != 0 && DudeIsAlive(targ))
        {
            if (targ is Player p && p.PwUpTime[K.kPwUpShadowCloak] > 0)
                return ListSkip;
            FollowTarget(e, targ, e.GoalAng);
            return ListOk;
        }
        return ListRemove;
    }

    // ---------------------------------------------------------------- touch triggers (nnexts.cpp)

    /// <summary>triggerTouchSprite: a sprite touched by another fires its Touch trigger.</summary>
    void TriggerTouchSprite(Sprite e, Sprite hit)
    {
        if (hit == null || hit.Removed || !hit.HasX)
            return;
        if (hit.XTouch != 0 && hit.IsTriggered == 0 && (hit.DudeLockout == 0 || e is Player))
            TrTriggerSprite(hit, K.kCmdSpriteTouch, e);
        e.XVel += 5;        // enough to make the hit new again
    }

    void TriggerTouchWall(Sprite e, Wall w)
    {
        if (w == null || !w.HasX)
            return;
        if (w.XTouch != 0 && w.IsTriggered == 0 && (w.DudeLockout == 0 || e is Player))
            TrTriggerWall(w, K.kCmdWallTouch, e);
        e.XVel += 5;
    }

    /// <summary>nnExtTriggerObject.</summary>
    void NnExtTriggerObject(XObject o, int command, XObject causer)
    {
        switch (o)
        {
            case Sector s: TrTriggerSector(s, command, causer); break;
            case Wall w: TrTriggerWall(w, command, causer); break;
            case Sprite e: TrTriggerSprite(e, command, causer); break;
        }
    }
}
