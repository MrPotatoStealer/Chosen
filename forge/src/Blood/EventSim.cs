// Chosen Blood module (GPL v2): Blood's events and triggers as NBlood runs them, for the editor's Play: eventq.cpp
// (evSend, evPost, evProcess, evKill), triggers.cpp (trInit, trMessage*, trTrigger*, Operate*, Link*, Set*State,
// AddBusy, trProcessBusy with its busy procedures, ProcessMotion, AlignSlopes, the generators), sectorfx.cpp
// (DoSectorLighting's waves). What a player does is the camera's: entering and leaving sectors, using what it aims
// at. Nothing else of the game runs (no actors, no damage, no sounds): what moves, lights and pans.
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;

namespace Chosen.Forge.Blood;

public sealed class EventSim
{
    // event targets (NBlood's object types)
    public const int TypeWall = 0, TypeSprite = 3, TypeSector = 6;
    // commands (eventq.h)
    const int CmdOff = 0, CmdOn = 1, CmdState = 2, CmdNotState = 4, CmdLink = 5, CmdLock = 6, CmdUnlock = 7,
        CmdToggleLock = 8, CmdStopOff = 9, CmdStopOn = 10, CmdStopNext = 11, CmdRepeat = 21, CmdSpritePush = 30,
        CmdSectorEnter = 42, CmdSectorExit = 43, CmdWallPush = 50;
    const int ChannelLevelStart = 7;
    // busy procedures (gBusyProc's order)
    const int ProcSprite = 1, ProcDoor = 2, ProcSlide = 3, ProcRotate = 4, ProcStep = 5, ProcGeneric = 6, ProcPath = 7;
    // sprite types (common_game.h)
    const int SwitchToggle = 20, SwitchOneWay = 21, SwitchCombo = 22, SwitchPadlock = 23, MarkerPath = 15,
        GenTrigger = 700, GenFirst = 700, GenLast = 708;
    const int StatDude = 6, StatPathMarker = 16, StatThing = 4;

    // things (NBlood's thingInfo): those an explosion hurts (dmgControl[kDamageExplode] not 0), the ones that blow up
    // (actExplodeSprite) and how far (explodeInfo's radius, Build units / 16: small 75, standard 150, large 225,
    // spray 120), the ones gone when broken (actActivateGibObject)
    const int ThingTNTBarrel = 400, ThingArmedProx = 401, ThingArmedRemote = 402, ThingCrateFace = 405, ThingWallCrack = 408,
        TrapMachinegun = 413, ThingObjectGib = 416, ThingObjectExplode = 417, ThingTNTStick = 418, ThingTNTBundle = 419,
        ThingSpray = 420, ThingBloodBits = 425, ThingBloodChunks = 426, ThingZombieHead = 427, ThingModernTNTProx = 433;
    static readonly HashSet<int> Breakable = new()
        { 400, 401, 402, 403, 404, 405, 406, 407, 408, 409, 410, 411, 412, 413, 416, 417, 418, 419, 420, 422, 425, 426, 427, 429, 430, 431, 433 };
    static bool Explosive(int t) => t is ThingTNTBarrel or ThingArmedProx or ThingArmedRemote or ThingTNTStick or ThingTNTBundle
        or ThingSpray or ThingModernTNTProx;
    static int ExplosionRadius(int t) => t switch { ThingTNTBarrel => 225, ThingTNTStick => 75, ThingSpray => 120, _ => 150 };
    static bool GibThing(int t) => t is ThingObjectGib or ThingObjectExplode or ThingBloodBits or ThingBloodChunks or ThingZombieHead;

    /// <summary>The sectors whose sprites went (broken, blown up) since the last step.</summary>
    readonly HashSet<int> _goneSectors = new();

    /// <summary>Game tics a second (Blood's clock) and a game frame's (4).</summary>
    public const int TicsPerSecond = 120, TicsPerFrame = 4;

    readonly MapDocument _stored, _d;
    int _clock;
    double _carry;
    long _seq;
    uint _rand = 0x2545F491;

    readonly record struct Event(int Time, long Seq, int Type, int Index, int Cmd);
    readonly SortedSet<Event> _queue = new(Comparer<Event>.Create((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Seq.CompareTo(b.Seq)));

    sealed class Busy
    {
        public int Sector, Proc, Delta, Value;
    }
    readonly List<Busy> _busy = new();

    // who receives each channel
    readonly Dictionary<int, List<(int type, int index)>> _rx = new();
    // the moving sectors' poses: how far along (after the wave), a path's markers, a step-turner's angle
    readonly Dictionary<int, double> _pose = new();
    readonly Dictionary<int, int> _pathBase = new();
    readonly Dictionary<int, (int m0, int m1)> _pathMarkers = new();
    readonly HashSet<int> _dirty = new();
    // the frame's effects: lighting (the shade added), bob (theta), the floor and ceiling a bob starts from
    readonly Dictionary<int, int> _light = new();
    readonly Dictionary<int, int> _bobTheta = new();
    readonly Dictionary<int, (int floor, int ceil)> _base = new();

    /// <summary>shown: a copy of stored, run (its x-objects' states, busy, its moving sectors' places, shades,
    /// slopes); stored stays as the map is.</summary>
    public EventSim(MapDocument stored, MapDocument shown)
    {
        _stored = stored;
        _d = shown;
    }

    public double Seconds => _clock / (double)TicsPerSecond;

    // ---------------------------------------------------------------- start

    /// <summary>trInit: every x-object at its state (busy full when on), the moving sectors posed there, the paths and
    /// generators started, then the level-start channel sent On.</summary>
    public void Start()
    {
        for (int w = 0; w < _d.Walls.Count; w++)
            if (_d.Walls[w].Xw is { } x)
            {
                x.Busy = x.State != 0 ? 65536 : 0;
                AddRx(x.RxID, TypeWall, w);
            }
        for (int s = 0; s < _d.Sectors.Count; s++)
        {
            var sec = _d.Sectors[s];
            _base[s] = (sec.FloorZ, sec.CeilingZ);
            if (sec.Xs is not { } x)
                continue;
            x.Busy = x.State != 0 ? 65536 : 0;
            AddRx(x.RxID, TypeSector, s);
            if (x.BobSpeed != 0)
                _bobTheta[s] = x.BobTheta;
            switch (sec.Type)
            {
                case SectorMotion.SlideMarked:
                case SectorMotion.Slide:
                case SectorMotion.RotateMarked:
                case SectorMotion.Rotate:
                case SectorMotion.ZMotion:
                case SectorMotion.ZMotionSprite:
                    if (SectorMotion.IsMover(_stored, s))
                        SetPose(s, x.Busy / 65536.0);
                    break;
                case SectorMotion.Path:
                    InitPath(s, x);
                    break;
            }
        }
        for (int i = 0; i < _d.Sprites.Count; i++)
        {
            var sp = _d.Sprites[i];
            if (sp.Xsp is not { } x)
                continue;
            // (actInit, before trInit: every thing on, but a proximity bomb and the machine gun trap)
            if (sp.StatNum == StatThing && sp.Type >= 400 && sp.Type < 436)
                x.State = sp.Type is ThingArmedProx or TrapMachinegun or ThingModernTNTProx ? 0 : 1;
            x.Busy = x.State != 0 ? 65536 : 0;
            AddRx(x.RxID, TypeSprite, i);
            if (sp.Type == SwitchPadlock)
                x.TriggerOnce = 1;
            else if (sp.Type >= GenFirst && sp.Type <= GenLast && x.State != x.RestState && x.BusyTime > 0)
                Post(TypeSprite, i, TicsPerSecond * (x.BusyTime + Random2(x.Data1)) / 10, CmdRepeat);
        }
        Send(-1, -1, ChannelLevelStart, CmdOn);
        PoseDirty();
        Effects();
    }

    void AddRx(int rx, int type, int index)
    {
        if (rx <= 0)
            return;
        if (!_rx.TryGetValue(rx, out var l))
            _rx[rx] = l = new List<(int, int)>();
        l.Add((type, index));
    }

    void InitPath(int s, XSector x)
    {
        int m = PathMarker(x.Data);
        if (m < 0)
            return;
        _pathBase[s] = m;
        _pathMarkers[s] = (m, m);
        if (x.State != 0)
            Post(TypeSector, s, 0, CmdOn);
    }

    int PathMarker(int id)
    {
        for (int i = 0; i < _d.Sprites.Count; i++)
            if (_d.Sprites[i].StatNum == StatPathMarker && _d.Sprites[i].Type == MarkerPath && _d.Sprites[i].Xsp?.Data1 == id)
                return i;
        return -1;
    }

    // ---------------------------------------------------------------- the clock

    /// <summary>The clock on by seconds: the events due run, each game frame the busy sectors step (then bob,
    /// slopes, lighting). The sectors drawn differently since (moved, tilted, lit).</summary>
    public HashSet<int> Step(double seconds)
    {
        var changed = new HashSet<int>();
        _carry += seconds * TicsPerSecond;
        // (a long pause: no more than two seconds made up at once)
        _carry = Math.Min(_carry, 2 * TicsPerSecond);
        while (_carry >= TicsPerFrame)
        {
            _carry -= TicsPerFrame;
            _clock += TicsPerFrame;
            Process();
            ProcessBusy();
            PoseDirty(changed);
            changed.UnionWith(Effects());
        }
        changed.UnionWith(_goneSectors);
        _goneSectors.Clear();
        return changed;
    }

    void Process()
    {
        while (_queue.Count > 0 && _queue.Min.Time <= _clock)
        {
            var e = _queue.Min;
            _queue.Remove(e);
            Message(e.Type, e.Index, e.Cmd, e.Type, e.Index);
        }
    }

    void Post(int type, int index, int delta, int cmd)
    {
        if (cmd == CmdState)
            cmd = State(type, index) != 0 ? CmdOn : CmdOff;
        else if (cmd == CmdNotState)
            cmd = State(type, index) != 0 ? CmdOff : CmdOn;
        _queue.Add(new Event(_clock + Math.Max(0, delta), _seq++, type, index, cmd));
    }

    void Kill(int type, int index) => _queue.RemoveWhere(e => e.Type == type && e.Index == index);

    /// <summary>evSend: a command on a channel to everything receiving it (but the sender); Blood's own channels
    /// below 100 but the level's start do nothing here (the level's end, secrets, text).</summary>
    void Send(int type, int index, int rx, int cmd)
    {
        if (rx <= 0)
            return;
        if (cmd == CmdState)
            cmd = State(type, index) != 0 ? CmdOn : CmdOff;
        else if (cmd == CmdNotState)
            cmd = State(type, index) != 0 ? CmdOff : CmdOn;
        if (!_rx.TryGetValue(rx, out var to))
            return;
        foreach (var (t, i) in to.ToList())
        {
            if (t == type && i == index)
                continue;
            if (t == TypeSprite && (_d.Sprites[i].Xsp == null || _d.Sprites[i].Xsp.RxID <= 0))
                continue;
            Message(t, i, cmd, type, index);
        }
    }

    int State(int type, int index) => type switch
    {
        TypeSector when index >= 0 => _d.Sectors[index].Xs?.State ?? 0,
        TypeWall when index >= 0 => _d.Walls[index].Xw?.State ?? 0,
        TypeSprite when index >= 0 => _d.Sprites[index].Xsp?.State ?? 0,
        _ => 0,
    };

    int SourceBusy(int type, int index) => type switch
    {
        TypeSector when index >= 0 => _d.Sectors[index].Xs?.Busy ?? 0,
        TypeWall when index >= 0 => _d.Walls[index].Xw?.Busy ?? 0,
        TypeSprite when index >= 0 => _d.Sprites[index].Xsp?.Busy ?? 0,
        _ => 0,
    };

    int Random2(int a)
    {
        _rand ^= _rand << 13;
        _rand ^= _rand >> 17;
        _rand ^= _rand << 5;
        int w = (int)(_rand & 0x7fff);
        return (int)(((long)w * a) >> 14) - a;
    }

    static int Tics(int tenths) => tenths * TicsPerSecond / 10;

    // ---------------------------------------------------------------- what a player does

    /// <summary>What the player set off and when (Report a Problem: the same in NBlood): seconds from the level's
    /// start, what ("enter", "exit", "use") and the sector, wall or sprite.</summary>
    public readonly List<PlayerEvent> PlayerLog = new();

    public readonly record struct PlayerEvent(double seconds, string what, int type, int index);

    /// <summary>The player left one sector for another (-1: none): its exit, the other's enter (trTriggerSector).</summary>
    public void PlayerMoved(int from, int to)
    {
        if (from >= 0 && from < _d.Sectors.Count && _d.Sectors[from].Xs is { Exit: not 0 } xf)
        {
            PlayerLog.Add(new(Seconds, "exit", TypeSector, from));
            TriggerSector(from, xf, CmdSectorExit);
        }
        if (to >= 0 && to < _d.Sectors.Count && _d.Sectors[to].Xs is { Enter: not 0 } xt)
        {
            PlayerLog.Add(new(Seconds, "enter", TypeSector, to));
            TriggerSector(to, xt, CmdSectorEnter);
        }
    }

    /// <summary>The player used (Space in the game: ActionScan) a wall, a sector's floor or ceiling or a sprite, from
    /// sector here: a wall to push, else the sector behind it if pushed through walls; a sprite to push; a sector to
    /// push; else its own sector if that pushes. Keys are not asked for. What was used, or null.</summary>
    public string Use(int wall, int sector, int sprite, int here)
    {
        string used = UseOf(wall, sector, sprite, here);
        if (used != null)
        {
            var parts = used.Split(' ');
            PlayerLog.Add(new(Seconds, "use", parts[0] switch { "wall" => TypeWall, "sprite" => TypeSprite, _ => TypeSector }, int.Parse(parts[1])));
        }
        return used;
    }

    string UseOf(int wall, int sector, int sprite, int here)
    {
        if (wall >= 0 && wall < _d.Walls.Count)
        {
            var w = _d.Walls[wall];
            if (w.Xw is { TriggerPush: not 0 } xw)
            {
                TriggerWall(wall, xw, CmdWallPush);
                return $"wall {wall}";
            }
            if (w.NextSector >= 0 && _d.Sectors[w.NextSector].Xs is { Wallpush: not 0 } xn)
            {
                TriggerSector(w.NextSector, xn, CmdSpritePush);
                return $"sector {w.NextSector}";
            }
        }
        else if (sprite >= 0 && sprite < _d.Sprites.Count && !Gone(sprite))
        {
            if (_d.Sprites[sprite].Xsp is { Push: not 0 } xs)
            {
                TriggerSprite(sprite, xs, CmdSpritePush);
                return $"sprite {sprite}";
            }
            if (Break(sprite))
                return $"sprite {sprite} broken";
        }
        else if (sector >= 0 && sector < _d.Sectors.Count && _d.Sectors[sector].Xs is { Push: not 0 } xp)
        {
            TriggerSector(sector, xp, CmdSpritePush);
            return $"sector {sector}";
        }
        if (here >= 0 && here < _d.Sectors.Count && _d.Sectors[here].Xs is { Push: not 0 } xh)
        {
            TriggerSector(here, xh, CmdSpritePush);
            return $"sector {here}";
        }
        return null;
    }

    void TriggerSector(int s, XSector x, int cmd)
    {
        if (x.Locked != 0 || x.IsTriggered != 0)
            return;
        if (x.TriggerOnce != 0)
            x.IsTriggered = 1;
        if (x.Decoupled != 0 && x.TxID > 0)
            Send(TypeSector, s, x.TxID, x.Command);
        else
            OperateSector(s, x, cmd);
    }

    void TriggerWall(int w, XWall x, int cmd)
    {
        if (x.Locked != 0 || x.IsTriggered != 0)
            return;
        if (x.TriggerOnce != 0)
            x.IsTriggered = 1;
        if (x.Decoupled != 0 && x.TxID > 0)
            Send(TypeWall, w, x.TxID, x.Command);
        else
            OperateWall(w, x, cmd);
    }

    void TriggerSprite(int i, XSprite x, int cmd)
    {
        if (x.Locked != 0 || x.IsTriggered != 0)
            return;
        if (x.TriggerOnce != 0)
            x.IsTriggered = 1;
        if (x.Decoupled != 0 && x.TxID > 0)
            Send(TypeSprite, i, x.TxID, x.Command);
        else
            OperateSprite(i, x, cmd);
    }

    // ---------------------------------------------------------------- messages

    /// <summary>trMessage*: a command reaching an x-object (locked: only unlocking); a link takes the sender's busy.</summary>
    void Message(int type, int index, int cmd, int srcType, int srcIndex)
    {
        switch (type)
        {
            case TypeSector when index >= 0 && index < _d.Sectors.Count && _d.Sectors[index].Xs is { } xs:
                if (xs.Locked != 0 && cmd != CmdUnlock && cmd != CmdToggleLock)
                    return;
                if (cmd == CmdLink)
                    LinkSector(index, xs, SourceBusy(srcType, srcIndex));
                else
                    OperateSector(index, xs, cmd);
                break;
            case TypeWall when index >= 0 && index < _d.Walls.Count && _d.Walls[index].Xw is { } xw:
                if (xw.Locked != 0 && cmd != CmdUnlock && cmd != CmdToggleLock)
                    return;
                if (cmd == CmdLink)
                {
                    xw.Busy = SourceBusy(srcType, srcIndex);
                    if ((xw.Busy & 0xffff) == 0)
                        SetWallState(index, xw, xw.Busy >> 16);
                }
                else
                    OperateWall(index, xw, cmd);
                break;
            case TypeSprite when index >= 0 && index < _d.Sprites.Count && _d.Sprites[index].Xsp is { } xp && !Gone(index):
                if (xp.Locked != 0 && cmd != CmdUnlock && cmd != CmdToggleLock)
                    return;
                if (cmd == CmdLink)
                    LinkSprite(index, xp, srcType, srcIndex);
                else
                    OperateSprite(index, xp, cmd);
                break;
        }
    }

    void LinkSector(int s, XSector x, int busy)
    {
        switch (_d.Sectors[s].Type)
        {
            case SectorMotion.ZMotionSprite:
            case SectorMotion.ZMotion:
            case SectorMotion.SlideMarked:
            case SectorMotion.Slide:
            case SectorMotion.RotateMarked:
            case SectorMotion.Rotate:
                BusyStep(s, busy);
                break;
            default:
                x.Busy = busy;
                if ((x.Busy & 0xffff) == 0)
                    SetSectorState(s, x, busy >> 16);
                break;
        }
    }

    void LinkSprite(int i, XSprite x, int srcType, int srcIndex)
    {
        if (_d.Sprites[i].Type == SwitchCombo)
        {
            if (srcType == TypeSprite && _d.Sprites[srcIndex].Xsp is { } src)
            {
                x.Data1 = src.Data1;
                SetSpriteState(i, x, x.Data1 == x.Data2 ? 1 : 0);
            }
            return;
        }
        x.Busy = SourceBusy(srcType, srcIndex);
        if ((x.Busy & 0xffff) == 0)
            SetSpriteState(i, x, x.Busy >> 16);
    }

    // ---------------------------------------------------------------- states

    bool SetSectorState(int s, XSector x, int state)
    {
        if ((x.Busy & 0xffff) == 0 && x.State == state)
            return false;
        x.Busy = state << 16;
        x.State = state;
        Kill(TypeSector, s);
        if (state == 1)
        {
            if (x.Command != CmdLink && x.TriggerOn != 0 && x.TxID != 0)
                Send(TypeSector, s, x.TxID, x.Command);
            if (x.StopOn != 0)
                x.StopOn = x.StopOff = 0;
            else if (x.ReTriggerA != 0)
                Post(TypeSector, s, Tics(x.WaitTimeA), CmdOff);
        }
        else
        {
            if (x.Command != CmdLink && x.TriggerOff != 0 && x.TxID != 0)
                Send(TypeSector, s, x.TxID, x.Command);
            if (x.StopOff != 0)
                x.StopOn = x.StopOff = 0;
            else if (x.ReTriggerB != 0)
                Post(TypeSector, s, Tics(x.WaitTimeB), CmdOn);
        }
        return true;
    }

    bool SetWallState(int w, XWall x, int state)
    {
        if ((x.Busy & 0xffff) == 0 && x.State == state)
            return false;
        x.Busy = state << 16;
        x.State = state;
        Kill(TypeWall, w);
        if (x.RestState != state && x.WaitTime > 0)
            Post(TypeWall, w, Tics(x.WaitTime), x.RestState != 0 ? CmdOn : CmdOff);
        if (x.TxID != 0 && x.Command != CmdLink && ((x.TriggerOn != 0 && x.State != 0) || (x.TriggerOff != 0 && x.State == 0)))
            Send(TypeWall, w, x.TxID, x.Command);
        return true;
    }

    bool SetSpriteState(int i, XSprite x, int state)
    {
        if ((x.Busy & 0xffff) == 0 && x.State == state)
            return false;
        x.Busy = state << 16;
        x.State = state;
        Kill(TypeSprite, i);
        if (x.RestState != state && x.WaitTime > 0)
            Post(TypeSprite, i, Tics(x.WaitTime), x.RestState != 0 ? CmdOn : CmdOff);
        if (x.TxID != 0 && x.Command != CmdLink && ((x.TriggerOn != 0 && x.State != 0) || (x.TriggerOff != 0 && x.State == 0)))
            Send(TypeSprite, i, x.TxID, x.Command);
        return true;
    }

    // ---------------------------------------------------------------- operating

    void OperateSector(int s, XSector x, int cmd)
    {
        switch (cmd)
        {
            case CmdLock: x.Locked = 1; return;
            case CmdUnlock: x.Locked = 0; return;
            case CmdToggleLock: x.Locked ^= 1; return;
            case CmdStopOff: x.StopOn = 0; x.StopOff = 1; return;
            case CmdStopOn: x.StopOn = 1; x.StopOff = 0; return;
            case CmdStopNext: x.StopOn = 1; x.StopOff = 1; return;
        }
        switch (_d.Sectors[s].Type)
        {
            case SectorMotion.ZMotionSprite: OperateDoor(s, x, cmd, ProcSprite); break;
            case SectorMotion.ZMotion: OperateDoor(s, x, cmd, ProcDoor); break;
            case SectorMotion.SlideMarked:
            case SectorMotion.Slide: OperateDoor(s, x, cmd, ProcSlide); break;
            case SectorMotion.RotateMarked:
            case SectorMotion.Rotate: OperateDoor(s, x, cmd, ProcRotate); break;
            case SectorMotion.RotateStep:
                if (cmd == CmdOn)
                {
                    x.State = 0;
                    x.Busy = 0;
                    AddBusy(s, ProcStep, 65536 / Math.Max(Tics(x.BusyTimeA), 1));
                }
                else if (cmd == CmdOff)
                {
                    x.State = 1;
                    x.Busy = 65536;
                    AddBusy(s, ProcStep, -65536 / Math.Max(Tics(x.BusyTimeB), 1));
                }
                break;
            case SectorMotion.Path:
                OperatePath(s, x, cmd);
                break;
            default:
                if (x.BusyTimeA == 0 && x.BusyTimeB == 0)
                    SetSectorState(s, x, cmd == CmdOff ? 0 : cmd == CmdOn ? 1 : x.State ^ 1);
                else
                    OperateDoor(s, x, cmd, ProcGeneric);
                break;
        }
    }

    void OperateDoor(int s, XSector x, int cmd, int proc)
    {
        switch (cmd)
        {
            case CmdOff:
                if (x.Busy != 0)
                    AddBusy(s, proc, -65536 / Math.Max(Tics(x.BusyTimeB), 1));
                break;
            case CmdOn:
                if (x.Busy != 0x10000)
                    AddBusy(s, proc, 65536 / Math.Max(Tics(x.BusyTimeA), 1));
                break;
            default:
                if ((x.Busy & 0xffff) != 0)
                {
                    if (x.Interruptable != 0)
                    {
                        ReverseBusy(s, proc);
                        x.State ^= 1;
                    }
                }
                else
                    AddBusy(s, proc, x.State == 0 ? 65536 / Math.Max(Tics(x.BusyTimeA), 1) : -65536 / Math.Max(Tics(x.BusyTimeB), 1));
                break;
        }
    }

    void OperatePath(int s, XSector x, int cmd)
    {
        if (!_pathMarkers.TryGetValue(s, out var pm) || _d.Sprites[pm.m0].Xsp is not { } x0)
            return;
        int next = PathMarker(x0.Data2);
        if (next < 0)
        {
            x.State = 0;
            x.Busy = 0;
            return;
        }
        _pathMarkers[s] = (pm.m0, next);
        x.OffFloorZ = _d.Sprites[pm.m0].Z;
        x.OnFloorZ = _d.Sprites[next].Z;
        if (cmd == CmdOn)
        {
            x.State = 0;
            x.Busy = 0;
            AddBusy(s, ProcPath, 65536 / Math.Max(Tics(x0.BusyTime), 1));
        }
    }

    void OperateWall(int w, XWall x, int cmd)
    {
        switch (cmd)
        {
            case CmdLock: x.Locked = 1; return;
            case CmdUnlock: x.Locked = 0; return;
            case CmdToggleLock: x.Locked ^= 1; return;
        }
        SetWallState(w, x, cmd == CmdOff ? 0 : cmd == CmdOn ? 1 : x.State ^ 1);
    }

    void OperateSprite(int i, XSprite x, int cmd)
    {
        switch (cmd)
        {
            case CmdLock: x.Locked = 1; return;
            case CmdUnlock: x.Locked = 0; return;
            case CmdToggleLock: x.Locked ^= 1; return;
        }
        var sp = _d.Sprites[i];
        // things broken or blown up (OperateSprite's thing cases)
        if (sp.StatNum == StatThing)
        {
            if (sp.Type is ThingWallCrack or ThingCrateFace)
            {
                if (SetSpriteState(i, x, 0))
                    Remove(i);
                return;
            }
            if (sp.Type == ThingTNTBarrel || (Explosive(sp.Type) && !(sp.Type is ThingArmedRemote or ThingArmedProx && cmd == CmdOn)))
            {
                Explode(i);
                return;
            }
            if (GibThing(sp.Type))
            {
                if (SetSpriteState(i, x, cmd == CmdOff ? 0 : cmd == CmdOn ? 1 : x.State ^ 1) && (sp.Cstat & 32768) == 0)
                    Remove(i);
                return;
            }
        }
        if (sp.StatNum == StatDude)
        {
            // (an enemy woken: its state only here)
            if (cmd == CmdOff)
                SetSpriteState(i, x, 0);
            else if (x.State == 0)
                SetSpriteState(i, x, 1);
            return;
        }
        switch (sp.Type)
        {
            case SwitchPadlock:
            case SwitchToggle:
                SetSpriteState(i, x, cmd == CmdOff ? 0 : cmd == CmdOn ? 1 : x.State ^ 1);
                return;
            case SwitchOneWay:
                SetSpriteState(i, x, cmd == CmdOff ? 0 : cmd == CmdOn ? 1 : x.RestState ^ 1);
                return;
            case SwitchCombo:
                if (cmd == CmdOff)
                {
                    if (--x.Data1 < 0)
                        x.Data1 += x.Data3;
                }
                else if (++x.Data1 >= x.Data3)
                    x.Data1 -= x.Data3;
                if (x.Command == CmdLink && x.TxID > 0)
                    Send(TypeSprite, i, x.TxID, CmdLink);
                SetSpriteState(i, x, x.Data1 == x.Data2 ? 1 : 0);
                return;
        }
        if (sp.Type >= GenFirst && sp.Type <= GenLast)
        {
            switch (cmd)
            {
                case CmdOff:
                    SetSpriteState(i, x, 0);
                    break;
                case CmdRepeat:
                    if (x.TxID != 0)
                        Send(TypeSprite, i, x.TxID, x.Command);
                    if (x.BusyTime > 0)
                        Post(TypeSprite, i, TicsPerSecond * (Random2(x.Data1) + x.BusyTime) / 10, CmdRepeat);
                    break;
                default:
                    if (x.State == 0)
                    {
                        SetSpriteState(i, x, 1);
                        Post(TypeSprite, i, 0, CmdRepeat);
                    }
                    break;
            }
            return;
        }
        SetSpriteState(i, x, cmd == CmdOff ? 0 : cmd == CmdOn ? 1 : x.State ^ 1);
    }

    // ---------------------------------------------------------------- things broken

    /// <summary>A sprite gone from the level (broken, blown up: actPostSprite to kStatFree).</summary>
    public bool Gone(int i) => _d.SpritesGone?.Contains(i) == true;

    void Remove(int i)
    {
        (_d.SpritesGone ??= new HashSet<int>()).Add(i);
        _goneSectors.Add(_d.Sprites[i].SectNum);
    }

    /// <summary>A thing killed as an explosion kills it (actDamageSprite: health to 0, its trigger sent Off), if an
    /// explosion hurts it at all (cracks, crate faces and the explode objects nothing else). Was it?</summary>
    bool Break(int i)
    {
        var sp = _d.Sprites[i];
        if (sp.StatNum != StatThing || !Breakable.Contains(sp.Type) || sp.Xsp is not { } x || Gone(i))
            return false;
        TriggerSprite(i, x, CmdOff);
        return true;
    }

    /// <summary>actExplodeSprite: the thing gone, the things in the explosion's reach broken (CheckProximity's box and
    /// distance, Build units / 16 across and / 256 up; walls between not asked).</summary>
    void Explode(int i)
    {
        if (Gone(i))
            return;
        var sp = _d.Sprites[i];
        Remove(i);
        PlayerLog.Add(new(Seconds, "explode", TypeSprite, i));
        int r = ExplosionRadius(sp.Type);
        for (int j = 0; j < _d.Sprites.Count; j++)
        {
            var o = _d.Sprites[j];
            if (j == i || o.StatNum != StatThing || Gone(j))
                continue;
            int dx = Math.Abs(o.X - sp.X) >> 4, dy = Math.Abs(o.Y - sp.Y) >> 4, dz = Math.Abs(o.Z - sp.Z) >> 8;
            if (dx < r && dy < r && dz < r && ApproxDist(dx, dy) < r)
                Break(j);
        }
    }

    /// <summary>Blood's approxDist (common_game.h).</summary>
    static int ApproxDist(int dx, int dy)
    {
        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        return dx > dy ? dx + ((3 * dy) >> 3) : dy + ((3 * dx) >> 3);
    }

    // ---------------------------------------------------------------- busy

    void AddBusy(int s, int proc, int delta)
    {
        if (delta == 0)
            return;
        var b = _busy.FirstOrDefault(b => b.Sector == s && b.Proc == proc);
        if (b == null)
            _busy.Add(b = new Busy { Sector = s, Proc = proc, Value = delta > 0 ? 0 : 65536 });
        b.Delta = delta;
    }

    void ReverseBusy(int s, int proc)
    {
        if (_busy.FirstOrDefault(b => b.Sector == s && b.Proc == proc) is { } b)
            b.Delta = -b.Delta;
    }

    /// <summary>trProcessBusy: each busy sector a frame further (4 tics of its rate), through its procedure.</summary>
    void ProcessBusy()
    {
        for (int i = _busy.Count - 1; i >= 0; i--)
        {
            if (i >= _busy.Count)
                continue;
            var b = _busy[i];
            b.Value = Math.Clamp(b.Value + b.Delta * TicsPerFrame, 0, 65536);
            if (RunProc(b.Sector, b.Proc, b.Value))
                _busy.Remove(b);
        }
    }

    /// <summary>A busy procedure (VDoorBusy, HDoorBusy...: the sector posed there, linked ones sent its busy, its state
    /// set at an end). Done (at an end)?</summary>
    bool RunProc(int s, int proc, int value)
    {
        var x = _d.Sectors[s].Xs;
        if (x == null)
            return true;
        switch (proc)
        {
            case ProcStep:
                return StepBusy(s, x, value);
            case ProcPath:
                return PathBusy(s, x, value);
            case ProcGeneric:
                x.Busy = value;
                break;
            default:
                BusyStep(s, value);
                return (value & 0xffff) == 0;
        }
        if (x.Command == CmdLink && x.TxID != 0)
            Send(TypeSector, s, x.TxID, CmdLink);
        if ((value & 0xffff) == 0)
        {
            SetSectorState(s, x, value >> 16);
            return true;
        }
        return false;
    }

    /// <summary>A z-motion, sliding or turning sector at busy (VSpriteBusy, VDoorBusy, HDoorBusy, RDoorBusy; a link
    /// too): posed by its wave (A going on, B going off), linked ones sent its busy, its state set at an end.</summary>
    void BusyStep(int s, int value)
    {
        var x = _d.Sectors[s].Xs;
        int wave = x.Busy < value ? x.BusyWaveA : x.BusyWaveB;
        if (SectorMotion.IsMover(_stored, s))
            SetPose(s, Wave(value, wave));
        x.Busy = value;
        if (x.Command == CmdLink && x.TxID != 0)
            Send(TypeSector, s, x.TxID, CmdLink);
        if ((value & 0xffff) == 0)
            SetSectorState(s, x, value >> 16);
    }

    bool StepBusy(int s, XSector x, int value)
    {
        if (!Marker(x.Marker0))
            return true;
        int ang = _d.Sprites[x.Marker0].Ang;
        bool up = x.Busy < value;
        double f = Wave(value, up ? x.BusyWaveA : x.BusyWaveB);
        double a = up ? x.Data + ang * f : x.Data - ang + ang * f;
        _pose[s] = a;
        _dirty.Add(s);
        x.Busy = value;
        if (x.Command == CmdLink && x.TxID != 0)
            Send(TypeSector, s, x.TxID, CmdLink);
        if ((value & 0xffff) == 0)
        {
            SetSectorState(s, x, value >> 16);
            x.Data = (up ? x.Data + ang : x.Data - ang) & 2047;
            _pose[s] = x.Data;
            return true;
        }
        return false;
    }

    bool PathBusy(int s, XSector x, int value)
    {
        if (!_pathMarkers.TryGetValue(s, out var pm) || _d.Sprites[pm.m0].Xsp is not { } x0 || _d.Sprites[pm.m1].Xsp is not { } x1)
            return true;
        _pose[s] = Wave(value, x0.Wave);
        _dirty.Add(s);
        x.Busy = value;
        if ((value & 0xffff) == 0)
        {
            Post(TypeSector, s, Tics(x1.WaitTime), CmdOn);
            x.State = 0;
            x.Busy = 0;
            // (posed at the marker reached, the next leg starting from it)
            PoseDirty();
            _pathMarkers[s] = (pm.m1, pm.m1);
            _pose[s] = 0;
            x.Data = x1.Data1;
            return true;
        }
        return false;
    }

    bool Marker(int i) => i >= 0 && i < _d.Sprites.Count;

    /// <summary>GetWaveValue for a busy (0..65536) as a fraction: 0 eased both ends, 1 even, 2 eased in, 3 eased out.</summary>
    static double Wave(int busy, int wave) => SectorMotion.Wave(busy / 65536.0, wave);

    void SetPose(int s, double f)
    {
        _pose[s] = f;
        _dirty.Add(s);
    }

    /// <summary>The sectors whose pose changed put there in the shown map (from the stored one: no drift).</summary>
    void PoseDirty(HashSet<int> changed = null)
    {
        foreach (int s in _dirty)
        {
            var sec = _stored.Sectors[s];
            var x = sec.Xs;
            double f = _pose[s];
            switch (sec.Type)
            {
                case SectorMotion.SlideMarked:
                case SectorMotion.Slide:
                {
                    var m0 = _stored.Sprites[x.Marker0];
                    var m1 = _stored.Sprites[x.Marker1];
                    SectorMotion.Translate(_stored, _d, s, f, m0.X, m0.Y, m0.Ang, m1.X, m1.Y, m1.Ang, sec.Type == SectorMotion.Slide);
                    SectorMotion.ZTranslate(_stored, _d, s, f);
                    break;
                }
                case SectorMotion.RotateMarked:
                case SectorMotion.Rotate:
                {
                    var m0 = _stored.Sprites[x.Marker0];
                    SectorMotion.Translate(_stored, _d, s, f, m0.X, m0.Y, 0, m0.X, m0.Y, m0.Ang, sec.Type == SectorMotion.Rotate);
                    SectorMotion.ZTranslate(_stored, _d, s, f);
                    break;
                }
                case SectorMotion.RotateStep:
                {
                    var m0 = _stored.Sprites[x.Marker0];
                    SectorMotion.TranslateStored(_stored, _d, s, m0.X, m0.Y, m0.X, m0.Y, f);
                    break;
                }
                case SectorMotion.Path:
                {
                    if (!_pathMarkers.TryGetValue(s, out var pm) || !_pathBase.TryGetValue(s, out int pb))
                        break;
                    var b = _stored.Sprites[pb];
                    var a0 = _stored.Sprites[pm.m0];
                    var a1 = _stored.Sprites[pm.m1];
                    double cx = a0.X + (a1.X - a0.X) * f, cy = a0.Y + (a1.Y - a0.Y) * f, ang = a0.Ang + (a1.Ang - a0.Ang) * f;
                    SectorMotion.TranslateStored(_stored, _d, s, b.X, b.Y, cx, cy, ang);
                    SectorMotion.ZTranslate(_stored, _d, s, f, _d.Sectors[s].Xs);
                    break;
                }
                default:
                    SectorMotion.ZTranslate(_stored, _d, s, f);
                    break;
            }
            _base[s] = (_d.Sectors[s].FloorZ, _d.Sectors[s].CeilingZ);
            changed?.Add(s);
        }
        _dirty.Clear();
    }

    // ---------------------------------------------------------------- the frame's effects

    static readonly byte[] Flicker1 =
    {
        0, 0, 1, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 0, 1, 0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 1, 1, 0, 1, 0, 0, 1,
        0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1,
    };
    static readonly byte[] Flicker2 =
    {
        1, 2, 4, 2, 3, 4, 3, 2, 0, 0, 1, 2, 4, 3, 2, 0, 2, 1, 0, 1, 0, 2, 3, 4, 3, 2, 1, 1, 2, 0, 0, 1,
        1, 2, 3, 4, 4, 3, 2, 1, 2, 3, 4, 4, 2, 1, 0, 1, 0, 0, 0, 0, 1, 2, 3, 4, 3, 2, 1, 2, 3, 4, 3, 2,
    };
    static readonly byte[] Flicker3 =
    {
        4, 4, 4, 4, 3, 4, 4, 4, 4, 4, 4, 2, 4, 3, 4, 4, 4, 4, 2, 1, 3, 3, 3, 4, 3, 4, 4, 4, 4, 4, 2, 4,
        4, 4, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 2, 1, 0, 1, 0, 1, 0, 1, 0, 2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 4,
    };
    static readonly byte[] Flicker4 =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 3, 0, 1, 0, 1, 0, 4, 4, 4, 4, 4, 2, 0,
        0, 0, 0, 4, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1, 4, 3, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };
    static readonly byte[] Strobe =
    {
        64, 64, 64, 48, 36, 27, 20, 15, 11, 9, 6, 5, 4, 3, 2, 2, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    static double Sin(int a) => Math.Sin((a & 2047) * Math.PI / 1024);
    static double Cos(int a) => Math.Cos((a & 2047) * Math.PI / 1024);

    /// <summary>sectorfx.cpp's GetWaveValue: a lighting wave's value at phase b for amplitude c.</summary>
    public static int LightWave(int wave, int b, int c)
    {
        b &= 2047;
        return wave switch
        {
            0 => c,
            1 => (b >> 10) * c,
            2 => (Math.Abs(128 - (b >> 3)) * c) >> 7,
            3 => ((b >> 3) * c) >> 8,
            4 => ((255 - (b >> 3)) * c) >> 8,
            5 => (c + (int)(c * Sin(b))) >> 1,
            6 => Flicker1[b >> 5] * c,
            7 => (Flicker2[b >> 5] * c) >> 2,
            8 => (Flicker3[b >> 5] * c) >> 2,
            9 => (Flicker4[b >> 4] * c) >> 2,
            10 => (Strobe[b >> 5] * c) >> 6,
            11 => b * 4 > 2048 ? 0 : (c - (int)(c * Cos(b * 4))) >> 1,
            _ => 0,
        };
    }

    /// <summary>The frame's lighting (DoSectorLighting: a lit sector's wave added to its floor's, ceiling's, walls'
    /// shade, always or by its busy), bob (ProcessMotion: the floor and ceiling up and down by the sine of its theta)
    /// and slopes (AlignSlopes). The sectors drawn differently.</summary>
    HashSet<int> Effects()
    {
        var changed = new HashSet<int>();
        for (int s = 0; s < _d.Sectors.Count; s++)
        {
            var x = _d.Sectors[s].Xs;
            if (x == null)
                continue;
            if (x.Amplitude != 0 && (x.ShadeAlways != 0 || x.Busy != 0))
            {
                int amp = x.ShadeAlways != 0 ? x.Amplitude : (int)(((long)x.Amplitude * x.Busy) >> 16);
                int shade = LightWave(x.Wave, x.Phase * 8 + x.Freq * _clock, amp);
                if (Light(s, shade))
                    changed.Add(s);
            }
            else if (_light.ContainsKey(s) && Light(s, 0))
                changed.Add(s);
            if (x.BobSpeed != 0 && _bobTheta.TryGetValue(s, out int theta))
            {
                if (x.BobAlways != 0)
                    theta += x.BobSpeed;
                else if (x.Busy == 0)
                    continue;
                else
                    theta += (int)(((long)x.BobSpeed * x.Busy) >> 16);
                _bobTheta[s] = theta;
                int dz = (int)(Sin(theta) * (x.BobZRange << 8));
                var (bf, bc) = _base[s];
                var sec = _d.Sectors[s];
                if (x.BobFloor != 0 && sec.FloorZ != bf + dz)
                {
                    sec.FloorZ = bf + dz;
                    changed.Add(s);
                }
                if (x.BobCeiling != 0 && sec.CeilingZ != bc + dz)
                {
                    sec.CeilingZ = bc + dz;
                    changed.Add(s);
                }
            }
        }
        changed.UnionWith(SectorMotion.AlignSlopes(_d));
        return changed;
    }

    /// <summary>A lit sector's shade added (from the stored shades, clipped as Blood clips them). Changed?</summary>
    bool Light(int s, int shade)
    {
        if (_light.TryGetValue(s, out int was) && was == shade)
            return false;
        if (shade == 0)
            _light.Remove(s);
        else
            _light[s] = shade;
        var x = _d.Sectors[s].Xs;
        var from = _stored.Sectors[s];
        var to = _d.Sectors[s];
        to.FloorShade = x.ShadeFloor != 0 ? Math.Clamp(from.FloorShade + shade, -128, 127) : from.FloorShade;
        to.CeilingShade = x.ShadeCeiling != 0 ? Math.Clamp(from.CeilingShade + shade, -128, 127) : from.CeilingShade;
        for (int w = from.WallPtr; w < from.WallPtr + from.WallNum; w++)
            _d.Walls[w].Shade = x.ShadeWalls != 0 ? Math.Clamp(_stored.Walls[w].Shade + shade, -128, 127) : _stored.Walls[w].Shade;
        return true;
    }

    // ---------------------------------------------------------------- for tests and the status

    /// <summary>A sector's busy (0..65536) now.</summary>
    public int SectorBusy(int s) => _d.Sectors[s].Xs?.Busy ?? 0;

    /// <summary>How many sectors are moving now.</summary>
    public int BusyCount => _busy.Count;
}
