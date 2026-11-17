using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Collision;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The conditions of a modern map (NBlood's nnextsif.cpp; types 501 and 502): a sprite that, given an object by the
/// event reaching it (or tracking the objects that send to it, every few game frames), checks one thing about it
/// (data1: the condition; data2..4 its arguments; the sprite's cstat the comparison) and sends its command on when the
/// answer is yes (502: no). A condition can pass on another object it found (the commands 64..66, "push") and take
/// back one it saved (100..102, "pop"), so chains of them walk from object to object.
/// </summary>
public sealed partial class World
{
    const int kCmdPush = 64, kCmdPop = 100, kPushRange = 3;

    readonly List<Sprite> _trackingConds = new();

    // the condition being checked (nnextsif.cpp's statics)
    Sprite _cond;
    Modern.CondState _cs;
    int _arg1, _arg2, _arg3, _cmpOp, _condCmd;
    bool _push, _condError;
    XObject _condCauser;
    Modern.ObjRef _obj;
    Sprite _oSpr;
    Player _oPlayer;
    Sector _oXSect;     // the focus sector's x-object (null: none)
    Wall _oXWall;
    bool _xAvail;

    void CondReset() => _trackingConds.Clear();

    /// <summary>nnExtInitSprite's part for a condition.</summary>
    void CondInitSprite(Sprite e)
    {
        e.Cond = new Modern.CondState();
        if (e.BusyTime > 0)
        {
            // a tracking condition: its counter (the x-sprite's busy) starts full, so it checks on the first game frame
            e.XBusy = e.BusyTime;
            if (e.WaitTime > 0)
            {
                e.XBusy += e.WaitTime * 120 / 10;
                e.WaitTime = 0;
            }
        }
        e.Decoupled = 0;        // always through its operate
        e.XSight = e.XImpact = e.XTouch = e.XProximity = e.XPush = e.XVector = 0;
        e.XState = e.RestState = 0;
        if (ModernMap == 2 && e.TriggerOn != 0 && e.TriggerOff == 0)
            e.HitTag |= KM.Flag64;
        else
            e.HitTag &= ~KM.Flag64;
        e.TriggerOn = e.TriggerOff = 0;
        e.XTarget = null;
        e.SysData2 = -1;
        e.StatNum = KM.kStatModernCondition;
        e.Cstat |= Cstat.Invisible;
    }

    Modern.ObjRef RefOf(XObject o) => o switch
    {
        Sprite e => new Modern.ObjRef(e),
        Sector s => new Modern.ObjRef(XObject.ObjSector, s.B),
        Wall w => new Modern.ObjRef(XObject.ObjWall, w.BWall),
        _ => Modern.ObjRef.None,
    };

    XObject XObjectOf(Modern.ObjRef r) => r.Type switch
    {
        XObject.ObjSprite => r.Spr,
        XObject.ObjSector => XSectorOf(r.Index),
        XObject.ObjWall => r.Index >= 0 && r.Index < _xwall.Length ? _xwall[r.Index] : null,
        _ => null,
    };

    /// <summary>conditionsTrackingAlloc: each tracking condition's objects: those sending on its RX channel (but
    /// conditions and switches), itself when there are none.</summary>
    void CondInit()
    {
        _trackingConds.Clear();
        foreach (var e in StatList(KM.kStatModernCondition))
        {
            if (!e.HasX || e.BusyTime == 0 || e.IsTriggered != 0)
                continue;
            var list = new List<Modern.ObjRef>();
            if (e.RxID >= K.kChannelUser)
            {
                foreach (var o in AllObjects())
                {
                    if (o == e || !o.HasX || o.Removed || o.TxID != e.RxID)
                        continue;
                    switch (o)
                    {
                        case Sprite s:
                            if (s.NoX != 0)
                                continue;
                            if (s.Type == KM.kModernCondition || s.Type == KM.kModernConditionFalse || s.Type == K.kSwitchToggle
                                || s.Type == K.kSwitchOneWay)
                                continue;
                            list.Add(new Modern.ObjRef(s));
                            break;
                        case Wall w:
                            if (w.Type == K.kSwitchToggle || w.Type == K.kSwitchOneWay)
                                continue;
                            list.Add(RefOf(w));
                            break;
                        case Sector sec:
                            list.Add(RefOf(sec));
                            break;
                    }
                }
            }
            if (list.Count == 0)
                list.Add(new Modern.ObjRef(e));     // tracks itself
            e.Cond.Tracked = list;
            _trackingConds.Add(e);
        }
    }

    /// <summary>conditionsTrackingProcess: each tracking condition counts up; at its busy time it checks its objects.</summary>
    void CondTrackingProcess()
    {
        for (int i = _trackingConds.Count - 1; i >= 0; i--)
        {
            var e = _trackingConds[i];
            if (e.Removed)
            {
                _trackingConds.RemoveAt(i);
                continue;
            }
            var cs = e.Cond;
            if (e.XLocked != 0 || e.IsTriggered != 0 || ++e.XBusy < e.BusyTime)
                continue;
            e.XBusy = 0;
            for (int t = cs.Tracked.Count - 1; t >= 0; t--)
            {
                if (t >= cs.Tracked.Count)
                    continue;
                UseConditionRef(e, K.kCmdOn, null, cs.Tracked[t], true);
            }
        }
    }

    /// <summary>conditionsLinkPlayer: the tracking conditions watching a player control's channel watch the player.</summary>
    void CondLinkPlayer(Sprite ctrl, Player p)
    {
        foreach (var e in _trackingConds)
        {
            if (e.RxID != ctrl.TxID || e.Cond?.Tracked == null)
                continue;
            for (int t = 0; t < e.Cond.Tracked.Count; t++)
                if (e.Cond.Tracked[t].Spr == ctrl)
                {
                    e.Cond.Tracked[t] = new Modern.ObjRef(p);
                    break;
                }
        }
    }

    void CondSetIsTriggered(Sprite e, int value)
    {
        e.IsTriggered = value;
        EvKill(e);
    }

    void CondSetIsLockedOne(Sprite e, int value)
    {
        e.XLocked = value;
        e.RestState = 0;
        e.XState = 0;
        EvKill(e);
        if (e.BusyTime != 0)
            e.XBusy = 0;
    }

    /// <summary>conditionsBubble: the action for a condition and, up its branch while it doesn't fork, the conditions
    /// sending to it.</summary>
    void CondBubble(Sprite start, bool lockAction, int value, int depth = 0)
    {
        if (lockAction)
            CondSetIsLockedOne(start, value);
        else
            CondSetIsTriggered(start, value);
        if (depth > 64)
            return;
        var conds = new List<Sprite>(StatList(KM.kStatModernCondition));
        foreach (var e in conds)
        {
            if (e.TxID != start.RxID)
                continue;
            bool fork = false;
            foreach (var e2 in conds)
            {
                if (e2.RxID != start.RxID || e2 == start)
                    continue;
                if (!lockAction)
                {
                    if (e2.TriggerOnce != 0 && ((value != 0 && e2.IsTriggered == 0) || (value == 0 && e2.IsTriggered != 0)))
                    {
                        fork = true;
                        break;
                    }
                }
                else if ((value != 0 && e2.XLocked == 0) || (value == 0 && e2.XLocked != 0))
                {
                    fork = true;
                    break;
                }
            }
            if (!fork)
                CondBubble(e, lockAction, value, depth + 1);
        }
    }

    /// <summary>A condition locked or unlocked (modernTypeOperateSprite): its branch with it.</summary>
    void CondSetIsLocked(Sprite e, int value) => CondBubble(e, true, value);

    void CondErr(string what)
    {
        _condError = true;
        if (_modernMissing.Add($"cond {_cond?.SprIndex} {what}"))
            Core.GameConsole.Print($"condition {_cond?.Data1} (sprite at {_cond?.BX:0}, {_cond?.BY:0}, RX {_cond?.RxID}, TX {_cond?.TxID}): {what}");
    }

    // ---------------------------------------------------------------- the interface

    bool Cmp(double val)
    {
        if ((_cmpOp & 0x2000) != 0)
            return (_cmpOp & Cstat.Block) != 0 ? val > _arg1 : val >= _arg1;
        if ((_cmpOp & 0x4000) != 0)
            return (_cmpOp & Cstat.Block) != 0 ? val < _arg1 : val <= _arg1;
        if ((_cmpOp & Cstat.Block) != 0)
            return val >= _arg1 && val <= _arg2;
        return val == _arg1;
    }

    bool Cmp(double val, int a1, int a2)
    {
        _arg1 = a1;
        _arg2 = a2;
        return Cmp(val);
    }

    void CondPush(Modern.ObjRef r)
    {
        _cs.X = r;
        if (_cond.XCommand > kCmdPush)
        {
            switch (_cond.XCommand - kCmdPush)
            {
                case 1: _cs.Z = _cs.X; break;
                case 2: _cs.S = _cs.X; break;
            }
        }
    }

    void CondRestore()
    {
        switch (_cond.XCommand - kCmdPop)
        {
            case 0:
                _cs.X = _cs.Y;
                break;
            case 1:
                _cs.X = _cs.Z;
                break;
            case 2:
                (_cs.X, _cs.S) = (_cs.S, _cs.X);
                break;
        }
    }

    bool ChkSprite(Sprite e)
    {
        if (e == null || e.Removed)
            return false;
        if (_push)
            CondPush(new Modern.ObjRef(e));
        return true;
    }

    bool ChkSector(int s)
    {
        if (s < 0 || s >= Board.SectorCount)
            return false;
        if (_push)
            CondPush(new Modern.ObjRef(XObject.ObjSector, s));
        return true;
    }

    bool ChkWall(int w)
    {
        if (w < 0 || w >= Board.WallCount)
            return false;
        if (_push)
            CondPush(new Modern.ObjRef(XObject.ObjWall, w));
        return true;
    }

    void ReceiveObjects(Modern.ObjRef from)
    {
        if (from.Type == XObject.ObjSprite && from.Spr != _cond && from.Spr != null && from.Spr.StatNum == KM.kStatModernCondition
            && from.Spr.Cond != null)
        {
            // the objects of the condition before
            var p = from.Spr.Cond;
            _cs.X = p.X;
            _cs.Y = p.Y;
            _cs.Z = p.Z;
            _cs.S = p.S;
        }
        else
            _cs.X = _cs.Y = _cs.Z = _cs.S = from;
    }

    /// <summary>useCondition for an event reaching the condition (the event's source the object it checks).</summary>
    void UseCondition(Sprite e, int cmd, XObject causer, XObject source) =>
        UseConditionRef(e, cmd, causer, source != null ? RefOf(source) : new Modern.ObjRef(e), false);

    void UseConditionRef(Sprite e, int cmd, XObject causer, Modern.ObjRef from, bool tracking)
    {
        // a tracking condition takes no commands from objects
        if (e.BusyTime != 0 && !tracking)
            return;
        var cs = e.Cond ??= new Modern.CondState();
        // (a condition may run inside another's: the statics are kept)
        var saved = (_cond, _cs, _arg1, _arg2, _arg3, _cmpOp, _condCmd, _push, _condCauser, _obj, _oSpr, _oPlayer, _oXSect, _oXWall, _xAvail);
        _cond = e;
        _cs = cs;
        try
        {
            UseConditionBody(e, cmd, causer, from);
        }
        finally
        {
            (_cond, _cs, _arg1, _arg2, _arg3, _cmpOp, _condCmd, _push, _condCauser, _obj, _oSpr, _oPlayer, _oXSect, _oXWall, _xAvail) = saved;
        }
    }

    void UseConditionBody(Sprite e, int cmd, XObject causer, Modern.ObjRef from)
    {
        var cs = _cs;
        bool delayBefore = false;
        if (e.WaitTime != 0)
        {
            delayBefore = (e.HitTag & KM.Flag64) != 0;
            bool flag8 = (e.HitTag & KM.Flag8) != 0;
            if (e.RestState == 0)
            {
                if (delayBefore)
                {
                    // starts waiting
                    EvPost(e, e.WaitTime * 120 / 10, K.kCmdRepeat, causer);
                    e.RestState = 1;
                    if (flag8)
                        ReceiveObjects(from);
                    return;
                }
                ReceiveObjects(from);
            }
            else if (cmd == K.kCmdRepeat)
            {
                if (delayBefore)
                    e.RestState = 0;
            }
            else
            {
                if ((delayBefore && !flag8) || (!delayBefore && flag8))
                    ReceiveObjects(from);       // goes on receiving while it waits
                return;
            }
        }
        else
            ReceiveObjects(from);
        if (e.RestState == 0)
        {
            bool ok;
            if (e.Data1 == 0)
                ok = true;
            else
            {
                _cmpOp = e.Cstat;
                _push = RngOk(e.XCommand, kCmdPush, kCmdPush + kPushRange);
                _arg1 = e.Data2;
                _arg2 = e.Data3;
                _arg3 = e.Data4;
                _condCmd = cmd;
                _condCauser = causer;
                _obj = cs.X;
                _condError = false;
                ok = CondCheck(e.Data1);
                if (_condError)
                    return;
            }
            if (ok ^ (e.Type == KM.kModernConditionFalse))
            {
                e.XState = 1;
                if (e.WaitTime != 0 && !delayBefore)
                {
                    // the delay after the check
                    EvPost(e, e.WaitTime * 120 / 10, K.kCmdRepeat, causer);
                    e.RestState = 1;
                    return;
                }
            }
            else
                e.XState = 0;
        }
        else if (cmd == K.kCmdRepeat)
            e.RestState = 0;
        else
            return;
        if (e.XState != 0)
        {
            // once per result
            if ((e.HitTag & KM.Flag4) != 0)
            {
                if (e.Unused2 != 0)
                    return;
                e.Unused2 = 1;
            }
            if (e.TriggerOnce != 0)
                CondBubble(e, false, 1);
            if (RngOk(e.XCommand, kCmdPop, kCmdPop + kPushRange))
                CondRestore();
            if (e.TxID != 0)
                EvSend(e, e.TxID, e.XCommand, cs.X.Type == XObject.ObjSprite ? cs.X.Spr : null);
            if (e.HitTag != 0)
            {
                // to the object in the focus
                if ((e.HitTag & KM.Flag1) != 0)
                {
                    CondTriggerObject(cs.X, e);
                    if ((e.HitTag & KM.Flag2) != 0 && cs.X.Equals(cs.Y))
                        return;
                }
                // to the first object
                if ((e.HitTag & KM.Flag2) != 0)
                    CondTriggerObject(cs.Y, e);
            }
        }
        else
            e.Unused2 = 0;
    }

    void CondTriggerObject(Modern.ObjRef r, Sprite cond)
    {
        if (XObjectOf(r) is { } o && o.HasX)
            NnExtTriggerObject(o, cond.XCommand, cond);
    }

    // the object in the focus made ready for a kind of condition; false: the wrong kind, or no x-data where it is needed
    bool FocusSector(bool xReq, out bool result)
    {
        result = false;
        if (_obj.Type != XObject.ObjSector || _obj.Index < 0 || _obj.Index >= Board.SectorCount)
        {
            CondErr($"object is not a sector");
            return false;
        }
        _oXSect = XSectorOf(_obj.Index);
        _xAvail = _oXSect != null;
        return _xAvail || !xReq;
    }

    bool FocusWall(bool xReq)
    {
        if (_obj.Type != XObject.ObjWall || _obj.Index < 0 || _obj.Index >= Board.WallCount)
        {
            CondErr("object is not a wall");
            return false;
        }
        _oXWall = _obj.Index < _xwall.Length ? _xwall[_obj.Index] : null;
        _xAvail = _oXWall != null;
        return _xAvail || !xReq;
    }

    bool FocusSprite(bool xReq)
    {
        if (_obj.Type != XObject.ObjSprite || _obj.Spr == null)
        {
            CondErr("object is not a sprite");
            return false;
        }
        _oSpr = _obj.Spr;
        _xAvail = _oSpr.HasX && _oSpr.NoX == 0;
        return _xAvail || !xReq;
    }

    /// <summary>The condition's check by its id (gConditions and the callers of nnextsif.cpp).</summary>
    bool CondCheck(int id)
    {
        bool isBool = CondIsBool(id);
        bool xReq = CondXReq(id);
        bool Default() => !isBool && Cmp(0);
        var b = Board;
        // game conditions
        if (id < 100)
        {
            switch (id)
            {
                case 1: return Cmp(LevelTime / (K.kTicsPerSec * 60));
                case 2: return Cmp(LevelTime / K.kTicsPerSec % 60);
                case 3: return Cmp(LevelTime % K.kTicsPerSec * 33 / 10);
                case 4: return Cmp(LevelTime);
                case 5: return Cmp(TotalKills);
                case 6: return Cmp(Kills);
                case 7: return Cmp(SecretsFound);
                case 8: return Cmp(SecretsTotal);
                case 20: return Cmp(GlobalVisibility >= 0 ? GlobalVisibility : (b.File.Visibility));
                case 21: return _arg3 >= 0 && TilesSeen.Contains(_arg3);
                case 30: return Chance(0x10000 * _arg3 / KM.kPercFull);
                case 31: return Cmp(NnExtRandom(_arg1, _arg2));
                case 47:
                {
                    int n = 0;
                    foreach (var _ in StatList((int)Blood.ClipRange(_arg3, 0, 1024)))
                        n++;
                    return Cmp(n);
                }
                case 48: return Cmp(Sprites.Count);
                case 57: return ChkSector(_arg1);
                case 58: return ChkWall(_arg1);
                case 59: return ChkSprite(SpriteOfB(_arg1));
                case 60:
                    foreach (var p in Players)
                        if (p.Ready && p.PlayerIndex + 1 == _arg3)
                        {
                            if (_push)
                                CondPush(new Modern.ObjRef(p));
                            return true;
                        }
                    return false;
            }
            CondErr("not implemented");
            return false;
        }
        // mixed object conditions
        if (id < 200)
        {
            // the object, whatever it is
            switch (_obj.Type)
            {
                case XObject.ObjWall:
                    if (!FocusWall(xReq))
                        return !_condError && Default();
                    break;
                case XObject.ObjSector:
                    if (!FocusSector(xReq, out _))
                        return !_condError && Default();
                    break;
                case XObject.ObjSprite:
                    if (!FocusSprite(xReq))
                        return !_condError && Default();
                    break;
                default:
                    CondErr("no object");
                    return false;
            }
            var wl = _obj.Type == XObject.ObjWall ? b.Walls[_obj.Index] : null;
            var sc = _obj.Type == XObject.ObjSector ? b.Sectors[_obj.Index] : null;
            var sp = _obj.Type == XObject.ObjSprite ? _oSpr : null;
            XObject xo = _obj.Type == XObject.ObjWall ? _oXWall : _obj.Type == XObject.ObjSector ? _oXSect : sp;
            bool PlaneCmp(Func<bool, double> f) => _arg3 switch
            {
                1 => Cmp(f(false)),
                2 => Cmp(f(true)),
                _ => Cmp(f(false)) || Cmp(f(true)),
            };
            switch (id)
            {
                case 100: return _obj.Type == XObject.ObjSector;
                case 105: return _obj.Type == XObject.ObjWall;
                case 110: return _obj.Type == XObject.ObjSprite && sp != null && !sp.Removed;
                case 115: return _xAvail;
                case 120: return Cmp(wl != null ? wl.Type : sc != null ? sc.Type : sp.Type);
                case 124:
                    if (wl != null) return Cmp(Tiles.SurfType(wl.Picnum));
                    if (sp != null) return Cmp(Tiles.SurfType(sp.Picnum));
                    return PlaneCmp(ceil => Tiles.SurfType(ceil ? sc.CeilingPicnum : sc.FloorPicnum));
                case 125:
                    if (wl != null) return Cmp(wl.Picnum);
                    if (sp != null) return Cmp(sp.Picnum);
                    return PlaneCmp(ceil => ceil ? sc.CeilingPicnum : sc.FloorPicnum);
                case 126:
                    if (wl != null) return Cmp(wl.Pal);
                    if (sp != null) return Cmp(sp.Pal);
                    return PlaneCmp(ceil => ceil ? sc.CeilingPal : sc.FloorPal);
                case 127:
                    if (wl != null) return Cmp(wl.Shade);
                    if (sp != null) return Cmp(sp.Shade);
                    return PlaneCmp(ceil => ceil ? sc.CeilingShade : sc.FloorShade);
                case 128:
                    if (wl != null) return _arg3 != 0 ? Cmp(wl.Cstat & _arg3) : (wl.Cstat & _arg1) != 0;
                    if (sp != null)
                    {
                        // (the port keeps Build's y-centre bit set on every sprite; a map's own value is asked here)
                        int cst = sp.Cstat;
                        return _arg3 != 0 ? Cmp(cst & _arg3) : (cst & _arg1) != 0;
                    }
                    return _arg3 switch
                    {
                        1 => (sc.FloorStat & _arg1) != 0,
                        2 => (sc.CeilingStat & _arg1) != 0,
                        _ => (sc.FloorStat & _arg1) != 0 || (sc.CeilingStat & _arg1) != 0,
                    };
                case 129:
                {
                    int tag = wl != null ? wl.Hitag : sc != null ? sc.Hitag : sp.HitTag;
                    return _arg3 != 0 ? Cmp(tag & _arg3) : (tag & _arg1) != 0;
                }
                case 130: return Cmp(wl != null ? wl.XRepeat : sc != null ? sc.FloorXPanning : sp.XRepeat);
                case 131: return Cmp(wl != null ? wl.XPanning : sc != null ? sc.CeilingXPanning : sp.XOffset);
                case 132: return Cmp(wl != null ? wl.YRepeat : sc != null ? sc.FloorYPanning : sp.YRepeat);
                case 133: return Cmp(wl != null ? wl.YPanning : sc != null ? sc.CeilingYPanning : sp.YOffset);
                case 141:
                case 142:
                case 143:
                case 144:
                    if (wl != null) return Cmp(_oXWall.Data1);
                    if (sc != null) return Cmp(_oXSect.SData);
                    return Cmp(id == 141 ? sp.Data1 : id == 142 ? sp.Data2 : id == 143 ? sp.Data3 : sp.Data4);
                case 150: return Cmp(xo.RxID);
                case 151: return Cmp(xo.TxID);
                case 152: return xo.XLocked != 0;
                case 153: return xo.TriggerOn != 0;
                case 154: return xo.TriggerOff != 0;
                case 155: return xo.TriggerOnce != 0;
                case 156: return xo.IsTriggered != 0;
                case 157: return xo.XState != 0;
                case 158: return Cmp(KM.kPercFull * xo.XBusy / 65536);
                case 159: return xo.DudeLockout != 0;
                case 170:
                case 171:
                {
                    bool frame = id == 171;
                    if (sp != null)
                        return Cmp(frame ? Seq.Status(sp) : Seq.Id(sp));
                    int k1 = wl != null ? SurfSeq.Wall : SurfSeq.Ceiling, k2 = wl != null ? SurfSeq.Masked : SurfSeq.Floor;
                    double Val(int kind) => SurfSeqValue(kind, _obj.Index, frame);
                    return _arg3 switch { 1 => Cmp(Val(k1)), 2 => Cmp(Val(k2)), _ => Cmp(Val(k1)) || Cmp(Val(k2)) };
                }
                case 195: return Cmp(_obj.Type == XObject.ObjSprite ? sp.SprIndex : _obj.Index);
                case 196: return Cmp(_xAvail ? 1 : -1);
                case 197:
                {
                    Modern.ObjRef[] serials = { _cs.X, _cs.Y, _cs.Z, _cs.S };
                    var t = new int[3];
                    string digits = Math.Abs(_arg1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    for (int i = 0; i < 3; i++)
                    {
                        int d = i < digits.Length ? digits[i] - '0' : -1;
                        if (d < 1 || d > 4)
                        {
                            CondErr("invalid arguments");
                            return false;
                        }
                        t[i] = serials[d - 1].Serial;
                    }
                    return Cmp(t[0], t[1], t[2]);
                }
                case 198: return ChkSprite(_condCauser as Sprite);
                case 199: return Cmp(_condCmd);
            }
            CondErr("not implemented");
            return false;
        }
        // wall conditions
        if (id < 300)
        {
            if (!FocusWall(xReq))
                return !_condError && Default();
            var wl = b.Walls[_obj.Index];
            switch (id)
            {
                case 200: return Cmp(wl.OverPicnum);
                case 201: return true;
                case 205: return ChkSector(wl.Sector);
                case 210: return wl.Type != K.kWallStack && Cmp(wl.Picnum, 4080, 4095);
                case 215: return ChkSector(wl.NextSector);
                case 220: return ChkWall(wl.NextWall);
                case 221: return ChkWall(wl.Point2);
                case 225: return wl.NextWall >= 0 && ChkSector(b.Walls[wl.NextWall].Sector);
            }
            CondErr("not implemented");
            return false;
        }
        // sector conditions
        if (id < 400)
        {
            if (!FocusSector(xReq, out _))
                return !_condError && Default();
            var sc = b.Sectors[_obj.Index];
            switch (id)
            {
                case 300: return Cmp(sc.Visibility);
                case 301: return true;
                case 302: return SectorsSeen.Contains(_obj.Index);
                case 305: return Cmp(sc.FloorHeinum);
                case 306: return Cmp(sc.CeilingHeinum);
                case 310:
                    foreach (var s in SpritesInB(_obj.Index))
                    {
                        if (!Cmp(s.Type))
                            continue;
                        if (_push)
                            CondPush(new Modern.ObjRef(s));
                        return true;
                    }
                    return false;
                case 350: return _oXSect.SUnderwater != 0;
                case 351: return Cmp(_oXSect.SDepth);
                case 355:
                case 356:
                {
                    bool ceil = id == 356;
                    switch (sc.Type)
                    {
                        case K.kSectorZMotion:
                        case K.kSectorRotate:
                        case K.kSectorSlide:
                        case K.kSectorSlideMarked:
                        case K.kSectorRotateMarked:
                        case K.kSectorRotateStep:
                        {
                            var xs = _oXSect;
                            double h1 = ceil ? Blood.ClipLow(Math.Abs(xs.OnCeilZ - xs.OffCeilZ), 1) : Blood.ClipLow(Math.Abs(xs.OnFloorZ - xs.OffFloorZ), 1);
                            double h2 = ceil ? Math.Abs(sc.CeilingZ - xs.OffCeilZ) : Math.Abs(sc.FloorZ - xs.OffFloorZ);
                            return Cmp(Blood.Int(KM.kPercFull * h2 / h1));
                        }
                    }
                    CondErr($"unsupported sector type {sc.Type}");
                    return false;
                }
                case 357: return !_oXSect.MotionPaused;
                case 358:
                case 359:
                {
                    var link = id == 358 ? UpperLinkOf(_obj.Index) : LowerLinkOf(_obj.Index);
                    if (link == null)
                        return false;
                    if (_push)
                    {
                        var other = link.Owner;
                        if (_arg3 == 1) CondPush(new Modern.ObjRef(link));
                        else if (_arg3 == 2 && other != null) CondPush(new Modern.ObjRef(other));
                        else if (other != null) CondPush(new Modern.ObjRef(XObject.ObjSector, SpriteBSector(other)));
                    }
                    return true;
                }
            }
            CondErr("not implemented");
            return false;
        }
        // sprite kinds
        if (!FocusSprite(xReq))
            return !_condError && Default();
        var e = _oSpr;
        if (id < 450)
        {
            // player conditions
            if (e is not Player p || !p.Ready)
                return Default();
            switch (id)
            {
                case 400: return Cmp(p.PlayerIndex + 1) && ChkSprite(p);
                case 401: return Cmp(p.TeamId + 1);
                case 402: return ValueIsBetween(_arg1, 0, 8) && p.HasKey[_arg1 - 1] != 0;
                case 403: return ValueIsBetween(_arg1, 0, 15) && p.HasWeapon[_arg1 - 1] != 0;
                case 404: return Cmp(p.CurWeapon);
                case 405: return ValueIsBetween(_arg1, 0, 6) && Cmp(p.PackAmount[_arg1 - 1]);
                case 406: return ValueIsBetween(_arg1, 0, 6) && p.PackActive[_arg1 - 1] != 0;
                case 407: return Cmp(p.PackItemId + 1);
                case 408:
                    if (_arg3 > 0 && _arg3 <= 51 - (K.kPwUpFeatherFall << 1) + 1)
                        return Cmp(Blood.Int(p.PwUpTime[K.kPwUpFeatherFall + _arg3 - 1] / KM.kPercFull));
                    CondErr($"unexpected powerup #{_arg3}");
                    return false;
                case 409: return ChkSprite(p.FraggerId);
                case 410:
                    switch (_arg1)
                    {
                        case 1: return p.InputForward > 0;
                        case 2: return p.InputForward < 0;
                        case 3: return p.InputStrafe > 0;
                        case 4: return p.InputStrafe < 0;
                        case 5: return p.InputJump;
                        case 6: return p.InputCrouch;
                        case 7: return p.InputShoot;
                        case 8: return p.InputShoot2;
                        case 9: return p.InputAction;
                    }
                    CondErr("specify a correct key");
                    return false;
                case 411: return p.IsRunning;
                case 412: return p.FallScream;
                case 413: return Cmp(p.LifeMode + 1);
                case 414: return Cmp(p.Posture + 1);
                case 419:
                    if (RngOk(_arg3, 0, 1025))
                    {
                        foreach (var s in StatList(_arg3))
                        {
                            if (!Cmp(s.Type) || s.Owner != p)
                                continue;
                            if (_push)
                                CondPush(new Modern.ObjRef(s));
                            return true;
                        }
                        return false;
                    }
                    CondErr("invalid arguments");
                    return false;
                case 420:
                    if (ValueIsBetween(_arg3, 0, 4))
                        return Cmp(Blood.Int(p.Armor[_arg3 - 1] * KM.kPercFull / 1600));
                    CondErr("invalid arguments");
                    return false;
                case 421:
                    if (ValueIsBetween(_arg3, 0, 12))
                        return Cmp(p.Ammo[_arg3 - 1]);
                    CondErr("invalid arguments");
                    return false;
                case 430: return Cmp(p.FragCount);
                case 431: return ChkSprite(p.AimTarget);
                case 435: return ChkSprite(p.VoodooTarget);
                case 445: return Cmp(p.WeaponQav);
                case 446: return Cmp(p.SceneQav);
                case 447: return p.GodMode || p.PwUpTime[K.kPwUpDeathMask] > 0;
                case 448: return p.PwUpTime[K.kPwUpShrinkShroom] > 0 || (p.XScale > 0 && p.XScale <= 128);
                case 449: return p.PwUpTime[K.kPwUpGrowShroom] > 0 || p.XScale >= 512;
            }
            CondErr("not implemented");
            return false;
        }
        if (id < 470)
        {
            // dude conditions
            if ((!IsDudeSprite(e) && e.Type != K.kThingBloodChunks) || e is Player)
            {
                CondErr("object is not a dude");
                return false;
            }
            var targ = e.XTarget;
            switch (id)
            {
                case 450:
                    if (targ == null || (!IsDudeSprite(targ) && targ.Type != K.kMarkerPath))
                        return false;
                    if (_push)
                        CondPush(new Modern.ObjRef(targ));
                    return true;
                case 451: return AiFightDudeIsAffected(e);
                case 452:
                case 453:
                case 454:
                {
                    if (targ == null)
                        return false;
                    double eyeAboveZ = Info.Dude(e.Type, Info.DI_eyeHeight) * e.YRepeat << 2;
                    double dx = targ.BX - e.BX, dy = targ.BY - e.BY;
                    bool t;
                    if (id == 452)
                    {
                        _arg1 *= 512;
                        _arg2 *= 512;
                        t = Cmp(Blood.ApproxDist(dx, dy));
                    }
                    else
                    {
                        t = CanSee(e.BX, e.BY, e.BZ, targ.BX, targ.BY, targ.BZ - eyeAboveZ, e, targ);
                        if (t && id == 454)
                        {
                            int a = ((1024 + Blood.GetAngle(dx, dy) - e.Ang) & 2047) - 1024;
                            t = Math.Abs(a) < (_arg1 <= 0 ? Info.Dude(e.Type, Info.DI_periphery) : Blood.ClipHigh(_arg1, 2048));
                        }
                    }
                    if (!t)
                        return false;
                    if (_push)
                        CondPush(new Modern.ObjRef(targ));
                    return true;
                }
                case 455: return e.DudeFlag4 != 0;
                case 456: return e.DudeDeaf != 0;
                case 457: return e.DudeGuard != 0;
                case 458: return e.DudeAmbush != 0;
                case 459: return (e.Unused1 & KM.kDudeFlagStealth) != 0;
                case 460:
                case 461:
                    if (e.DudeFlag4 != 0 && targ != null && targ.Type == K.kMarkerPath)
                    {
                        if (id == 460)
                        {
                            if (AiPatrolMarkerBusy(e, targ) is { } busy)
                            {
                                if (_push)
                                    CondPush(new Modern.ObjRef(busy));
                                return true;
                            }
                        }
                        else if (AiPatrolMarkerReached(e))
                        {
                            if (_push)
                                CondPush(new Modern.ObjRef(targ));
                            return true;
                        }
                    }
                    return false;
                case 462:
                    if (e.DudeFlag4 == 0 || targ == null || targ.Type != K.kMarkerPath)
                        return Cmp(0);
                    if ((e.Unused1 & KM.kDudeFlagStealth) == 0 || !ValueIsBetween(e.Data3, 0, kMaxPatrolSpotValue))
                        return Cmp(0);
                    return Cmp(KM.kPercFull * e.Data3 / kMaxPatrolSpotValue);
                case 465: return Info.Dude(e.Type, Info.DI_lockOut) != 0;
                case 466: return Cmp(e.AiState?.Type ?? -1);
                case 467: return Cmp(e.StateTimer);
            }
            CondErr("not implemented");
            return false;
        }
        if (id < 500)
        {
            // custom dude conditions
            if (!(e.Type == KM.kDudeModernCustom || (e.Type == K.kThingBloodChunks && e.InitType == KM.kDudeModernCustom)) || e.Custom == null)
            {
                CondErr("object is not a custom dude");
                return false;
            }
            var cd = e.Custom;
            switch (id)
            {
                case 470: return !cd.IsLeechBroken && cd.Leech != null && ChkSprite(cd.Leech);
                case 471:
                    if (cd.IsLeechBroken)
                        return true;
                    if (_push && cd.Leech != null)
                        CondPush(new Modern.ObjRef(cd.Leech));
                    return false;
                case 472: return Cmp(cd.SlaveCount);
                case 473:
                    switch (_arg3)
                    {
                        case 1: return false;
                        case 2: return cd.CanBurn();
                        case 3: return cd.CanCrouch();
                        case 4: return cd.CanElectrocute();
                        case 5: return false;
                        case 6: return cd.CanRecoil();
                        case 7: return cd.CanSwim();
                        case 8: return cd.CanMove();
                    }
                    CondErr("invalid arguments");
                    return false;
                case 474: return Cmp(cd.CurrentDispersion);
            }
            CondErr("not implemented");
            return false;
        }
        // sprite conditions
        switch (id)
        {
            case 500: return Cmp(_arg3 == 0 ? e.Ang & 2047 : e.Ang);
            case 501: return true;
            case 505: return Cmp(e.StatNum);
            case 506: return (e.HitTag & K.kHitagRespawn) != 0 || e.StatNum == K.kStatRespawn;
            case 507: return Cmp(e.Slope);
            case 510: return Cmp(e.ClipDist);
            case 515: return ChkSprite(e.Owner);
            case 520: return ChkSector(SpriteBSector(e));
            case 525:
                if (_arg3 == 0)
                {
                    switch (_arg1)
                    {
                        case 0: return e.XVel != 0 || e.YVel != 0 || e.ZVel != 0;
                        case 1: return e.XVel != 0;
                        case 2: return e.YVel != 0;
                        case 3: return e.ZVel != 0;
                    }
                    CondErr("invalid arguments");
                    return false;
                }
                goto case 526;
            case 526:
                switch (_arg3)
                {
                    case 0: return Cmp(e.XVel) || Cmp(e.YVel) || Cmp(e.ZVel);
                    case 1: return Cmp(e.XVel);
                    case 2: return Cmp(e.YVel);
                    case 3: return Cmp(e.ZVel);
                }
                CondErr("invalid arguments");
                return false;
            case 530: return SpriteSector(e) is { } us && us.SUnderwater != 0;
            case 531:
                if (_arg1 == -1)
                {
                    for (int i = 0; i < K.kDamageMax; i++)
                        if (!NnExtIsImmune(e, i, 0))
                            return false;
                    return true;
                }
                return NnExtIsImmune(e, _arg1, 0);
            case 535: return CondHitscan(e, 1);
            case 536: return CondHitscan(e, 2);
            case 537: return CondHitscan(e, 3);
            case 538: return CondHitscan(e, 4);
            case 539: return CondHitscan(e, 5);
            case 545:
                foreach (var d in StatList(K.kStatDude))
                {
                    if (d == e || !IsDudeSprite(d) || !d.HasX || d.XHealth <= 0 || d.XTarget != e)
                        continue;
                    if (_push)
                        CondPush(new Modern.ObjRef(d));
                    return true;
                }
                return false;
            case 550:
            {
                double t = 0;
                if (IsDudeSprite(e))
                    t = e.SysData2 > 0 ? Blood.ClipHigh(e.SysData2 << 4, 65535) : Info.Dude(e.Type, Info.DI_startHealth) << 4;
                else if (e.Type == K.kThingBloodChunks)
                    return Cmp(0);
                else if (IsThingSprite(e))
                    t = Info.Thing(e.Type, Info.TI_startHealth) << 4;
                t = Blood.Int(KM.kPercFull * e.XHealth / Blood.ClipLow(t, 1));
                return Cmp(t == 0 && e.XHealth != 0 ? 1 : t);
            }
            case 555:
                if (e.CeilHit != Hit.Sector)
                    return false;
                if (_push)
                    CondPush(new Modern.ObjRef(XObject.ObjSector, SectorOfHit(e.CeilHitEnt, e)));
                return true;
            case 556:
                if (e.FlorHit != Hit.Sector)
                    return false;
                if (_push)
                    CondPush(new Modern.ObjRef(XObject.ObjSector, SectorOfHit(e.FlorHitEnt, e)));
                return true;
            case 557:
                if (e.Hit != Hit.Wall)
                    return false;
                if (_push && e.HitEnt is Wall hw)
                    CondPush(RefOf(hw));
                return true;
            case 558:
            {
                Sprite found = null;
                if (_xAvail)
                {
                    if (_arg3 == 0 || _arg3 == 1)
                        if (e.FlorHit == Hit.Sprite)
                            found = e.FlorHitEnt as Sprite;
                    if (found == null && (_arg3 == 0 || _arg3 == 2))
                        if (e.Hit == Hit.Sprite)
                            found = e.HitEnt as Sprite;
                    if (found == null && (_arg3 == 0 || _arg3 == 3))
                        if (e.CeilHit == Hit.Sprite)
                            found = e.CeilHitEnt as Sprite;
                }
                // something touching this sprite
                if (found == null && SpriteBSector(e) >= 0)
                {
                    foreach (var o in SpritesInB(SpriteBSector(e)))
                    {
                        if (!o.HasX)
                            continue;
                        if ((_arg3 == 1 || _arg3 == 0) && o.CeilHit == Hit.Sprite && o.CeilHitEnt == e) { found = o; break; }
                        if ((_arg3 == 2 || _arg3 == 0) && o.Hit == Hit.Sprite && o.HitEnt == e) { found = o; break; }
                        if ((_arg3 == 3 || _arg3 == 0) && o.FlorHit == Hit.Sprite && o.FlorHitEnt == e) { found = o; break; }
                    }
                }
                if (found == null || found.Removed)
                    return false;
                if (_push)
                    CondPush(new Modern.ObjRef(found));
                return true;
            }
            case 565:
            {
                int t = IsDudeSprite(e) ? 2400 : 1200;
                if (!Cmp(KM.kPercFull * e.BurnTime / t))
                    return false;
                if (_push && e.BurnSource != null)
                    CondPush(new Modern.ObjRef(e.BurnSource));
                return true;
            }
            case 566:
                foreach (var f in StatList(K.kStatFlare))
                {
                    if (!f.HasX || (f.HitTag & K.kHitagFree) != 0 || f.XTarget != e)
                        continue;
                    if (_push)
                        CondPush(new Modern.ObjRef(f));
                    return true;
                }
                return false;
            case 569: return ChkSprite(e.XTarget);
            case 570: return Cmp(GetSpriteMassBySize(e));
        }
        CondErr("not implemented");
        return false;
    }

    // which conditions answer yes or no (the rest compare a value), and which need the object's x-data
    static bool CondIsBool(int id) => id switch
    {
        21 or 30 or 57 or 58 or 59 or 60 or 100 or 105 or 110 or 115 or (>= 152 and <= 157) or 159 or 198 or 201 or 205 or 210
            or 215 or 220 or 221 or 225 or 301 or 302 or 310 or 350 or 357 or 358 or 359 or 402 or 403 or 406 or 409 or 410 or 411
            or 412 or 419 or 431 or 435 or 447 or 448 or 449 or (>= 450 and <= 461) or 465 or 470 or 471 or 473 or 501 or 506
            or 515 or 520 or 525 or 530 or 531 or (>= 535 and <= 539) or 545 or (>= 555 and <= 558) or 566 or 569 => true,
        _ => false,
    };

    static bool CondXReq(int id) => id switch
    {
        (>= 141 and <= 144) or (>= 150 and <= 159) or 170 or 171 or 350 or 351 or 355 or 356 or 357 or (>= 450 and <= 467) or 550
            or 555 or 556 or 557 or 565 or 569 or 570 => true,
        _ => false,
    };

    double SurfSeqValue(int kind, int index, bool frame)
    {
        foreach (var i in SurfSeqs)
            if (i.Kind == kind && i.Index == index)
                return frame ? i.Frame : i.SeqId;
        return -1;
    }

    /// <summary>The tiles and sectors drawn lately (NBlood's gotpic and gotsector, what conditions 21 and 302 ask): the
    /// renderer fills them.</summary>
    public readonly HashSet<int> TilesSeen = new(), SectorsSeen = new();

    /// <summary>helperChkHitscan: what a hitscan from the sprite along the way it faces (a player: its aim) hits.</summary>
    bool CondHitscan(Sprite e, int what)
    {
        int nAng = e.Ang & 2047;
        int oldCstat = e.Cstat;
        int mask = _arg1 == 1 ? Contents.MaskBlock : Contents.MaskShot;
        double range = _arg3 << 1;
        int hit;
        if (e is Player p)
            hit = HitScan(e, p.ZWeapon, p.AimDx, p.AimDy, p.AimDz, range, mask);
        else
        {
            double dz = 0;
            if (IsDudeSprite(e))
                dz = e.DudeSlope;
            else if ((oldCstat & Cstat.AlignMask) == KM.CstatSlope)
                dz = (oldCstat & Cstat.YFlip) != 0 ? 0x08000 - Math.Abs(e.Slope) : -(0x08000 - Math.Abs(e.Slope));
            else if ((oldCstat & Cstat.AlignMask) == Cstat.Floor)
                dz = (oldCstat & Cstat.YFlip) != 0 ? 0x10000 : -0x10000;
            hit = HitScan(e, e.BZ, Blood.Sar(Blood.Cos(nAng), 16), Blood.Sar(Blood.Sin(nAng), 16), dz, range, mask);
        }
        if (hit < 0)
            return false;
        switch (what)
        {
            case 1:
                if (hit != 1) return false;
                if (_push) CondPush(new Modern.ObjRef(XObject.ObjSector, HitSectB));
                return true;
            case 2:
                if (hit != 2) return false;
                if (_push) CondPush(new Modern.ObjRef(XObject.ObjSector, HitSectB));
                return true;
            case 3:
                if (hit != 0 && hit != 4) return false;
                if (_push) CondPush(new Modern.ObjRef(XObject.ObjWall, HitWallB));
                return true;
            case 4:
                if (hit != 3 || HitEnt is not Sprite hs) return false;
                if (_push) CondPush(new Modern.ObjRef(hs));
                return true;
            case 5:
                if (hit != 4) return false;
                if (_push) CondPush(new Modern.ObjRef(XObject.ObjWall, HitWallB));
                return true;
        }
        CondErr("invalid arguments");
        return false;
    }
}
