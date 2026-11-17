using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Godot;

namespace Chosen.Sim;

/// <summary>Marks a field the map entity's key of the same name (or the given one) sets, case-insensitively.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class MapKeyAttribute : Attribute
{
    public readonly string Name;
    public MapKeyAttribute(string name = null) => Name = name;
}

/// <summary>
/// What Blood's sprites, sectors and walls share: the x-object data (XSPRITE/XSECTOR/XWALL) that the trigger system
/// works on: channels, state, busy, commands and the data fields. Blood3D's QuakeC kept these on every entity.
/// </summary>
public abstract class XObject
{
    public const int ObjWall = 0, ObjSprite = 3, ObjSector = 6;
    public abstract int ObjType { get; }

    public int Index;
    public string ClassName = "";
    public Dictionary<string, string> Keys;   // the map entity's keys
    public bool Removed;
    public int EntityIndex = -1;
    public int Edict = -1;                 // DarkPlaces' entity slot: the order QuakeC goes through entities (Edicts)              // its entity in the .map (brush entities: the drawn node)

    [MapKey("stype")] public int Type;        // Blood type (sprites: from the classname)
    [MapKey] public string TargetName = "", Target = "";

    // x-data
    public bool HasX = true;
    [MapKey] public int XState;
    public int XBusy;                         // 0..65536: the position between off and on
    [MapKey] public int TxID, RxID;
    [MapKey] public int XCommand;
    public int TriggerOn = 1, TriggerOff = 1; // sendOn / sendOff
    [MapKey] public int BusyTime, WaitTime, RestState, Interruptable, DropMsg, Decoupled, TriggerOnce;
    public int IsTriggered;
    [MapKey] public int XKey, XWave, XPush, XVector, XImpact, XPickup, XTouch, XSight, XProximity;
    [MapKey] public int DudeLockout, XLocked, XRespawn, LockMsg;
    [MapKey] public int Data1, Data2, Data3, Data4;
    [MapKey] public string Message = "";
    public XObject SMarker0;                  // what "target" names (teleport destination, sound sector's sector)

    /// <summary>Sets the [MapKey] fields from the map entity's keys.</summary>
    public void ReadKeys(Dictionary<string, string> keys)
    {
        Keys = keys;
        var fields = KeyFields(GetType());
        foreach (var kv in keys)
        {
            if (!fields.TryGetValue(kv.Key, out var f))
                continue;
            var t = f.FieldType;
            var v = kv.Value;
            if (t == typeof(int))
                f.SetValue(this, (int)Math.Round(ParseD(v), MidpointRounding.AwayFromZero));
            else if (t == typeof(double))
                f.SetValue(this, ParseD(v));
            else if (t == typeof(string))
                f.SetValue(this, v);
            else if (t == typeof(Vector3))
                f.SetValue(this, ParseVec(v));
        }
        if (keys.TryGetValue("sendOn", out var on))
            TriggerOn = on == "" ? 1 : (int)ParseD(on);
        if (keys.TryGetValue("sendOff", out var off))
            TriggerOff = off == "" ? 1 : (int)ParseD(off);
    }

    static readonly Dictionary<Type, Dictionary<string, FieldInfo>> _keyFields = new();

    static Dictionary<string, FieldInfo> KeyFields(Type type)
    {
        lock (_keyFields)
        {
            if (_keyFields.TryGetValue(type, out var d))
                return d;
            d = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var a = f.GetCustomAttribute<MapKeyAttribute>();
                if (a != null)
                    d[a.Name ?? f.Name] = f;
            }
            _keyFields[type] = d;
            return d;
        }
    }

    public static double ParseD(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static Vector3 ParseVec(string s)
    {
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length < 3 ? Vector3.Zero : new Vector3((float)ParseD(p[0]), (float)ParseD(p[1]), (float)ParseD(p[2]));
    }

    public static Vector4 ParseVec4(string s)
    {
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length < 4 ? Vector4.Zero
            : new Vector4((float)ParseD(p[0]), (float)ParseD(p[1]), (float)ParseD(p[2]), (float)ParseD(p[3]));
    }

    public override string ToString() => $"{ClassName}#{Index}" + (TargetName != "" ? $"({TargetName})" : "");
}
