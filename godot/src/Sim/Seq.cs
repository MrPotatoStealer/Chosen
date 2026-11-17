using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>
/// SEQ animation instances on sprites (seq.cpp, via Blood3D's server/seq.qc): a sequence steps its frames every
/// ticksPerFrame tics; each frame sets the sprite's tile, repeats, shade, palette and the cstat/hitag bits it carries.
/// </summary>
public static class Seq
{
    /// <summary>UpdateSprite: apply a frame to the sprite.</summary>
    public static void UpdateSprite(World w, Sprite e, int seq, int frame)
    {
        int tile = SeqData.FrameTile(seq, frame);
        int fl = SeqData.FrameFlags(seq, frame);
        var (xr, yr) = SeqData.FrameRepeat(seq, frame);
        if ((e.HitTag & K.kPhysGravity) != 0)
        {
            if (Tiles.H(e.Picnum) != Tiles.H(tile) || Tiles.YOfs(e.Picnum) != Tiles.YOfs(tile) || (yr != 0 && yr != e.YRepeat))
                e.HitTag |= K.kPhysFalling;
        }
        e.Picnum = tile;
        int pal = SeqData.FramePal(seq, frame);
        if (pal != 0)
            e.Pal = pal;
        e.Shade = SeqData.FrameShade(seq, frame);
        if (xr != 0)
            e.XRepeat = e.XScale != 0 ? (int)Blood.ClipRange(Blood.MulScale8(xr, e.XScale), 0, 255) : xr;
        if (yr != 0)
            e.YRepeat = e.XScale != 0 ? (int)Blood.ClipRange(Blood.MulScale8(yr, e.XScale), 0, 255) : yr;
        e.Cstat = Bit(e.Cstat, Cstat.Trans, (fl & SeqData.FrameTrans) != 0);
        e.Cstat = Bit(e.Cstat, Cstat.Trans2, (fl & SeqData.FrameTrans2) != 0);
        e.Cstat = Bit(e.Cstat, Cstat.Block, (fl & SeqData.FrameBlockable) != 0);
        e.Cstat = Bit(e.Cstat, Cstat.Hitscan, (fl & SeqData.FrameHittable) != 0);
        e.Cstat = Bit(e.Cstat, Cstat.Invisible, (fl & SeqData.FrameInvisible) != 0);
        e.Cstat = Bit(e.Cstat, Cstat.Pushable, (fl & SeqData.FramePushable) != 0);
        e.HitTag = Bit(e.HitTag, K.kHitagSmoke, (fl & SeqData.FrameSmoke) != 0);
        e.HitTag = Bit(e.HitTag, K.kHitagAutoAim, (fl & SeqData.FrameAutoAim) != 0);
        int flip = SeqData.FrameFlip(seq, frame);
        e.HitTag = Bit(e.HitTag, K.kHitagFlipX, (flip & 1) != 0);
        e.HitTag = Bit(e.HitTag, K.kHitagFlipY, (flip & 2) != 0);
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
        w.UpdateBox(e);
    }

    static int Bit(int v, int bit, bool on) => on ? v | bit : v & ~bit;

    static void InstUpdate(World w, Sprite e)
    {
        UpdateSprite(w, e, e.SeqId, e.SeqFrame);
        int fl = SeqData.FrameFlags(e.SeqId, e.SeqFrame);
        if ((fl & SeqData.FramePlaySound) != 0)
        {
            int snd = SeqData.SoundId(e.SeqId);
            int range = SeqData.FrameSoundRange(e.SeqId, e.SeqFrame);
            if (range > 0)
                snd += w.Random(range == 1 ? 2 : range);
            w.Sound3D(e, snd);
        }
        if (SeqData.FrameSurfaceSound(e.SeqId, e.SeqFrame))
            w.SeqSurfaceSound(e);
        if ((fl & SeqData.FrameTrigger) != 0)
            e.SeqCallback?.Invoke(e);
    }

    /// <summary>seqSpawn: start a sequence on a sprite (no restart if it already plays it).</summary>
    public static void Spawn(World w, int seq, Sprite e, Action<Sprite> cb = null)
    {
        if (e == null)
            return;
        if (!SeqData.Exists(seq))
        {
            Godot.GD.PushWarning($"Missing sequence #{seq}");
            return;
        }
        if (e.SeqPlaying && e.SeqId == seq)
            return;
        e.SeqPlaying = true;
        e.SeqId = seq;
        e.SeqCallback = cb;
        e.SeqTime = SeqData.TicksPerFrame(seq);
        e.SeqFrame = 0;
        InstUpdate(w, e);
    }

    public static void Kill(Sprite e) => e.SeqPlaying = false;
    public static int Status(Sprite e) => e.SeqPlaying ? e.SeqFrame : -1;
    public static int Id(Sprite e) => e.SeqPlaying ? e.SeqId : -1;

    /// <summary>Advance one sprite's sequence by nTicks.</summary>
    public static void ProcessOne(World w, Sprite e, int nTicks)
    {
        int seq = e.SeqId;
        e.SeqTime -= nTicks;
        while (e.SeqTime < 0)
        {
            e.SeqTime += SeqData.TicksPerFrame(seq);
            e.SeqFrame++;
            int n = SeqData.FrameCount(seq);
            if (e.SeqFrame >= n)
            {
                if ((SeqData.Flags(seq) & SeqData.FlagLoop) != 0)
                    e.SeqFrame = 0;
                else
                {
                    e.SeqPlaying = false;
                    e.SeqFrame = n - 1;
                    if ((SeqData.Flags(seq) & SeqData.FlagRemove) != 0)
                        w.SeqRemove(e);
                    return;
                }
            }
            InstUpdate(w, e);
            if (e.Removed || !e.SeqPlaying)
                return;
        }
    }

    public static void Process(World w, int nTicks)
    {
        // a callback may add or remove sprites: walk a snapshot
        foreach (var e in w.Sprites.ToArray())
            if (e.SeqPlaying && !e.Removed)
                ProcessOne(w, e, nTicks);
    }
}
