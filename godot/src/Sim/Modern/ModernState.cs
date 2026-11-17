using System;
using System.Collections.Generic;

namespace Chosen.Sim.Modern;

/// <summary>An object a condition looks at (its serial in NBlood): a sprite, or a Build wall or sector by index
/// (with or without x-data).</summary>
public readonly struct ObjRef : IEquatable<ObjRef>
{
    public readonly int Type;       // XObject.ObjWall, ObjSprite, ObjSector; -1: none
    public readonly int Index;      // the Build wall or sector
    public readonly Sprite Spr;

    public ObjRef(Sprite e) { Type = XObject.ObjSprite; Index = -1; Spr = e; }
    public ObjRef(int type, int index) { Type = type; Index = index; Spr = null; }
    public static readonly ObjRef None = new(-1, -1);
    public bool IsNone => Type < 0;
    /// <summary>The number conditions compare objects by (a serial as NBlood makes one).</summary>
    public int Serial => Type switch
    {
        XObject.ObjSector => 300000 + Index, XObject.ObjWall => 400000 + Index, XObject.ObjSprite => 500000 + (Spr?.SprIndex ?? 0),
        _ => -1,
    };
    public bool Equals(ObjRef o) => Type == o.Type && Index == o.Index && Spr == o.Spr;
    public override bool Equals(object o) => o is ObjRef r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Type, Index, Spr);
}

/// <summary>A condition's state (what NBlood keeps in the x-sprite's target fields and sysData1): the object in its
/// focus (X), the one it first received (Y), two more slots it can save the focus in (Z, S), and for a tracking
/// condition (one with a busy time) the objects it checks each time its counter runs out.</summary>
public sealed class CondState
{
    public ObjRef X = ObjRef.None, Y = ObjRef.None, Z = ObjRef.None, S = ObjRef.None;
    public List<ObjRef> Tracked;
}

/// <summary>A debris sprite's mass and what goes with it (nnexts.cpp SPRITEMASS: made from its picture's size,
/// repeats, clipdist and surface; kept while those stay).</summary>
public sealed class SpriteMass
{
    public int SeqId = -1, Picnum = -1, XRepeat, YRepeat, ClipDist, Mass, AirVel, Fraction;
}

/// <summary>A patrolling dude's state beside its x-sprite's fields: the marker it came from (NBlood keeps its index in
/// targetX).</summary>
public sealed class PatrolState
{
    public Sprite PrevMarker;
}
