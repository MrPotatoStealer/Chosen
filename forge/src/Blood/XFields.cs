// Chosen Blood module (GPL v2): what each x-field of Blood's sectors, walls and sprites means (NBlood's db.cpp widths,
// triggers.cpp, actor.cpp, asound.cpp, sectorfx.cpp; xmapedit's names for commands, waves, keys, depths, respawn).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Blood.Types;
using TypeInfo = Chosen.Shared.Blood.Types.TypeInfo;

namespace Chosen.Forge.Blood;

/// <summary>How a field is edited: a number (with its meaning beside it), a check box, a list of named values, a
/// channel, a sound, a time in tenths of a second, a Build angle, a height, a sprite type, bits each a check box.</summary>
public enum FieldKind { Number, Flag, Choice, Channel, Sound, Time, Angle, Z, Type, Bits }

/// <summary>One field: its label, group, kind, valid range (the bits Blood stores it in), named values, help and a
/// meaning shown beside it as it is typed.</summary>
public sealed class FieldSpec
{
    public string Field = "", Label = "", Group = "", Tip = "";
    /// <summary>A data field's caption for its type (objtypes.ini: "Off->On", "Radius1"); null for the rest.</summary>
    public string Caption;
    public FieldKind Kind = FieldKind.Number;
    public int Min, Max;
    public (int value, string name)[] Choices = Array.Empty<(int, string)>();
    /// <summary>The value in words ("1.5 s", "south", "player 2"); null: nothing to add.</summary>
    public Func<int, string> Meaning;
    /// <summary>Type kind: the sprite types to choose from.</summary>
    public Func<TypeInfo, bool> Types;
    /// <summary>Only NBlood's modern maps read it.</summary>
    public bool Modern;

    public string Name(int v) => Choices.FirstOrDefault(c => c.value == v).name;

    /// <summary>What a value means, or why it is out of range.</summary>
    public string Describe(int v)
    {
        if (v < Min || v > Max)
            return $"out of range ({Min}..{Max})";
        if (Kind == FieldKind.Choice && Name(v) is { } n)
            return n;
        if (Kind == FieldKind.Bits)
            return v == 0 ? "" : string.Join(", ", Choices.Where(c => (v & c.value) != 0).Select(c => c.name));
        return Meaning?.Invoke(v) ?? "";
    }

    /// <summary>The help shown when the mouse rests on it: the tip, the range, modern-only.</summary>
    public string Help => (Tip.Length > 0 ? Tip + "\n" : "") + $"{Min}..{Max}" + (Modern ? " (NBlood's modern maps only)" : "");
}

public static class XFields
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- named values

    public static readonly (int, string)[] Commands =
    {
        (0, "Off"), (1, "On"), (2, "State (its own state)"), (3, "Toggle"), (4, "Not state"), (5, "Link (follow the sender)"),
        (6, "Lock"), (7, "Unlock"), (8, "Toggle lock"), (9, "Sector: stop at off"), (10, "Sector: stop at on"),
        (11, "Sector: stop next"), (13, "Sector: pause (modern)"), (14, "Sector: continue (modern)"),
        (15, "Set enemy flags (modern)"), (16, "Kill events (modern)"),
    };
    public static readonly (int, string)[] BusyWaves = { (0, "Sine"), (1, "Linear"), (2, "Slow off"), (3, "Slow on") };
    public static readonly (int, string)[] LightWaves =
    {
        (0, "None"), (1, "Square"), (2, "Saw"), (3, "Ramp up"), (4, "Ramp down"), (5, "Sine"), (6, "Flicker 1"), (7, "Flicker 2"),
        (8, "Flicker 3"), (9, "Flicker 4"), (10, "Strobe"), (11, "Search"),
    };
    public static readonly (int, string)[] Keys =
        { (0, "None"), (1, "Skull key"), (2, "Eye key"), (3, "Fire key"), (4, "Dagger key"), (5, "Spider key"), (6, "Moon key"), (7, "Key 7") };
    public static readonly (int, string)[] Depths =
        { (0, "None"), (1, "Tread"), (2, "Puddle"), (3, "Wade"), (4, "Pond"), (5, "Bath"), (6, "Jungle"), (7, "Swim") };
    public static readonly (int, string)[] Respawns = { (0, "Optional (game setting)"), (1, "Never"), (2, "Always"), (3, "Permanent") };
    // (a sector's damage type: a Damage sector's as stored, another's less one, 0 none: actor.cpp)
    public static readonly (int, string)[] Damages =
        { (0, "Fall"), (1, "Burn"), (2, "Bullet"), (3, "Explode"), (4, "Drown"), (5, "Spirit"), (6, "Tesla") };
    public static readonly (int, string)[] SectorDamages =
        { (0, "None"), (1, "Fall"), (2, "Burn"), (3, "Bullet"), (4, "Explode"), (5, "Drown"), (6, "Spirit"), (7, "Tesla") };
    public static readonly (int, string)[] Explosions =
    {
        (0, "Small (TNT stick)"), (1, "Standard (bundle, bombs)"), (2, "Large (barrel)"), (3, "Fireball"), (4, "Spray can"),
        (5, "Explosion 5"), (6, "Explosion 6"), (7, "Napalm"),
    };
    public static readonly (int, string)[] Skills =
        { (1, "Still Kicking"), (2, "Pink On The Inside"), (4, "Lightly Broiled"), (8, "Well Done"), (16, "Extra Crispy") };
    public static readonly (int, string)[] Palettes = Chosen.Shared.Blood.Files.Extractor.PluNames.Select((n, i) => (i, n)).ToArray();

    /// <summary>Channels Blood keeps for itself (eventq.h): below 100 the game's; the mapper's start at 100.</summary>
    public static string ReservedChannel(int c) => c switch
    {
        0 => "none",
        1 => "set total secrets",
        2 => "secret found",
        3 => "show text",
        4 => "end level",
        5 => "end level (secret exit)",
        6 => "end level to a level (modern)",
        7 => "level start",
        8 => "level start: bloodbath",
        9 => "level start: cooperative",
        10 => "level start: teams",
        15 => "a team A player died",
        16 => "a team B player died",
        17 => "level start: NBlood (modern)",
        18 => "level start: Raze (modern)",
        29 => "all players (modern)",
        >= 30 and <= 37 => $"player {c - 29} (modern)",
        50 => "the event's causer (modern)",
        60 or 61 => "modern map (its revision)",
        < 100 => "reserved by the game",
        _ => null,
    };

    static string Seconds(int tenths) => $"{(tenths / 10.0).ToString("0.#", Ci)} s";

    /// <summary>A Build angle in words: degrees and the compass (0 east, 512 south).</summary>
    public static string Compass(int a)
    {
        a &= 2047;
        string[] dirs = { "east", "south-east", "south", "south-west", "west", "north-west", "north", "north-east" };
        return $"{(a * 360.0 / 2048).ToString("0.#", Ci)}°, {dirs[((a + 128) / 256) % 8]}";
    }

    // ---------------------------------------------------------------- ranges from Blood's bit widths

    static readonly Dictionary<(Type, string), (int min, int max)> _ranges = new();

    /// <summary>A field's range as the map stores it: each field written with growing values and read back (an
    /// unsigned field of n bits keeps 0..2^n-1, a signed one -2^(n-1)..2^(n-1)-1).</summary>
    public static (int min, int max) Range(Type t, string field)
    {
        if (_ranges.TryGetValue((t, field), out var r))
            return r;
        var fi = t.GetField(field);
        var read = t.GetMethod("Read", BindingFlags.Public | BindingFlags.Static);
        var write = t.GetMethod("Write");
        int size = (int)t.GetProperty("Size").GetValue(Activator.CreateInstance(t));
        int Back(int v)
        {
            var x = Activator.CreateInstance(t);
            fi.SetValue(x, v);
            var buf = new byte[size];
            write.Invoke(x, new object[] { buf, 0 });
            return (int)fi.GetValue(read.Invoke(null, new object[] { buf, 0, size }));
        }
        int bits = 0;
        bool signed = false;
        for (int k = 0; k < 31; k++)
        {
            int got = Back(1 << k);
            if (got == 1 << k)
                bits = k + 1;
            else
            {
                signed = got < 0;
                if (signed)
                    bits = k + 1;
                break;
            }
        }
        if (bits >= 31)
            r = (int.MinValue, int.MaxValue);
        else
            r = signed ? (-(1 << (bits - 1)), (1 << (bits - 1)) - 1) : (0, (1 << bits) - 1);
        _ranges[(t, field)] = r;
        return r;
    }

    // ---------------------------------------------------------------- the tables

    static FieldSpec F(Type t, string field, string label, string group, FieldKind kind = FieldKind.Number, string tip = "",
        (int, string)[] choices = null, Func<int, string> meaning = null)
    {
        var (min, max) = Range(t, field);
        var s = new FieldSpec { Field = field, Label = label, Group = group, Kind = kind, Tip = tip, Min = min, Max = max,
            Choices = choices ?? Array.Empty<(int, string)>(), Meaning = meaning };
        if (kind == FieldKind.Time)
            s.Meaning ??= Seconds;
        if (kind == FieldKind.Angle)
            s.Meaning ??= Compass;
        if (kind == FieldKind.Channel)
            s.Meaning ??= ReservedChannel;
        return s;
    }

    const string GTrig = "Triggers", GBy = "Triggered by", GTime = "Timing", GMotion = "Motion", GLight = "Lighting",
        GPan = "Panning", GFx = "Effects", GData = "Data", GPlace = "Where it appears", GDude = "Enemy", GState = "Set by the game as it runs";

    static IEnumerable<FieldSpec> TriggerFields(Type t, bool sector)
    {
        yield return F(t, "RxID", "RX id", GTrig, FieldKind.Channel, "the channel it listens to (100 and up are yours; below: the game's)");
        yield return F(t, "TxID", "TX id", GTrig, FieldKind.Channel, "the channel it sends its command on when it turns on or off");
        yield return F(t, "Command", "Command", GTrig, FieldKind.Choice, "what it sends on TX", Commands,
            v => v >= 64 ? $"number {v - 64} (modern)" : null);
        yield return F(t, "State", "On", GTrig, FieldKind.Flag, "its state at the start (on: a door starts open)");
        yield return F(t, "RestState", "Rests on", GTrig, FieldKind.Flag, "the state a wait time takes it back to");
        yield return F(t, "TriggerOn", "Send when on", GTrig, FieldKind.Flag, "sends its command on TX when it turns on");
        yield return F(t, "TriggerOff", "Send when off", GTrig, FieldKind.Flag, "sends its command on TX when it turns off");
        yield return F(t, "Decoupled", "Decoupled", GTrig, FieldKind.Flag, "a trigger sends on TX only: it doesn't operate itself");
        yield return F(t, "TriggerOnce", "Once", GTrig, FieldKind.Flag, "triggers once, then never again");
        yield return F(t, "Locked", "Locked", GTrig, FieldKind.Flag, "ignores everything but unlock");
        yield return F(t, "Key", "Key", GTrig, FieldKind.Choice, "the key the player needs to use it", Keys);
        yield return F(t, "DudeLockout", "Not enemies", GTrig, FieldKind.Flag, "enemies can't trigger it");
        yield return F(t, sector ? "Interruptable" : t == typeof(XSprite) ? "Interrutable" : "Interruptable", "Interruptable", GTrig, FieldKind.Flag,
            "a command reverses it while it is busy");
    }

    public static readonly FieldSpec[] Sector = BuildSector();
    public static readonly FieldSpec[] Wall = BuildWall();
    public static readonly FieldSpec[] Sprite = BuildSprite();

    static FieldSpec[] BuildSector()
    {
        var t = typeof(XSector);
        var l = TriggerFields(t, true).ToList();
        l.Add(F(t, "Enter", "Entered", GBy, FieldKind.Flag, "the player walking in"));
        l.Add(F(t, "Exit", "Left", GBy, FieldKind.Flag, "the player walking out"));
        l.Add(F(t, "Push", "Used", GBy, FieldKind.Flag, "the player pressing use (Space) in it or on its floor"));
        l.Add(F(t, "Wallpush", "Wall used", GBy, FieldKind.Flag, "the player using one of its walls from outside"));
        l.Add(F(t, "Vector", "Shot", GBy, FieldKind.Flag, "a hitscan weapon hitting it"));
        l.Add(F(t, "Data", "Data", GData, tip: "what it means depends on the sector type"));
        l.Add(F(t, "BusyTimeA", "Busy time (off-on)", GTime, FieldKind.Time, "how long going on takes, tenths of a second"));
        l.Add(F(t, "WaitTimeA", "Wait time (off-on)", GTime, FieldKind.Time, "on for this long, then back off (0: stays)"));
        l.Add(F(t, "BusyWaveA", "Wave (off-on)", GTime, FieldKind.Choice, "how the motion eases", BusyWaves));
        l.Add(F(t, "ReTriggerA", "Retrigger (off-on)", GTime, FieldKind.Flag, "turns back off by itself when it gets on (wait time)"));
        l.Add(F(t, "BusyTimeB", "Busy time (on-off)", GTime, FieldKind.Time, "how long going off takes (0: the same as on)"));
        l.Add(F(t, "WaitTimeB", "Wait time (on-off)", GTime, FieldKind.Time, "off for this long, then back on (0: stays)"));
        l.Add(F(t, "BusyWaveB", "Wave (on-off)", GTime, FieldKind.Choice, "how the motion eases", BusyWaves));
        l.Add(F(t, "ReTriggerB", "Retrigger (on-off)", GTime, FieldKind.Flag, "turns back on by itself when it gets off"));
        l.Add(F(t, "StopOn", "Stop at on", GTime, FieldKind.Flag, "a path or step sector stops when it reaches on"));
        l.Add(F(t, "StopOff", "Stop at off", GTime, FieldKind.Flag, "stops when it reaches off"));
        l.Add(F(t, "OffFloorZ", "Floor off", GMotion, FieldKind.Z, "a lift's or door's floor height when off"));
        l.Add(F(t, "OnFloorZ", "Floor on", GMotion, FieldKind.Z, "its floor height when on"));
        l.Add(F(t, "OffCeilZ", "Ceiling off", GMotion, FieldKind.Z, "its ceiling height when off"));
        l.Add(F(t, "OnCeilZ", "Ceiling on", GMotion, FieldKind.Z, "its ceiling height when on"));
        l.Add(F(t, "Crush", "Crush", GMotion, FieldKind.Flag, "squashes what is in its way instead of stopping"));
        l.Add(F(t, "Marker0", "Marker 1", GMotion, tip: "the sprite marking where it turns or slides from (set with the type)",
            meaning: v => v == 65535 ? "none" : $"sprite {v}"));
        l.Add(F(t, "Marker1", "Marker 2", GMotion, tip: "the sprite marking where it slides to", meaning: v => v == 65535 ? "none" : $"sprite {v}"));
        l.Add(F(t, "Wave", "Light wave", GLight, FieldKind.Choice, "the shape of the light's change", LightWaves));
        l.Add(F(t, "Amplitude", "Light amplitude", GLight, tip: "how far the shade goes: negative brighter, positive darker",
            meaning: v => v < 0 ? "brighter" : v > 0 ? "darker" : null));
        l.Add(F(t, "Freq", "Light frequency", GLight, tip: "how fast it changes"));
        l.Add(F(t, "Phase", "Light phase", GLight, tip: "where in its wave it starts (sectors flickering out of step)"));
        l.Add(F(t, "ShadeAlways", "Light always", GLight, FieldKind.Flag, "changes all the time, not only while busy"));
        l.Add(F(t, "ShadeFloor", "Light floor", GLight, FieldKind.Flag, "the floor's shade changes"));
        l.Add(F(t, "ShadeCeiling", "Light ceiling", GLight, FieldKind.Flag, "the ceiling's shade changes"));
        l.Add(F(t, "ShadeWalls", "Light walls", GLight, FieldKind.Flag, "the walls' shades change"));
        l.Add(F(t, "Color", "Coloured", GLight, FieldKind.Flag, "the palettes below while lit"));
        l.Add(F(t, "Floorpal", "Floor palette", GLight, FieldKind.Choice, "the floor's palette while lit (Coloured)", Palettes));
        l.Add(F(t, "Ceilpal", "Ceiling palette", GLight, FieldKind.Choice, "the ceiling's palette while lit (Coloured)", Palettes));
        l.Add(F(t, "PanVel", "Pan speed", GPan, tip: "how fast the floor or ceiling scrolls"));
        l.Add(F(t, "PanAngle", "Pan angle", GPan, FieldKind.Angle, "the way it scrolls"));
        l.Add(F(t, "PanAlways", "Pan always", GPan, FieldKind.Flag, "scrolls all the time, not only while on"));
        l.Add(F(t, "PanFloor", "Pan floor", GPan, FieldKind.Flag, "the floor scrolls"));
        l.Add(F(t, "PanCeiling", "Pan ceiling", GPan, FieldKind.Flag, "the ceiling scrolls"));
        l.Add(F(t, "Drag", "Drag", GPan, FieldKind.Flag, "moves what stands on it with the floor (a conveyor)"));
        l.Add(F(t, "Underwater", "Underwater", GFx, FieldKind.Flag, "the player swims in it"));
        l.Add(F(t, "Depth", "Depth", GFx, FieldKind.Choice, "how deep its water or goo is (feet in it, wading, swimming)", Depths));
        l.Add(F(t, "DamageType", "Damage type", GFx, FieldKind.Choice, "hurts who is in it (a Damage sector: its type as listed, none means Fall)", SectorDamages));
        l.Add(F(t, "WindVel", "Wind speed", GFx, tip: "pushes the player"));
        l.Add(F(t, "WindAng", "Wind angle", GFx, FieldKind.Angle, "the way the wind blows"));
        l.Add(F(t, "WindAlways", "Wind always", GFx, FieldKind.Flag, "blows all the time, not only while on"));
        l.Add(F(t, "BobZRange", "Bob height", GFx, tip: "floats up and down this far (Build z / 256)"));
        l.Add(F(t, "BobSpeed", "Bob speed", GFx, tip: "how fast it bobs"));
        l.Add(F(t, "BobTheta", "Bob phase", GFx, FieldKind.Angle, "where in its bob it starts"));
        l.Add(F(t, "BobAlways", "Bob always", GFx, FieldKind.Flag, "bobs all the time, not only while on"));
        l.Add(F(t, "BobFloor", "Bob floor", GFx, FieldKind.Flag, "the floor bobs"));
        l.Add(F(t, "BobCeiling", "Bob ceiling", GFx, FieldKind.Flag, "the ceiling bobs"));
        l.Add(F(t, "BobRotate", "Bob rotates", GFx, FieldKind.Flag, "sprites on it turn as it bobs"));
        AddState(l, t);
        return l.ToArray();
    }

    static FieldSpec[] BuildWall()
    {
        var t = typeof(XWall);
        var l = TriggerFields(t, false).ToList();
        l.Add(F(t, "TriggerPush", "Used", GBy, FieldKind.Flag, "the player pressing use (Space) on it"));
        l.Add(F(t, "TriggerVector", "Shot", GBy, FieldKind.Flag, "a hitscan weapon hitting it"));
        l.Add(F(t, "TriggerTouch", "Touched", GBy, FieldKind.Flag, "the player bumping into it"));
        l.Add(F(t, "Data", "Data", GData, tip: "what it means depends on the wall type"));
        l.Add(F(t, "BusyTime", "Busy time", GTime, FieldKind.Time, "how long going on or off takes, tenths of a second"));
        l.Add(F(t, "WaitTime", "Wait time", GTime, FieldKind.Time, "on (or off) for this long, then back to its rest state (0: stays)"));
        l.Add(F(t, "PanXVel", "Pan speed across", GPan, tip: "how fast its picture scrolls sideways (negative: the other way)"));
        l.Add(F(t, "PanYVel", "Pan speed up", GPan, tip: "how fast its picture scrolls up and down"));
        l.Add(F(t, "PanAlways", "Pan always", GPan, FieldKind.Flag, "scrolls all the time, not only while on"));
        AddState(l, t);
        return l.ToArray();
    }

    static FieldSpec[] BuildSprite()
    {
        var t = typeof(XSprite);
        var l = new List<FieldSpec>
        {
            F(t, "Data1", "Data 1", GData), F(t, "Data2", "Data 2", GData), F(t, "Data3", "Data 3", GData), F(t, "Data4", "Data 4", GData),
        };
        l.AddRange(TriggerFields(t, false));
        l.Add(F(t, "LockMsg", "Locked message", GTrig, tip: "the map's message shown when the player tries it locked"));
        l.Add(F(t, "Push", "Used", GBy, FieldKind.Flag, "the player pressing use (Space) on it"));
        l.Add(F(t, "Vector", "Shot", GBy, FieldKind.Flag, "a hitscan weapon hitting it"));
        l.Add(F(t, "Impact", "Hit", GBy, FieldKind.Flag, "a missile or explosion hitting it"));
        l.Add(F(t, "Pickup", "Picked up", GBy, FieldKind.Flag, "the player picking it up"));
        l.Add(F(t, "Touch", "Touched", GBy, FieldKind.Flag, "the player bumping into it"));
        l.Add(F(t, "Sight", "Seen", GBy, FieldKind.Flag, "the player seeing it"));
        l.Add(F(t, "Proximity", "Near", GBy, FieldKind.Flag, "the player coming near"));
        l.Add(F(t, "BusyTime", "Busy time", GTime, FieldKind.Time, "a generator's time between repeats, a switch's busy time; tenths of a second"));
        l.Add(F(t, "WaitTime", "Wait time", GTime, FieldKind.Time, "on (or off) for this long, then back to its rest state (0: stays)"));
        l.Add(F(t, "Wave", "Wave", GTime, FieldKind.Choice, "how a busy motion eases", BusyWaves));
        l.Add(F(t, "LSkill", "Not on", GPlace, FieldKind.Bits, "the difficulties it isn't in", Skills));
        l.Add(F(t, "LS", "Not in single player", GPlace, FieldKind.Flag));
        l.Add(F(t, "LB", "Not in Bloodbath", GPlace, FieldKind.Flag));
        l.Add(F(t, "LC", "Not in cooperative", GPlace, FieldKind.Flag));
        l.Add(F(t, "LT", "Not in teams", GPlace, FieldKind.Flag));
        l.Add(F(t, "Respawn", "Respawn", GPlace, FieldKind.Choice, "an item's or enemy's respawning", Respawns));
        l.Add(F(t, "DropMsg", "Drops", GPlace, FieldKind.Type, "the item it leaves when killed or broken (0: nothing)"));
        l[^1].Types = ti => ti.Type >= 40 && ti.Type < 152;
        l[^1].Meaning = v => v == 0 ? "nothing" : BloodNames.Sprite(v);
        l.Add(F(t, "DudeDeaf", "Deaf", GDude, FieldKind.Flag, "doesn't hear the player"));
        l.Add(F(t, "DudeAmbush", "Ambush", GDude, FieldKind.Flag, "waits still until it sees the player"));
        l.Add(F(t, "DudeGuard", "Still", GDude, FieldKind.Flag, "doesn't move from its place"));
        l.Add(F(t, "DudeFlag4", "Patrol (modern)", GDude, FieldKind.Flag, "walks about (NBlood's modern maps)"));
        AddState(l, t);
        return l.ToArray();
    }

    /// <summary>The fields the game sets as it runs (and the unused ones): last, after the rest, every field not
    /// listed above (none lost).</summary>
    static void AddState(List<FieldSpec> l, Type t)
    {
        var known = new HashSet<string>(l.Select(s => s.Field)) { "Reference" };
        foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.FieldType == typeof(int)))
            if (known.Add(fi.Name))
                l.Add(F(t, fi.Name, fi.Name, GState, tip: StateTip(fi.Name)));
    }

    static string StateTip(string f) => f switch
    {
        "Busy" => "how far it is between off (0) and on (65536)",
        "IsTriggered" => "a once trigger that has fired",
        "Shade" => "the light's current change",
        "Health" => "its health as the game counts it (an enemy's own: Data 4)",
        _ when f.StartsWith("Unused", StringComparison.Ordinal) || f == "Reserved" => "unused by Blood",
        _ when f.Contains("Frac", StringComparison.Ordinal) => "a panning's fraction",
        _ => "kept as it is",
    };

    // ---------------------------------------------------------------- data fields by type

    /// <summary>A sprite's data field n (1..4) for its type: the caption's meaning, a sound, an enemy type, gibs...</summary>
    public static FieldSpec SpriteData(int type, int n)
    {
        var baseSpec = Sprite[n - 1];
        var info = BloodNames.Types.Get(ObjKind.Sprite, type);
        string cap = info?.Data[n - 1];
        var s = new FieldSpec { Field = baseSpec.Field, Label = baseSpec.Label, Group = GData, Min = baseSpec.Min, Max = baseSpec.Max,
            Modern = info != null && (info.Modern || info.DataModern[n - 1]) };
        // a custom dude (and its spawner): the type table has no captions for what its data fields are in a modern map
        if (type is ModernTypes.CustomDude or ModernTypes.CustomDudeSpawn && n <= 3)
        {
            s.Modern = true;
            if (n == 1)
            {
                s.Caption = "Descriptor";
                s.Tip = "the dude's descriptor: the mod's CDUDnnnn.CDU file of this number (what it looks like, its weapons, sounds, health). "
                    + "With no such file it is an old (version 1) dude and this is its weapon: a vector (1-22), a missile (300-317), "
                    + "a thrown thing (400-435), a summoned dude (200-254) or an explosion of itself (459-466)";
                s.Meaning = v => v == 0 ? null : ModernTypes.CustomDudeName(v) ?? "no descriptor in the map's folder: a version 1 dude's weapon";
            }
            else if (n == 2)
            {
                s.Caption = "Flags";
                s.Tip = "a descriptor's dude: 1 awake from the start (never sleeps), 2 crouching, 4 no last stand, 8 cruel (goes for innocents), "
                    + "16 innocent; added up. A version 1 dude: its first sequence (0: 11520)";
                s.Meaning = ModernTypes.CustomDudeFlags;
            }
            else
            {
                s.Caption = "Sound";
                s.Tip = "a version 1 dude's first sound (a descriptor names its own); 0: the cultist's";
            }
            return s;
        }
        if (cap == null)
            return s;
        s.Tip = cap;
        s.Caption = cap;
        if (SoundLibrary.Fields(type).Contains(n))
        {
            s.Kind = FieldKind.Sound;
            s.Tip = cap + ": the sound it plays";
            return s;
        }
        switch (cap)
        {
            case "Enemy":
                s.Kind = FieldKind.Type;
                s.Types = ti => ti.Type >= 200 && ti.Type < 255;
                s.Meaning = v => v == 0 ? "none" : BloodNames.Sprite(v);
                s.Tip = "an enemy it spawns (each of the four picked at random)";
                break;
            case "Item":
                s.Kind = FieldKind.Type;
                s.Types = ti => ti.Type >= 40 && ti.Type < 152;
                s.Meaning = v => v == 0 ? "none" : BloodNames.Sprite(v);
                break;
            case "Gib1" or "Gib2" or "Gib3":
                s.Tip = "a gib type plus one (0: none), thrown when it breaks";
                s.Meaning = v => v == 0 ? "none" : $"gib type {v - 1}";
                break;
            case "Player":
                s.Tip = "which player (0 the first)";
                s.Meaning = v => $"player {v + 1}";
                break;
            case "Radius1":
                s.Tip = "full volume within this distance (game units); must be less than Radius 2";
                s.Meaning = v => $"{v} game units";
                break;
            case "Radius2":
                s.Tip = "silent beyond this distance (game units); the volume falls off from Radius 1";
                s.Meaning = v => $"{v} game units";
                break;
            case "Volume":
                if (type == 710)
                {
                    s.Tip = "0..127: its loudness at Radius 1 (more counts as 127)";
                    s.Meaning = v => v > 127 ? "127 (the most)" : $"{v * 100 / 127}%";
                }
                else
                    s.Tip = "its loudness (modern maps; 0: the sound's own)";
                break;
            case "Pitch":
                s.Tip = "its pitch (modern maps; 0: the sound's own)";
                break;
            case "Rand Mul":
                s.Tip = "a random extra time, up to this many tenths of a second, added to each repeat";
                s.Meaning = v => $"up to {Seconds(v)} more";
                break;
            case "Explosion":
                s.Kind = FieldKind.Choice;
                s.Choices = Explosions;
                break;
            case "Palette":
                s.Kind = FieldKind.Choice;
                s.Choices = Palettes;
                s.Tip = "the view's palette under it";
                break;
            case "Angle":
                s.Kind = FieldKind.Angle;
                s.Meaning = Compass;
                break;
            case "Health":
                s.Tip = "its health (0: the enemy's own)";
                s.Meaning = v => v == 0 ? "its own" : null;
                break;
            case "Upper ID" or "Lower ID":
                s.Tip = "pairs it with the link of the same id above or below (rooms over rooms)";
                break;
            case "Id":
                s.Tip = "this path marker's id (a path sector or the marker before names it)";
                break;
            case "Next Id":
                s.Tip = "the id of the marker after it";
                break;
            case "Current":
                s.Tip = "the count it starts at";
                break;
            case "Goal":
                s.Tip = "it is on when the count reaches this";
                break;
            case "Max":
                s.Tip = "the count goes round after this";
                break;
            case "Strength":
                s.Tip = "how hard the earth shakes";
                break;
            case "Amount" or "Ammo":
                s.Tip = cap.ToLowerInvariant() + " it gives (0: the usual)";
                break;
        }
        return s;
    }

    /// <summary>A sector's or wall's data field for its type (Damage, Path ID, Sprite type, Gib ID, Stack ID).</summary>
    public static FieldSpec XData(ObjKind kind, int type)
    {
        var baseSpec = (kind == ObjKind.Sector ? Sector : Wall).First(f => f.Field == "Data");
        var info = BloodNames.Types.Get(kind, type);
        string cap = info?.Data[0];
        var s = new FieldSpec { Field = "Data", Label = "Data", Group = GData, Min = baseSpec.Min, Max = baseSpec.Max, Tip = cap ?? baseSpec.Tip,
            Modern = info != null && (info.Modern || info.DataModern[0]) };
        switch (cap)
        {
            case "Damage":
                s.Tip = "how hard it hurts, 0..1000 (0: 1000, the most)";
                s.Meaning = v => v == 0 || v >= 1000 ? "the most" : $"{v / 10}%";
                break;
            case "Path ID":
                s.Tip = "the id of the path marker it starts at";
                break;
            case "Sprite type":
                s.Kind = FieldKind.Type;
                s.Types = _ => true;
                s.Meaning = v => BloodNames.Sprite(v);
                s.Tip = "it counts the sprites of this type in it";
                break;
        }
        return s;
    }

    /// <summary>The group headings in their order.</summary>
    public static readonly string[] Groups = { GData, GTrig, GBy, GTime, GMotion, GLight, GPan, GFx, GPlace, GDude, GState };
}
