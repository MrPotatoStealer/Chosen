using System;

namespace Chosen.Client;

/// <summary>
/// The Yamaha OPL3 (YMF262) FM synthesizer as NBlood emulates it: Nuke.YKT's Nuked OPL3 1.8 (audiolib/src/opl3.cpp,
/// GPL 2) with its stereo extension (free panning through a sine table, as the AdLib MIDI driver enables) and the
/// buffered register writes. The C pointers to sample values (a slot's modulator, a channel's four outputs, the zero
/// source) are shared cells here.
/// </summary>
public sealed class Opl3
{
    sealed class Cell { public short V; }

    sealed class Slot
    {
        public Channel Channel;
        public Opl3 Chip;
        public Cell Mod;
        public bool TremOn;
        public uint PgReset, PgPhase;
        public ushort PgPhaseOut;
        public readonly Cell Out = new(), FbMod = new();
        public short PrOut;
        public ushort EgRout, EgOut;
        public byte EgGen, EgKsl;
        public byte RegVib, RegType, RegKsr, RegMult, RegKsl, RegTl, RegAr, RegDr, RegSl, RegRr, RegWf;
        public byte Key, SlotNum;
    }

    sealed class Channel
    {
        public readonly Slot[] Slots = new Slot[2];
        public Channel Pair;
        public Opl3 Chip;
        public readonly Cell[] Out = new Cell[4];
        public int LeftPan, RightPan;
        public byte ChType;
        public ushort FNum;
        public byte Block, Fb, Con, Alg, Ksv;
        public ushort Cha, Chb;
        public byte ChNum;
    }

    struct WriteBuf
    {
        public ulong Time;
        public ushort Reg;
        public byte Data;
    }

    const int RsmFrac = 10, WriteBufSize = 1024, WriteBufDelay = 2;
    const byte Ch2Op = 0, Ch4Op = 1, Ch4Op2 = 2, ChDrum = 3;
    const byte EgkNorm = 0x01, EgkDrum = 0x02;
    const byte EgAttack = 0, EgDecay = 1, EgSustain = 2, EgRelease = 3;

    static readonly ushort[] LogSinRom =
    {
        0x859, 0x6c3, 0x607, 0x58b, 0x52e, 0x4e4, 0x4a6, 0x471, 0x443, 0x41a, 0x3f5, 0x3d3, 0x3b5, 0x398, 0x37e, 0x365,
        0x34e, 0x339, 0x324, 0x311, 0x2ff, 0x2ed, 0x2dc, 0x2cd, 0x2bd, 0x2af, 0x2a0, 0x293, 0x286, 0x279, 0x26d, 0x261,
        0x256, 0x24b, 0x240, 0x236, 0x22c, 0x222, 0x218, 0x20f, 0x206, 0x1fd, 0x1f5, 0x1ec, 0x1e4, 0x1dc, 0x1d4, 0x1cd,
        0x1c5, 0x1be, 0x1b7, 0x1b0, 0x1a9, 0x1a2, 0x19b, 0x195, 0x18f, 0x188, 0x182, 0x17c, 0x177, 0x171, 0x16b, 0x166,
        0x160, 0x15b, 0x155, 0x150, 0x14b, 0x146, 0x141, 0x13c, 0x137, 0x133, 0x12e, 0x129, 0x125, 0x121, 0x11c, 0x118,
        0x114, 0x10f, 0x10b, 0x107, 0x103, 0x0ff, 0x0fb, 0x0f8, 0x0f4, 0x0f0, 0x0ec, 0x0e9, 0x0e5, 0x0e2, 0x0de, 0x0db,
        0x0d7, 0x0d4, 0x0d1, 0x0cd, 0x0ca, 0x0c7, 0x0c4, 0x0c1, 0x0be, 0x0bb, 0x0b8, 0x0b5, 0x0b2, 0x0af, 0x0ac, 0x0a9,
        0x0a7, 0x0a4, 0x0a1, 0x09f, 0x09c, 0x099, 0x097, 0x094, 0x092, 0x08f, 0x08d, 0x08a, 0x088, 0x086, 0x083, 0x081,
        0x07f, 0x07d, 0x07a, 0x078, 0x076, 0x074, 0x072, 0x070, 0x06e, 0x06c, 0x06a, 0x068, 0x066, 0x064, 0x062, 0x060,
        0x05e, 0x05c, 0x05b, 0x059, 0x057, 0x055, 0x053, 0x052, 0x050, 0x04e, 0x04d, 0x04b, 0x04a, 0x048, 0x046, 0x045,
        0x043, 0x042, 0x040, 0x03f, 0x03e, 0x03c, 0x03b, 0x039, 0x038, 0x037, 0x035, 0x034, 0x033, 0x031, 0x030, 0x02f,
        0x02e, 0x02d, 0x02b, 0x02a, 0x029, 0x028, 0x027, 0x026, 0x025, 0x024, 0x023, 0x022, 0x021, 0x020, 0x01f, 0x01e,
        0x01d, 0x01c, 0x01b, 0x01a, 0x019, 0x018, 0x017, 0x017, 0x016, 0x015, 0x014, 0x014, 0x013, 0x012, 0x011, 0x011,
        0x010, 0x00f, 0x00f, 0x00e, 0x00d, 0x00d, 0x00c, 0x00c, 0x00b, 0x00a, 0x00a, 0x009, 0x009, 0x008, 0x008, 0x007,
        0x007, 0x007, 0x006, 0x006, 0x005, 0x005, 0x005, 0x004, 0x004, 0x004, 0x003, 0x003, 0x003, 0x002, 0x002, 0x002,
        0x002, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000,
    };

    static readonly ushort[] ExpRom =
    {
        0x7fa, 0x7f5, 0x7ef, 0x7ea, 0x7e4, 0x7df, 0x7da, 0x7d4, 0x7cf, 0x7c9, 0x7c4, 0x7bf, 0x7b9, 0x7b4, 0x7ae, 0x7a9,
        0x7a4, 0x79f, 0x799, 0x794, 0x78f, 0x78a, 0x784, 0x77f, 0x77a, 0x775, 0x770, 0x76a, 0x765, 0x760, 0x75b, 0x756,
        0x751, 0x74c, 0x747, 0x742, 0x73d, 0x738, 0x733, 0x72e, 0x729, 0x724, 0x71f, 0x71a, 0x715, 0x710, 0x70b, 0x706,
        0x702, 0x6fd, 0x6f8, 0x6f3, 0x6ee, 0x6e9, 0x6e5, 0x6e0, 0x6db, 0x6d6, 0x6d2, 0x6cd, 0x6c8, 0x6c4, 0x6bf, 0x6ba,
        0x6b5, 0x6b1, 0x6ac, 0x6a8, 0x6a3, 0x69e, 0x69a, 0x695, 0x691, 0x68c, 0x688, 0x683, 0x67f, 0x67a, 0x676, 0x671,
        0x66d, 0x668, 0x664, 0x65f, 0x65b, 0x657, 0x652, 0x64e, 0x649, 0x645, 0x641, 0x63c, 0x638, 0x634, 0x630, 0x62b,
        0x627, 0x623, 0x61e, 0x61a, 0x616, 0x612, 0x60e, 0x609, 0x605, 0x601, 0x5fd, 0x5f9, 0x5f5, 0x5f0, 0x5ec, 0x5e8,
        0x5e4, 0x5e0, 0x5dc, 0x5d8, 0x5d4, 0x5d0, 0x5cc, 0x5c8, 0x5c4, 0x5c0, 0x5bc, 0x5b8, 0x5b4, 0x5b0, 0x5ac, 0x5a8,
        0x5a4, 0x5a0, 0x59c, 0x599, 0x595, 0x591, 0x58d, 0x589, 0x585, 0x581, 0x57e, 0x57a, 0x576, 0x572, 0x56f, 0x56b,
        0x567, 0x563, 0x560, 0x55c, 0x558, 0x554, 0x551, 0x54d, 0x549, 0x546, 0x542, 0x53e, 0x53b, 0x537, 0x534, 0x530,
        0x52c, 0x529, 0x525, 0x522, 0x51e, 0x51b, 0x517, 0x514, 0x510, 0x50c, 0x509, 0x506, 0x502, 0x4ff, 0x4fb, 0x4f8,
        0x4f4, 0x4f1, 0x4ed, 0x4ea, 0x4e7, 0x4e3, 0x4e0, 0x4dc, 0x4d9, 0x4d6, 0x4d2, 0x4cf, 0x4cc, 0x4c8, 0x4c5, 0x4c2,
        0x4be, 0x4bb, 0x4b8, 0x4b5, 0x4b1, 0x4ae, 0x4ab, 0x4a8, 0x4a4, 0x4a1, 0x49e, 0x49b, 0x498, 0x494, 0x491, 0x48e,
        0x48b, 0x488, 0x485, 0x482, 0x47e, 0x47b, 0x478, 0x475, 0x472, 0x46f, 0x46c, 0x469, 0x466, 0x463, 0x460, 0x45d,
        0x45a, 0x457, 0x454, 0x451, 0x44e, 0x44b, 0x448, 0x445, 0x442, 0x43f, 0x43c, 0x439, 0x436, 0x433, 0x430, 0x42d,
        0x42a, 0x428, 0x425, 0x422, 0x41f, 0x41c, 0x419, 0x416, 0x414, 0x411, 0x40e, 0x40b, 0x408, 0x406, 0x403, 0x400,
    };

    static readonly byte[] Mt = { 1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30 };
    static readonly byte[] KslRom = { 0, 32, 40, 45, 48, 51, 53, 55, 56, 58, 59, 60, 61, 62, 63, 64 };
    static readonly byte[] KslShift = { 8, 1, 2, 0 };
    static readonly byte[,] EgIncStep = { { 0, 0, 0, 0 }, { 1, 0, 0, 0 }, { 1, 0, 1, 0 }, { 1, 1, 1, 0 } };
    static readonly sbyte[] AdSlot =
    {
        0, 1, 2, 3, 4, 5, -1, -1, 6, 7, 8, 9, 10, 11, -1, -1, 12, 13, 14, 15, 16, 17, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    };
    static readonly byte[] ChSlot = { 0, 1, 2, 6, 7, 8, 12, 13, 14, 18, 19, 20, 24, 25, 26, 30, 31, 32 };
    static readonly int[] PanpotLut = new int[256];

    static Opl3()
    {
        for (int i = 0; i < 256; i++)
            PanpotLut[i] = (int)(Math.Sin(i * Math.PI / 512.0) * 65536.0);
    }

    readonly Channel[] _channel = new Channel[18];
    readonly Slot[] _slot = new Slot[36];
    readonly Cell _zero = new();
    ushort _timer;
    ulong _egTimer;
    byte _egTimerRem, _egState, _egAdd, _newm, _nts, _rhy, _vibPos, _vibShift, _tremolo, _tremoloPos, _tremoloShift;
    uint _noise;
    readonly int[] _mixBuff = new int[2];
    byte _rmHhBit2, _rmHhBit3, _rmHhBit7, _rmHhBit8, _rmTcBit3, _rmTcBit5;
    byte _stereoExt;
    int _rateRatio, _sampleCnt;
    readonly short[] _oldSamples = new short[2], _samples = new short[2];
    ulong _writeBufSampleCnt, _writeBufLastTime;
    uint _writeBufCur, _writeBufLast;
    readonly WriteBuf[] _writeBuf = new WriteBuf[WriteBufSize];

    public Opl3(int sampleRate) => Reset(sampleRate);

    // ---------------------------------------------------------------- envelope
    static short CalcExp(uint level)
    {
        if (level > 0x1fff)
            level = 0x1fff;
        return (short)((ExpRom[level & 0xff] << 1) >> (int)(level >> 8));
    }

    static short EnvelopeSin(int wf, ushort phase, ushort envelope)
    {
        uint env = (uint)envelope << 3;
        ushort outv;
        int neg = 0;
        phase &= 0x3ff;
        switch (wf)
        {
            case 0:
                if ((phase & 0x200) != 0) neg = 0xffff;
                outv = (phase & 0x100) != 0 ? LogSinRom[(phase & 0xff) ^ 0xff] : LogSinRom[phase & 0xff];
                return (short)(CalcExp(outv + env) ^ neg);
            case 1:
                outv = (phase & 0x200) != 0 ? (ushort)0x1000
                    : (phase & 0x100) != 0 ? LogSinRom[(phase & 0xff) ^ 0xff] : LogSinRom[phase & 0xff];
                return CalcExp(outv + env);
            case 2:
                outv = (phase & 0x100) != 0 ? LogSinRom[(phase & 0xff) ^ 0xff] : LogSinRom[phase & 0xff];
                return CalcExp(outv + env);
            case 3:
                outv = (phase & 0x100) != 0 ? (ushort)0x1000 : LogSinRom[phase & 0xff];
                return CalcExp(outv + env);
            case 4:
                if ((phase & 0x300) == 0x100) neg = 0xffff;
                outv = (phase & 0x200) != 0 ? (ushort)0x1000
                    : (phase & 0x80) != 0 ? LogSinRom[((phase ^ 0xff) << 1) & 0xff] : LogSinRom[(phase << 1) & 0xff];
                return (short)(CalcExp(outv + env) ^ neg);
            case 5:
                outv = (phase & 0x200) != 0 ? (ushort)0x1000
                    : (phase & 0x80) != 0 ? LogSinRom[((phase ^ 0xff) << 1) & 0xff] : LogSinRom[(phase << 1) & 0xff];
                return CalcExp(outv + env);
            case 6:
                if ((phase & 0x200) != 0) neg = 0xffff;
                return (short)(CalcExp(env) ^ neg);
            default:
                if ((phase & 0x200) != 0)
                {
                    neg = 0xffff;
                    phase = (ushort)((phase & 0x1ff) ^ 0x1ff);
                }
                outv = (ushort)(phase << 3);
                return (short)(CalcExp(outv + env) ^ neg);
        }
    }

    static void UpdateKsl(Slot slot)
    {
        int ksl = (KslRom[slot.Channel.FNum >> 6] << 2) - ((0x08 - slot.Channel.Block) << 5);
        if (ksl < 0)
            ksl = 0;
        slot.EgKsl = (byte)ksl;
    }

    void EnvelopeCalc(Slot slot)
    {
        byte regRate = 0;
        byte reset = 0;
        slot.EgOut = (ushort)(slot.EgRout + (slot.RegTl << 2) + (slot.EgKsl >> KslShift[slot.RegKsl]) + (slot.TremOn ? _tremolo : 0));
        if (slot.Key != 0 && slot.EgGen == EgRelease)
        {
            reset = 1;
            regRate = slot.RegAr;
        }
        else
        {
            switch (slot.EgGen)
            {
                case EgAttack: regRate = slot.RegAr; break;
                case EgDecay: regRate = slot.RegDr; break;
                case EgSustain: if (slot.RegType == 0) regRate = slot.RegRr; break;
                case EgRelease: regRate = slot.RegRr; break;
            }
        }
        slot.PgReset = reset;
        byte ks = (byte)(slot.Channel.Ksv >> ((slot.RegKsr ^ 1) << 1));
        bool nonzero = regRate != 0;
        byte rate = (byte)(ks + (regRate << 2));
        byte rateHi = (byte)(rate >> 2);
        byte rateLo = (byte)(rate & 0x03);
        if ((rateHi & 0x10) != 0)
            rateHi = 0x0f;
        byte egShift = (byte)(rateHi + _egAdd);
        byte shift = 0;
        if (nonzero)
        {
            if (rateHi < 12)
            {
                if (_egState != 0)
                {
                    switch (egShift)
                    {
                        case 12: shift = 1; break;
                        case 13: shift = (byte)((rateLo >> 1) & 0x01); break;
                        case 14: shift = (byte)(rateLo & 0x01); break;
                    }
                }
            }
            else
            {
                shift = (byte)((rateHi & 0x03) + EgIncStep[rateLo, _timer & 0x03]);
                if ((shift & 0x04) != 0)
                    shift = 0x03;
                if (shift == 0)
                    shift = _egState;
            }
        }
        int egRout = slot.EgRout;
        int egInc = 0;
        bool egOff = false;
        if (reset != 0 && rateHi == 0x0f)
            egRout = 0x00;
        if ((slot.EgRout & 0x1f8) == 0x1f8)
            egOff = true;
        if (slot.EgGen != EgAttack && reset == 0 && egOff)
            egRout = 0x1ff;
        switch (slot.EgGen)
        {
            case EgAttack:
                if (slot.EgRout == 0)
                    slot.EgGen = EgDecay;
                else if (slot.Key != 0 && shift > 0 && rateHi != 0x0f)
                    egInc = (short)(~slot.EgRout >> (4 - shift));
                break;
            case EgDecay:
                if ((slot.EgRout >> 4) == slot.RegSl)
                    slot.EgGen = EgSustain;
                else if (!egOff && reset == 0 && shift > 0)
                    egInc = 1 << (shift - 1);
                break;
            case EgSustain:
            case EgRelease:
                if (!egOff && reset == 0 && shift > 0)
                    egInc = 1 << (shift - 1);
                break;
        }
        slot.EgRout = (ushort)((egRout + egInc) & 0x1ff);
        if (reset != 0)
            slot.EgGen = EgAttack;
        if (slot.Key == 0)
            slot.EgGen = EgRelease;
    }

    static void KeyOn(Slot slot, byte type) => slot.Key |= type;
    static void KeyOff(Slot slot, byte type) => slot.Key &= (byte)~type;

    // ---------------------------------------------------------------- phase
    void PhaseGenerate(Slot slot)
    {
        ushort fNum = slot.Channel.FNum;
        if (slot.RegVib != 0)
        {
            int range = (fNum >> 7) & 7;
            byte vibpos = _vibPos;
            if ((vibpos & 3) == 0)
                range = 0;
            else if ((vibpos & 1) != 0)
                range >>= 1;
            range >>= _vibShift;
            if ((vibpos & 4) != 0)
                range = -range;
            fNum = (ushort)(fNum + range);
        }
        uint basefreq = ((uint)fNum << slot.Channel.Block) >> 1;
        ushort phase = (ushort)(slot.PgPhase >> 9);
        if (slot.PgReset != 0)
            slot.PgPhase = 0;
        slot.PgPhase += (basefreq * Mt[slot.RegMult]) >> 1;
        uint noise = _noise;
        slot.PgPhaseOut = phase;
        if (slot.SlotNum == 13)
        {
            _rmHhBit2 = (byte)((phase >> 2) & 1);
            _rmHhBit3 = (byte)((phase >> 3) & 1);
            _rmHhBit7 = (byte)((phase >> 7) & 1);
            _rmHhBit8 = (byte)((phase >> 8) & 1);
        }
        if (slot.SlotNum == 17 && (_rhy & 0x20) != 0)
        {
            _rmTcBit3 = (byte)((phase >> 3) & 1);
            _rmTcBit5 = (byte)((phase >> 5) & 1);
        }
        if ((_rhy & 0x20) != 0)
        {
            int rmXor = (_rmHhBit2 ^ _rmHhBit7) | (_rmHhBit3 ^ _rmTcBit5) | (_rmTcBit3 ^ _rmTcBit5);
            switch (slot.SlotNum)
            {
                case 13:
                    slot.PgPhaseOut = (ushort)(rmXor << 9);
                    slot.PgPhaseOut |= (ushort)((rmXor ^ (int)(noise & 1)) != 0 ? 0xd0 : 0x34);
                    break;
                case 16:
                    slot.PgPhaseOut = (ushort)((_rmHhBit8 << 9) | ((_rmHhBit8 ^ (int)(noise & 1)) << 8));
                    break;
                case 17:
                    slot.PgPhaseOut = (ushort)((rmXor << 9) | 0x80);
                    break;
            }
        }
        uint nBit = ((noise >> 14) ^ noise) & 0x01;
        _noise = (noise >> 1) | (nBit << 22);
    }

    // ---------------------------------------------------------------- slot
    void SlotWrite20(Slot slot, byte data)
    {
        slot.TremOn = ((data >> 7) & 0x01) != 0;
        slot.RegVib = (byte)((data >> 6) & 0x01);
        slot.RegType = (byte)((data >> 5) & 0x01);
        slot.RegKsr = (byte)((data >> 4) & 0x01);
        slot.RegMult = (byte)(data & 0x0f);
    }

    static void SlotWrite40(Slot slot, byte data)
    {
        slot.RegKsl = (byte)((data >> 6) & 0x03);
        slot.RegTl = (byte)(data & 0x3f);
        UpdateKsl(slot);
    }

    static void SlotWrite60(Slot slot, byte data)
    {
        slot.RegAr = (byte)((data >> 4) & 0x0f);
        slot.RegDr = (byte)(data & 0x0f);
    }

    static void SlotWrite80(Slot slot, byte data)
    {
        slot.RegSl = (byte)((data >> 4) & 0x0f);
        if (slot.RegSl == 0x0f)
            slot.RegSl = 0x1f;
        slot.RegRr = (byte)(data & 0x0f);
    }

    void SlotWriteE0(Slot slot, byte data)
    {
        slot.RegWf = (byte)(data & 0x07);
        if (_newm == 0x00)
            slot.RegWf &= 0x03;
    }

    static void SlotGenerate(Slot slot) =>
        slot.Out.V = EnvelopeSin(slot.RegWf, (ushort)(slot.PgPhaseOut + slot.Mod.V), slot.EgOut);

    static void SlotCalcFb(Slot slot)
    {
        slot.FbMod.V = slot.Channel.Fb != 0x00 ? (short)((slot.PrOut + slot.Out.V) >> (0x09 - slot.Channel.Fb)) : (short)0;
        slot.PrOut = slot.Out.V;
    }

    // ---------------------------------------------------------------- channel
    void UpdateRhythm(byte data)
    {
        _rhy = (byte)(data & 0x3f);
        if ((_rhy & 0x20) != 0)
        {
            Channel c6 = _channel[6], c7 = _channel[7], c8 = _channel[8];
            c6.Out[0] = c6.Slots[1].Out;
            c6.Out[1] = c6.Slots[1].Out;
            c6.Out[2] = _zero;
            c6.Out[3] = _zero;
            c7.Out[0] = c7.Slots[0].Out;
            c7.Out[1] = c7.Slots[0].Out;
            c7.Out[2] = c7.Slots[1].Out;
            c7.Out[3] = c7.Slots[1].Out;
            c8.Out[0] = c8.Slots[0].Out;
            c8.Out[1] = c8.Slots[0].Out;
            c8.Out[2] = c8.Slots[1].Out;
            c8.Out[3] = c8.Slots[1].Out;
            for (int ch = 6; ch < 9; ch++)
                _channel[ch].ChType = ChDrum;
            SetupAlg(c6);
            SetupAlg(c7);
            SetupAlg(c8);
            if ((_rhy & 0x01) != 0) KeyOn(c7.Slots[0], EgkDrum); else KeyOff(c7.Slots[0], EgkDrum);
            if ((_rhy & 0x02) != 0) KeyOn(c8.Slots[1], EgkDrum); else KeyOff(c8.Slots[1], EgkDrum);
            if ((_rhy & 0x04) != 0) KeyOn(c8.Slots[0], EgkDrum); else KeyOff(c8.Slots[0], EgkDrum);
            if ((_rhy & 0x08) != 0) KeyOn(c7.Slots[1], EgkDrum); else KeyOff(c7.Slots[1], EgkDrum);
            if ((_rhy & 0x10) != 0)
            {
                KeyOn(c6.Slots[0], EgkDrum);
                KeyOn(c6.Slots[1], EgkDrum);
            }
            else
            {
                KeyOff(c6.Slots[0], EgkDrum);
                KeyOff(c6.Slots[1], EgkDrum);
            }
        }
        else
        {
            for (int ch = 6; ch < 9; ch++)
            {
                _channel[ch].ChType = Ch2Op;
                SetupAlg(_channel[ch]);
                KeyOff(_channel[ch].Slots[0], EgkDrum);
                KeyOff(_channel[ch].Slots[1], EgkDrum);
            }
        }
    }

    void WriteA0(Channel ch, byte data)
    {
        if (_newm != 0 && ch.ChType == Ch4Op2)
            return;
        ch.FNum = (ushort)((ch.FNum & 0x300) | data);
        ch.Ksv = (byte)((ch.Block << 1) | ((ch.FNum >> (0x09 - _nts)) & 0x01));
        UpdateKsl(ch.Slots[0]);
        UpdateKsl(ch.Slots[1]);
        if (_newm != 0 && ch.ChType == Ch4Op)
        {
            ch.Pair.FNum = ch.FNum;
            ch.Pair.Ksv = ch.Ksv;
            UpdateKsl(ch.Pair.Slots[0]);
            UpdateKsl(ch.Pair.Slots[1]);
        }
    }

    void WriteB0(Channel ch, byte data)
    {
        if (_newm != 0 && ch.ChType == Ch4Op2)
            return;
        ch.FNum = (ushort)((ch.FNum & 0xff) | ((data & 0x03) << 8));
        ch.Block = (byte)((data >> 2) & 0x07);
        ch.Ksv = (byte)((ch.Block << 1) | ((ch.FNum >> (0x09 - _nts)) & 0x01));
        UpdateKsl(ch.Slots[0]);
        UpdateKsl(ch.Slots[1]);
        if (_newm != 0 && ch.ChType == Ch4Op)
        {
            ch.Pair.FNum = ch.FNum;
            ch.Pair.Block = ch.Block;
            ch.Pair.Ksv = ch.Ksv;
            UpdateKsl(ch.Pair.Slots[0]);
            UpdateKsl(ch.Pair.Slots[1]);
        }
    }

    void SetupAlg(Channel ch)
    {
        if (ch.ChType == ChDrum)
        {
            if (ch.ChNum == 7 || ch.ChNum == 8)
            {
                ch.Slots[0].Mod = _zero;
                ch.Slots[1].Mod = _zero;
                return;
            }
            if ((ch.Alg & 0x01) == 0)
            {
                ch.Slots[0].Mod = ch.Slots[0].FbMod;
                ch.Slots[1].Mod = ch.Slots[0].Out;
            }
            else
            {
                ch.Slots[0].Mod = ch.Slots[0].FbMod;
                ch.Slots[1].Mod = _zero;
            }
            return;
        }
        if ((ch.Alg & 0x08) != 0)
            return;
        if ((ch.Alg & 0x04) != 0)
        {
            var p = ch.Pair;
            p.Out[0] = p.Out[1] = p.Out[2] = p.Out[3] = _zero;
            switch (ch.Alg & 0x03)
            {
                case 0x00:
                    p.Slots[0].Mod = p.Slots[0].FbMod;
                    p.Slots[1].Mod = p.Slots[0].Out;
                    ch.Slots[0].Mod = p.Slots[1].Out;
                    ch.Slots[1].Mod = ch.Slots[0].Out;
                    ch.Out[0] = ch.Slots[1].Out;
                    ch.Out[1] = _zero;
                    ch.Out[2] = _zero;
                    ch.Out[3] = _zero;
                    break;
                case 0x01:
                    p.Slots[0].Mod = p.Slots[0].FbMod;
                    p.Slots[1].Mod = p.Slots[0].Out;
                    ch.Slots[0].Mod = _zero;
                    ch.Slots[1].Mod = ch.Slots[0].Out;
                    ch.Out[0] = p.Slots[1].Out;
                    ch.Out[1] = ch.Slots[1].Out;
                    ch.Out[2] = _zero;
                    ch.Out[3] = _zero;
                    break;
                case 0x02:
                    p.Slots[0].Mod = p.Slots[0].FbMod;
                    p.Slots[1].Mod = _zero;
                    ch.Slots[0].Mod = p.Slots[1].Out;
                    ch.Slots[1].Mod = ch.Slots[0].Out;
                    ch.Out[0] = p.Slots[0].Out;
                    ch.Out[1] = ch.Slots[1].Out;
                    ch.Out[2] = _zero;
                    ch.Out[3] = _zero;
                    break;
                case 0x03:
                    p.Slots[0].Mod = p.Slots[0].FbMod;
                    p.Slots[1].Mod = _zero;
                    ch.Slots[0].Mod = p.Slots[1].Out;
                    ch.Slots[1].Mod = _zero;
                    ch.Out[0] = p.Slots[0].Out;
                    ch.Out[1] = ch.Slots[0].Out;
                    ch.Out[2] = ch.Slots[1].Out;
                    ch.Out[3] = _zero;
                    break;
            }
        }
        else if ((ch.Alg & 0x01) == 0)
        {
            ch.Slots[0].Mod = ch.Slots[0].FbMod;
            ch.Slots[1].Mod = ch.Slots[0].Out;
            ch.Out[0] = ch.Slots[1].Out;
            ch.Out[1] = _zero;
            ch.Out[2] = _zero;
            ch.Out[3] = _zero;
        }
        else
        {
            ch.Slots[0].Mod = ch.Slots[0].FbMod;
            ch.Slots[1].Mod = _zero;
            ch.Out[0] = ch.Slots[0].Out;
            ch.Out[1] = ch.Slots[1].Out;
            ch.Out[2] = _zero;
            ch.Out[3] = _zero;
        }
    }

    void WriteC0(Channel ch, byte data)
    {
        ch.Fb = (byte)((data & 0x0e) >> 1);
        ch.Con = (byte)(data & 0x01);
        ch.Alg = ch.Con;
        if (_newm != 0)
        {
            if (ch.ChType == Ch4Op)
            {
                ch.Pair.Alg = (byte)(0x04 | (ch.Con << 1) | ch.Pair.Con);
                ch.Alg = 0x08;
                SetupAlg(ch.Pair);
            }
            else if (ch.ChType == Ch4Op2)
            {
                ch.Alg = (byte)(0x04 | (ch.Pair.Con << 1) | ch.Con);
                ch.Pair.Alg = 0x08;
                SetupAlg(ch);
            }
            else
                SetupAlg(ch);
        }
        else
            SetupAlg(ch);
        if (_newm != 0)
        {
            ch.Cha = ((data >> 4) & 0x01) != 0 ? (ushort)0xffff : (ushort)0;
            ch.Chb = ((data >> 5) & 0x01) != 0 ? (ushort)0xffff : (ushort)0;
        }
        else
            ch.Cha = ch.Chb = 0xffff;
        if (_stereoExt == 0)
        {
            ch.LeftPan = unchecked((int)((uint)ch.Cha << 16));
            ch.RightPan = unchecked((int)((uint)ch.Chb << 16));
        }
    }

    void WriteD0(Channel ch, byte data)
    {
        if (_stereoExt != 0)
        {
            ch.LeftPan = PanpotLut[data ^ 0xff];
            ch.RightPan = PanpotLut[data];
        }
    }

    void ChannelKeyOn(Channel ch)
    {
        if (_newm != 0)
        {
            if (ch.ChType == Ch4Op)
            {
                KeyOn(ch.Slots[0], EgkNorm);
                KeyOn(ch.Slots[1], EgkNorm);
                KeyOn(ch.Pair.Slots[0], EgkNorm);
                KeyOn(ch.Pair.Slots[1], EgkNorm);
            }
            else if (ch.ChType == Ch2Op || ch.ChType == ChDrum)
            {
                KeyOn(ch.Slots[0], EgkNorm);
                KeyOn(ch.Slots[1], EgkNorm);
            }
        }
        else
        {
            KeyOn(ch.Slots[0], EgkNorm);
            KeyOn(ch.Slots[1], EgkNorm);
        }
    }

    void ChannelKeyOff(Channel ch)
    {
        if (_newm != 0)
        {
            if (ch.ChType == Ch4Op)
            {
                KeyOff(ch.Slots[0], EgkNorm);
                KeyOff(ch.Slots[1], EgkNorm);
                KeyOff(ch.Pair.Slots[0], EgkNorm);
                KeyOff(ch.Pair.Slots[1], EgkNorm);
            }
            else if (ch.ChType == Ch2Op || ch.ChType == ChDrum)
            {
                KeyOff(ch.Slots[0], EgkNorm);
                KeyOff(ch.Slots[1], EgkNorm);
            }
        }
        else
        {
            KeyOff(ch.Slots[0], EgkNorm);
            KeyOff(ch.Slots[1], EgkNorm);
        }
    }

    void Set4Op(byte data)
    {
        for (int bit = 0; bit < 6; bit++)
        {
            int chnum = bit >= 3 ? bit + 9 - 3 : bit;
            if (((data >> bit) & 0x01) != 0)
            {
                _channel[chnum].ChType = Ch4Op;
                _channel[chnum + 3].ChType = Ch4Op2;
            }
            else
            {
                _channel[chnum].ChType = Ch2Op;
                _channel[chnum + 3].ChType = Ch2Op;
            }
        }
    }

    static short Clip(int sample) => (short)Math.Clamp(sample, -32768, 32767);

    void ProcessSlot(Slot slot)
    {
        SlotCalcFb(slot);
        EnvelopeCalc(slot);
        PhaseGenerate(slot);
        SlotGenerate(slot);
    }

    // ---------------------------------------------------------------- output
    /// <summary>OPL3_Generate: one sample at the chip's own rate (49716 Hz).</summary>
    void Generate(short[] buf)
    {
        buf[1] = Clip(_mixBuff[1]);
        for (int i = 0; i < 36; i++)
            ProcessSlot(_slot[i]);
        int mix = 0;
        for (int i = 0; i < 18; i++)
        {
            var o = _channel[i].Out;
            short accm = (short)(o[0].V + o[1].V + o[2].V + o[3].V);
            mix += (short)((accm * _channel[i].LeftPan) >> 16);
        }
        _mixBuff[0] = mix;
        buf[0] = Clip(_mixBuff[0]);
        mix = 0;
        for (int i = 0; i < 18; i++)
        {
            var o = _channel[i].Out;
            short accm = (short)(o[0].V + o[1].V + o[2].V + o[3].V);
            mix += (short)((accm * _channel[i].RightPan) >> 16);
        }
        _mixBuff[1] = mix;
        if ((_timer & 0x3f) == 0x3f)
            _tremoloPos = (byte)((_tremoloPos + 1) % 210);
        _tremolo = _tremoloPos < 105 ? (byte)(_tremoloPos >> _tremoloShift) : (byte)((210 - _tremoloPos) >> _tremoloShift);
        if ((_timer & 0x3ff) == 0x3ff)
            _vibPos = (byte)((_vibPos + 1) & 7);
        _timer++;
        _egAdd = 0;
        if (_egTimer != 0)
        {
            int shift = 0;
            while (shift < 36 && ((_egTimer >> shift) & 1) == 0)
                shift++;
            _egAdd = shift > 12 ? (byte)0 : (byte)(shift + 1);
        }
        if (_egTimerRem != 0 || _egState != 0)
        {
            if (_egTimer == 0xfffffffff)
            {
                _egTimer = 0;
                _egTimerRem = 1;
            }
            else
            {
                _egTimer++;
                _egTimerRem = 0;
            }
        }
        _egState ^= 1;
        while (_writeBuf[_writeBufCur].Time <= _writeBufSampleCnt)
        {
            ref var wb = ref _writeBuf[_writeBufCur];
            if ((wb.Reg & 0x200) == 0)
                break;
            wb.Reg &= 0x1ff;
            WriteReg(wb.Reg, wb.Data);
            _writeBufCur = (_writeBufCur + 1) & (WriteBufSize - 1);
        }
        _writeBufSampleCnt++;
    }

    /// <summary>OPL3_GenerateResampled: one stereo sample at the output rate (linear interpolation).</summary>
    public void GenerateResampled(out short left, out short right)
    {
        while (_sampleCnt >= _rateRatio)
        {
            _oldSamples[0] = _samples[0];
            _oldSamples[1] = _samples[1];
            Generate(_samples);
            _sampleCnt -= _rateRatio;
        }
        left = (short)((_oldSamples[0] * (_rateRatio - _sampleCnt) + _samples[0] * _sampleCnt) / _rateRatio);
        right = (short)((_oldSamples[1] * (_rateRatio - _sampleCnt) + _samples[1] * _sampleCnt) / _rateRatio);
        _sampleCnt += 1 << RsmFrac;
    }

    public void Reset(int sampleRate)
    {
        _timer = 0;
        _egTimer = 0;
        _egTimerRem = _egState = _egAdd = _newm = _nts = _rhy = _vibPos = _tremolo = _tremoloPos = 0;
        _rmHhBit2 = _rmHhBit3 = _rmHhBit7 = _rmHhBit8 = _rmTcBit3 = _rmTcBit5 = 0;
        _stereoExt = 0;
        _mixBuff[0] = _mixBuff[1] = 0;
        _sampleCnt = 0;
        _oldSamples[0] = _oldSamples[1] = _samples[0] = _samples[1] = 0;
        _writeBufSampleCnt = _writeBufLastTime = 0;
        _writeBufCur = _writeBufLast = 0;
        Array.Clear(_writeBuf);
        _zero.V = 0;
        for (int s = 0; s < 36; s++)
        {
            _slot[s] = new Slot
            {
                Chip = this, Mod = _zero, EgRout = 0x1ff, EgOut = 0x1ff, EgGen = EgRelease, TremOn = false, SlotNum = (byte)s,
            };
        }
        for (int c = 0; c < 18; c++)
            _channel[c] = new Channel { Chip = this, ChNum = (byte)c };
        for (int c = 0; c < 18; c++)
        {
            var ch = _channel[c];
            int ls = ChSlot[c];
            ch.Slots[0] = _slot[ls];
            ch.Slots[1] = _slot[ls + 3];
            _slot[ls].Channel = ch;
            _slot[ls + 3].Channel = ch;
            if (c % 9 < 3)
                ch.Pair = _channel[c + 3];
            else if (c % 9 < 6)
                ch.Pair = _channel[c - 3];
            ch.Out[0] = ch.Out[1] = ch.Out[2] = ch.Out[3] = _zero;
            ch.ChType = Ch2Op;
            ch.Cha = ch.Chb = 0xffff;
            ch.LeftPan = ch.RightPan = 0x10000;
            SetupAlg(ch);
        }
        _noise = 1;
        _rateRatio = (sampleRate << RsmFrac) / 49716;
        _tremoloShift = 4;
        _vibShift = 1;
    }

    public void WriteReg(ushort reg, byte v)
    {
        int high = (reg >> 8) & 0x01;
        int regm = reg & 0xff;
        switch (regm & 0xf0)
        {
            case 0x00:
                if (high != 0)
                {
                    switch (regm & 0x0f)
                    {
                        case 0x04: Set4Op(v); break;
                        case 0x05:
                            _newm = (byte)(v & 0x01);
                            _stereoExt = (byte)((v >> 1) & 0x01);
                            break;
                    }
                }
                else if ((regm & 0x0f) == 0x08)
                    _nts = (byte)((v >> 6) & 0x01);
                break;
            case 0x20:
            case 0x30:
                if (AdSlot[regm & 0x1f] >= 0)
                    SlotWrite20(_slot[18 * high + AdSlot[regm & 0x1f]], v);
                break;
            case 0x40:
            case 0x50:
                if (AdSlot[regm & 0x1f] >= 0)
                    SlotWrite40(_slot[18 * high + AdSlot[regm & 0x1f]], v);
                break;
            case 0x60:
            case 0x70:
                if (AdSlot[regm & 0x1f] >= 0)
                    SlotWrite60(_slot[18 * high + AdSlot[regm & 0x1f]], v);
                break;
            case 0x80:
            case 0x90:
                if (AdSlot[regm & 0x1f] >= 0)
                    SlotWrite80(_slot[18 * high + AdSlot[regm & 0x1f]], v);
                break;
            case 0xe0:
            case 0xf0:
                if (AdSlot[regm & 0x1f] >= 0)
                    SlotWriteE0(_slot[18 * high + AdSlot[regm & 0x1f]], v);
                break;
            case 0xa0:
                if ((regm & 0x0f) < 9)
                    WriteA0(_channel[9 * high + (regm & 0x0f)], v);
                break;
            case 0xb0:
                if (regm == 0xbd && high == 0)
                {
                    _tremoloShift = (byte)((((v >> 7) ^ 1) << 1) + 2);
                    _vibShift = (byte)(((v >> 6) & 0x01) ^ 1);
                    UpdateRhythm(v);
                }
                else if ((regm & 0x0f) < 9)
                {
                    var ch = _channel[9 * high + (regm & 0x0f)];
                    WriteB0(ch, v);
                    if ((v & 0x20) != 0)
                        ChannelKeyOn(ch);
                    else
                        ChannelKeyOff(ch);
                }
                break;
            case 0xc0:
                if ((regm & 0x0f) < 9)
                    WriteC0(_channel[9 * high + (regm & 0x0f)], v);
                break;
            case 0xd0:
                if ((regm & 0x0f) < 9)
                    WriteD0(_channel[9 * high + (regm & 0x0f)], v);
                break;
        }
    }

    /// <summary>OPL3_WriteRegBuffered: a register write that lands a couple of chip samples after the last one.</summary>
    public void WriteRegBuffered(ushort reg, byte v)
    {
        uint last = _writeBufLast;
        ref var wb = ref _writeBuf[last];
        if ((wb.Reg & 0x200) != 0)
        {
            WriteReg((ushort)(wb.Reg & 0x1ff), wb.Data);
            _writeBufCur = (last + 1) & (WriteBufSize - 1);
            _writeBufSampleCnt = wb.Time;
        }
        wb.Reg = (ushort)(reg | 0x200);
        wb.Data = v;
        ulong time1 = _writeBufLastTime + WriteBufDelay;
        ulong time2 = _writeBufSampleCnt;
        if (time1 < time2)
            time1 = time2;
        wb.Time = time1;
        _writeBufLastTime = time1;
        _writeBufLast = (last + 1) & (WriteBufSize - 1);
    }
}
