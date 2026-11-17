namespace Chosen.Sim;

/// <summary>
/// Stand-ins for the parts of Blood3D's QuakeC server not ported yet, and small shared helpers. Each names the
/// QuakeC function it replaces.
/// </summary>
public sealed partial class World
{
    // player.qc
    public static void PlayerResetInertia(Player p)
    {
        p.ZView = p.BZ - p.PostureInfo(Player.PO_eyeAboveZ);
        p.ZWeapon = p.BZ - p.PostureInfo(Player.PO_weaponAboveZ);
    }

    // sfx.qc
    /// <summary>sfxKill3DSound(e, channel, sound id): stop the object's sounds on a channel (-1: any) or of an id.</summary>
    public void Kill3DSound(XObject e, int channel = -1, int soundId = -1) => SoundKilled?.Invoke(e, channel, soundId);
    public event System.Action<XObject, int, int> SoundKilled;
    public void SoundLocal(Player p, int soundId) => Sound3D(p, soundId, -2);   // sndStartSample (heard everywhere)
}
