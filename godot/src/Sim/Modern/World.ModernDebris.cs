using System;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The debris sprites of a modern map (nnexts.cpp: gPhysSpritesList, debrisMove): any sprite a properties or velocity
/// changer gave physics (its physAttr: moved, falling, floating or swimming in water, pushed by touch, shots and
/// explosions), with a mass of its own made from its size.
/// </summary>
public sealed partial class World
{
    /// <summary>getSpriteMassBySize: the sprite's mass, made again when its size or picture changed.</summary>
    int GetSpriteMassBySize(Sprite e)
    {
        var cached = e.Mass ??= new Modern.SpriteMass();
        int seqId = -1;
        if (IsDudeSprite(e))
        {
            if (e.Type != KM.kDudeModernCustom && e.Type != K.kDudePodMother)
                seqId = Info.Dude(e.Type, Info.DI_seqStartID);
        }
        else
            seqId = Seq.Id(e);
        int clipDist = e.ClipDist;
        if (((seqId >= 0 && seqId == cached.SeqId) || e.Picnum == cached.Picnum) && e.XRepeat == cached.XRepeat
            && e.YRepeat == cached.YRepeat && clipDist == cached.ClipDist && cached.Mass != 0)
            return cached.Mass;
        int picnum = e.Picnum;
        int massDiv = 30, addMul = 2, subMul = 2;
        if (seqId >= 0 && SeqData.Exists(seqId))
            picnum = SeqData.FrameTile(seqId, 0);
        clipDist = (int)Blood.ClipLow(e.ClipDist, 1);
        int x = Tiles.W(picnum), y = Tiles.H(picnum);
        int xrepeat = e.XRepeat, yrepeat = e.YRepeat;
        massDiv = Tiles.SurfType(e.Picnum) switch
        {
            1 => 16, 2 => 18, 3 => 21, 4 => 25, 5 => 28, 6 => 26, 7 => 27, 8 => 35, 9 => 22, 10 => 37, 11 => 33, 12 => 36,
            13 => 24, 14 => 23, _ => massDiv,
        };
        int mass = ((x + y) * (clipDist / 2)) / massDiv;
        if (xrepeat > 64)
            mass += (xrepeat - 64) * addMul;
        else if (xrepeat < 64 && mass > 0)
        {
            for (int i = 64 - xrepeat; i > 0; i--)
                if ((mass -= subMul) <= 100 && subMul-- <= 1)
                {
                    mass -= i;
                    break;
                }
        }
        if (yrepeat > 64)
            mass += (yrepeat - 64) * addMul;
        else if (yrepeat < 64 && mass > 0)
        {
            for (int i = 64 - yrepeat; i > 0; i--)
                if ((mass -= subMul) <= 100 && subMul-- <= 1)
                {
                    mass -= i;
                    break;
                }
        }
        cached.Mass = mass <= 0 ? 1 + Random(10) : (int)Blood.ClipRange(mass, 1, 65535);
        cached.AirVel = (int)Blood.ClipRange(400 - cached.Mass, 32, 400);
        cached.Fraction = (int)Blood.ClipRange(60000 - (cached.Mass << 7), 8192, 60000);
        cached.XRepeat = e.XRepeat;
        cached.YRepeat = e.YRepeat;
        cached.Picnum = e.Picnum;
        cached.SeqId = seqId;
        cached.ClipDist = e.ClipDist;
        return cached.Mass;
    }

    /// <summary>A properties changer's physics settings for a sprite (its data2's digits: movement and gravity, touch,
    /// weapons, swimming and flying), or a thing turned to debris; the sprite joins or leaves the debris list.</summary>
    void SetDebrisPhysics(Sprite e, Sprite src, bool thing2debris)
    {
        int flags = e.PhysAttr;
        int oldFlags = flags;
        if (thing2debris)
        {
            if ((e.HitTag & K.kPhysMove) != 0) flags |= K.kPhysMove;
            else flags &= ~K.kPhysMove;
            if ((e.HitTag & K.kPhysGravity) != 0) flags |= K.kPhysGravity | K.kPhysFalling;
            else flags &= ~(K.kPhysGravity | K.kPhysFalling);
            e.HitTag &= ~(K.kPhysMove | K.kPhysGravity | K.kPhysFalling);
            e.XVel = e.YVel = e.ZVel = 0;
            e.RestState = e.XState;
        }
        else
        {
            var digits = new int[6];
            string s = src.Data2.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < digits.Length && i < s.Length; i++)
                digits[i] = s[i] >= '0' && s[i] <= '9' ? s[i] - '0' : 0;
            // the first digit: movement and gravity
            switch (digits[0])
            {
                case 0: flags &= ~K.kPhysMove; flags &= ~(K.kPhysGravity | K.kPhysFalling); break;
                case 1: flags |= K.kPhysMove; flags &= ~(K.kPhysGravity | K.kPhysFalling); break;
                case 2: flags &= ~K.kPhysMove; flags |= K.kPhysGravity | K.kPhysFalling; break;
                case 3: flags |= K.kPhysMove; flags |= K.kPhysGravity | K.kPhysFalling; break;
            }
            // the second: pushed by touch
            if (digits[1] == 0) flags &= ~KM.kPhysDebrisTouch;
            else if (digits[1] == 1) flags |= KM.kPhysDebrisTouch;
            // the third: weapons
            switch (digits[2])
            {
                case 0: flags &= ~KM.kPhysDebrisVector; flags &= ~KM.kPhysDebrisExplode; break;
                case 1: flags |= KM.kPhysDebrisVector; flags &= ~KM.kPhysDebrisExplode; break;
                case 2: flags &= ~KM.kPhysDebrisVector; flags |= KM.kPhysDebrisExplode; break;
                case 3: flags |= KM.kPhysDebrisVector; flags |= KM.kPhysDebrisExplode; break;
            }
            // the fourth: swimming and flying
            const int swimFly = KM.kPhysDebrisSwim | KM.kPhysDebrisFly | KM.kPhysDebrisFloat;
            switch (digits[3])
            {
                case 0: flags &= ~swimFly; break;
                case 1: flags = (flags & ~swimFly) | KM.kPhysDebrisSwim; break;
                case 2: flags = (flags & ~swimFly) | KM.kPhysDebrisSwim | KM.kPhysDebrisFloat; break;
                case 3: flags = (flags & ~swimFly) | KM.kPhysDebrisFly; break;
                case 4: flags = (flags & ~swimFly) | KM.kPhysDebrisFly | KM.kPhysDebrisFloat; break;
                case 5: flags = (flags & ~swimFly) | KM.kPhysDebrisSwim | KM.kPhysDebrisFly; break;
                case 6: flags |= swimFly; break;
            }
        }
        bool exists = _physSprites.Contains(e);
        if ((flags & K.kPhysGravity) != 0 || (flags & K.kPhysMove) != 0)
        {
            if (oldFlags == 0)
                e.XVel = e.YVel = e.ZVel = 0;
            e.PhysAttr = flags;
            if (!exists)
            {
                // a thing turns to debris: other physics
                if (e.StatNum == K.kStatThing)
                    e.StatNum = K.kStatDecoration;
                // swimming ones start turning
                if ((flags & KM.kPhysDebrisSwim) != 0 && e.XVel == 0 && e.YVel == 0 && e.ZVel == 0)
                    e.GoalAng = (e.Ang + Random3(K.kAng45)) & 2047;
                if ((e.PhysAttr & KM.kPhysDebrisVector) != 0)
                {
                    e.Cstat |= Cstat.Hitscan;
                    UpdateBox(e);
                }
                _physSprites.Add(e);
                GetSpriteMassBySize(e);
            }
        }
        else if (exists)
        {
            e.PhysAttr = flags;
            e.XVel = e.YVel = e.ZVel = 0;
            if (IsThingSprite(e))
                e.StatNum = K.kStatThing;
            _physSprites.Remove(e);
        }
    }

    /// <summary>debrisConcuss: an explosion pushes and damages a debris sprite.</summary>
    void DebrisConcuss(Sprite owner, Sprite e, double x, double y, double z, double dmg)
    {
        if (!e.HasX)
            return;
        double dx = e.BX - x, dy = e.BY - y, dz = Blood.Sar(e.BZ - z, 4);
        dmg = Blood.Int(0x40000 * dmg / (0x40000 + dx * dx + dy * dy + dz * dz));
        double size = Blood.Sar((double)Tiles.W(e.Picnum) * e.XRepeat * Tiles.H(e.Picnum) * e.YRepeat, 1);
        bool thing = IsThingSprite(e);
        if ((e.PhysAttr & KM.kPhysDebrisExplode) != 0)
        {
            int mass = e.Mass?.Mass ?? 0;
            if (mass > 0)
            {
                double t = Blood.Int(dmg * size / mass);
                e.XVel += Blood.MulScale16(t, dx);
                e.YVel += Blood.MulScale16(t, dy);
                e.ZVel += Blood.MulScale16(t, dz);
            }
            if (thing)
                e.StatNum = K.kStatThing;       // for the damage only
        }
        ActDamageSprite(owner, e, K.kDamageExplode, dmg);
        if (thing && !e.Removed && e.StatNum == K.kStatThing)
            e.StatNum = K.kStatDecoration;
    }

    void DebrisBubble(Sprite e)
    {
        var (top, bottom) = Extents(e);
        int count = 1 + Random(5);
        for (int i = 0; i < count; i++)
        {
            double nDist = (e.XRepeat * (Tiles.W(e.Picnum) >> 1)) >> 2;
            int nAngle = Random(2048);
            double x = e.BX + Blood.MulScale30(nDist, Blood.Cos(nAngle));
            double y = e.BY + Blood.MulScale30(nDist, Blood.Sin(nAngle));
            double z = bottom - Random((int)(bottom - top));
            var fx = FxSpawnIn(K.FX_23 + Random(3), SpriteBSector(e), x, y, z);
            if (fx != null)
            {
                fx.XVel = e.XVel + Random2(0x1aaaa);
                fx.YVel = e.YVel + Random2(0x1aaaa);
                fx.ZVel = e.ZVel + Random2(0x1aaaa);
            }
        }
        if (Chance(0x2000))
            EvPostCallback(e, 0, K.kCallbackEnemeyBubble);
    }

    /// <summary>idListProcessPhysSprite: a debris sprite's game frame: the floor's push, air drag, a player's push,
    /// its move, its slow turn (bubbling under water), and kept between floor and ceiling.</summary>
    int ProcessPhysSprite(Sprite e)
    {
        if (!e.HasX)
            return ListRemove;
        if ((e.PhysAttr & K.kPhysMove) == 0 && (e.PhysAttr & K.kPhysGravity) == 0)
            return ListRemove;
        int sect = SpriteBSector(e);
        if (sect < 0)
            return ListOk;
        var xs = XSectorOf(sect);
        int mass = e.Mass?.Mass ?? GetSpriteMassBySize(e);
        int airVel = e.Mass.AirVel;
        var (top, bottom) = Extents(e);
        if (xs != null)
        {
            if (xs.SUnderwater != 0)
                airVel <<= 6;
            if (xs.PanVel != 0 && Board.FloorZAt(sect, e.BX, e.BY) <= bottom)
            {
                int angle = xs.PanAngle;
                double speed = 0;
                if (xs.PanAlways != 0 || xs.XState != 0 || xs.XBusy != 0)
                {
                    speed = xs.PanVel << 9;
                    if (xs.PanAlways == 0 && xs.XBusy != 0)
                        speed = Blood.MulScale16(speed, xs.XBusy);
                }
                if ((Board.Sectors[sect].FloorStat & 64) != 0)
                {
                    var w = Board.Walls[Board.Sectors[sect].WallPtr];
                    var w2 = Board.Walls[w.Point2];
                    angle = (angle + Blood.GetAngle(w2.X - w.X, w2.Y - w.Y) + 512) & 2047;
                }
                e.XVel += Blood.MulScale30(speed, Blood.Cos(angle));
                e.YVel += Blood.MulScale30(speed, Blood.Sin(angle));
            }
        }
        ActAirDrag(e, airVel);
        if ((e.PhysAttr & KM.kPhysDebrisTouch) != 0)
        {
            foreach (var p in Players)
            {
                if (!p.Ready || p.Removed)
                    continue;
                if (p.Hit == Hit.Sprite && p.HitEnt == e)
                {
                    double nSpeed = Blood.ApproxDist(p.XVel, p.YVel);
                    nSpeed = Blood.ClipLow(nSpeed - Blood.Sar(nSpeed * mass, 6), 0x9000 - (mass << 3));
                    e.XVel += Blood.MulScale30(nSpeed, Blood.Cos(p.Ang));
                    e.YVel += Blood.MulScale30(nSpeed, Blood.Sin(p.Ang));
                    e.Hit = Hit.Sprite;
                    e.HitEnt = p;
                }
            }
        }
        if ((e.PhysAttr & K.kPhysGravity) != 0)
            e.PhysAttr |= K.kPhysFalling;
        if ((e.PhysAttr & K.kPhysFalling) != 0 || e.XVel != 0 || e.YVel != 0 || e.ZVel != 0 || VelFloor[sect] != 0 || VelCeil[sect] != 0)
            DebrisMove(e);
        if (e.Removed)
            return ListRemove;
        if (e.XVel != 0 || e.YVel != 0)
            e.GoalAng = Blood.GetAngle(e.XVel, e.YVel) & 2047;
        int ang = e.Ang & 2047;
        bool uwater = SpriteIsUnderwater(e);
        if (!uwater)
            EvKillCallback(e, K.kCallbackEnemeyBubble);
        else if (Chance(0x1000 - mass))
        {
            if (e.ZVel > 0x100)
                DebrisBubble(e);
            if (ang == e.GoalAng)
            {
                e.GoalAng = (e.Ang + Random3(K.kAng60)) & 2047;
                DebrisBubble(e);
            }
        }
        int angStep = (int)Blood.ClipLow(Blood.MulScale8(1, Blood.Sar(Math.Abs(e.XVel) + Math.Abs(e.YVel), 5)), uwater ? 1 : 0);
        if (ang < e.GoalAng)
            e.Ang = (int)Blood.ClipHigh(ang + angStep, e.GoalAng);
        else if (ang > e.GoalAng)
            e.Ang = (int)Blood.ClipLow(ang - angStep, e.GoalAng);
        sect = SpriteBSector(e);
        if (sect >= 0)
        {
            double cz = Board.CeilZAt(sect, e.BX, e.BY), fz = Board.FloorZAt(sect, e.BX, e.BY);
            (top, bottom) = Extents(e);
            if (fz >= bottom && LowerLinkOf(sect) == null && (Board.Sectors[sect].CeilingStat & 1) == 0)
                e.SetBZ(e.BZ + Blood.ClipLow(cz - top, 0));
            (top, bottom) = Extents(e);
            if (cz <= top && UpperLinkOf(sect) == null && (Board.Sectors[sect].FloorStat & 1) == 0)
                e.SetBZ(e.BZ + Blood.ClipHigh(fz - bottom, 0));
        }
        return ListOk;
    }

    /// <summary>debrisMove: a debris sprite's movement: its clipped move and bounce off walls, gravity or buoyancy,
    /// links, the floor (bouncing, splashes) and ceiling, an Impact trigger on what it hits, and the floor's drag.</summary>
    void DebrisMove(Sprite e)
    {
        int sect = SpriteBSector(e);
        var (top, bottom) = Extents(e);
        int moveHit = 0;
        double floorDist = Blood.Sar(bottom - e.BZ, 2), ceilDist = Blood.Sar(e.BZ - top, 2);
        double clipDist = e.ClipDist << 2;
        int mass = e.Mass.Mass;
        int tmpFraction = e.Mass.Fraction;
        var xs = XSectorOf(sect);
        bool uwater = false;
        if (xs != null && xs.SUnderwater != 0)
        {
            tmpFraction >>= 1;
            uwater = true;
        }
        if (e.XVel != 0 || e.YVel != 0)
        {
            int oldcstat = e.Cstat;
            e.Cstat &= ~(Cstat.Block | Cstat.Hitscan);
            UpdateBox(e);
            moveHit = e.Hit = ClipMove(e, Blood.Sar(e.XVel, 12), Blood.Sar(e.YVel, 12), clipDist, ceilDist, floorDist);
            e.HitEnt = _clip.Ent;
            e.Cstat = oldcstat;
            UpdateBox(e);
            if (e.Hit == Hit.Wall)
                (e.XVel, e.YVel) = WallBounce(e.XVel, e.YVel, tmpFraction);
        }
        sect = SpriteBSector(e);
        if (sect < 0)
            return;
        xs = XSectorOf(sect);
        if (xs != null)
            uwater = xs.SUnderwater != 0;
        if (e.ZVel != 0)
            e.SetBZ(e.BZ + Blood.Sar(e.ZVel, 8));
        var zr = GetZRange(e, clipDist);
        (top, bottom) = Extents(e);
        if ((e.PhysAttr & KM.kPhysDebrisSwim) != 0 && uwater)
        {
            double vc = 0;
            double cz = Board.CeilZAt(sect, e.BX, e.BY), fz = Board.FloorZAt(sect, e.BX, e.BY);
            double div = Blood.ClipLow(bottom - top, 1);
            if (LowerLinkOf(sect) != null)
                cz += cz < 0 ? 0x500 : -0x500;
            if (top > cz && ((e.PhysAttr & KM.kPhysDebrisFloat) == 0 || fz <= bottom * 4))
                e.ZVel -= Blood.Int(Blood.Sar(bottom - zr.CeilZ, 6) * 256 / mass);
            if (fz < bottom)
                vc = 58254 + Blood.Int((bottom - fz) * -80099 / div);
            if (vc != 0)
            {
                e.SetBZ(e.BZ + Blood.Sar(Blood.Sar(vc * 4, 1), 8));
                e.ZVel += vc;
            }
        }
        else if ((e.PhysAttr & K.kPhysGravity) != 0 && bottom < zr.FloorZ)
        {
            e.SetBZ(e.BZ + 455);
            e.ZVel += 58254;
        }
        int link = CheckLink(e);
        if (link != 0)
        {
            zr = GetZRange(e, clipDist);
            if ((e.Cstat & Cstat.Invisible) == 0 && (link == K.kMarkerUpWater || link == K.kMarkerUpGoo))
            {
                int pitch = 150000 - (mass << 9) + Random3(8192);
                Sound3DCP(e, 720, -1, 0, pitch, 75 - Random(40));
                if (!SpriteIsUnderwater(e))
                    EvKillCallback(e, K.kCallbackEnemeyBubble);
                else
                {
                    EvPostCallback(e, 0, K.kCallbackEnemeyBubble);
                    for (int i = 2; i <= 5; i++)
                        if (Chance(0x5000 * i))
                            EvPostCallback(e, Random(5), K.kCallbackEnemeyBubble);
                }
            }
            sect = SpriteBSector(e);
        }
        (top, bottom) = Extents(e);
        if (zr.FloorZ <= bottom)
        {
            e.FlorHit = zr.FloorHit;
            e.FlorHitEnt = zr.FloorEnt;
            double velFloor = sect >= 0 ? VelFloor[sect] : 0;
            double v30 = e.ZVel - velFloor;
            if (v30 > 0)
            {
                e.PhysAttr |= K.kPhysFalling;
                double xv = e.XVel, yv = e.YVel;
                Physics.FloorBounceVector(ref xv, ref yv, ref v30, tmpFraction, FloorNormal);
                e.XVel = xv;
                e.YVel = yv;
                e.ZVel = v30;
                if (Math.Abs(e.ZVel) < 0x10000)
                {
                    e.ZVel = velFloor;
                    e.PhysAttr &= ~K.kPhysFalling;
                }
                moveHit = zr.FloorHit != 0 ? zr.FloorHit : Hit.Sector;
                switch (zr.FloorSurf)
                {
                    case K.kSurfLava:
                    {
                        var fx = FxSpawnIn(K.FX_10, sect, e.BX, e.BY, zr.FloorZ);
                        if (fx == null)
                            break;
                        for (int i = 0; i < 7; i++)
                        {
                            var fx2 = FxSpawnIn(K.FX_14, sect, fx.BX, fx.BY, fx.BZ);
                            if (fx2 == null)
                                continue;
                            fx2.XVel = Random2(0x6aaaa);
                            fx2.YVel = Random2(0x6aaaa);
                            fx2.ZVel = -Random(0xd5555);
                        }
                        break;
                    }
                    case K.kSurfWater:
                        FxSpawnIn(K.FX_9, sect, e.BX, e.BY, zr.FloorZ);
                        break;
                }
            }
            else if (e.ZVel == 0)
                e.PhysAttr &= ~K.kPhysFalling;
        }
        else
        {
            e.FlorHit = 0;
            if ((e.PhysAttr & K.kPhysGravity) != 0)
                e.PhysAttr |= K.kPhysFalling;
        }
        if (top <= zr.CeilZ)
        {
            e.CeilHit = zr.CeilHit;
            e.CeilHitEnt = zr.CeilEnt;
            moveHit = zr.CeilHit != 0 ? zr.CeilHit : Hit.Sector;
            e.SetBZ(e.BZ + Blood.ClipLow(zr.CeilZ - top, 0));
            if (e.ZVel <= 0 && (e.PhysAttr & K.kPhysFalling) != 0)
                e.ZVel = Blood.MulScale16(-e.ZVel, 0x2000);
        }
        else
        {
            e.CeilHit = 0;
            (top, bottom) = Extents(e);
        }
        if (moveHit != 0 && e.XImpact != 0 && e.XLocked == 0 && e.IsTriggered == 0 && (e.XState == e.RestState || e.Interruptable != 0))
        {
            if (IsThingSprite(e))
                e.StatNum = K.kStatThing;
            TrTriggerSprite(e, K.kCmdToggle, e);
            if (e.Removed)
                return;
        }
        if (e.XVel == 0 && e.YVel == 0)
            return;
        if (zr.FloorHit == Hit.Sprite && zr.FloorEnt is Sprite hs && (hs.Cstat & 0x30) == 0)
        {
            e.XVel += e.BX - hs.BX;
            e.YVel += e.BY - hs.BY;
            return;
        }
        e.XHeight = Blood.Sar(Blood.ClipLow(zr.FloorZ - bottom, 0), 8);
        if (uwater || e.XHeight >= 0x100)
            return;
        double nDrag = 0x2a00;
        if (e.XHeight > 0)
            nDrag -= Blood.Int(nDrag * e.XHeight / 0x100);
        e.XVel -= Blood.MulScale16R(e.XVel, nDrag);
        e.YVel -= Blood.MulScale16R(e.YVel, nDrag);
        if (Blood.ApproxDist(e.XVel, e.YVel) < 0x1000)
            e.XVel = e.YVel = 0;
    }
}
