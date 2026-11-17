using System.Collections.Generic;

namespace Chosen.Map;

/// <summary>The level's settings as key/value pairs: the worldspawn record LevelLoader makes for a Build level
/// (bloodVisibility, _weather; read by the lights, fog, weather and atmosphere).</summary>
public sealed class MapEntity
{
    public readonly Dictionary<string, string> Keys = new();

    public string ClassName => Get("classname");
    public string Get(string key, string fallback = "") => Keys.TryGetValue(key, out var v) ? v : fallback;
}
