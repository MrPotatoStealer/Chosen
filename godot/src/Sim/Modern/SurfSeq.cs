using System.Collections.Generic;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>
/// SEQ animations on walls and sectors (seq.cpp's UpdateWall, UpdateMasked, UpdateCeiling, UpdateFloor: the instances
/// of types 0, 4, 1 and 2): a sequence stepping a wall's picture (or a two-sided wall's masked middle on both sides) or
/// a sector's floor or ceiling picture, with the palette, shade and flags its frames carry. Blood's own levels play
/// none; a modern map's sequence spawner starts them (World.UseSeqSpawnerGen).
/// </summary>
public static class SurfSeq
{
    public const int Wall = 0, Ceiling = 1, Floor = 2, Masked = 4;

    public sealed class Inst
    {
        public int Kind, Index, SeqId, Frame, Time;
    }

    static List<Inst> List(World w) => w.SurfSeqs;

    static Inst Find(World w, int kind, int index)
    {
        foreach (var i in List(w))
            if (i.Kind == kind && i.Index == index)
                return i;
        return null;
    }

    /// <summary>seqSpawn on a wall (Build wall index) or a sector's plane (Build sector index): no restart of the one
    /// it plays.</summary>
    public static void Spawn(World w, int seq, int kind, int index)
    {
        if (!SeqData.Exists(seq) || w.Board == null)
            return;
        var inst = Find(w, kind, index);
        if (inst == null)
        {
            inst = new Inst { Kind = kind, Index = index };
            List(w).Add(inst);
        }
        else if (inst.SeqId == seq)
            return;
        inst.SeqId = seq;
        inst.Time = SeqData.TicksPerFrame(seq);
        inst.Frame = 0;
        Update(w, inst);
    }

    public static void Kill(World w, int kind, int index) => List(w).RemoveAll(i => i.Kind == kind && i.Index == index);

    static int Bit(int v, int bit, bool on) => on ? v | bit : v & ~bit;

    static void Update(World w, Inst i)
    {
        var b = w.Board;
        int tile = SeqData.FrameTile(i.SeqId, i.Frame), pal = SeqData.FramePal(i.SeqId, i.Frame);
        int fl = SeqData.FrameFlags(i.SeqId, i.Frame);
        bool trans = (fl & SeqData.FrameTrans) != 0, trans2 = (fl & SeqData.FrameTrans2) != 0,
            block = (fl & SeqData.FrameBlockable) != 0, hit = (fl & SeqData.FrameHittable) != 0;
        switch (i.Kind)
        {
            case Wall:
            {
                var wl = b.Walls[i.Index];
                wl.Picnum = tile;
                if (pal != 0)
                    wl.Pal = pal;
                wl.Cstat = Bit(Bit(Bit(Bit(wl.Cstat, 128, trans), 512, trans2), 1, block), 64, hit);
                w.SurfaceChanged(i.Index, true);
                break;
            }
            case Masked:
            {
                var wl = b.Walls[i.Index];
                if (wl.NextWall < 0)
                    break;
                var nx = b.Walls[wl.NextWall];
                wl.OverPicnum = nx.OverPicnum = tile;
                if (pal != 0)
                    wl.Pal = nx.Pal = pal;
                wl.Cstat = Bit(Bit(Bit(Bit(wl.Cstat, 128, trans), 512, trans2), 1, block), 64, hit);
                nx.Cstat = Bit(Bit(Bit(Bit(nx.Cstat, 128, trans), 512, trans2), 1, block), 64, hit);
                w.SurfaceChanged(i.Index, true);
                break;
            }
            case Ceiling:
            {
                var s = b.Sectors[i.Index];
                s.CeilingPicnum = tile;
                s.CeilingShade = SeqData.FrameShade(i.SeqId, i.Frame);
                if (pal != 0)
                    s.CeilingPal = pal;
                w.SurfaceChanged(i.Index, false);
                break;
            }
            case Floor:
            {
                var s = b.Sectors[i.Index];
                s.FloorPicnum = tile;
                s.FloorShade = SeqData.FrameShade(i.SeqId, i.Frame);
                if (pal != 0)
                    s.FloorPal = pal;
                w.SurfaceChanged(i.Index, false);
                break;
            }
        }
        if ((fl & SeqData.FramePlaySound) != 0 && SeqData.SoundId(i.SeqId) > 0)
            w.SurfaceSound(i.Kind, i.Index, SeqData.SoundId(i.SeqId));
    }

    public static void Process(World w, int nTicks)
    {
        var list = List(w);
        for (int k = 0; k < list.Count; k++)
        {
            var i = list[k];
            int seq = i.SeqId;
            i.Time -= nTicks;
            bool ended = false;
            while (i.Time < 0 && !ended)
            {
                i.Time += System.Math.Max(SeqData.TicksPerFrame(seq), 1);
                i.Frame++;
                int n = SeqData.FrameCount(seq);
                if (i.Frame >= n)
                {
                    if ((SeqData.Flags(seq) & SeqData.FlagLoop) != 0)
                        i.Frame = 0;
                    else
                    {
                        ended = true;
                        break;
                    }
                }
                Update(w, i);
            }
            if (ended)
            {
                list.RemoveAt(k);
                k--;
            }
        }
    }
}
