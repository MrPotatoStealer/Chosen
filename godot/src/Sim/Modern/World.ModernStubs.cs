using System.Collections.Generic;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>
/// The dude target changer (NBlood's nnexts.cpp aiFight...; type 33: dudes made to fight each other). Not ported yet:
/// no level of the installed add-ons uses it; a map that does is told so in the console.
/// </summary>
public sealed partial class World
{
    void UseTargetChanger(Sprite src, Sprite e) => ModernMissing("dude target changers (type 33)");

    void AiFightActivateDudes(int rx)
    {
        foreach (var o in Receivers(rx))
            if (o is Sprite d && IsDudeSprite(d) && d.AiState != null && d.AiState.Type == AiState.GenIdle)
                AiInitSprite(d);
    }

    bool AiFightGetDudesForBattle(Sprite src)
    {
        foreach (var o in Receivers(src.TxID))
            if (o is Sprite d && IsDudeSprite(d) && d.XHealth > 0)
                return true;
        foreach (int rx in RedirectedChannels(src))
            foreach (var o in Receivers(rx))
                if (o is Sprite d && IsDudeSprite(d) && d.XHealth > 0)
                    return true;
        return false;
    }

    void AiFightFreeTargets(Sprite e)
    {
        foreach (var d in new List<Sprite>(StatList(K.kStatDude)))
            if (IsDudeSprite(d) && d.HasX && d.XTarget == e)
                AiSetTargetXYZ(d, d.BX, d.BY, d.BZ);
    }

    void AiFightFreeAllTargets(Sprite src)
    {
        if (src.TxID <= 0)
            return;
        foreach (var o in Receivers(src.TxID).ToArray())
            if (o is Sprite d && d.HasX)
                AiFightFreeTargets(d);
    }

    bool AiFightDudeIsAffected(Sprite dude)
    {
        if (dude.RxID <= 0 || dude.XLocked == 1)
            return false;
        foreach (var tc in StatList(KM.kStatModernDudeTargetChanger))
        {
            if (!tc.HasX || tc.TxID <= 0 || tc.XState != 1)
                continue;
            foreach (var o in Receivers(tc.TxID))
                if (o == dude)
                    return true;
        }
        return false;
    }
}
