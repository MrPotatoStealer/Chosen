using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>
/// The player's weapons (weapon.cpp, via Blood3D's server/weapon.qc). Weapon animations are QAVs: the game times
/// them (their sounds and fire callbacks); the client draws their layers.
/// </summary>
public sealed partial class World
{
    public int AutoAim = 1;               // gAutoAim (cl_autoaim): 0 never, 1 always, 2 hitscan only, 3 pitchfork only
    public int WeaponSwitch = 1;          // gWeaponSwitch: 1 = switch to a better weapon on pickup

    static int WeaponModeCount(int w) => w <= 0 || w >= 13 ? 0 : 1;
    static int WeaponModeAmmo(int w) => w == 0 || w == 1 || w == 13 ? -1 : w - 1;

    static bool CheckLitSprayOrTNT(Player p) =>
        (p.CurWeapon == K.kWeaponSprayCan && p.WeaponState is 5 or 6 or 7)
        || (p.CurWeapon == K.kWeaponTNT && p.WeaponState is 4 or 5 or 6);

    static bool BannedUnderwater(int w) => w == K.kWeaponSprayCan || w == K.kWeaponTNT;

    bool CheckWeaponAmmo(Player p, int nWeapon, int nAmmo, int minAmmo)
    {
        if (InfiniteAmmo || nAmmo == -1) return true;
        if (nWeapon == K.kWeaponRemoteTNT && p.WeaponAmmo == 11 && p.WeaponState == 11) return true;
        if (nWeapon == K.kWeaponLifeLeech && p.XHealth > 0) return true;
        return p.Ammo[nAmmo] >= minAmmo;
    }

    bool CheckAmmo(Player p, int nAmmo, int minAmmo)
    {
        if (InfiniteAmmo || nAmmo == -1) return true;
        if (p.CurWeapon == K.kWeaponRemoteTNT && p.WeaponAmmo == 11 && p.WeaponState == 11) return true;
        if (p.CurWeapon == K.kWeaponLifeLeech && p.XHealth >= minAmmo * 16) return true;
        return p.Ammo[nAmmo] >= minAmmo;
    }

    bool CheckAmmo2(Player p, int ammotype, int amount) => InfiniteAmmo || ammotype == -1 || p.Ammo[ammotype] >= amount;

    void SpawnBulletEject(Player p, double a2, double a3)
    {
        p.ZView = p.BZ - p.PostureInfo(Player.PO_eyeAboveZ);
        double dz = p.ZWeapon - Blood.Int((p.ZWeapon - p.ZView) / 2);
        FxSpawnEjectingBrass(p, dz, a2, a3);
    }

    void SpawnShellEject(Player p, double a2, double a3)
    {
        p.ZView = p.BZ - p.PostureInfo(Player.PO_eyeAboveZ);
        double t = p.ZWeapon - p.ZView;
        double dz = p.ZWeapon - t + Blood.Sar(t, 2);
        FxSpawnEjectingShell(p, dz, a2, a3);
    }

    // ---------------------------------------------------------------- QAV timing (qav.cpp QAV::Play)
    void QavPlay(Player p, double start, double end, Action<int, Player> cb)
    {
        int q = p.WeaponQav;
        if (q < 0)
            return;
        int tpf = QavData.Ticks(q);
        if (tpf <= 0)
            return;
        int nf = QavData.FrameCount(q);
        int frame = start < 0 ? (int)Blood.Int((start + 1) / tpf) : (int)Blood.Int(start / tpf) + 1;
        for (double ticks = tpf * frame; ticks <= end; frame++, ticks += tpf)
        {
            if (frame < 0 || frame >= nf)
                continue;
            int fi = QavData.First(q) + frame;
            int snd = QavData.FrameSound(fi);
            int qcb = QavData.FrameCallback(fi);
            int prio = (qcb >> 8) & 255;
            int cbid = qcb < 0 ? 0 : qcb & 255;     // -1 = no callback (Blood calls it only for nCallbackId > 0)
            if (snd > 0)
                Sound3D(p, snd, 16 + prio, 6);
            if (cbid > 0 && cb != null)
                cb(cbid, p);
        }
    }

    void WeaponPlay(Player p)
    {
        if (p.WeaponQav == -1)
            return;
        double nTicks = QavData.Duration(p.WeaponQav) - p.WeaponTimer;
        QavPlay(p, nTicks - GameClock.TicsPerFrame, nTicks, p.QavCallback);
    }

    void StartQAV(Player p, int nWeaponQAV, Action<int, Player> cb = null, bool loop = false)
    {
        p.WeaponQav = nWeaponQAV;
        p.WeaponTimer = QavData.Duration(nWeaponQAV);
        p.QavCallback = cb;
        p.QavLoop = loop;
        p.WeaponQavStart = FrameClock;
        WeaponPlay(p);
        p.WeaponTimer -= GameClock.TicsPerFrame;
    }

    // ---------------------------------------------------------------- aim (UpdateAimVector)
    // WEAPONTRACK: { xspeed, yspeed, angleRange, thingAngleRange, predict, isProjectile }
    static readonly int[,] WeaponTrackTable =
    {
        { 0, 0, 0, 0, 0, 0 },
        { 24576, 24576, 113, 85, 1118481, 0 },
        { 32768, 32768, 113, 85, 2796202, 1 },
        { 65536, 65536, 56, 28, 0, 0 },
        { 24576, 32768, 56, 28, 0, 0 },
        { 24576, 24576, 56, 28, 2796202, 1 },
        { 24576, 24576, 113, 85, 0, 1 },
        { 24576, 24576, 113, 56, 0, 1 },
        { 32768, 65536, 113, 85, 2446677, 1 },
        { 65536, 65536, 113, 0, 0, 1 },
        { 65536, 65536, 170, 0, 0, 0 },
        { 24576, 24576, 113, 85, 0, 1 },
        { 24576, 24576, 113, 85, 0, 1 },
        { 24576, 24576, 113, 85, 0, 0 },
    };

    static int WeaponTrack(int w, int f) => w >= 1 && w <= 13 ? WeaponTrackTable[w, f] : 0;

    void UpdateAimVector(Player p)
    {
        double x = p.BX, y = p.BY, z = p.ZWeapon;
        double adx = Blood.Sar(Blood.Cos(p.Ang), 16), ady = Blood.Sar(Blood.Sin(p.Ang), 16), adz = p.Slope;
        Sprite nTarget = null;
        p.AimTargetsCount = 0;
        bool autoaimW = p.CurWeapon == K.kWeaponVoodoo || p.CurWeapon == K.kWeaponLifeLeech || (AutoAim == 3 && p.CurWeapon == K.kWeaponPitchfork);
        int predict = WeaponTrack(p.CurWeapon, 4);
        if (AutoAim == 1 || (AutoAim == 2 && WeaponTrack(p.CurWeapon, 5) == 0) || autoaimW)
        {
            double nClosest = 2147483647;
            foreach (var e in StatList(K.kStatDude))
            {
                if (e == p || (!FriendlyFire && IsTargetTeammate(p, e)) || (e.HitTag & 32) != 0 || (e.HitTag & 8) == 0)
                    continue;
                double x2 = e.BX, y2 = e.BY, z2 = e.BZ;
                double nDist = Blood.ApproxDist(x2 - x, y2 - y);
                if (nDist == 0 || nDist > 51200)
                    continue;
                if (predict != 0)
                {
                    double t = Blood.Int(nDist * 4096 / predict);
                    x2 += Blood.Sar(e.XVel * t, 12);
                    y2 += Blood.Sar(e.YVel * t, 12);
                    z2 += Blood.Sar(e.ZVel * t, 8);
                }
                double lx = x + Blood.MulScale30(Blood.Cos(p.Ang), nDist);
                double ly = y + Blood.MulScale30(Blood.Sin(p.Ang), nDist);
                double lz = z + Math.Floor(p.Slope * nDist / 1024);
                double zRange = Math.Floor(9460 * nDist / 1024);
                var (top, bottom) = Extents(e);
                if (lz - zRange > bottom || lz + zRange < top)
                    continue;
                int angle = Blood.GetAngle(x2 - x, y2 - y);
                if (Math.Abs(Blood.AngMask(angle - p.Ang + 1024) - 1024) > WeaponTrack(p.CurWeapon, 2))
                    continue;
                if (p.AimTargetsCount < 16 && CanSee(x, y, z, x2, y2, z2, p, e))
                    p.AimTargets[p.AimTargetsCount++] = e;
                double dz = Blood.Sar(lz - z2, 8), dy = Blood.Sar(ly - y2, 4), dx = Blood.Sar(lx - x2, 4);
                double nDist2 = Math.Floor(Math.Sqrt(dx * dx + dy * dy + dz * dz));
                if (nDist2 >= nClosest)
                    continue;
                double center = e.YRepeat * Info.Dude(e.Type, K.DI_aimHeight) * 4;
                double dzCenter = (z2 - center) - z;
                if (CanSee(x, y, z, x2, y2, z2, p, e))
                {
                    nClosest = nDist2;
                    adx = Blood.Sar(Blood.Cos(angle), 16);
                    ady = Blood.Sar(Blood.Sin(angle), 16);
                    adz = Blood.DivScale10(dzCenter, nDist);
                    nTarget = e;
                }
            }
            if (WeaponTrack(p.CurWeapon, 3) > 0)
            {
                foreach (var e in StatList(K.kStatThing))
                {
                    if ((!FriendlyFire && IsTargetTeammate(p, e)) || (e.HitTag & 8) == 0)
                        continue;
                    double x2 = e.BX, y2 = e.BY, z2 = e.BZ;
                    double dx = x2 - x, dy = y2 - y, dz = z2 - z;
                    double nDist = Blood.ApproxDist(dx, dy);
                    if (nDist == 0 || nDist > 51200)
                        continue;
                    double lx = x + Blood.MulScale30(Blood.Cos(p.Ang), nDist);
                    double ly = y + Blood.MulScale30(Blood.Sin(p.Ang), nDist);
                    double lz = z + Math.Floor(p.Slope * nDist / 1024);
                    double zRange = Math.Floor(9460 * nDist / 1024);
                    var (top, bottom) = Extents(e);
                    if (lz - zRange > bottom || lz + zRange < top)
                        continue;
                    int angle = Blood.GetAngle(dx, dy);
                    if (Math.Abs(Blood.AngMask(angle - p.Ang + 1024) - 1024) > WeaponTrack(p.CurWeapon, 3))
                        continue;
                    if (p.AimTargetsCount < 16 && CanSee(x, y, z, x2, y2, z2, p, e))
                        p.AimTargets[p.AimTargetsCount++] = e;
                    double dz2 = Blood.Sar(lz - z2, 8), dy2 = Blood.Sar(ly - y2, 4), dx2 = Blood.Sar(lx - x2, 4);
                    double nDist2 = Math.Floor(Math.Sqrt(dx2 * dx2 + dy2 * dy2 + dz2 * dz2));
                    if (nDist2 >= nClosest)
                        continue;
                    if (CanSee(x, y, z, x2, y2, z2, p, e))
                    {
                        nClosest = nDist2;
                        adx = Blood.Sar(Blood.Cos(angle), 16);
                        ady = Blood.Sar(Blood.Sin(angle), 16);
                        adz = Blood.DivScale10(dz, nDist);
                        nTarget = e;
                    }
                }
            }
        }
        var r = Blood.RotateVector(adx, ady, -p.Ang);
        double rz = adz - p.Slope;
        p.RelAimDx = Blood.Interpolate(p.RelAimDx, r.x, WeaponTrack(p.CurWeapon, 0));
        p.RelAimDy = Blood.Interpolate(p.RelAimDy, r.y, WeaponTrack(p.CurWeapon, 0));
        p.RelAimDz = Blood.Interpolate(p.RelAimDz, rz, WeaponTrack(p.CurWeapon, 1));
        var a = Blood.RotateVector(p.RelAimDx, p.RelAimDy, p.Ang);
        p.AimDx = a.x;
        p.AimDy = a.y;
        p.AimDz = p.RelAimDz + p.Slope;
        p.AimTarget = nTarget;
    }

    // ---------------------------------------------------------------- fire functions
    double WZ(Player p) => p.ZWeapon - p.BZ;

    void FirePitchfork(int nTrigger, Player p)
    {
        if (nTrigger == 1024)
        {
            for (int i = 0; i < 4; i++)
                ActFireVector(p, 0, WZ(p), p.AimDx, p.AimDy, p.AimDz, K.kVectorTine);
        }
        else
        {
            int r1 = Random2(2000), r2 = Random2(2000), r3 = Random2(2000);
            for (int i = 0; i < 4; i++)
                ActFireVector(p, (2 * i - 3) * 40, WZ(p), p.AimDx + r1, p.AimDy + r2, p.AimDz + r3, K.kVectorTine);
        }
    }

    void FireSpray(int nTrigger, Player p)
    {
        PlayerFireMissile(p, 0, p.AimDx, p.AimDy, p.AimDz, K.kMissileFlameSpray);
        UseAmmo(p, 6, 4);
        if (CheckAmmo(p, 6, 1))
            Sound3D(p, 441, 1, 2);
        else
            Kill3DSound(p, -1, 441);
    }

    static int SprayCanCount() => Info.AmmoItem(K.kItemAmmoSprayCan, 6);

    void ThrowCan(int nTrigger, Player p)
    {
        Kill3DSound(p, -1, 441);
        double nSpeed = Blood.MulScale16(p.ThrowPower, 1537911) + 419430;
        Sound3D(p, 455, 1);
        var t = PlayerFireThing(p, 0, -9460, K.kThingArmedSpray, nSpeed);
        if (t != null)
        {
            Sound3D(t, 441);
            t.Shade = -128;
            EvPost(t, (int)p.FuseTime, K.kCmdOn, p);
            t.XImpact = 1;
            UseAmmo(p, 6, SprayCanCount());
            p.ThrowPower = 0;
        }
    }

    void DropCan(int nTrigger, Player p)
    {
        Kill3DSound(p, -1, 441);
        var t = PlayerFireThing(p, 0, 0, K.kThingArmedSpray, 0);
        if (t != null)
        {
            EvPost(t, (int)p.FuseTime, K.kCmdOn, p);
            UseAmmo(p, 6, SprayCanCount());
        }
    }

    void ExplodeCan(int nTrigger, Player p)
    {
        Kill3DSound(p, -1, 441);
        var t = PlayerFireThing(p, 0, 0, K.kThingArmedSpray, 0);
        EvPost(t, 0, K.kCmdOn, p);
        UseAmmo(p, 6, SprayCanCount());
        StartQAV(p, 15);
        p.CurWeapon = K.kWeaponNone;
        p.ThrowPower = 0;
    }

    void ThrowBundle(int nTrigger, Player p)
    {
        Kill3DSound(p, 16);
        double nSpeed = Blood.MulScale16(p.ThrowPower, 1537911) + 419430;
        Sound3D(p, 455, 1);
        var t = PlayerFireThing(p, 0, -9460, K.kThingArmedTNTBundle, nSpeed);
        if (p.FuseTime < 0)
            t.XImpact = 1;
        else
            EvPost(t, (int)p.FuseTime, K.kCmdOn, p);
        UseAmmo(p, 5, 1);
        p.ThrowPower = 0;
    }

    void DropBundle(int nTrigger, Player p)
    {
        Kill3DSound(p, 16);
        var t = PlayerFireThing(p, 0, 0, K.kThingArmedTNTBundle, 0);
        EvPost(t, (int)p.FuseTime, K.kCmdOn, p);
        UseAmmo(p, 5, 1);
    }

    void ExplodeBundle(int nTrigger, Player p)
    {
        Kill3DSound(p, 16);
        var t = PlayerFireThing(p, 0, 0, K.kThingArmedTNTBundle, 0);
        EvPost(t, 0, K.kCmdOn, p);
        UseAmmo(p, 5, 1);
        StartQAV(p, 24);
        p.CurWeapon = K.kWeaponNone;
        p.ThrowPower = 0;
    }

    void ThrowProx(int nTrigger, Player p)
    {
        double nSpeed = Blood.MulScale16(p.ThrowPower, 1537911) + 419430;
        Sound3D(p, 455, 1);
        var t = PlayerFireThing(p, 0, -9460, K.kThingArmedProxBomb, nSpeed);
        EvPost(t, 240, K.kCmdOn, p);
        UseAmmo(p, 10, 1);
        p.ThrowPower = 0;
    }

    void DropProx(int nTrigger, Player p)
    {
        var t = PlayerFireThing(p, 0, 0, K.kThingArmedProxBomb, 0);
        EvPost(t, 240, K.kCmdOn, p);
        UseAmmo(p, 10, 1);
    }

    void SetRemoteChannel(Sprite t, Player p)
    {
        if (t.RxID > 0 && _receivers.TryGetValue(t.RxID, out var old))
            old.Remove(t);
        t.RxID = 90 + (p.Type - K.kDudePlayer1);
        AddReceiver(t);
    }

    void ThrowRemote(int nTrigger, Player p)
    {
        double nSpeed = Blood.MulScale16(p.ThrowPower, 1537911) + 419430;
        Sound3D(p, 455, 1);
        var t = PlayerFireThing(p, 0, -9460, K.kThingArmedRemoteBomb, nSpeed);
        SetRemoteChannel(t, p);
        UseAmmo(p, 11, 1);
        p.ThrowPower = 0;
    }

    void DropRemote(int nTrigger, Player p)
    {
        var t = PlayerFireThing(p, 0, 0, K.kThingArmedRemoteBomb, 0);
        SetRemoteChannel(t, p);
        UseAmmo(p, 11, 1);
    }

    void FireRemote(int nTrigger, Player p) => EvSend(p, 90 + (p.Type - K.kDudePlayer1), K.kCmdOn, p);

    void FireShotgun(int nTrigger, Player p)
    {
        if (nTrigger == 1)
        {
            Sound3D(p, 411, 2);
            p.TiltEffect = 30;
            p.Visibility = 20;
        }
        else
        {
            Sound3D(p, 412, 2);
            p.TiltEffect = 50;
            p.Visibility = 40;
        }
        int n = nTrigger * 16;
        for (int i = 0; i < n; i++)
        {
            int r1, r2, r3, nType;
            if (nTrigger == 1)
            {
                r1 = Random3(1500);
                r2 = Random3(1500);
                r3 = Random3(500);
                nType = K.kVectorShell;
            }
            else
            {
                r1 = Random3(2500);
                r2 = Random3(2500);
                r3 = Random3(1500);
                nType = K.kVectorShellAP;
            }
            ActFireVector(p, 0, WZ(p), p.AimDx + r1, p.AimDy + r2, p.AimDz + r3, nType);
        }
        UseAmmo(p, p.WeaponAmmo, nTrigger);
        p.FlashEffect = 1;
    }

    void EjectShell(int nTrigger, Player p)
    {
        SpawnShellEject(p, 25, 35);
        SpawnShellEject(p, 48, 35);
    }

    void FireTommy(int nTrigger, Player p)
    {
        Sound3D(p, 431, -1);
        if (nTrigger == 1)
        {
            int r1 = Random3(400), r2 = Random3(1200), r3 = Random3(1200);
            ActFireVector(p, 0, WZ(p), p.AimDx + r3, p.AimDy + r2, p.AimDz + r1, K.kVectorTommyRegular);
            SpawnBulletEject(p, -15, -45);
            p.Visibility = 20;
        }
        else if (nTrigger == 2)
        {
            int r1 = Random3(400), r2 = Random3(1200), r3 = Random3(1200);
            ActFireVector(p, -120, WZ(p), p.AimDx + r3, p.AimDy + r2, p.AimDz + r1, K.kVectorTommyRegular);
            SpawnBulletEject(p, -140, -45);
            r1 = Random3(400);
            r2 = Random3(1200);
            r3 = Random3(1200);
            ActFireVector(p, 120, WZ(p), p.AimDx + r3, p.AimDy + r2, p.AimDz + r1, K.kVectorTommyRegular);
            SpawnBulletEject(p, 140, 45);
            p.Visibility = 30;
        }
        UseAmmo(p, p.WeaponAmmo, nTrigger);
        p.FlashEffect = 1;
    }

    static double SpreadAngle(Player p, int nTrigger) =>
        Blood.AngMask(Blood.GetAngle(p.AimDx, p.AimDy) + (Blood.Int(112.0 * (nTrigger - 1) / 14) - 56));

    void SpreadShot(Player p, double offset, double dx, double dy, double ejX, double ejY)
    {
        int r1 = Random3(300), r2 = Random3(600), r3 = Random3(600);
        ActFireVector(p, offset, WZ(p), dx + r3, dy + r2, p.AimDz + r1, K.kVectorTommyAP);
        SpawnBulletEject(p, ejX, ejY);
    }

    void FireSpread(int nTrigger, Player p)
    {
        double angle = SpreadAngle(p, nTrigger);
        double dx = Blood.Sar(Blood.Cos(angle), 16), dy = Blood.Sar(Blood.Sin(angle), 16);
        Sound3D(p, 431, -1);
        SpreadShot(p, 0, dx, dy, Random2(30), Random2(90));
        p.Visibility = 20;
        UseAmmo(p, p.WeaponAmmo, 1);
        p.FlashEffect = 1;
    }

    void AltFireSpread(int nTrigger, Player p)
    {
        double angle = SpreadAngle(p, nTrigger);
        double dx = Blood.Sar(Blood.Cos(angle), 16), dy = Blood.Sar(Blood.Sin(angle), 16);
        Sound3D(p, 431, -1);
        SpreadShot(p, -120, dx, dy, Random2(120), Random2(45));
        SpreadShot(p, 120, dx, dy, Random2(-120), Random2(-45));
        p.TiltEffect = 20;
        p.Visibility = 30;
        UseAmmo(p, p.WeaponAmmo, 2);
        p.FlashEffect = 1;
    }

    void AltFireSpread2(int nTrigger, Player p)
    {
        double angle = SpreadAngle(p, nTrigger);
        double dx = Blood.Sar(Blood.Cos(angle), 16), dy = Blood.Sar(Blood.Sin(angle), 16);
        Sound3D(p, 431, -1);
        if (PowerupCheck(p, K.kPwUpTwoGuns) != 0 && CheckAmmo2(p, 3, 2))
        {
            SpreadShot(p, -120, dx, dy, Random2(120), Random2(45));
            SpreadShot(p, 120, dx, dy, Random2(-120), Random2(-45));
            p.TiltEffect = 30;
            p.Visibility = 45;
            UseAmmo(p, p.WeaponAmmo, 2);
        }
        else
        {
            SpreadShot(p, 0, dx, dy, Random2(30), Random2(90));
            p.TiltEffect = 20;
            p.Visibility = 30;
            UseAmmo(p, p.WeaponAmmo, 1);
        }
        p.FlashEffect = 1;
        if (!CheckAmmo2(p, 3, 1))
        {
            WeaponLower(p);
            p.WeaponState = -1;
        }
    }

    static double FlareOffset(int nTrigger) => nTrigger == 2 ? -120 : nTrigger == 3 ? 120 : 0;

    void FireFlare(int nTrigger, Player p)
    {
        PlayerFireMissile(p, FlareOffset(nTrigger), p.AimDx, p.AimDy, p.AimDz, K.kMissileFlareRegular);
        UseAmmo(p, 1, 1);
        Sound3D(p, 420, 2);
        p.Visibility = 30;
        p.FlashEffect = 1;
    }

    void AltFireFlare(int nTrigger, Player p)
    {
        PlayerFireMissile(p, FlareOffset(nTrigger), p.AimDx, p.AimDy, p.AimDz, K.kMissileFlareAlt);
        UseAmmo(p, 1, 8);
        Sound3D(p, 420, 2);
        p.Visibility = 45;
        p.FlashEffect = 1;
    }

    void FireVoodoo(int nTrigger, Player p)
    {
        nTrigger--;
        if (nTrigger == 4)
        {
            ActDamageSprite(p, p, K.kDamageBullet, 16);
            return;
        }
        var t = p.VoodooTarget;
        if (t == null || t.Removed || (!FriendlyFire && IsTargetTeammate(p, t)))
            return;
        int dmg = nTrigger switch { 0 => 272, 1 => 144, 3 => 784, 2 => 176, _ => 0 };
        if (dmg == 0)
            return;
        Sound3D(p, nTrigger == 3 ? 463 : 460, 2);
        FxSpawnBlood(t, 272);
        int nDamage = ActDamageSprite(p, t, K.kDamageSpirit, dmg);
        if (nTrigger == 1 && t is Player tp1)
            WeaponLower(tp1);
        if (nTrigger == 2 && t is Player tp2)
            tp2.BlindEffect = 128;
        UseAmmo(p, 9, Blood.Int(nDamage / 4.0));
    }

    void AltFireVoodoo(int nTrigger, Player p)
    {
        if (nTrigger != 2)
            return;
        double px = p.BX, py = p.BY;
        if (WeaponsV10x)
        {
            // NBlood's v1.0x voodoo: every target in reach takes the full charge's damage, 20 charges are used and
            // the doll stays
            if (Math.Min(p.Ammo[9], p.AimTargetsCount) > 0)
            {
                for (int i = 0; i < p.AimTargetsCount; i++)
                {
                    var t = p.AimTargets[i];
                    if (t == null || t.Removed || (!FriendlyFire && IsTargetTeammate(p, t)))
                        continue;
                    double nDist = Blood.ApproxDist(t.BX - px, t.BY - py);
                    if (nDist > 0 && nDist < 51200)
                    {
                        int vc = (int)Blood.Sar(p.Ammo[9], 3);
                        double v8 = p.Ammo[9] * 2;
                        double nDamage = (v8 + Random(vc)) * 16;
                        nDamage = Blood.Int(nDamage * ((51200 - nDist) + 1) / 51200);
                        ActDamageSprite(p, t, K.kDamageSpirit, nDamage);
                        if (t is Player tp && (!tp.GodMode || PowerupCheck(tp, K.kPwUpDeathMask) == 0))
                            PowerupActivate(tp, K.kPwUpDeliriumShroom);
                        FxSpawnBlood(t, 0);
                    }
                }
            }
            UseAmmo(p, 9, 20);
            p.WeaponState = 0;
            return;
        }
        double nCount = Math.Min(p.Ammo[9], p.AimTargetsCount);
        if (nCount > 0)
        {
            double v4 = p.Ammo[9] - Blood.Int(p.Ammo[9] / nCount) * nCount;
            for (int i = 0; i < p.AimTargetsCount; i++)
            {
                var t = p.AimTargets[i];
                if (t == null || t.Removed || (!FriendlyFire && IsTargetTeammate(p, t)))
                    continue;
                if (v4 > 0)
                    v4--;
                double nDist = Blood.ApproxDist(t.BX - px, t.BY - py);
                if (nDist > 0 && nDist < 51200)
                {
                    int vc = (int)Blood.Sar(p.Ammo[9], 3);
                    double v8 = p.Ammo[9] * 2;
                    double nDamage = (v8 + Random2(vc)) * 16;
                    nDamage = Blood.Int(nDamage * ((51200 - nDist) + 1) / 51200);
                    nDamage = ActDamageSprite(p, t, K.kDamageSpirit, nDamage);
                    UseAmmo(p, 9, nDamage);
                    if (t is Player tp && (!tp.GodMode || PowerupCheck(tp, K.kPwUpDeathMask) == 0))
                        PowerupActivate(tp, K.kPwUpDeliriumShroom);
                    FxSpawnBlood(t, 0);
                }
            }
        }
        UseAmmo(p, 9, p.Ammo[9]);
        p.HasWeapon[K.kWeaponVoodoo] = 0;
        p.WeaponState = -1;
    }

    void DropVoodoo(int nTrigger, Player p)
    {
        Sound3D(p, 455, 2);
        var t = PlayerFireThing(p, 0, -4730, K.kThingVoodooHead, 838860);
        if (t != null)
        {
            t.Data1 = (int)p.Ammo[9];
            EvPostCallback(t, 90, K.kCallbackDropVoodoo);
            UseAmmo(p, 6, SprayCanCount());
            UseAmmo(p, 9, p.Ammo[9]);
            p.HasWeapon[K.kWeaponVoodoo] = 0;
        }
    }

    void FireTesla(int nTrigger, Player p)
    {
        if (nTrigger <= 0 || nTrigger > 6)
            return;
        nTrigger--;
        int ofs, id, ammo, snd, light;
        switch (nTrigger)
        {
            case 0: ofs = 0; id = 306; ammo = 1; snd = 470; light = 20; break;
            case 1: ofs = -140; id = 306; ammo = 1; snd = 470; light = 30; break;
            case 2: ofs = 140; id = 306; ammo = 1; snd = 470; light = 30; break;
            case 3: ofs = 0; id = 302; ammo = 35; snd = 471; light = 40; break;
            case 4: ofs = -140; id = 302; ammo = 35; snd = 471; light = 50; break;
            default: ofs = 140; id = 302; ammo = 35; snd = 471; light = 50; break;
        }
        if (!CheckAmmo2(p, 7, ammo))
        {
            ofs = 0; id = 306; ammo = 1; snd = 470; light = 20;
            if (!CheckAmmo2(p, 7, ammo))
            {
                p.WeaponState = -1;
                p.WeaponQav = 76;
                p.FlashEffect = 0;
                return;
            }
        }
        PlayerFireMissile(p, ofs, p.AimDx, p.AimDy, p.AimDz, id);
        UseAmmo(p, p.WeaponAmmo, ammo);
        Sound3D(p, snd, 1);
        p.Visibility = light;
        p.FlashEffect = 1;
    }

    void AltFireTesla(int nTrigger, Player p)
    {
        PlayerFireMissile(p, 0, p.AimDx, p.AimDy, p.AimDz, K.kMissileTeslaAlt);
        UseAmmo(p, p.WeaponAmmo, 35);
        Sound3D(p, 471, 2);
        p.Visibility = 40;
        p.FlashEffect = 1;
    }

    void FireNapalm(int nTrigger, Player p)
    {
        double offset = nTrigger == 2 ? -50 : nTrigger == 3 ? 50 : 0;
        PlayerFireMissile(p, offset, p.AimDx, p.AimDy, p.AimDz, K.kMissileFireballNapalm);
        Sound3D(p, 480, 2);
        UseAmmo(p, 4, 1);
        p.FlashEffect = 1;
    }

    void FireNapalm2(int nTrigger, Player p)
    {
        PlayerFireMissile(p, -120, p.AimDx, p.AimDy, p.AimDz, K.kMissileFireballNapalm);
        PlayerFireMissile(p, 120, p.AimDx, p.AimDy, p.AimDz, K.kMissileFireballNapalm);
        Sound3D(p, 480, 2);
        UseAmmo(p, 4, 2);
        p.FlashEffect = 1;
    }

    void AltFireNapalm(int nTrigger, Player p)
    {
        double nSpeed = Blood.MulScale16(32768, 1537911) + 419430;
        var m = PlayerFireThing(p, 0, -4730, K.kThingNapalmBall, nSpeed);
        if (m != null)
        {
            m.Data4 = (int)Math.Min(p.Ammo[4], 12);
            UseAmmo(p, 4, m.Data4);
            Seq.Spawn(this, 22, m);
            ActBurnSprite(p, m, 600);
            EvPostCallback(m, 0, K.kCallbackFXFlameLick);
            Sound3D(m, 480, 2);
            p.Visibility = 30;
            p.FlashEffect = 1;
        }
    }

    void FireLifeLeech(int nTrigger, Player p)
    {
        if (!CheckAmmo(p, 8, 1))
            return;
        int r1 = Random2(2000), r2 = Random2(2000), r3 = Random2(1000);
        var m = PlayerFireMissile(p, 0, p.AimDx + r1, p.AimDy + r2, p.AimDz + r3, 315);
        if (m != null)
        {
            m.XTarget = p.AimTarget;
            m.Ang = nTrigger == 2 ? 1024 : 0;
        }
        if (CheckAmmo2(p, 8, 1))
            UseAmmo(p, 8, 1);
        else
            ActDamageSprite(p, p, K.kDamageSpirit, 16);
        p.Visibility = Math.Min(p.Visibility + 5, 50);
    }

    void AltFireLifeLeech(int nTrigger, Player p)
    {
        Sound3D(p, 455, 2);
        var m = PlayerFireThing(p, 0, -4730, K.kThingDroppedLifeLeech, 104857);
        if (m == null)
            return;
        m.Cstat |= 4096;
        m.XPush = 1;
        m.XProximity = 1;
        m.DudeLockout = 1;
        m.StateTimer = 1;
        EvPostCallback(m, 120, K.kCallbackLeechStateTimer);
        if (GameType <= K.kGameTypeCoop)
        {
            double nAmmo = p.Ammo[8];
            if (nAmmo < 25 && p.XHealth > (25 - nAmmo) * 16)
            {
                ActDamageSprite(p, p, K.kDamageSpirit, (25 - nAmmo) * 16);
                nAmmo = 25;
            }
            m.Data3 = (int)nAmmo;
            UseAmmo(p, 8, nAmmo);
        }
        else
        {
            m.Data3 = (int)p.Ammo[8];
            p.Ammo[8] = 0;
        }
        p.HasWeapon[K.kWeaponLifeLeech] = 0;
    }

    void FireBeast(int nTrigger, Player p)
    {
        int r1 = Random2(2000), r2 = Random2(2000), r3 = Random2(2000);
        ActFireVector(p, 0, WZ(p), p.AimDx + r1, p.AimDy + r2, p.AimDz + r3, K.kVectorBeastSlash);
    }

    // ---------------------------------------------------------------- raise / lower / state
    void WeaponRaise(Player p)
    {
        int prevWeapon = p.CurWeapon;
        p.CurWeapon = p.InputNewWeapon;
        p.InputNewWeapon = K.kWeaponNone;
        p.WeaponAmmo = WeaponModeAmmo(p.CurWeapon);
        bool twoGuns = PowerupCheck(p, K.kPwUpTwoGuns) != 0;
        switch (p.CurWeapon)
        {
            case K.kWeaponPitchfork:
                p.WeaponState = 0;
                StartQAV(p, 0);
                break;
            case K.kWeaponSprayCan:
                if (p.WeaponState == 2)
                {
                    p.WeaponState = 3;
                    StartQAV(p, 8);
                }
                else
                {
                    p.WeaponState = 0;
                    StartQAV(p, 4);
                }
                break;
            case K.kWeaponTNT:
                if (InfiniteAmmo || CheckAmmo2(p, 5, 1))
                {
                    if (p.WeaponState == 2 && prevWeapon == K.kWeaponNone)
                        prevWeapon = K.kWeaponSprayCan;
                    p.WeaponState = 3;
                    StartQAV(p, prevWeapon == K.kWeaponSprayCan ? 16 : 18);
                }
                break;
            case K.kWeaponProxyTNT:
                if (InfiniteAmmo || CheckAmmo2(p, 10, 1))
                {
                    p.WeaponState = 7;
                    StartQAV(p, 25);
                }
                break;
            case K.kWeaponRemoteTNT:
                if (InfiniteAmmo || CheckAmmo2(p, 11, 1))
                {
                    p.WeaponState = 10;
                    StartQAV(p, 31);
                }
                else
                {
                    StartQAV(p, 32);
                    p.WeaponState = 11;
                }
                break;
            case K.kWeaponShotgun:
                if (twoGuns)
                {
                    StartQAV(p, InfiniteAmmo || p.Ammo[2] >= 4 ? 59 : 50);
                    if (InfiniteAmmo || p.Ammo[2] >= 4) p.WeaponState = 7;
                    else if (p.Ammo[2] > 1) p.WeaponState = 3;
                    else if (p.Ammo[2] > 0) p.WeaponState = 2;
                    else p.WeaponState = 1;
                }
                else
                {
                    if (InfiniteAmmo || p.Ammo[2] > 1) p.WeaponState = 3;
                    else if (p.Ammo[2] > 0) p.WeaponState = 2;
                    else p.WeaponState = 1;
                    StartQAV(p, 50);
                }
                break;
            case K.kWeaponTommy:
                if (twoGuns && CheckAmmo2(p, 3, 2))
                {
                    p.WeaponState = 1;
                    StartQAV(p, 69);
                }
                else
                {
                    p.WeaponState = 0;
                    StartQAV(p, 64);
                }
                break;
            case K.kWeaponVoodoo:
                if (InfiniteAmmo || CheckAmmo2(p, 9, 1))
                {
                    p.WeaponState = 2;
                    StartQAV(p, 100);
                }
                break;
            case K.kWeaponFlare:
                if (twoGuns && CheckAmmo2(p, 1, 2))
                {
                    StartQAV(p, 45);
                    p.WeaponState = 3;
                }
                else
                {
                    StartQAV(p, 41);
                    p.WeaponState = 2;
                }
                break;
            case K.kWeaponTesla:
                if (CheckAmmo2(p, 7, 1))
                {
                    p.WeaponState = 2;
                    StartQAV(p, twoGuns ? 82 : 74);
                }
                else
                {
                    p.WeaponState = 3;
                    StartQAV(p, 74);
                }
                break;
            case K.kWeaponNapalm:
                if (twoGuns)
                {
                    StartQAV(p, 120);
                    p.WeaponState = 3;
                }
                else
                {
                    StartQAV(p, 89);
                    p.WeaponState = 2;
                }
                break;
            case K.kWeaponLifeLeech:
                p.WeaponState = 2;
                StartQAV(p, 111);
                break;
            case K.kWeaponBeast:
                p.WeaponState = 2;
                StartQAV(p, 93);
                break;
        }
    }

    void WeaponLower(Player p)
    {
        if (CheckLitSprayOrTNT(p))
            return;
        p.ThrowPower = 0;
        int prevState = p.WeaponState;
        int prevWeapon = p.CurWeapon;
        bool twoGuns = PowerupCheck(p, K.kPwUpTwoGuns) != 0;
        switch (p.CurWeapon)
        {
            case K.kWeaponPitchfork:
                StartQAV(p, 3);
                break;
            case K.kWeaponSprayCan:
                Kill3DSound(p, -1, 441);
                if (prevState == 1)
                {
                    if (p.InputNewWeapon == K.kWeaponTNT)
                    {
                        p.WeaponState = 2;
                        StartQAV(p, 11);
                        WeaponRaise(p);
                        return;
                    }
                }
                else if (prevState == 2)
                {
                    p.WeaponState = 1;
                    WeaponRaise(p);
                    return;
                }
                else if (prevState == 4)
                {
                    if (p.InputNewWeapon == K.kWeaponTNT)
                    {
                        p.WeaponState = 2;
                        StartQAV(p, 11);
                        return;
                    }
                    p.WeaponState = 1;
                    StartQAV(p, 11);
                    WeaponLower(p);
                }
                else if (prevState == 0)
                {
                    if (p.InputNewWeapon == K.kWeaponTNT)
                    {
                        p.WeaponState = 3;
                        StartQAV(p, 16);
                        WeaponRaise(p);
                        return;
                    }
                }
                else if (prevState == 3)
                {
                    if (p.InputNewWeapon == K.kWeaponTNT)
                    {
                        p.WeaponState = 2;
                        StartQAV(p, 11);
                        return;
                    }
                    p.WeaponState = 1;
                    if (p.InputNewWeapon == K.kWeaponSprayCan)
                    {
                        StartQAV(p, 11);
                        p.InputNewWeapon = K.kWeaponNone;
                        WeaponLower(p);
                    }
                    else
                        StartQAV(p, 11);
                }
                else if (prevState == 7 && p.InputNewWeapon == K.kWeaponNone)
                {
                    p.WeaponState = 1;
                    StartQAV(p, 11);
                }
                break;
            case K.kWeaponTNT:
                if (prevState == 1)
                {
                    if (p.InputNewWeapon == K.kWeaponSprayCan)
                    {
                        p.WeaponState = 2;
                        StartQAV(p, 11);
                        WeaponRaise(p);
                        return;
                    }
                    StartQAV(p, 7);
                }
                else if (prevState == 2)
                    WeaponRaise(p);
                else if (prevState == 3)
                {
                    if (p.InputNewWeapon == K.kWeaponSprayCan)
                    {
                        p.WeaponState = 2;
                        StartQAV(p, 17);
                    }
                    else
                        StartQAV(p, 19);
                }
                break;
            case K.kWeaponProxyTNT:
                if (prevState == 7)
                    StartQAV(p, 26);
                break;
            case K.kWeaponRemoteTNT:
                if (prevState == 10)
                    StartQAV(p, 34);
                else if (prevState == 11)
                    StartQAV(p, 35);
                break;
            case K.kWeaponShotgun:
                StartQAV(p, twoGuns && (InfiniteAmmo || CheckAmmo(p, 2, 4)) ? 63 : 58);
                break;
            case K.kWeaponTommy:
                StartQAV(p, twoGuns && p.WeaponState == 1 ? 72 : 68);
                break;
            case K.kWeaponFlare:
                StartQAV(p, twoGuns && p.WeaponState == 3 ? 49 : 44);
                break;
            case K.kWeaponVoodoo:
                StartQAV(p, 109);
                break;
            case K.kWeaponTesla:
                StartQAV(p, CheckAmmo2(p, 7, 10) && twoGuns ? 88 : 81);
                break;
            case K.kWeaponNapalm:
                StartQAV(p, twoGuns && (InfiniteAmmo || CheckAmmo(p, 4, 2)) ? 124 : 92);
                break;
            case K.kWeaponLifeLeech:
                StartQAV(p, 119);
                break;
            case K.kWeaponBeast:
                StartQAV(p, 99);
                break;
        }
        p.CurWeapon = K.kWeaponNone;
        p.QavLoop = false;
        if (prevWeapon != K.kWeaponTNT && prevWeapon != K.kWeaponSprayCan)
            p.WeaponState = 0;
    }

    void WeaponUpdateState(Player p)
    {
        int va = p.CurWeapon, vb = p.WeaponState;
        bool twoGuns = PowerupCheck(p, K.kPwUpTwoGuns) != 0;
        switch (va)
        {
            case K.kWeaponPitchfork:
                p.WeaponQav = 1;
                break;
            case K.kWeaponSprayCan:
                if (vb == 0)
                {
                    p.WeaponState = 1;
                    StartQAV(p, 5);
                }
                else if (vb == 1)
                {
                    if (CheckAmmo(p, 6, 1))
                    {
                        p.WeaponState = 3;
                        StartQAV(p, 8);
                    }
                    else
                        p.WeaponQav = 6;
                }
                else if (vb == 3)
                    p.WeaponQav = 9;
                else if (vb == 4)
                {
                    if (CheckAmmo(p, 6, 1))
                    {
                        p.WeaponQav = 9;
                        p.WeaponState = 3;
                    }
                    else
                    {
                        p.WeaponState = 1;
                        StartQAV(p, 11);
                    }
                    Kill3DSound(p, -1, 441);
                }
                break;
            case K.kWeaponTNT:
                if (vb == 1)
                {
                    if (p.WeaponAmmo == 5 && CheckAmmo(p, 5, 1))
                    {
                        p.WeaponState = 3;
                        StartQAV(p, 16);
                    }
                }
                else if (vb == 0)
                {
                    p.WeaponState = 1;
                    StartQAV(p, 5);
                }
                else if (vb == 2)
                {
                    if (p.Ammo[5] > 0)
                    {
                        p.WeaponState = 3;
                        StartQAV(p, 16);
                    }
                    else
                        p.WeaponQav = 6;
                }
                else if (vb == 3)
                    p.WeaponQav = 20;
                break;
            case K.kWeaponProxyTNT:
                if (vb == 7)
                    p.WeaponQav = 27;
                else if (vb == 8)
                {
                    p.WeaponState = 7;
                    StartQAV(p, 25);
                }
                break;
            case K.kWeaponRemoteTNT:
                if (vb == 10)
                    p.WeaponQav = 36;
                else if (vb == 11)
                    p.WeaponQav = 37;
                else if (vb == 12)
                {
                    if (p.Ammo[11] > 0)
                    {
                        p.WeaponState = 10;
                        StartQAV(p, 31);
                    }
                    else
                        p.WeaponState = -1;
                }
                break;
            case K.kWeaponShotgun:
                if (vb == 6)
                    p.WeaponState = twoGuns && (InfiniteAmmo || CheckAmmo(p, 2, 4)) ? 7 : 1;
                else if (vb == 7)
                    p.WeaponQav = 60;
                else if (vb == 1)
                {
                    if (CheckAmmo(p, 2, 1))
                    {
                        Sound3D(p, 410, 3, 2);
                        StartQAV(p, 57, EjectShell);
                        if (twoGuns && (InfiniteAmmo || CheckAmmo(p, 2, 4)))
                            p.WeaponState = 7;
                        else if (InfiniteAmmo || p.Ammo[2] > 1)
                            p.WeaponState = 3;
                        else
                            p.WeaponState = 2;
                    }
                    else
                        p.WeaponQav = 51;
                }
                else if (vb == 2)
                    p.WeaponQav = 52;
                else if (vb == 3)
                    p.WeaponQav = 53;
                break;
            case K.kWeaponTommy:
                if (twoGuns && CheckAmmo2(p, 3, 2))
                {
                    p.WeaponQav = 70;
                    p.WeaponState = 1;
                }
                else
                {
                    p.WeaponQav = 65;
                    p.WeaponState = 0;
                }
                break;
            case K.kWeaponFlare:
                if (twoGuns)
                {
                    if (vb == 3 && CheckAmmo2(p, 1, 2))
                        p.WeaponQav = 46;
                    else
                    {
                        p.WeaponQav = 42;
                        p.WeaponState = 2;
                    }
                }
                else
                    p.WeaponQav = 42;
                break;
            case K.kWeaponVoodoo:
                p.WeaponQav = p.XHeight < 256 && Math.Abs(p.SwayHeight) > 768 ? 102 : 101;
                break;
            case K.kWeaponTesla:
                if (vb == 2)
                    p.WeaponQav = CheckAmmo2(p, 7, 10) && twoGuns ? 83 : 75;
                else if (vb == 3)
                    p.WeaponQav = 76;
                break;
            case K.kWeaponNapalm:
                if (vb == 3)
                    p.WeaponQav = twoGuns && (InfiniteAmmo || CheckAmmo(p, 4, 2)) ? 121 : 90;
                else if (vb == 2)
                    p.WeaponQav = 90;
                break;
            case K.kWeaponLifeLeech:
                if (vb == 2)
                    p.WeaponQav = 112;
                break;
            case K.kWeaponBeast:
                p.WeaponQav = 94;
                break;
        }
    }

    // ---------------------------------------------------------------- selection helpers
    bool WeaponIsEquipable(Player p, int nWeapon, bool checkUnderwater)
    {
        if (!(nWeapon >= K.kWeaponPitchfork && nWeapon <= K.kWeaponRemoteTNT))
            return false;
        if (checkUnderwater && p.IsUnderwater && BannedUnderwater(nWeapon))
            return false;
        return p.HasWeapon[nWeapon] != 0 && WeaponModeCount(nWeapon) != 0 && CheckWeaponAmmo(p, nWeapon, WeaponModeAmmo(nWeapon), 1);
    }

    // gWeaponUpgrade[cur][new]
    static bool WeaponUpgradeTable(int cur, int nw) => cur switch
    {
        0 or 1 => nw >= 1 && nw <= 12,
        2 => nw >= 2 && nw <= 4,
        3 => nw == 3 || nw == 4,
        4 => nw == 4,
        5 => nw == 5,
        6 => (nw >= 2 && nw <= 4) || nw == 6,
        7 => (nw >= 2 && nw <= 4) || nw == 7,
        8 => nw == 8,
        9 => nw == 8 || nw == 9,
        10 => nw == 3 || nw == 4 || nw == 10,
        11 => nw == 11,
        12 => nw == 12,
        _ => false,
    };

    int WeaponUpgrade(Player p, int newWeapon)
    {
        int weapon = p.CurWeapon;
        if (!CheckLitSprayOrTNT(p) && (WeaponSwitch & 1) != 0 && (WeaponUpgradeTable(p.CurWeapon, newWeapon) || (WeaponSwitch & 2) != 0))
            weapon = newWeapon;
        return weapon;
    }

    static int OrderNext(int w) => w switch
    {
        0 => K.kWeaponPitchfork, 1 => K.kWeaponFlare, 2 => K.kWeaponShotgun, 3 => K.kWeaponTommy, 4 => K.kWeaponNapalm,
        5 => K.kWeaponTNT, 6 => K.kWeaponSprayCan, 7 => K.kWeaponTesla, 8 => K.kWeaponLifeLeech, 9 => K.kWeaponVoodoo,
        10 => K.kWeaponProxyTNT, 11 => K.kWeaponRemoteTNT, _ => K.kWeaponPitchfork,
    };

    static int OrderPrev(int w) => w switch
    {
        0 or 1 => K.kWeaponRemoteTNT, 2 => K.kWeaponPitchfork, 3 => K.kWeaponFlare, 4 => K.kWeaponShotgun, 5 => K.kWeaponTommy,
        6 => K.kWeaponNapalm, 7 => K.kWeaponTNT, 8 => K.kWeaponSprayCan, 9 => K.kWeaponTesla, 10 => K.kWeaponLifeLeech,
        11 => K.kWeaponVoodoo, 12 => K.kWeaponProxyTNT, _ => K.kWeaponPitchfork,
    };

    int WeaponFindNext(Player p, bool bDir)
    {
        int weapon = p.CurWeapon;
        do
        {
            weapon = bDir ? OrderNext(weapon) : OrderPrev(weapon);
            if (WeaponModeCount(weapon) != 0 && p.HasWeapon[weapon] != 0)
            {
                if (weapon == K.kWeaponLifeLeech ? CheckAmmo(p, WeaponModeAmmo(weapon), 1) : CheckAmmo2(p, WeaponModeAmmo(weapon), 1))
                    break;
            }
        } while (weapon != p.CurWeapon);
        if (weapon == p.CurWeapon && (WeaponModeCount(weapon) == 0 || !CheckAmmo(p, WeaponModeAmmo(weapon), 1)))
            weapon = K.kWeaponPitchfork;
        return weapon;
    }

    // weaponOrder[0] = { 3, 4, 2, 8, 9, 10, 7, 1, 1, 1, 1, 1, 1, 1 }
    static readonly int[] WeaponOrder = { 3, 4, 2, 8, 9, 10, 7, 1, 1, 1, 1, 1, 1, 1 };

    int WeaponFindLoaded(Player p)
    {
        int weapon = K.kWeaponPitchfork;
        if (WeaponModeCount(p.CurWeapon) > 1 && CheckAmmo(p, WeaponModeAmmo(p.CurWeapon), 1))
            weapon = p.CurWeapon;
        if (weapon == K.kWeaponPitchfork)
        {
            for (int i = 0; i < K.kWeaponMax; i++)
            {
                int w = WeaponOrder[i];
                if (p.HasWeapon[w] != 0 && WeaponModeCount(w) != 0 && CheckWeaponAmmo(p, w, WeaponModeAmmo(w), 1))
                    return w;
            }
        }
        return weapon;
    }

    // ---------------------------------------------------------------- throwables
    double ThrowPowerNow(Player p) => Blood.ClipHigh(Blood.DivScale16(FrameClock - p.ThrowTime, 240), 65536);

    bool ProcessSprayCan(Player p)
    {
        bool bUseShootAsThrow = p.InputShoot;
        switch (p.WeaponState)
        {
            case 5:
                if (!p.InputShoot2 || bUseShootAsThrow)
                    p.WeaponState = 6;
                return true;
            case 6:
                if (p.InputShoot2 && !bUseShootAsThrow)
                {
                    p.WeaponState = 3;
                    p.FuseTime = p.WeaponTimer;
                    StartQAV(p, 13, DropCan);
                }
                else if (p.InputShoot)
                {
                    p.WeaponState = 7;
                    p.FuseTime = 0;
                    p.ThrowTime = FrameClock;
                }
                return true;
            case 7:
                p.ThrowPower = ThrowPowerNow(p);
                if (!p.InputShoot)
                {
                    if (p.FuseTime == 0)
                        p.FuseTime = p.WeaponTimer;
                    p.WeaponState = 1;
                    StartQAV(p, 14, ThrowCan);
                }
                return true;
        }
        return false;
    }

    bool ProcessTNT(Player p)
    {
        bool bUseShootAsThrow = p.InputShoot;
        switch (p.WeaponState)
        {
            case 4:
                if (!p.InputShoot2 || bUseShootAsThrow)
                    p.WeaponState = 5;
                return true;
            case 5:
                if (p.InputShoot2 && !bUseShootAsThrow)
                {
                    p.WeaponState = 1;
                    p.FuseTime = p.WeaponTimer;
                    StartQAV(p, 22, DropBundle);
                }
                else if (p.InputShoot)
                {
                    p.WeaponState = 6;
                    p.FuseTime = 0;
                    p.ThrowTime = FrameClock;
                }
                return true;
            case 6:
                p.ThrowPower = ThrowPowerNow(p);
                if (!p.InputShoot)
                {
                    if (p.FuseTime == 0)
                        p.FuseTime = p.WeaponTimer;
                    p.WeaponState = 1;
                    StartQAV(p, 23, ThrowBundle);
                }
                return true;
        }
        return false;
    }

    bool ProcessProxy(Player p)
    {
        if (p.WeaponState != 9)
            return false;
        p.ThrowPower = ThrowPowerNow(p);
        p.WeaponTimer = 0;
        if (!p.InputShoot)
        {
            p.WeaponState = 8;
            StartQAV(p, 29, ThrowProx);
        }
        return true;
    }

    bool ProcessRemote(Player p)
    {
        if (p.WeaponState != 13)
            return false;
        p.ThrowPower = ThrowPowerNow(p);
        if (!p.InputShoot)
        {
            p.WeaponState = 11;
            StartQAV(p, 39, ThrowRemote);
        }
        return true;
    }

    bool ProcessLeech(Player p)
    {
        if (p.WeaponState == 4)
        {
            p.WeaponState = 6;
            StartQAV(p, 114, FireLifeLeech, true);
            return true;
        }
        if (p.WeaponState == 6)
        {
            if (!p.InputShoot2)
            {
                p.WeaponState = 2;
                StartQAV(p, 118);
                return true;
            }
        }
        else if (p.WeaponState == 8)
        {
            p.WeaponState = 2;
            StartQAV(p, 118);
            return true;
        }
        return false;
    }

    bool ProcessTesla(Player p)
    {
        bool two = CheckAmmo2(p, 7, 10) && PowerupCheck(p, K.kPwUpTwoGuns) != 0;
        if (p.WeaponState == 4)
        {
            p.WeaponState = 5;
            StartQAV(p, two ? 84 : 77, FireTesla, true);
            return true;
        }
        if (p.WeaponState == 5)
        {
            if (!p.InputShoot)
            {
                p.WeaponState = 2;
                StartQAV(p, two ? 87 : 80);
                return true;
            }
        }
        else if (p.WeaponState == 7)
        {
            p.WeaponState = 2;
            StartQAV(p, two ? 87 : 80);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- WeaponProcess
    void WeaponProcess(Player p)
    {
        p.FlashEffect = Math.Max(p.FlashEffect - 1, 0);
        bool bAlreadySetLastWeapon = false, bTNTRemoteProxyCycling = true;
        if (p.XHealth == 0)
        {
            p.QavLoop = false;
            Kill3DSound(p, 1);
        }
        if (p.IsUnderwater && BannedUnderwater(p.CurWeapon))
        {
            p.LastWeapon = p.CurWeapon;
            if (CheckLitSprayOrTNT(p))
            {
                if (p.CurWeapon == K.kWeaponSprayCan)
                {
                    p.FuseTime = p.WeaponTimer;
                    DropCan(1, p);
                    p.WeaponState = 3;
                }
                else if (p.CurWeapon == K.kWeaponTNT)
                {
                    p.FuseTime = p.WeaponTimer;
                    DropBundle(1, p);
                    p.WeaponState = 1;
                }
            }
            WeaponLower(p);
            p.ThrowPower = 0;
        }
        WeaponPlay(p);
        UpdateAimVector(p);
        p.WeaponTimer -= GameClock.TicsPerFrame;
        bool bShoot = p.InputShoot, bShoot2 = p.InputShoot2;
        if (p.QavLoop && p.XHealth > 0)
        {
            if (bShoot && CheckAmmo(p, p.WeaponAmmo, 1))
            {
                while (p.WeaponTimer <= 0)
                    p.WeaponTimer += QavData.Duration(p.WeaponQav);
            }
            else
            {
                p.WeaponTimer = 0;
                p.QavLoop = false;
            }
            return;
        }
        p.WeaponTimer = Math.Max(p.WeaponTimer, 0);
        switch (p.CurWeapon)
        {
            case K.kWeaponSprayCan: if (ProcessSprayCan(p)) return; break;
            case K.kWeaponTNT: if (ProcessTNT(p)) return; break;
            case K.kWeaponProxyTNT: if (ProcessProxy(p)) return; break;
            case K.kWeaponRemoteTNT: if (ProcessRemote(p)) return; break;
        }
        if (p.WeaponTimer > 0)
            return;
        if (p.XHealth == 0 || p.CurWeapon == K.kWeaponNone)
            p.WeaponQav = -1;
        if (p.CurWeapon == K.kWeaponLifeLeech) { if (ProcessLeech(p)) return; }
        else if (p.CurWeapon == K.kWeaponTesla) { if (ProcessTesla(p)) return; }
        if (p.CurWeapon == K.kWeaponNone && p.InputNewWeapon != K.kWeaponNone)
        {
            p.InputNextWeapon = false;
            p.InputPrevWeapon = false;
            p.InputLastWeapon = false;
        }
        if (p.InputLastWeapon)
        {
            p.InputLastWeapon = false;
            int weapon = p.CurWeapon;
            if (weapon != 0 && weapon != p.LastWeapon && WeaponIsEquipable(p, p.LastWeapon, true))
            {
                p.InputNextWeapon = false;
                p.InputPrevWeapon = false;
                p.NextWeapon = K.kWeaponNone;
                p.WeaponMode[p.LastWeapon] = 0;
                p.InputNewWeapon = p.LastWeapon;
                p.LastWeapon = weapon;
                bAlreadySetLastWeapon = true;
                bTNTRemoteProxyCycling = false;
            }
        }
        bool oldNext = p.InputNextWeapon, oldPrev = p.InputPrevWeapon;
        if (p.InputNextWeapon)
        {
            p.InputNextWeapon = false;
            p.NextWeapon = K.kWeaponNone;
            int weapon = WeaponFindNext(p, true);
            p.WeaponMode[weapon] = 0;
            bTNTRemoteProxyCycling = false;
            p.InputNewWeapon = weapon;
        }
        if (p.InputPrevWeapon)
        {
            p.InputPrevWeapon = false;
            p.NextWeapon = K.kWeaponNone;
            int weapon = WeaponFindNext(p, false);
            p.WeaponMode[weapon] = 0;
            bTNTRemoteProxyCycling = false;
            p.InputNewWeapon = weapon;
        }
        if (p.NextWeapon != 0)
        {
            Kill3DSound(p, -1, 441);
            p.InputNewWeapon = p.NextWeapon;
            p.NextWeapon = K.kWeaponNone;
        }
        if (p.WeaponState == -1)
        {
            p.WeaponState = 0;
            int weapon = WeaponFindLoaded(p);
            p.WeaponMode[weapon] = 0;
            if (p.CurWeapon != 0)
            {
                p.LastWeapon = p.CurWeapon;
                WeaponLower(p);
                p.NextWeapon = weapon;
                return;
            }
            p.InputNewWeapon = weapon;
        }
        if (p.InputNewWeapon != 0)
        {
            if (p.IsUnderwater && BannedUnderwater(p.InputNewWeapon) && !CheckLitSprayOrTNT(p) && (oldNext || oldPrev))
            {
                int oldWeapon = p.CurWeapon;
                p.CurWeapon = p.InputNewWeapon;
                for (int i = 0; i < 3; i++)
                {
                    p.CurWeapon = WeaponFindNext(p, oldNext);
                    if (!BannedUnderwater(p.CurWeapon))
                    {
                        p.InputNewWeapon = p.CurWeapon;
                        p.WeaponMode[p.InputNewWeapon] = 0;
                        break;
                    }
                }
                p.CurWeapon = oldWeapon;
            }
            if (bTNTRemoteProxyCycling && p.InputNewWeapon == K.kWeaponTNT)
            {
                if (p.CurWeapon == K.kWeaponTNT)
                {
                    if (CheckAmmo2(p, 10, 1)) p.InputNewWeapon = K.kWeaponProxyTNT;
                    else if (CheckAmmo2(p, 11, 1)) p.InputNewWeapon = K.kWeaponRemoteTNT;
                }
                else if (p.CurWeapon == K.kWeaponProxyTNT)
                {
                    if (CheckAmmo2(p, 11, 1)) p.InputNewWeapon = K.kWeaponRemoteTNT;
                    else if (CheckAmmo2(p, 5, 1) && !p.IsUnderwater) p.InputNewWeapon = K.kWeaponTNT;
                }
                else if (p.CurWeapon == K.kWeaponRemoteTNT)
                {
                    if (CheckAmmo2(p, 5, 1) && !p.IsUnderwater) p.InputNewWeapon = K.kWeaponTNT;
                    else if (CheckAmmo2(p, 10, 1)) p.InputNewWeapon = K.kWeaponProxyTNT;
                }
                else
                {
                    if (CheckAmmo2(p, 5, 1) && !p.IsUnderwater) p.InputNewWeapon = K.kWeaponTNT;
                    else if (CheckAmmo2(p, 10, 1)) p.InputNewWeapon = K.kWeaponProxyTNT;
                    else if (CheckAmmo2(p, 11, 1)) p.InputNewWeapon = K.kWeaponRemoteTNT;
                }
            }
            else if (p.InputNewWeapon == K.kWeaponSprayCan && p.CurWeapon == K.kWeaponSprayCan && p.WeaponState == 2)
            {
                p.WeaponState = 1;
                p.InputNewWeapon = K.kWeaponNone;
                return;
            }
            if (p.XHealth == 0 || p.HasWeapon[p.InputNewWeapon] == 0)
            {
                p.InputNewWeapon = K.kWeaponNone;
                return;
            }
            if (p.IsUnderwater && BannedUnderwater(p.InputNewWeapon) && !CheckLitSprayOrTNT(p))
            {
                p.InputNewWeapon = K.kWeaponNone;
                return;
            }
            int nWeapon = p.InputNewWeapon;
            int v4c = WeaponModeCount(nWeapon);
            if (p.CurWeapon == 0)
            {
                int nAmmoType = WeaponModeAmmo(nWeapon);
                if (v4c > 1)
                {
                    if (CheckAmmo(p, nAmmoType, 1) || nAmmoType == 11)
                        WeaponRaise(p);
                    p.InputNewWeapon = K.kWeaponNone;
                }
                else if (CheckWeaponAmmo(p, nWeapon, nAmmoType, 1))
                    WeaponRaise(p);
                else
                {
                    p.WeaponState = 0;
                    int weapon = WeaponFindLoaded(p);
                    p.WeaponMode[weapon] = 0;
                    if (p.CurWeapon != 0)
                    {
                        WeaponLower(p);
                        p.NextWeapon = weapon;
                        return;
                    }
                    p.InputNewWeapon = weapon;
                }
                return;
            }
            if (nWeapon == p.CurWeapon && v4c <= 1)
            {
                p.InputNewWeapon = K.kWeaponNone;
                return;
            }
            for (int i = nWeapon == p.CurWeapon ? 1 : 0; i <= v4c; i++)
            {
                if (CheckWeaponAmmo(p, nWeapon, WeaponModeAmmo(nWeapon), 1))
                {
                    if (!bAlreadySetLastWeapon)
                        p.LastWeapon = p.CurWeapon;
                    WeaponLower(p);
                    p.WeaponMode[nWeapon] = 0;
                    return;
                }
            }
            p.InputNewWeapon = K.kWeaponNone;
            return;
        }
        if (p.CurWeapon != 0 && !CheckAmmo(p, p.WeaponAmmo, 1) && p.WeaponAmmo != 11)
        {
            p.WeaponState = -1;
            return;
        }
        bool twoGuns = PowerupCheck(p, K.kPwUpTwoGuns) != 0;
        if (bShoot)
        {
            switch (p.CurWeapon)
            {
                case K.kWeaponPitchfork:
                    StartQAV(p, 2, FirePitchfork);
                    return;
                case K.kWeaponSprayCan:
                    if (p.WeaponState == 3)
                    {
                        p.WeaponState = 4;
                        StartQAV(p, 10, FireSpray, true);
                        return;
                    }
                    break;
                case K.kWeaponTNT:
                    if (p.WeaponState == 3)
                    {
                        p.WeaponState = 6;
                        p.FuseTime = -1;
                        p.ThrowTime = FrameClock;
                        StartQAV(p, 21, ExplodeBundle);
                        return;
                    }
                    break;
                case K.kWeaponProxyTNT:
                    if (p.WeaponState == 7)
                    {
                        p.WeaponQav = 27;
                        p.WeaponState = 9;
                        p.ThrowTime = FrameClock;
                        return;
                    }
                    break;
                case K.kWeaponRemoteTNT:
                    if (p.WeaponState == 10)
                    {
                        p.WeaponQav = 36;
                        p.WeaponState = 13;
                        p.ThrowTime = FrameClock;
                        return;
                    }
                    if (p.WeaponState == 11)
                    {
                        p.WeaponState = 12;
                        StartQAV(p, 40, FireRemote);
                        return;
                    }
                    break;
                case K.kWeaponShotgun:
                    if (p.WeaponState == 7)
                    {
                        p.WeaponState = 6;
                        StartQAV(p, 61, FireShotgun);
                        return;
                    }
                    if (p.WeaponState == 3)
                    {
                        p.WeaponState = 2;
                        StartQAV(p, 54, FireShotgun);
                        return;
                    }
                    if (p.WeaponState == 2)
                    {
                        p.WeaponState = 1;
                        StartQAV(p, 55, FireShotgun);
                        return;
                    }
                    break;
                case K.kWeaponTommy:
                    StartQAV(p, twoGuns && CheckAmmo2(p, 3, 2) ? 71 : 66, FireTommy, true);
                    return;
                case K.kWeaponFlare:
                    StartQAV(p, twoGuns && CheckAmmo2(p, 1, 2) ? 48 : 43, FireFlare);
                    return;
                case K.kWeaponVoodoo:
                {
                    // nChance[] = { 0xa000, 0xc000, 0xe000, 0x10000 }
                    int nRand = WRand() * 2;
                    int i = nRand <= 40960 ? 0 : nRand <= 49152 ? 1 : nRand <= 57344 ? 2 : 3;
                    p.VoodooTarget = p.AimTarget;
                    if (p.VoodooTarget == null || p.VoodooTarget.StatNum != K.kStatDude)
                        i = 4;
                    StartQAV(p, 103 + i, FireVoodoo);
                    return;
                }
                case K.kWeaponTesla:
                    if (p.WeaponState == 2)
                    {
                        p.WeaponState = 4;
                        StartQAV(p, CheckAmmo2(p, 7, 10) && twoGuns ? 84 : 77, FireTesla);
                        return;
                    }
                    if (p.WeaponState == 5)
                    {
                        StartQAV(p, CheckAmmo2(p, 7, 10) && twoGuns ? 84 : 77, FireTesla);
                        return;
                    }
                    break;
                case K.kWeaponNapalm:
                    StartQAV(p, twoGuns && (InfiniteAmmo || CheckAmmo(p, 4, 2)) ? 122 : 91, FireNapalm);
                    return;
                case K.kWeaponLifeLeech:
                    Sound3D(p, 494, 2);
                    StartQAV(p, 116, FireLifeLeech);
                    return;
                case K.kWeaponBeast:
                    StartQAV(p, 95 + Random(4), FireBeast);
                    return;
            }
        }
        if (bShoot2)
        {
            switch (p.CurWeapon)
            {
                case K.kWeaponPitchfork:
                    StartQAV(p, 2, FirePitchfork);
                    return;
                case K.kWeaponSprayCan:
                    if (p.WeaponState == 3)
                    {
                        p.WeaponState = 5;
                        StartQAV(p, 12, ExplodeCan);
                        return;
                    }
                    break;
                case K.kWeaponTNT:
                    if (p.WeaponState == 3)
                    {
                        p.WeaponState = 4;
                        StartQAV(p, 21, ExplodeBundle);
                        return;
                    }
                    if (p.WeaponState == 7)
                    {
                        p.WeaponState = 8;
                        StartQAV(p, 28, DropProx);
                        return;
                    }
                    if (p.WeaponState == 10)
                    {
                        p.WeaponState = 11;
                        StartQAV(p, 38, DropRemote);
                        return;
                    }
                    if (p.WeaponState == 11)
                    {
                        if (p.Ammo[11] > 0)
                        {
                            p.WeaponState = 10;
                            StartQAV(p, 30);
                        }
                        return;
                    }
                    break;
                case K.kWeaponProxyTNT:
                    if (p.WeaponState == 7)
                    {
                        p.WeaponState = 8;
                        StartQAV(p, 28, DropProx);
                        return;
                    }
                    break;
                case K.kWeaponRemoteTNT:
                    if (p.WeaponState == 10)
                    {
                        p.WeaponState = 11;
                        StartQAV(p, 38, DropRemote);
                        return;
                    }
                    if (p.WeaponState == 11)
                    {
                        if (p.Ammo[11] > 0)
                        {
                            p.WeaponState = 10;
                            StartQAV(p, 30);
                        }
                        return;
                    }
                    break;
                case K.kWeaponShotgun:
                    if (p.WeaponState == 7)
                    {
                        p.WeaponState = 6;
                        StartQAV(p, 62, FireShotgun);
                        return;
                    }
                    if (p.WeaponState == 3)
                    {
                        p.WeaponState = 1;
                        StartQAV(p, 56, FireShotgun);
                        return;
                    }
                    if (p.WeaponState == 2)
                    {
                        p.WeaponState = 1;
                        StartQAV(p, 55, FireShotgun);
                        return;
                    }
                    break;
                case K.kWeaponTommy:
                    StartQAV(p, twoGuns && CheckAmmo2(p, 3, 2) ? 73 : 67, AltFireSpread2);
                    return;
                case K.kWeaponVoodoo:
                    Sound3D(p, 461, 2);
                    StartQAV(p, 110, AltFireVoodoo);
                    return;
                case K.kWeaponTesla:
                    if (CheckAmmo2(p, 7, 35))
                        StartQAV(p, CheckAmmo2(p, 7, 70) && twoGuns ? 85 : 78, FireTesla);
                    else
                        StartQAV(p, CheckAmmo2(p, 7, 10) && twoGuns ? 84 : 77, FireTesla);
                    return;
                case K.kWeaponNapalm:
                    // NBlood's v1.0x weapons: the alt fire shoots like the primary (two guns: both barrels)
                    if (twoGuns)
                        StartQAV(p, WeaponsV10x ? 123 : 122, WeaponsV10x ? FireNapalm2 : AltFireNapalm);
                    else
                        StartQAV(p, 91, WeaponsV10x ? FireNapalm : AltFireNapalm);
                    return;
                case K.kWeaponFlare:
                    if (CheckAmmo(p, 1, 8))
                        StartQAV(p, twoGuns && CheckAmmo2(p, 1, 16) ? 48 : 43, AltFireFlare);
                    else
                        StartQAV(p, twoGuns && CheckAmmo2(p, 1, 2) ? 48 : 43, FireFlare);
                    return;
                case K.kWeaponLifeLeech:
                    if (GameType <= K.kGameTypeCoop && !CheckAmmo2(p, 8, 1) && p.XHealth < 400)
                    {
                        Sound3D(p, 494, 2);
                        StartQAV(p, 116, FireLifeLeech);
                    }
                    else
                    {
                        StartQAV(p, 119);
                        AltFireLifeLeech(1, p);
                        p.WeaponState = -1;
                    }
                    return;
            }
        }
        WeaponUpdateState(p);
    }
}
