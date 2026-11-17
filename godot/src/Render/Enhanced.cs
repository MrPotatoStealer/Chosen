using Godot;

namespace Chosen.Render;

/// <summary>
/// The enhanced features (Options > Enhanced), none of them Blood's own and all off by default: see-through water, and
/// the fog (EnhancedFog). Each is a saved cvar; the renderers read them and follow their changes.
/// </summary>
public static class Enhanced
{
    /// <summary>Water surfaces with a link to the room under (or over) them drawn as portals: the other room seen
    /// through, the surface blended over it (PortalRenderer, LiveRenderer's water flats, BuildShading.SetWater).</summary>
    public static readonly Core.Cvar WaterPortals = Core.Cvars.Register("r_waterportals", "1",
        "1: water surfaces linked to a room under (or over) them are see-through, that room drawn through them", saved: true);

    /// <summary>How opaque a see-through water surface is (the global water_alpha).</summary>
    public static readonly Core.Cvar WaterAlpha = Core.Cvars.Register("r_wateralpha", "0.6",
        "how opaque see-through water surfaces are (r_waterportals 1): 0 clear .. 1 as Blood draws them", saved: true);

    static Enhanced()
    {
        WaterAlpha.Changed += ApplyWaterAlpha;
        ApplyWaterAlpha();
    }

    /// <summary>Registers the cvars (the menu and console find them) and sends their shader values.</summary>
    public static void Init() => WaterFx.Init();

    static void ApplyWaterAlpha() =>
        RenderingServer.GlobalShaderParameterSet("water_alpha", Mathf.Clamp(WaterAlpha.Float, 0f, 1f));

    /// <summary>A link of water (its upper side on the surface, its lower side the underwater room's ceiling).</summary>
    public static bool IsWaterLink(int linkType) => linkType == Data.K.kMarkerUpWater || linkType == Data.K.kMarkerLowWater;
}
