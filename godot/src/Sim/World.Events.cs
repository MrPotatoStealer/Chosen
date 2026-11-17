using System.Collections.Generic;
using Chosen.Data;

namespace Chosen.Sim;

public sealed partial class World
{
    sealed class Event
    {
        public double Time;
        public XObject Target;
        public int Cmd;
        public int Func = -1;
        public XObject Causer;
        public int Slot;               // each event is an entity (evAlloc)
    }

    // sorted by time, earliest first; equal times keep posting order
    readonly List<Event> _events = new();
    XObject _linkSource;           // gLinkSource: the source of a kCmdLink being sent


    void EvInsert(Event ev)
    {
        int i = _events.Count;
        while (i > 0 && _events[i - 1].Time > ev.Time)
            i--;
        _events.Insert(i, ev);
    }

    static int SourceState(XObject e) => e?.XState ?? 0;

    /// <summary>evPost: deliver cmd to target after delta tics.</summary>
    public void EvPost(XObject target, int delta, int cmd, XObject causer)
    {
        if (cmd == K.kCmdState)
            cmd = SourceState(target) != 0 ? K.kCmdOn : K.kCmdOff;
        else if (cmd == K.kCmdNotState)
            cmd = SourceState(target) != 0 ? K.kCmdOff : K.kCmdOn;
        EvInsert(new Event { Time = FrameClock + delta, Target = target, Cmd = cmd, Causer = causer, Slot = Edicts.Alloc(_edictTime) });
    }

    public void EvPostCallback(XObject target, int delta, int funcId) =>
        EvInsert(new Event { Time = FrameClock + delta, Target = target, Cmd = K.kCmdCallback, Func = funcId, Slot = Edicts.Alloc(_edictTime) });

    /// <summary>evKill: drop every pending event for target.</summary>
    public void EvKill(XObject target) => _events.RemoveAll(e => e.Target == target && FreeEvent(e));

    public void EvKillCallback(XObject target, int funcId) =>
        _events.RemoveAll(e => e.Target == target && e.Cmd == K.kCmdCallback && e.Func == funcId && FreeEvent(e));

    bool FreeEvent(Event e)
    {
        Edicts.Free(e.Slot, _edictTime);
        return true;
    }

    public void EvKillAll() => _events.Clear();

    /// <summary>evSend: a command on a channel, to every x-object receiving on it (and the special channels).</summary>
    public void EvSend(XObject source, int rxId, int command, XObject causer)
    {
        if (command == K.kCmdState)
            command = SourceState(source) != 0 ? K.kCmdOn : K.kCmdOff;
        else if (command == K.kCmdNotState)
            command = SourceState(source) != 0 ? K.kCmdOff : K.kCmdOn;
        switch (rxId)
        {
            case K.kChannelTextOver:
                if (command >= K.kCmdNumberic)
                    TextOver(command - K.kCmdNumberic);
                return;
            case K.kChannelLevelExitNormal:
                EndLevel(0);
                return;
            case K.kChannelLevelExitSecret:
                EndLevel(1);
                return;
            case KM.kChannelModernEndLevelCustom:
                // a modern map: the level ends and the numbered level of the episode is next
                if (IsModern && command >= K.kCmdNumberic)
                    LevelEndLevelCustom(command - K.kCmdNumberic);
                return;
            case K.kChannelSetTotalSecrets:
                if (command >= K.kCmdNumberic)
                    SecretsTotal = command - K.kCmdNumberic;
                return;
            case K.kChannelSecretFound:
                if (command >= K.kCmdNumberic)
                    TriggerSecret(command - K.kCmdNumberic);
                return;
        }
        if (rxId >= K.kChannelRemoteBomb0 && rxId <= K.kChannelRemoteBomb7)
        {
            foreach (var e in Sprites.ToArray())
                if (e.StatNum == K.kStatThing && (e.HitTag & K.kHitagFree) == 0 && e.HasX && e.RxID == rxId)
                    TrMessage(e, command, causer);
            return;
        }
        if (rxId == K.kChannelTeamAFlagCaptured || rxId == K.kChannelTeamBFlagCaptured)
        {
            foreach (var e in Sprites.ToArray())
                if (e.StatNum == K.kStatItem && (e.HitTag & K.kHitagFree) == 0 && e.HasX && e.RxID == rxId)
                    TrMessage(e, command, causer);
            return;
        }
        if (rxId <= 0)
            return;
        if (IsModern && ModernEvSend(source, rxId, command, causer))
            return;
        var oldLink = _linkSource;
        var oldSource = _evSource;
        _linkSource = source;
        if (_receivers.TryGetValue(rxId, out var list))
        {
            foreach (var e in list.ToArray())
            {
                if (e == source || !e.HasX || e.Removed)
                    continue;
                if (e is Sprite sp && (sp.HitTag & K.kHitagFree) != 0)
                    continue;
                _evSource = source;
                TrMessage(e, command, causer);
            }
        }
        _linkSource = oldLink;
        _evSource = oldSource;
    }

    // x-objects by receive channel, in level order (findfloat(world, rxID, n) order)
    readonly Dictionary<int, List<XObject>> _receivers = new();

    void IndexReceivers()
    {
        _receivers.Clear();
        foreach (var o in AllObjects())
            AddReceiver(o);
    }

    void AddReceiver(XObject o)
    {
        if (o.RxID <= 0)
            return;
        if (!_receivers.TryGetValue(o.RxID, out var l))
            _receivers[o.RxID] = l = new List<XObject>();
        int i = l.Count;
        while (i > 0 && l[i - 1].Edict > o.Edict)
            i--;
        l.Insert(i, o);
    }

    /// <summary>evProcess: deliver the events due by now.</summary>
    void EvProcess()
    {
        while (_events.Count > 0 && _events[0].Time <= FrameClock)
        {
            var ev = _events[0];
            _events.RemoveAt(0);
            FreeEvent(ev);
            if (ev.Target == null || ev.Target.Removed)
                continue;
            if (ev.Cmd == K.kCmdCallback)
                EvCallback(ev.Func, ev.Target);
            else
            {
                _evSource = ev.Target;      // a posted event names the object itself
                TrMessage(ev.Target, ev.Cmd, ev.Causer);
                _evSource = null;
            }
        }
    }
}
