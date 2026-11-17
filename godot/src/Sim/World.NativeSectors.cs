using System.Collections.Generic;
using Chosen.Data;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// Blood's sector motion on the level's Build data (triggers.cpp, a level loaded from its .MAP): TranslateSector moves
/// a sliding or turning sector's wall points from their base places (DragPoint takes the walls joined to them along) and
/// the sprites in it; ZTranslateSector moves floors and ceilings and what rests on them; the busy functions drive them
/// (VCrushBusy .. PathBusy), ProcessMotion bobs, AlignSlopes tilts, trInit puts every mover at its starting state.
/// </summary>
public sealed partial class World
{
    /// <summary>velFloor / velCeil: how far each sector's floor and ceiling moved this game frame (trProcessBusy).</summary>
    public int[] VelFloor = System.Array.Empty<int>(), VelCeil = System.Array.Empty<int>();

    static int Interp(int a, int b, int c) => a + (int)Blood.MulScale16(b - a, c);

    /// <summary>The sprites in Build sector b now (headspritesect), a copy to change them while going through it.</summary>
    List<Sprite> SpritesInB(int b)
    {
        EnsureSectSprites();
        var list = _sectSprites.TryGetValue(b, out var l) ? new List<Sprite>(l) : new List<Sprite>();
        list.RemoveAll(e => e.Removed);
        return list;
    }

    static (int x, int y) BuildXY(Vector3 q) => ((int)System.Math.Round(q.X * 16), (int)System.Math.Round(-q.Y * 16));

    void SetBuildXY(Sprite e, int x, int y) => e.Origin = new Vector3(x / 16f, -y / 16f, e.Origin.Z);

    /// <summary>TranslateSector: sector s's walls (all, or the marked: 16384 with the transform, 32768 against it) from
    /// their base places turned by the angle between a8 and a11 and moved between (a6, a7) and (a9, a10), a2..a3 of
    /// the way (both 16.16); the sprites in it the same (8192 with it, 16384 against it), and with Drag what stands on
    /// its floor.</summary>
    void TranslateSector(Sector xs, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, bool allWalls)
    {
        var b = Board;
        int nSector = xs.B;
        var sec = b.Sectors[nSector];
        int v20 = Interp(a6, a9, a2), vc = Interp(a6, a9, a3), v28 = vc - v20;
        int v24 = Interp(a7, a10, a2), v8 = Interp(a7, a10, a3), v2c = v8 - v24;
        int v44 = Interp(a8, a11, a2), vbp = Interp(a8, a11, a3), v14 = vbp - v44;
        int nWall = sec.WallPtr;
        if (allWalls)
        {
            for (int i = 0; i < sec.WallNum; nWall++, i++)
            {
                int x = b.BaseWallX[nWall], y = b.BaseWallY[nWall];
                if (vbp != 0)
                    Build.Board.RotatePoint(ref x, ref y, vbp, a4, a5);
                b.DragPoint(nWall, x + vc - a4, y + v8 - a5);
            }
        }
        else
        {
            for (int i = 0; i < sec.WallNum; nWall++, i++)
            {
                int v10 = b.Walls[nWall].Point2;
                int x = b.BaseWallX[nWall], y = b.BaseWallY[nWall];
                if ((b.Walls[nWall].Cstat & 16384) != 0)
                {
                    if (vbp != 0)
                        Build.Board.RotatePoint(ref x, ref y, vbp, a4, a5);
                    b.DragPoint(nWall, x + vc - a4, y + v8 - a5);
                    if ((b.Walls[v10].Cstat & 49152) == 0)
                    {
                        x = b.BaseWallX[v10];
                        y = b.BaseWallY[v10];
                        if (vbp != 0)
                            Build.Board.RotatePoint(ref x, ref y, vbp, a4, a5);
                        b.DragPoint(v10, x + vc - a4, y + v8 - a5);
                    }
                    continue;
                }
                if ((b.Walls[nWall].Cstat & 32768) != 0)
                {
                    if (vbp != 0)
                        Build.Board.RotatePoint(ref x, ref y, -vbp, a4, a5);
                    b.DragPoint(nWall, x - (vc - a4), y - (v8 - a5));
                    if ((b.Walls[v10].Cstat & 49152) == 0)
                    {
                        x = b.BaseWallX[v10];
                        y = b.BaseWallY[v10];
                        if (vbp != 0)
                            Build.Board.RotatePoint(ref x, ref y, -vbp, a4, a5);
                        b.DragPoint(v10, x - (vc - a4), y - (v8 - a5));
                    }
                }
            }
        }
        // (vanilla Blood turns the sprites going against it about (a4, a4): the x for the y; a modern map about (a4, a5))
        int sprDy = IsModern ? a5 : a4;
        foreach (var e in SpritesInB(nSector))
        {
            // (a modern map's markers flagged 1 move with their sector)
            if ((e.StatNum == K.kStatMarker || e.StatNum == K.kStatPathMarker) && !(IsModern && (e.HitTag & 1) != 0))
                continue;
            var (x, y) = BuildXY(e.BaseOrigin);
            if ((e.Cstat & (Cstat.MoveForward | Cstat.MoveReverse)) != 0)
            {
                // a sprite of the sector stays in it wherever this puts it, as Blood's sectnum does: E1M3's track props
                // ride out of their sector's walls to the train, and walking them into the next sector here ended the ride
                if (SpriteBSector(e) != nSector)
                    continue;
                if ((e.Cstat & Cstat.MoveForward) != 0)
                {
                    if (vbp != 0)
                        Build.Board.RotatePoint(ref x, ref y, vbp, a4, a5);
                    e.Ang = (e.Ang + v14) & 2047;
                    SetBuildXY(e, x + vc - a4, y + v8 - a5);
                }
                else
                {
                    if (vbp != 0)
                        Build.Board.RotatePoint(ref x, ref y, -vbp, a4, sprDy);
                    e.Ang = (e.Ang - v14) & 2047;
                    SetBuildXY(e, x - (vc - a4), y - (v8 - a5));
                }
                e.SectAt = e.Origin;
            }
            else if (xs.SDrag != 0)
            {
                var (_, bottom) = Extents(e);
                double floorZ = b.FloorZAt(nSector, e.BX, e.BY);
                if ((e.Cstat & 48) == 0 && floorZ <= bottom)
                {
                    var (px, py) = BuildXY(e.Origin);
                    if (v14 != 0)
                        Build.Board.RotatePoint(ref px, ref py, v14, v20, v24);
                    e.Ang = (e.Ang + v14) & 2047;
                    SetBuildXY(e, px + v28, py + v2c);
                    if (e is Player pl)
                        pl.MoverTurn += v14 * 360f / 2048f;
                }
            }
            UpdateBox(e);
        }
        // a modern map: the sprites just outside its walls
        if (IsModern && _sprNearSect.TryGetValue(nSector, out var near))
        {
            foreach (var e in near)
            {
                if (e.Removed)
                    continue;
                var (x, y) = BuildXY(e.BaseOrigin);
                if ((e.Cstat & Cstat.MoveForward) != 0)
                {
                    if (vbp != 0)
                        Build.Board.RotatePoint(ref x, ref y, vbp, a4, a5);
                    e.Ang = (e.Ang + v14) & 2047;
                    SetBuildXY(e, x + vc - a4, y + v8 - a5);
                }
                else if ((e.Cstat & Cstat.MoveReverse) != 0)
                {
                    if (vbp != 0)
                        Build.Board.RotatePoint(ref x, ref y, -vbp, a4, sprDy);
                    e.Ang = (e.Ang - v14) & 2047;
                    SetBuildXY(e, x - (vc - a4), y - (v8 - a5));
                }
                else
                    continue;
                UpdateBox(e);
            }
        }
    }

    // a modern map: the sprites within 16 units outside a moving sector's walls that are flagged to move (8192,
    // 16384): they move with it (nnexts.h SPRINSECT)
    readonly Dictionary<int, List<Sprite>> _sprNearSect = new();

    static bool IsMovableSectorType(int type) =>
        type != 0 && type != K.kSectorDamage && type != K.kSectorTeleport && type != K.kSectorCounter;

    void SprNearSectInit(int nDist = 16)
    {
        _sprNearSect.Clear();
        var b = Board;
        for (int i = 0; i < b.SectorCount; i++)
        {
            int type = b.Sectors[i].Type;
            if (!IsMovableSectorType(type) || type == K.kSectorZMotionSprite || type == K.kSectorSlideMarked || type == K.kSectorRotateMarked)
                continue;
            var list = new List<Sprite>();
            foreach (int w in b.SectorWalls(i))
            {
                int ns = b.Walls[w].NextSector;
                if (ns < 0)
                    continue;
                long wx = b.Walls[w].X, wy = b.Walls[w].Y;
                long lx = b.Walls[b.Walls[w].Point2].X - wx, ly = b.Walls[b.Walls[w].Point2].Y - wy;
                foreach (var e in SpritesInB(ns))
                {
                    if (list.Contains(e) || list.Count >= 256)
                        continue;
                    var (sx, sy) = BuildXY(e.Origin);
                    long qx = sx - wx, qy = sy - wy;
                    long num = (qx * lx + qy * ly) >> 4, den = (lx * lx + ly * ly) >> 4;
                    if (num <= 0 || num >= den)
                        continue;
                    double xi = wx + lx * num / den, yi = wy + ly * num / den;
                    if (Blood.ApproxDist(xi - sx, yi - sy) > nDist)
                        continue;
                    // only those flagged to move, in a sector that doesn't move itself; no dudes, markers only when flagged
                    if ((e.Cstat & (Cstat.MoveForward | Cstat.MoveReverse)) == 0 || IsMovableSectorType(b.Sectors[ns].Type))
                        continue;
                    if (e.StatNum == K.kStatDude)
                        continue;
                    if ((e.StatNum == K.kStatMarker || e.StatNum == K.kStatPathMarker) && (e.HitTag & 1) == 0)
                        continue;
                    list.Add(e);
                }
            }
            if (list.Count > 0)
                _sprNearSect[i] = list;
        }
    }

    /// <summary>ZTranslateSector: the floor and ceiling between their OFF and ON heights by the wave at a3, what rests on
    /// the floor (or is flagged to follow it, 8192; a falling thing told to fall, flags 2) moved with it, the sprites
    /// flagged 16384 with the ceiling.</summary>
    void ZTranslateSector(Sector xs, int a3, int a4)
    {
        var b = Board;
        int nSector = xs.B;
        var sec = b.Sectors[nSector];
        int dfz = xs.OnFloorZ - xs.OffFloorZ, dcz = xs.OnCeilZ - xs.OffCeilZ;
        if (dfz != 0)
        {
            int oldZ = sec.FloorZ;
            int z = xs.OffFloorZ + (int)Blood.MulScale16(dfz, GetWaveValue(a3, a4));
            b.BaseFloor[nSector] = z;
            b.SetFloorZ(nSector, z);
            VelFloor[nSector] += (z - oldZ) << 8;
            foreach (var e in SpritesInB(nSector))
            {
                if (e.StatNum == K.kStatMarker || e.StatNum == K.kStatPathMarker)
                    continue;
                var (_, bottom) = Extents(e);
                if ((e.Cstat & Cstat.MoveForward) != 0)
                    e.SetBZ(e.BZ + z - oldZ);
                else if ((e.HitTag & 2) != 0)
                    e.HitTag |= 4;
                else if (oldZ <= bottom && (e.Cstat & 48) == 0)
                    e.SetBZ(e.BZ + z - oldZ);
                else
                    continue;
                RidePlane(e, z - oldZ);
            }
            if (IsModern && _sprNearSect.TryGetValue(nSector, out var nearF))
                foreach (var e in nearF)
                    if (!e.Removed && (e.Cstat & Cstat.MoveForward) != 0)
                    {
                        e.SetBZ(e.BZ + z - oldZ);
                        UpdateBox(e);
                    }
        }
        if (dcz != 0)
        {
            int oldZ = sec.CeilingZ;
            int z = xs.OffCeilZ + (int)Blood.MulScale16(dcz, GetWaveValue(a3, a4));
            b.BaseCeil[nSector] = z;
            b.SetCeilZ(nSector, z);
            VelCeil[nSector] += z - oldZ;
            foreach (var e in SpritesInB(nSector))
            {
                if (e.StatNum == K.kStatMarker || e.StatNum == K.kStatPathMarker)
                    continue;
                if ((e.Cstat & Cstat.MoveReverse) != 0)
                {
                    e.SetBZ(e.BZ + z - oldZ);
                    UpdateBox(e);
                }
            }
            if (IsModern && _sprNearSect.TryGetValue(nSector, out var nearC))
                foreach (var e in nearC)
                    if (!e.Removed && (e.Cstat & Cstat.MoveReverse) != 0)
                    {
                        e.SetBZ(e.BZ + z - oldZ);
                        UpdateBox(e);
                    }
        }
    }

    /// <summary>A sprite a plane moved (its view follows by itself: playerProcess springs it after the body).</summary>
    void RidePlane(Sprite e, int dz) => UpdateBox(e);

    /// <summary>GetHighestSprite: the sprite (of status st, kStatFree: any) in sector b whose top is highest, and that
    /// height (the floor's when none).</summary>
    Sprite GetHighestSprite(int b, int st, out double z)
    {
        z = Board.Sectors[b].FloorZ;
        Sprite best = null;
        foreach (var e in SpritesInB(b))
        {
            if (e.StatNum != st && st != K.kStatFree)
                continue;
            var (top, _) = Extents(e);
            if (e.BZ - top < z)
            {
                z = e.BZ - top;
                best = e;
            }
        }
        return best;
    }

    /// <summary>GetCrushedSpriteExtents: of the dudes and things in sector b, the one whose top is highest under its
    /// ceiling, and its extents.</summary>
    Sprite GetCrushedSpriteExtents(int b, out double zTop, out double zBot)
    {
        zTop = zBot = 0;
        Sprite best = null;
        double vbp = Board.Sectors[b].CeilingZ;
        foreach (var e in SpritesInB(b))
        {
            if (e.StatNum != K.kStatDude && e.StatNum != K.kStatThing)
                continue;
            var (top, bottom) = Extents(e);
            if (vbp > top)
            {
                vbp = top;
                zTop = top;
                zBot = bottom;
                best = e;
            }
        }
        return best;
    }

    int NativeBusyEnd(Sector xs, int a2, XObject causer)
    {
        xs.XBusy = a2;
        if (xs.XCommand == K.kCmdLink && xs.TxID != 0)
            EvSend(xs, xs.TxID, K.kCmdLink, causer);
        if ((a2 & 0xffff) == 0)
        {
            SetSectorState(xs, a2 >> 16, causer);
            SectorEndSound(xs, a2 >> 16);
            return 3;
        }
        return 0;
    }

    int VCrushBusy(Sector xs, int a2, XObject causer)
    {
        int nWave = xs.XBusy < a2 ? xs.BusyWaveA : xs.BusyWaveB;
        int dz1 = xs.OnCeilZ - xs.OffCeilZ;
        int vc = xs.OffCeilZ;
        if (dz1 != 0)
            vc += (int)Blood.MulScale16(dz1, GetWaveValue(a2, nWave));
        int dz2 = xs.OnFloorZ - xs.OffFloorZ;
        int v10 = xs.OffFloorZ;
        if (dz2 != 0)
            v10 += (int)Blood.MulScale16(dz2, GetWaveValue(a2, nWave));
        if (GetHighestSprite(xs.B, K.kStatDude, out double v18) != null && vc >= v18)
            return 1;
        if (dz1 != 0)
            Board.SetCeilZ(xs.B, vc);
        if (dz2 != 0)
            Board.SetFloorZ(xs.B, v10);
        return NativeBusyEnd(xs, a2, causer);
    }

    int NativeVSpriteBusy(Sector xs, int a2, XObject causer)
    {
        int nWave = xs.XBusy < a2 ? xs.BusyWaveA : xs.BusyWaveB;
        int dz1 = xs.OnFloorZ - xs.OffFloorZ;
        if (dz1 != 0)
            foreach (var e in SpritesInB(xs.B))
                if ((e.Cstat & Cstat.MoveForward) != 0)
                {
                    e.SetBZ(-e.BaseOrigin.Z * 256.0 + Blood.MulScale16(dz1, GetWaveValue(a2, nWave)));
                    UpdateBox(e);
                }
        int dz2 = xs.OnCeilZ - xs.OffCeilZ;
        if (dz2 != 0)
            foreach (var e in SpritesInB(xs.B))
                if ((e.Cstat & Cstat.MoveReverse) != 0)
                {
                    e.SetBZ(-e.BaseOrigin.Z * 256.0 + Blood.MulScale16(dz2, GetWaveValue(a2, nWave)));
                    UpdateBox(e);
                }
        return NativeBusyEnd(xs, a2, causer);
    }

    int VDoorBusy(Sector xs, int a2, XObject causer)
    {
        int vbp = xs.XState != 0 ? 65536 / System.Math.Max(120 * xs.BusyTimeA / 10, 1)
            : -65536 / System.Math.Max(120 * xs.BusyTimeB / 10, 1);
        var e = GetCrushedSpriteExtents(xs.B, out _, out _);
        bool closing = a2 > xs.XBusy ? xs.OnCeilZ > xs.OffCeilZ || xs.OnFloorZ < xs.OffFloorZ
            : xs.OffCeilZ > xs.OnCeilZ || xs.OffFloorZ < xs.OnFloorZ;
        if (e != null && a2 != xs.XBusy && closing)
        {
            int step = (vbp / 2) * 4;
            int a2n = a2 > xs.XBusy ? a2 - step : a2 + step;
            int damage = (xs.SData == 0 ? 500 : xs.SData) << 4;
            if (xs.Interruptable != 0)
            {
                if (xs.SCrush != 0)
                {
                    if (e.XHealth <= 0)
                        return 2;
                    ActDamageSprite(e, e, K.kDamageFall, damage);
                }
                a2 = System.Math.Clamp(a2n, 0, 65536);
            }
            else if (xs.SCrush != 0 && e.XHealth > 0)
            {
                ActDamageSprite(e, e, K.kDamageFall, damage);
                a2 = System.Math.Clamp(a2n, 0, 65536);
            }
        }
        int nWave = xs.XBusy < a2 ? xs.BusyWaveA : xs.BusyWaveB;
        ZTranslateSector(xs, a2, nWave);
        return NativeBusyEnd(xs, a2, causer);
    }

    int HDoorBusy(Sector xs, int a2, XObject causer)
    {
        int nWave = xs.XBusy < a2 ? xs.BusyWaveA : xs.BusyWaveB;
        if (xs.SMarker0 is not Sprite m1 || xs.SMarker1 is not Sprite m2)
            return NativeBusyEnd(xs, a2, causer);
        var (x1, y1) = BuildXY(m1.Origin);
        var (x2, y2) = BuildXY(m2.Origin);
        TranslateSector(xs, GetWaveValue(xs.XBusy, nWave), GetWaveValue(a2, nWave), x1, y1, x1, y1, m1.Ang, x2, y2, m2.Ang,
            xs.Type == K.kSectorSlide);
        ZTranslateSector(xs, a2, nWave);
        return NativeBusyEnd(xs, a2, causer);
    }

    int RDoorBusy(Sector xs, int a2, XObject causer)
    {
        int nWave = xs.XBusy < a2 ? xs.BusyWaveA : xs.BusyWaveB;
        if (xs.SMarker0 is not Sprite m)
            return NativeBusyEnd(xs, a2, causer);
        var (x, y) = BuildXY(m.Origin);
        TranslateSector(xs, GetWaveValue(xs.XBusy, nWave), GetWaveValue(a2, nWave), x, y, x, y, 0, x, y, m.Ang,
            xs.Type == K.kSectorRotate);
        ZTranslateSector(xs, a2, nWave);
        return NativeBusyEnd(xs, a2, causer);
    }

    int NativeStepRotateBusy(Sector xs, int a2, XObject causer)
    {
        if (xs.SMarker0 is not Sprite m)
            return NativeBusyEnd(xs, a2, causer);
        var (x, y) = BuildXY(m.Origin);
        int vbp;
        if (xs.XBusy < a2)
        {
            vbp = xs.SData + m.Ang;
            int nWave = xs.BusyWaveA;
            TranslateSector(xs, GetWaveValue(xs.XBusy, nWave), GetWaveValue(a2, nWave), x, y, x, y, xs.SData, x, y, vbp, true);
        }
        else
        {
            vbp = xs.SData - m.Ang;
            int nWave = xs.BusyWaveB;
            TranslateSector(xs, GetWaveValue(xs.XBusy, nWave), GetWaveValue(a2, nWave), x, y, x, y, vbp, x, y, xs.SData, true);
        }
        int r = NativeBusyEnd(xs, a2, causer);
        if (r == 3)
            xs.SData = vbp & 2047;
        return r;
    }

    int NativePathBusy(Sector xs, int a2, XObject causer)
    {
        if (xs.SBasePath is not Sprite basePath || xs.SMarker0 is not Sprite m1 || xs.SMarker1 is not Sprite m2)
            return 3;
        int nWave = m1.XWave;
        var (bx, by) = BuildXY(basePath.Origin);
        var (x1, y1) = BuildXY(m1.Origin);
        var (x2, y2) = BuildXY(m2.Origin);
        TranslateSector(xs, GetWaveValue(xs.XBusy, nWave), GetWaveValue(a2, nWave), bx, by, x1, y1, m1.Ang, x2, y2, m2.Ang, true);
        ZTranslateSector(xs, a2, nWave);
        xs.XBusy = a2;
        if ((a2 & 0xffff) == 0)
        {
            EvPost(xs, 120 * m2.WaitTime / 10, K.kCmdOn, causer);
            xs.XState = 0;
            xs.XBusy = 0;
            if (m1.Data4 != 0)
                PathSound(xs, m1.Data4);
            xs.SMarker0 = m2;
            xs.SData = m2.Data1;
            return 3;
        }
        return 0;
    }

    int NativeRunBusyProc(Sector s, int a2) => s.BusyProc switch
    {
        K.BUSYID_0 => VCrushBusy(s, a2, null),
        K.BUSYID_1 => NativeVSpriteBusy(s, a2, null),
        K.BUSYID_2 => VDoorBusy(s, a2, null),
        K.BUSYID_3 => HDoorBusy(s, a2, null),
        K.BUSYID_4 => RDoorBusy(s, a2, null),
        K.BUSYID_5 => NativeStepRotateBusy(s, a2, null),
        K.BUSYID_7 => NativePathBusy(s, a2, null),
        _ => GenSectorBusy(s, a2, null),
    };

    /// <summary>LinkSector: a linked sector set to its source's busy by its own busy function.</summary>
    void NativeLinkSector(Sector s, XObject src)
    {
        int nBusy = GetSourceBusy(src);
        switch (s.Type)
        {
            case K.kSectorZMotionSprite: NativeVSpriteBusy(s, nBusy, null); break;
            case K.kSectorZMotion: VDoorBusy(s, nBusy, null); break;
            case K.kSectorSlideMarked:
            case K.kSectorSlide: HDoorBusy(s, nBusy, null); break;
            case K.kSectorRotateMarked:
            case K.kSectorRotate: RDoorBusy(s, nBusy, null); break;
            default:
                s.XBusy = nBusy;
                if ((s.XBusy & 0xffff) == 0)
                    SetSectorState(s, nBusy >> 16, null);
                break;
        }
    }

    /// <summary>OperatePath: on, the path sector sets off to the marker its current one names (data2), its floor
    /// between theirs.</summary>
    void NativeOperatePath(Sector s, int cmd, XObject causer)
    {
        if (s.SMarker0 is not Sprite m0)
            return;
        var m = FindPathMarker(m0.Data2);
        if (m == null)
        {
            s.XState = 0;
            s.XBusy = 0;
            return;
        }
        // a modern map: the marker reached is triggered
        if (IsModern && m0.HasX && m0.XState != 1)
            TrTriggerSprite(m0, K.kCmdOn, causer);
        s.SMarker1 = m;
        s.OffFloorZ = (int)System.Math.Round(m0.BZ);
        s.OnFloorZ = (int)System.Math.Round(m.BZ);
        if (cmd == K.kCmdOn)
        {
            s.XState = 0;
            s.XBusy = 0;
            s.BusyActive = false;
            AddBusy(s, K.BUSYID_7, 65536 / System.Math.Max(120 * m0.BusyTime / 10, 1));
            s.BusyVal = 0;
            if (m0.Data3 != 0)
                PathSound(s, m0.Data3);
        }
    }

    /// <summary>OperateTeleport: the dudes in the sector to its destination marker (a player always, a monster when no
    /// dude is there), their height by the floors' difference.</summary>
    void NativeOperateTeleport(Sector s)
    {
        if (s.SMarker0 is not Sprite dest)
            return;
        int destSect = SpriteBSector(dest);
        foreach (var e in SpritesInB(s.B))
        {
            if (e.StatNum != K.kStatDude)
                continue;
            bool bPlayer = e is Player;
            if (!bPlayer && destSect >= 0 && SpritesInB(destSect).Exists(o => o.StatNum == K.kStatDude))
                continue;
            if (destSect >= 0)
                foreach (var o in SpritesInB(destSect))
                {
                    if (o.StatNum == K.kStatDude)
                        ActDamageSprite(s.SDataEnt ?? s, o, K.kDamageExplode, 4000);
                    else if (o.StatNum == K.kStatThing)
                        ActDamageSprite(s.SDataEnt ?? s, o, K.kDamageExplode, 8000);
                }
            double dz = destSect >= 0 ? Board.Sectors[destSect].FloorZ - Board.Sectors[s.B].FloorZ : 0;
            e.Origin = new Vector3(dest.Origin.X, dest.Origin.Y, e.Origin.Z);
            e.SetBZ(e.BZ + dz);
            e.Ang = dest.Ang;
            if (destSect >= 0)
                SetSpriteBSector(e, destSect);
            Sound3D(dest, 201, -1);
            e.XVel = e.YVel = e.ZVel = 0;
            e.PrevOrigin = e.Origin;
            UpdateBox(e);
            if (e is Player p)
            {
                PlayerResetInertia(p);
                p.ZViewVel = 0;
                p.ZWeaponVel = 0;
                p.AngOld = p.Ang;
                p.FixAngle = true;
            }
        }
    }

    /// <summary>ProcessMotion: bobbing floors and ceilings (bobTheta by bobSpeed a frame, always or with the busy), what
    /// is flagged to follow them and what rests on them.</summary>
    void NativeProcessMotion()
    {
        var b = Board;
        foreach (var xs in Sectors)
        {
            if (xs.BobSpeed == 0)
                continue;
            if (xs.BobAlways != 0)
                xs.BobTheta += xs.BobSpeed;
            else if (xs.XBusy == 0)
                continue;
            else
                xs.BobTheta += (int)Blood.MulScale16(xs.BobSpeed, xs.XBusy);
            xs.BobTheta &= 2047;     // an 11-bit field
            int vdi = (int)Blood.MulScale30(Blood.Sin(xs.BobTheta), xs.BobZRange << 8);
            int ns = xs.B;
            foreach (var e in SpritesInB(ns))
                if ((e.Cstat & 24576) != 0)
                {
                    e.SetBZ(e.BZ + vdi);
                    UpdateBox(e);
                }
            if (xs.BobFloor != 0)
            {
                int floorZ = b.Sectors[ns].FloorZ;
                b.SetFloorZ(ns, b.BaseFloor[ns] + vdi);
                foreach (var e in SpritesInB(ns))
                {
                    if ((e.HitTag & 2) != 0)
                        e.HitTag |= 4;
                    else
                    {
                        var (_, bottom) = Extents(e);
                        if (bottom >= floorZ && (e.Cstat & 48) == 0)
                        {
                            e.SetBZ(e.BZ + vdi);
                            RidePlane(e, vdi);
                        }
                    }
                }
            }
            if (xs.BobCeiling != 0)
            {
                int ceilZ = b.Sectors[ns].CeilingZ;
                b.SetCeilZ(ns, b.BaseCeil[ns] + vdi);
                foreach (var e in SpritesInB(ns))
                {
                    var (top, _) = Extents(e);
                    if (top <= ceilZ && (e.Cstat & 48) == 0)
                    {
                        e.SetBZ(e.BZ + vdi);
                        UpdateBox(e);
                    }
                }
            }
        }
    }

    /// <summary>AlignSlopes: a sector whose filler names a wall tilts its floor and ceiling about its first wall to meet
    /// the next sector's planes at that wall's middle (alignflorslope, alignceilslope).</summary>
    void NativeAlignSlopes()
    {
        var b = Board;
        for (int s = 0; s < b.SectorCount; s++)
        {
            var sec = b.Sectors[s];
            int filler = sec.FogPal;
            if (filler == 0)
                continue;
            var w = b.Walls[sec.WallPtr + filler];
            var w2 = b.Walls[w.Point2];
            int ns = w.NextSector;
            if (ns < 0)
                continue;
            int x = (w.X + w2.X) / 2, y = (w.Y + w2.Y) / 2;
            AlignSlope(s, x, y, (int)b.FloorZAt(ns, x, y), false);
            AlignSlope(s, x, y, (int)b.CeilZAt(ns, x, y), true);
        }
    }

    /// <summary>alignflorslope / alignceilslope: the plane's heinum so it passes through (x, y, z), turning about its
    /// first wall.</summary>
    void AlignSlope(int s, int x, int y, int z, bool ceiling)
    {
        var b = Board;
        var sec = b.Sectors[s];
        var w = b.Walls[sec.WallPtr];
        var w2 = b.Walls[w.Point2];
        long dax = w2.X - w.X, day = w2.Y - w.Y;
        long i = (y - w.Y) * dax - (x - w.X) * day;
        if (i == 0)
            return;
        long len = (long)System.Math.Floor(System.Math.Sqrt((double)(dax * dax + day * day)));
        int baseZ = ceiling ? sec.CeilingZ : sec.FloorZ;
        int heinum = (int)((((long)(z - baseZ) << 8) * len) / i);
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        int was = ceiling ? sec.CeilingHeinum : sec.FloorHeinum;
        stat = heinum == 0 ? stat & ~2 : stat | 2;
        if (ceiling)
        {
            sec.CeilingHeinum = heinum;
            sec.CeilingStat = stat;
        }
        else
        {
            sec.FloorHeinum = heinum;
            sec.FloorStat = stat;
        }
        if (heinum != was)
            b.PlaneVersion[s]++;
    }

    // the panning's fractions (the x-sectors' floorXPanFrac.. and the x-walls' xpanFrac, ypanFrac)
    int[] _floorPanFrac = System.Array.Empty<int>(), _ceilPanFrac = System.Array.Empty<int>(), _wallPanFrac = System.Array.Empty<int>();

    static int PicBits(int n)
    {
        int b = 0;
        while ((1 << b) < n)
            b++;
        return b;
    }

    /// <summary>DoSectorPanning: the floors and ceilings of panning sectors (panVel along panAngle, always or with the
    /// busy) and the walls of panning x-walls move their pictures, in 1/256 of a panning step.</summary>
    void NativeSectorPanning()
    {
        var b = Board;
        if (_floorPanFrac.Length != b.SectorCount)
        {
            _floorPanFrac = new int[b.SectorCount * 2];
            _ceilPanFrac = new int[b.SectorCount * 2];
            _wallPanFrac = new int[b.WallCount * 2];
            for (int i = 0; i < b.SectorCount; i++)
                if (b.Sectors[i].Xs is { } x)
                {
                    _floorPanFrac[2 * i] = x.FloorXPanFrac;
                    _floorPanFrac[2 * i + 1] = x.FloorYPanFrac;
                    _ceilPanFrac[2 * i] = x.CeilXPanFrac;
                    _ceilPanFrac[2 * i + 1] = x.CeilYPanFrac;
                }
            for (int i = 0; i < b.WallCount; i++)
                if (b.Walls[i].Xw is { } x)
                {
                    _wallPanFrac[2 * i] = x.XpanFrac;
                    _wallPanFrac[2 * i + 1] = x.YpanFrac;
                }
        }
        foreach (var xs in Sectors)
        {
            var bx = b.Sectors[xs.B].Xs;
            if (bx.PanVel == 0 || !(bx.PanAlways != 0 || xs.XBusy != 0))
                continue;
            var sec = b.Sectors[xs.B];
            int angle = bx.PanAngle + 1024;
            int speed = bx.PanVel << 10;
            if (bx.PanAlways == 0 && (xs.XBusy & 0xffff) != 0)
                speed = (int)Blood.MulScale16(speed, xs.XBusy);
            if (bx.PanFloor != 0)
            {
                int tile = sec.FloorPicnum;
                int px = (sec.FloorXPanning << 8) + _floorPanFrac[2 * xs.B];
                int py = (sec.FloorYPanning << 8) + _floorPanFrac[2 * xs.B + 1];
                if ((sec.FloorStat & 64) != 0)
                    angle -= 512;
                int xBits = PicBits(Tiles.W(tile)) - ((sec.FloorStat & 8) != 0 ? 1 : 0);
                px += (int)Blood.MulScale30(speed << 2, Blood.Cos(angle)) >> xBits;
                int yBits = PicBits(Tiles.H(tile)) - ((sec.FloorStat & 8) != 0 ? 1 : 0);
                py -= (int)Blood.MulScale30(speed << 2, Blood.Sin(angle)) >> yBits;
                sec.FloorXPanning = (px >> 8) & 255;
                sec.FloorYPanning = (py >> 8) & 255;
                _floorPanFrac[2 * xs.B] = px & 255;
                _floorPanFrac[2 * xs.B + 1] = py & 255;
            }
            if (bx.PanCeiling != 0)
            {
                int tile = sec.CeilingPicnum;
                int px = (sec.CeilingXPanning << 8) + _ceilPanFrac[2 * xs.B];
                int py = (sec.CeilingYPanning << 8) + _ceilPanFrac[2 * xs.B + 1];
                if ((sec.CeilingStat & 64) != 0)
                    angle -= 512;
                int xBits = PicBits(Tiles.W(tile)) - ((sec.CeilingStat & 8) != 0 ? 1 : 0);
                px += (int)Blood.MulScale30(speed << 2, Blood.Cos(-angle)) >> xBits;
                int yBits = PicBits(Tiles.H(tile)) - ((sec.CeilingStat & 8) != 0 ? 1 : 0);
                py -= (int)Blood.MulScale30(speed << 2, Blood.Sin(-angle)) >> yBits;
                sec.CeilingXPanning = (px >> 8) & 255;
                sec.CeilingYPanning = (py >> 8) & 255;
                _ceilPanFrac[2 * xs.B] = px & 255;
                _ceilPanFrac[2 * xs.B + 1] = py & 255;
            }
        }
        foreach (var w in Walls)
        {
            var bw = b.Walls[w.BWall];
            var xw = bw.Xw;
            if (xw.PanXVel == 0 && xw.PanYVel == 0 || !(xw.PanAlways != 0 || w.XBusy != 0))
                continue;
            int psx = xw.PanXVel << 10, psy = xw.PanYVel << 10;
            if (xw.PanAlways == 0 && (w.XBusy & 0xffff) != 0)
            {
                psx = (int)Blood.MulScale16(psx, w.XBusy);
                psy = (int)Blood.MulScale16(psy, w.XBusy);
            }
            int tile = bw.Picnum;
            int px = (bw.XPanning << 8) + _wallPanFrac[2 * w.BWall];
            int py = (bw.YPanning << 8) + _wallPanFrac[2 * w.BWall + 1];
            px += (psx << 2) >> PicBits(Tiles.W(tile));
            py += (psy << 2) >> PicBits(Tiles.H(tile));
            bw.XPanning = (px >> 8) & 255;
            bw.YPanning = (py >> 8) & 255;
            _wallPanFrac[2 * w.BWall] = px & 255;
            _wallPanFrac[2 * w.BWall + 1] = py & 255;
        }
    }

    /// <summary>A Build sector's floor and ceiling panning now, fractions and all (x, y of each).</summary>
    public Vector4 SectorPanNow(int s)
    {
        var sec = Board.Sectors[s];
        if (_floorPanFrac.Length != 2 * Board.SectorCount)
            return new Vector4(sec.FloorXPanning, sec.FloorYPanning, sec.CeilingXPanning, sec.CeilingYPanning);
        return new Vector4(sec.FloorXPanning + _floorPanFrac[2 * s] / 256f, sec.FloorYPanning + _floorPanFrac[2 * s + 1] / 256f,
            sec.CeilingXPanning + _ceilPanFrac[2 * s] / 256f, sec.CeilingYPanning + _ceilPanFrac[2 * s + 1] / 256f);
    }

    /// <summary>A Build wall's panning now, fractions and all.</summary>
    public Vector2 WallPanNow(int w)
    {
        var bw = Board.Walls[w];
        if (_wallPanFrac.Length != 2 * Board.WallCount)
            return new Vector2(bw.XPanning, bw.YPanning);
        return new Vector2(bw.XPanning + _wallPanFrac[2 * w] / 256f, bw.YPanning + _wallPanFrac[2 * w + 1] / 256f);
    }

    /// <summary>A Build sector's light-effect shade offsets now (floor, ceiling, walls), as DoSectorLighting changes
    /// their shades.</summary>
    public Vector3 SectorShadeFxNow(int s)
    {
        var xs = XSectorOf(s);
        if (xs == null || xs.FxAmp == 0)
            return Vector3.Zero;
        return new Vector3(xs.FxFloor != 0 ? xs.FxShade : 0, xs.FxCeil != 0 ? xs.FxShade : 0, xs.FxShadeWalls != 0 ? xs.FxShade : 0);
    }

    /// <summary>trInit for a level from its Build data: base places, the x-objects' busy from their state, the sliding
    /// and turning sectors put at their state (from as built, ON, back to OFF and on to their busy), the z-motion
    /// sectors' planes, path sectors on their first marker, generators, the level start channel.</summary>
    void NativeTrInit()
    {
        var b = Board;
        VelFloor = new int[b.SectorCount];
        VelCeil = new int[b.SectorCount];
        for (int i = 0; i < b.WallCount; i++)
        {
            b.BaseWallX[i] = b.Walls[i].X;
            b.BaseWallY[i] = b.Walls[i].Y;
        }
        foreach (var w in Walls)
            if (w.XState != 0)
                w.XBusy = 65536;
        foreach (var e in Sprites)
            e.BaseOrigin = e.Origin;
        _sprNearSect.Clear();
        if (IsModern)
            SprNearSectInit();
        for (int i = 0; i < b.SectorCount; i++)
        {
            b.BaseFloor[i] = b.Sectors[i].FloorZ;
            b.BaseCeil[i] = b.Sectors[i].CeilingZ;
        }
        foreach (var xs in Sectors)
        {
            if (xs.XState != 0)
                xs.XBusy = 65536;
            switch (xs.Type)
            {
                case K.kSectorCounter:
                    if (IsModern)
                        xs.TriggerOff = 0;
                    else
                        xs.TriggerOnce = 1;
                    EvPostCallback(xs, 0, K.kCallbackCounterCheck);
                    break;
                case K.kSectorSlideMarked:
                case K.kSectorSlide:
                case K.kSectorRotateMarked:
                case K.kSectorRotate:
                {
                    var m1 = xs.SMarker0 as Sprite;
                    var m2 = xs.Type == K.kSectorSlideMarked || xs.Type == K.kSectorSlide ? xs.SMarker1 as Sprite : null;
                    if (m1 != null)
                    {
                        var (x1, y1) = BuildXY(m1.Origin);
                        if (m2 != null)
                        {
                            var (x2, y2) = BuildXY(m2.Origin);
                            TranslateSector(xs, 0, -65536, x1, y1, x1, y1, m1.Ang, x2, y2, m2.Ang, xs.Type == K.kSectorSlide);
                        }
                        else
                            TranslateSector(xs, 0, -65536, x1, y1, x1, y1, 0, x1, y1, m1.Ang, xs.Type == K.kSectorRotate);
                        foreach (int w in b.SectorWalls(xs.B))
                        {
                            b.BaseWallX[w] = b.Walls[w].X;
                            b.BaseWallY[w] = b.Walls[w].Y;
                        }
                        foreach (var e in SpritesInB(xs.B))
                            e.BaseOrigin = e.Origin;
                        if (IsModern && _sprNearSect.TryGetValue(xs.B, out var nearSprites))
                            foreach (var e in nearSprites)
                                e.BaseOrigin = e.Origin;
                        if (m2 != null)
                        {
                            var (x2, y2) = BuildXY(m2.Origin);
                            TranslateSector(xs, 0, xs.XBusy, x1, y1, x1, y1, m1.Ang, x2, y2, m2.Ang, xs.Type == K.kSectorSlide);
                        }
                        else
                            TranslateSector(xs, 0, xs.XBusy, x1, y1, x1, y1, 0, x1, y1, m1.Ang, xs.Type == K.kSectorRotate);
                    }
                    ZTranslateSector(xs, xs.XBusy, 1);
                    break;
                }
                case K.kSectorZMotionSprite:
                case K.kSectorZMotion:
                    ZTranslateSector(xs, xs.XBusy, 1);
                    break;
                case K.kSectorPath:
                {
                    var m = FindPathMarker(xs.SData);
                    if (m != null)
                    {
                        xs.SMarker0 = m;
                        xs.SBasePath = m;
                        if (xs.XState != 0)
                            EvPost(xs, 0, K.kCmdOn, null);
                    }
                    break;
                }
            }
        }
        foreach (var e in Sprites.ToArray())
        {
            if (!e.HasX || e.Removed || e.NoX != 0)
                continue;
            if (e.XState != 0)
                e.XBusy = 65536;
            if (IsModern)
                ModernTrInitSprite(e);
            if (e.Type == K.kSwitchPadlock)
                e.TriggerOnce = 1;
            else if (e.Type >= K.kGenTrigger && e.Type <= K.kGenSound && e.Type != K.kGenMissileEctoSkull + 1000)
                InitGenerator(e);
            else if (e.Type == K.kThingArmedProxBomb)
                e.XProximity = 1;
            else if (e.Type == K.kThingFallingRock)
            {
                if (e.XState != 0) e.HitTag |= 7;
                else e.HitTag &= ~7;
            }
            if (e.XVector != 0)
                e.Cstat |= Cstat.Hitscan;
            if (e.XPush != 0)
                e.Cstat |= 4096;
            UpdateBox(e);
        }
        // (a modern map's level start waits for its players: NBlood starts them before trInit, and the map's player
        // controls answer the level start)
        if (IsModern)
        {
            _levelStartPending = true;
            return;
        }
        SendLevelStart();
    }

    bool _levelStartPending;

    void SendLevelStart()
    {
        EvSend(null, K.kChannelLevelStart, K.kCmdOn, null);
        if (IsModern)
            EvSend(null, KM.kChannelLevelStartNBLOOD, K.kCmdOn, null);
        if (GameType == K.kGameTypeCoop)
            EvSend(null, K.kChannelLevelStartCoop, K.kCmdOn, null);
        else if (GameType == K.kGameTypeBloodBath)
            EvSend(null, K.kChannelLevelStartMatch, K.kCmdOn, null);
        else if (GameType == K.kGameTypeTeams)
        {
            EvSend(null, K.kChannelLevelStartMatch, K.kCmdOn, null);
            EvSend(null, K.kChannelLevelStartTeamsOnly, K.kCmdOn, null);
        }
    }
}
