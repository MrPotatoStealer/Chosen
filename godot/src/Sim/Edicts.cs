using System.Collections.Generic;

namespace Chosen.Sim;

/// <summary>
/// DarkPlaces 2014's entity slots (PRVM_ED_Alloc / PRVM_ED_CanAlloc), kept so the port goes through its entities in
/// the order Blood3D's QuakeC does: findfloat loops run in slot order, and a new entity takes the lowest free slot it
/// may reuse, else a new one at the end. DarkPlaces decides by real time; here a frame is 1/30 s:
/// a slot is never reused within 0.1 s of being freed (prvm_reuseedicts_neverinsameframe), one freed in the first 2 s
/// of the level is then free at once (prvm_reuseedicts_startuptime), any other after 1 s; while the map spawns, any
/// free slot is (prvm_reuseedicts_always_allow). DarkPlaces itself varies from run to run where a reuse falls near
/// one of those limits.
/// </summary>
public sealed class Edicts
{
    public const int ReservedSlots = 2;           // the world and the one client (svs.maxclients)
    public const int NotReusedFrames = 3;         // 0.1 s
    public const int ReusedAfterFrames = 30;      // 1 s
    public int StartupFrames = 45;                // the first 2 s, less the level's loading time before frame 0
    public bool Loading;                          // the map's entities are spawning

    readonly List<bool> _used = new();
    readonly List<int> _freedAt = new();
    int _firstFree = ReservedSlots;               // no free slot below this one

    public int Count => _used.Count;

    public void Reset()
    {
        _used.Clear();
        _freedAt.Clear();
        for (int i = 0; i < ReservedSlots; i++)
        {
            _used.Add(true);
            _freedAt.Add(0);
        }
        _firstFree = ReservedSlots;
        Loading = false;
    }

    /// <summary>A slot for an entity spawned at this frame.</summary>
    public int Alloc(int frame)
    {
        for (int i = _firstFree; i < _used.Count; i++)
        {
            if (_used[i] || !CanReuse(i, frame))
                continue;
            _used[i] = true;
            if (i == _firstFree)
                AdvanceFirstFree();
            return i;
        }
        _used.Add(true);
        _freedAt.Add(0);
        if (_firstFree == _used.Count - 1)
            _firstFree = _used.Count;
        return _used.Count - 1;
    }

    bool CanReuse(int i, int frame)
    {
        if (Loading)
            return true;
        int since = frame - _freedAt[i];
        if (since <= NotReusedFrames)
            return false;
        return _freedAt[i] < StartupFrames || since > ReusedAfterFrames;
    }

    public void Free(int slot, int frame)
    {
        if (slot < ReservedSlots || slot >= _used.Count || !_used[slot])
            return;
        _used[slot] = false;
        _freedAt[slot] = frame;
        if (slot < _firstFree)
            _firstFree = slot;
    }

    void AdvanceFirstFree()
    {
        while (_firstFree < _used.Count && _used[_firstFree])
            _firstFree++;
    }
}
