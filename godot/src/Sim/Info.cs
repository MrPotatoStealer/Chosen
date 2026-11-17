using static Chosen.Data.GenTables;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Blood's per-type data (DUDEINFO, THINGINFO, ITEMDATA...), from the generated tables (actor_base.qc).</summary>
public static class Info
{
    // DUDEINFO fields
    public const int DI_seqStartID = 0, DI_startHealth = 1, DI_mass = 2, DI_at6 = 3, DI_clipdist = 4, DI_eyeHeight = 5,
        DI_aimHeight = 6, DI_hearDist = 7, DI_seeDist = 8, DI_periphery = 9, DI_meleeDist = 10, DI_fleeHealth = 11,
        DI_hinderDamage = 12, DI_changeTarget = 13, DI_changeTargetKin = 14, DI_alertChance = 15, DI_lockOut = 16,
        DI_frontSpeed = 17, DI_sideSpeed = 18, DI_backSpeed = 19, DI_angSpeed = 20, DI_gibType = 21,
        DI_startDamage = 24, DI_curDamage = 31;
    // THINGINFO fields
    public const int TI_startHealth = 0, TI_mass = 1, TI_clipdist = 2, TI_flags = 3, TI_elastic = 4, TI_dmgResist = 5,
        TI_cstat = 6, TI_picnum = 7, TI_shade = 8, TI_pal = 9, TI_xrepeat = 10, TI_yrepeat = 11, TI_dmgControl = 12;
    // ITEMDATA / AMMOITEMDATA / WEAPONITEMDATA: cstat, picnum, shade, pal, xrepeat, yrepeat, ...
    public const int ID_cstat = 0, ID_picnum = 1, ID_shade = 2, ID_pal = 3, ID_xrepeat = 4, ID_yrepeat = 5;

    public static int Dude(int type, int field) =>
        type < K.kDudeBase || type >= K.kDudeMax ? 0 : tbl_dude[(type - K.kDudeBase) * tbl_dude_STRIDE + field];

    public static int PlayerTemplate(int mode, int field) => tbl_playertemplate[mode * tbl_playertemplate_STRIDE + field];

    public static int Thing(int type, int field) =>
        type < K.kThingBase || type >= K.kThingBase + tbl_thing_COUNT ? 0 : tbl_thing[(type - K.kThingBase) * tbl_thing_STRIDE + field];

    public static int Item(int type, int field) => tbl_item[(type - K.kItemBase) * tbl_item_STRIDE + field];
    public static int AmmoItem(int type, int field) => tbl_ammoitem[(type - K.kItemAmmoBase) * tbl_ammoitem_STRIDE + field];
    public static int WeaponItem(int type, int field) => tbl_weaponitem[(type - K.kItemWeaponBase) * tbl_weaponitem_STRIDE + field];

    public static bool IsDude(int t) => t >= K.kDudeBase && t < K.kDudeMax;
    public static bool IsPlayer(int t) => t >= K.kDudePlayer1 && t <= K.kDudePlayer8;
    public static bool IsThing(int t) => t >= K.kThingBase && t < K.kThingMax;
}
