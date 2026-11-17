using System;
using System.Collections.Generic;

namespace Chosen.Client;

/// <summary>
/// Blood's MIDI music on an FM card, as NBlood plays it with its OPL3 driver: Apogee's MIDI player (audiolib's
/// midi.cpp: tracks run on the song's tempo, EMIDI's track selection for the AdLib card, loops and contexts, looping
/// at the end) driving the AdLib General MIDI driver (driver_adlib.cpp: 18 two-operator voices on both register
/// sets, Blood's timbre bank GMTIMBRE, pitch bend, channel volume and panning) on an emulated OPL3 (Opl3). Render
/// fills stereo 16-bit samples at the output rate.
/// </summary>
public sealed class MidiFm
{
    // ---------------------------------------------------------------- AdLib driver (driver_adlib.cpp)
    struct Timbre
    {
        public byte Save0, Save1, Level0, Level1, Env10, Env11, Env20, Env21, Wave0, Wave1, Feedback;
        public sbyte Transpose, Velocity;
    }

    sealed class Voice
    {
        public int Num, Key, Velocity, Channel = -1, Timbre = -1, Port;
        public uint PitchLeft, Status;
    }

    sealed class Chan
    {
        public readonly List<Voice> Voices = new();
        public int Timbre, Pitchbend, KeyOffset;
        public uint KeyDetune;
        public int Volume, Pan, Detune;
        public uint Rpn;
        public int PitchBendRange, PitchBendSemiTones, PitchBendHundreds;
    }

    const int NumVoices = 9, NumChannels = 16, MaxVolume = 127, DefaultChannelVolume = 90, DefaultPitchBendRange = 200;
    const int MaxOctave = 7, MaxNote = MaxOctave * 12 + 11, FinetuneMax = 31, FinetuneRange = FinetuneMax + 1;
    const int PitchbendCenter = 1638400;
    const uint NoteOn = 0x2000, NoteOff = 0x0000;
    const int TotalLevelMask = 0x3f, KslMask = 0xc0, FeedbackMask = 0x0e, ConnectionBit = 0x01, StereoBits = 0x30;
    const int RegTest = 0x01, EnableWaveSelect = 0x20, RegKbdSplit = 0x08, RegPercussion = 0xbd, RegMode = 0x05;
    const int RegKslLevel = 0x40, RegAttackDecay = 0x60, RegSustainRelease = 0x80, RegWaveSelect = 0xe0, RegAmVib = 0x20;
    const int RegFnumLow = 0xa0, RegKeyonBlock = 0xb0, RegFeedbackConnection = 0xc0;
    const float PostAmp = 3f;

    static readonly uint[] OctavePitch = { 0x0000, 0x0400, 0x0800, 0x0C00, 0x1000, 0x1400, 0x1800, 0x1C00 };
    static readonly uint[,] NotePitch =
    {
        { 0x157, 0x16b, 0x181, 0x198, 0x1b0, 0x1ca, 0x1e5, 0x202, 0x220, 0x241, 0x263, 0x287 },
        { 0x157, 0x16b, 0x181, 0x198, 0x1b0, 0x1ca, 0x1e5, 0x202, 0x220, 0x242, 0x264, 0x288 },
        { 0x158, 0x16c, 0x182, 0x199, 0x1b1, 0x1cb, 0x1e6, 0x203, 0x221, 0x243, 0x265, 0x289 },
        { 0x158, 0x16c, 0x183, 0x19a, 0x1b2, 0x1cc, 0x1e7, 0x204, 0x222, 0x244, 0x266, 0x28a },
        { 0x159, 0x16d, 0x183, 0x19a, 0x1b3, 0x1cd, 0x1e8, 0x205, 0x223, 0x245, 0x267, 0x28b },
        { 0x15a, 0x16e, 0x184, 0x19b, 0x1b3, 0x1ce, 0x1e9, 0x206, 0x224, 0x246, 0x268, 0x28c },
        { 0x15a, 0x16e, 0x185, 0x19c, 0x1b4, 0x1ce, 0x1ea, 0x207, 0x225, 0x247, 0x269, 0x28e },
        { 0x15b, 0x16f, 0x185, 0x19d, 0x1b5, 0x1cf, 0x1eb, 0x208, 0x226, 0x248, 0x26a, 0x28f },
        { 0x15b, 0x170, 0x186, 0x19d, 0x1b6, 0x1d0, 0x1ec, 0x209, 0x227, 0x249, 0x26b, 0x290 },
        { 0x15c, 0x170, 0x187, 0x19e, 0x1b7, 0x1d1, 0x1ec, 0x20a, 0x228, 0x24a, 0x26d, 0x291 },
        { 0x15d, 0x171, 0x188, 0x19f, 0x1b7, 0x1d2, 0x1ed, 0x20b, 0x229, 0x24b, 0x26e, 0x292 },
        { 0x15d, 0x172, 0x188, 0x1a0, 0x1b8, 0x1d3, 0x1ee, 0x20c, 0x22a, 0x24c, 0x26f, 0x293 },
        { 0x15e, 0x172, 0x189, 0x1a0, 0x1b9, 0x1d4, 0x1ef, 0x20d, 0x22b, 0x24d, 0x270, 0x295 },
        { 0x15f, 0x173, 0x18a, 0x1a1, 0x1ba, 0x1d4, 0x1f0, 0x20e, 0x22c, 0x24e, 0x271, 0x296 },
        { 0x15f, 0x174, 0x18a, 0x1a2, 0x1bb, 0x1d5, 0x1f1, 0x20f, 0x22d, 0x24f, 0x272, 0x297 },
        { 0x160, 0x174, 0x18b, 0x1a3, 0x1bb, 0x1d6, 0x1f2, 0x210, 0x22e, 0x250, 0x273, 0x298 },
        { 0x161, 0x175, 0x18c, 0x1a3, 0x1bc, 0x1d7, 0x1f3, 0x211, 0x22f, 0x251, 0x274, 0x299 },
        { 0x161, 0x176, 0x18c, 0x1a4, 0x1bd, 0x1d8, 0x1f4, 0x212, 0x230, 0x252, 0x276, 0x29b },
        { 0x162, 0x176, 0x18d, 0x1a5, 0x1be, 0x1d9, 0x1f5, 0x212, 0x231, 0x254, 0x277, 0x29c },
        { 0x162, 0x177, 0x18e, 0x1a6, 0x1bf, 0x1d9, 0x1f5, 0x213, 0x232, 0x255, 0x278, 0x29d },
        { 0x163, 0x178, 0x18f, 0x1a6, 0x1bf, 0x1da, 0x1f6, 0x214, 0x233, 0x256, 0x279, 0x29e },
        { 0x164, 0x179, 0x18f, 0x1a7, 0x1c0, 0x1db, 0x1f7, 0x215, 0x235, 0x257, 0x27a, 0x29f },
        { 0x164, 0x179, 0x190, 0x1a8, 0x1c1, 0x1dc, 0x1f8, 0x216, 0x236, 0x258, 0x27b, 0x2a1 },
        { 0x165, 0x17a, 0x191, 0x1a9, 0x1c2, 0x1dd, 0x1f9, 0x217, 0x237, 0x259, 0x27c, 0x2a2 },
        { 0x166, 0x17b, 0x192, 0x1aa, 0x1c3, 0x1de, 0x1fa, 0x218, 0x238, 0x25a, 0x27e, 0x2a3 },
        { 0x166, 0x17b, 0x192, 0x1aa, 0x1c3, 0x1df, 0x1fb, 0x219, 0x239, 0x25b, 0x27f, 0x2a4 },
        { 0x167, 0x17c, 0x193, 0x1ab, 0x1c4, 0x1e0, 0x1fc, 0x21a, 0x23a, 0x25c, 0x280, 0x2a6 },
        { 0x168, 0x17d, 0x194, 0x1ac, 0x1c5, 0x1e0, 0x1fd, 0x21b, 0x23b, 0x25d, 0x281, 0x2a7 },
        { 0x168, 0x17d, 0x194, 0x1ad, 0x1c6, 0x1e1, 0x1fe, 0x21c, 0x23c, 0x25e, 0x282, 0x2a8 },
        { 0x169, 0x17e, 0x195, 0x1ad, 0x1c7, 0x1e2, 0x1ff, 0x21d, 0x23d, 0x260, 0x283, 0x2a9 },
        { 0x16a, 0x17f, 0x196, 0x1ae, 0x1c8, 0x1e3, 0x1ff, 0x21e, 0x23e, 0x261, 0x284, 0x2ab },
        { 0x16a, 0x17f, 0x197, 0x1af, 0x1c8, 0x1e4, 0x200, 0x21f, 0x23f, 0x262, 0x286, 0x2ac },
    };
    static readonly int[,] SlotVoice =
    {
        { 0, 3 }, { 1, 4 }, { 2, 5 }, { 6, 9 }, { 7, 10 }, { 8, 11 }, { 12, 15 }, { 13, 16 }, { 14, 17 },
    };
    static readonly int[] OffsetSlot = { 0, 1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 16, 17, 18, 19, 20, 21 };

    readonly Opl3 _chip;
    readonly int _rate;
    readonly Timbre[] _timbres = new Timbre[256];
    readonly int[,] _voiceLevel = new int[18, 2], _voiceKsl = new int[18, 2];
    readonly Voice[] _voice = new Voice[NumVoices * 2];
    readonly List<Voice> _pool = new();
    readonly Chan[] _chan = new Chan[NumChannels];

    /// <summary>A player at the output rate with Blood's timbre bank (GMTIMBRE.TMB: 256 x 13 bytes; NBlood's FM piano
    /// fix applied).</summary>
    public MidiFm(int sampleRate, byte[] timbreBank)
    {
        _rate = sampleRate;
        _chip = new Opl3(sampleRate);
        var t = (byte[])timbreBank.Clone();
        // sndLoadGMTimbre's gFMPianoFix (on by default): an empty first timbre (the piano) gets a working one
        byte[] fixedPiano = { 0x33, 0x31, 0x5a, 0x00, 0xb2, 0xb1, 0x50, 0xf5, 0x00, 0x01, 0x00, 0x00, 0x00 };
        bool broken = t.Length >= 13;
        for (int i = 0; i < 13 && broken; i++)
            broken = t[i] == 0;
        if (broken)
            Array.Copy(fixedPiano, t, 13);
        for (int i = 0, p = 0; i < 256 && p + 13 <= t.Length; i++)
        {
            _timbres[i] = new Timbre
            {
                Save0 = t[p++], Save1 = t[p++], Level0 = t[p++], Level1 = t[p++], Env10 = t[p++], Env11 = t[p++],
                Env20 = t[p++], Env21 = t[p++], Wave0 = t[p++], Wave1 = t[p++], Feedback = t[p++],
                Transpose = (sbyte)t[p++], Velocity = (sbyte)t[p++],
            };
        }
        for (int i = 0; i < _voice.Length; i++)
            _voice[i] = new Voice { Num = i };
        for (int i = 0; i < NumChannels; i++)
            _chan[i] = new Chan();
        AlShutdown();
    }

    void SendToPort(int port, int reg, int data) => _chip.WriteRegBuffered((ushort)(reg + ((port & 2) << 7)), (byte)data);
    void Send(int voicePort, int reg, int data) => SendToPort(voicePort != 0 ? 0x388 : 0x38A, reg, data);

    void SetVoiceTimbre(int voice)
    {
        var v = _voice[voice];
        int channel = v.Channel;
        int patch = channel == 9 ? v.Key + 128 : _chan[channel].Timbre;
        if (v.Timbre == patch)
            return;
        v.Timbre = patch;
        var tb = _timbres[patch & 255];
        int port = v.Port;
        int voc = voice >= NumVoices ? voice - NumVoices : voice;
        int slot = SlotVoice[voc, 0];
        int off = OffsetSlot[slot];
        _voiceLevel[slot, port] = TotalLevelMask - (tb.Level0 & TotalLevelMask);
        _voiceKsl[slot, port] = tb.Level0 & KslMask;
        Send(port, RegFnumLow + voc, 0);
        Send(port, RegKeyonBlock + voc, 0);
        Send(port, RegSustainRelease + off, 0xff);
        Send(port, RegAttackDecay + off, tb.Env10);
        Send(port, RegSustainRelease + off, tb.Env20);
        Send(port, RegAmVib + off, tb.Save0);
        Send(port, RegWaveSelect + off, tb.Wave0);
        Send(port, RegKslLevel + off, tb.Level0);
        slot = SlotVoice[voc, 1];
        Send(port, RegFeedbackConnection + voc, (tb.Feedback & (FeedbackMask | ConnectionBit)) | StereoBits);
        off = OffsetSlot[slot];
        _voiceLevel[slot, port] = TotalLevelMask - (tb.Level1 & TotalLevelMask);
        _voiceKsl[slot, port] = tb.Level1 & KslMask;
        Send(port, RegKslLevel + off, TotalLevelMask);
        Send(port, RegSustainRelease + off, 0xff);
        Send(port, RegAttackDecay + off, tb.Env11);
        Send(port, RegSustainRelease + off, tb.Env21);
        Send(port, RegAmVib + off, tb.Save1);
        Send(port, RegWaveSelect + off, tb.Wave1);
    }

    void SetVoiceVolume(int voice)
    {
        var v = _voice[voice];
        int channel = v.Channel;
        var tb = _timbres[v.Timbre & 255];
        int velocity = Math.Min(v.Velocity + tb.Velocity, 0x7f);
        int voc = voice >= NumVoices ? voice - NumVoices : voice;
        int slot = SlotVoice[voc, 1];
        int port = v.Port;
        uint t1 = (uint)_voiceLevel[slot, port] * (uint)(velocity + 0x80);
        t1 = ((uint)_chan[channel].Volume * t1) >> 15;
        uint volume = t1 ^ TotalLevelMask;
        volume |= (uint)_voiceKsl[slot, port];
        Send(port, RegKslLevel + OffsetSlot[slot], (int)volume);
        if ((tb.Feedback & 0x01) != 0)
        {
            int slot0 = SlotVoice[voc, 0];
            uint t2 = ((uint)_chan[channel].Volume * t1) >> 15;      // AL_AdditiveMode off: t1 as computed above
            volume = t2 ^ TotalLevelMask;
            volume |= (uint)_voiceKsl[slot0, port];
            Send(port, RegKslLevel + OffsetSlot[slot0], (int)volume);
        }
    }

    int AllocVoice()
    {
        if (_pool.Count == 0)
            return -1;
        var v = _pool[0];
        _pool.RemoveAt(0);
        return v.Num;
    }

    int GetVoice(int channel, int key)
    {
        foreach (var v in _chan[channel].Voices)
            if (v.Key == key)
                return v.Num;
        return -1;
    }

    void SetVoicePitch(int voice)
    {
        var v = _voice[voice];
        int port = v.Port;
        int voc = voice >= NumVoices ? voice - NumVoices : voice;
        int channel = v.Channel;
        int note;
        if (channel == 9)
            note = _timbres[(v.Key + 128) & 255].Transpose;
        else
            note = v.Key + _timbres[_chan[channel].Timbre & 255].Transpose;
        note += _chan[channel].KeyOffset - 12;
        note = Math.Clamp(note, 0, MaxNote);
        int detune = (int)_chan[channel].KeyDetune;
        uint pitch = OctavePitch[note / 12] | NotePitch[detune, note % 12];
        v.PitchLeft = pitch;
        pitch |= v.Status;
        Send(port, RegFnumLow + voc, (int)(pitch & 0xff));
        Send(port, RegKeyonBlock + voc, (int)((pitch >> 8) & 0xff));
    }

    void SetVoicePan(int voice)
    {
        var v = _voice[voice];
        int voc = voice >= NumVoices ? voice - NumVoices : voice;
        Send(v.Port, 0xD0 + voc, _chan[v.Channel].Pan << 1);      // AL_Stereo
    }

    void SetChannelVolume(int channel, int volume)
    {
        _chan[channel].Volume = Math.Clamp(volume, 0, MaxVolume);
        foreach (var v in _chan[channel].Voices)
            SetVoiceVolume(v.Num);
    }

    void SetChannelPan(int channel, int pan)
    {
        if (channel != 9)
            _chan[channel].Pan = pan;
        foreach (var v in _chan[channel].Voices)
            SetVoicePan(v.Num);
    }

    void ResetVoices()
    {
        _pool.Clear();
        for (int i = 0; i < _voice.Length; i++)
        {
            var v = _voice[i];
            v.Key = 0;
            v.Velocity = 0;
            v.Channel = -1;
            v.Timbre = -1;
            v.Port = i < NumVoices ? 0 : 1;
            v.Status = NoteOff;
            _pool.Add(v);
        }
        for (int c = 0; c < NumChannels; c++)
        {
            var ch = _chan[c];
            ch.Voices.Clear();
            ch.Timbre = ch.Pitchbend = ch.KeyOffset = 0;
            ch.KeyDetune = 0;
            ch.Detune = 0;
            ch.Rpn = 0;
            ch.Volume = DefaultChannelVolume;
            ch.Pan = 64;
            ch.PitchBendRange = DefaultPitchBendRange;
            ch.PitchBendSemiTones = DefaultPitchBendRange / 100;
            ch.PitchBendHundreds = DefaultPitchBendRange % 100;
        }
    }

    void FlushCard(int port)
    {
        for (int i = 0; i < NumVoices; i++)
        {
            int slot1 = OffsetSlot[SlotVoice[i, 0]], slot2 = OffsetSlot[SlotVoice[i, 1]];
            SendToPort(port, RegFnumLow + i, 0);
            SendToPort(port, RegKeyonBlock + i, 0);
            SendToPort(port, RegWaveSelect + slot1, 0);
            SendToPort(port, RegWaveSelect + slot2, 0);
            SendToPort(port, RegAttackDecay + slot1, 0xff);
            SendToPort(port, RegAttackDecay + slot2, 0xff);
            SendToPort(port, RegSustainRelease + slot1, 0xff);
            SendToPort(port, RegSustainRelease + slot2, 0xff);
            SendToPort(port, RegKslLevel + slot1, 0xff);
            SendToPort(port, RegKslLevel + slot2, 0xff);
        }
    }

    void AlShutdown()
    {
        _chip.Reset(_rate);
        SendToPort(0x388, RegTest, EnableWaveSelect);
        SendToPort(0x388, RegKbdSplit, 0);
        SendToPort(0x388, RegPercussion, 0);
        SendToPort(0x38A, RegMode, (1 << 1) + 1);       // AL_SetStereo: OPL3 mode with the stereo extension
        FlushCard(0x388);
        FlushCard(0x38A);
        ResetVoices();
    }

    void AlNoteOff(int channel, int key)
    {
        if (channel >= NumChannels)
            return;
        int voice = GetVoice(channel, key);
        if (voice < 0)
            return;
        var v = _voice[voice];
        v.Status = NoteOff;
        int voc = voice >= NumVoices ? voice - NumVoices : voice;
        Send(v.Port, RegKeyonBlock + voc, (int)((v.PitchLeft >> 8) & 0xff));
        _chan[channel].Voices.Remove(v);
        _pool.Add(v);
    }

    void AlNoteOn(int channel, int key, int velocity)
    {
        if (channel >= NumChannels)
            return;
        if (velocity == 0)
        {
            AlNoteOff(channel, key);
            return;
        }
        int voice = AllocVoice();
        if (voice < 0)
        {
            if (_chan[9].Voices.Count > 0)
            {
                AlNoteOff(9, _chan[9].Voices[0].Key);
                voice = AllocVoice();
            }
            if (voice < 0)
                return;
        }
        var v = _voice[voice];
        v.Key = key;
        v.Channel = channel;
        v.Velocity = velocity;
        v.Status = NoteOn;
        _chan[channel].Voices.Add(v);
        SetVoiceTimbre(voice);
        SetVoiceVolume(voice);
        SetVoicePitch(voice);
        SetVoicePan(voice);
    }

    void AlControlChange(int channel, int type, int data)
    {
        if (channel >= NumChannels)
            return;
        var ch = _chan[channel];
        switch (type)
        {
            case MidiVolume: SetChannelVolume(channel, data); break;
            case MidiPan: SetChannelPan(channel, data); break;
            case MidiDetune: ch.Detune = data; break;
            case MidiAllNotesOff:
                while (ch.Voices.Count > 0)
                    AlNoteOff(channel, ch.Voices[0].Key);
                break;
            case MidiResetAllControllers:
                ResetVoices();
                SetChannelVolume(channel, DefaultChannelVolume);
                SetChannelPan(channel, 64);
                ch.Detune = 0;
                break;
            case MidiRpnMsb:
                ch.Rpn &= 0x00FF;
                ch.Rpn |= (uint)(data & 0xFF) << 8;
                break;
            case MidiRpnLsb:
                ch.Rpn &= 0xFF00;
                ch.Rpn |= (uint)(data & 0xFF);
                break;
            case MidiDataEntryMsb:
                if (ch.Rpn == MidiPitchbendMsb)
                {
                    ch.PitchBendSemiTones = data;
                    ch.PitchBendRange = ch.PitchBendSemiTones * 100 + ch.PitchBendHundreds;
                }
                break;
            case MidiDataEntryLsb:
                if (ch.Rpn == MidiPitchbendLsb)
                {
                    ch.PitchBendHundreds = data;
                    ch.PitchBendRange = ch.PitchBendSemiTones * 100 + ch.PitchBendHundreds;
                }
                break;
        }
    }

    void AlProgramChange(int channel, int patch)
    {
        if (channel < NumChannels)
            _chan[channel].Timbre = patch;
    }

    void AlPitchBend(int channel, int lsb, int msb)
    {
        if (channel >= NumChannels)
            return;
        var ch = _chan[channel];
        int pitchbend = lsb + (msb << 8);
        int totalBend = pitchbend * ch.PitchBendRange / (PitchbendCenter / FinetuneRange);
        ch.Pitchbend = pitchbend;
        ch.KeyOffset = totalBend / FinetuneRange;
        ch.KeyOffset -= ch.PitchBendSemiTones;
        ch.KeyDetune = (uint)(totalBend % FinetuneRange);
        foreach (var v in ch.Voices)
            SetVoicePitch(v.Num);
    }

    // ---------------------------------------------------------------- MIDI player (midi.cpp)
    const int MidiVolume = 7, MidiPan = 10, MidiDetune = 94, MidiRpnMsb = 100, MidiRpnLsb = 101, MidiDataEntryMsb = 6,
        MidiDataEntryLsb = 38, MidiPitchbendMsb = 0, MidiPitchbendLsb = 0, MidiHold1 = 0x40, MidiSostenuto = 0x42,
        MidiReverb = 0x5b, MidiChorus = 0x5d, MidiAllSoundsOff = 0x78, MidiResetAllControllers = 0x79,
        MidiAllNotesOff = 0x7b, MidiMonoModeOn = 0x7E, MidiBankSelectMsb = 0, MidiBankSelectLsb = 32;
    const int EmidiInfinite = -1, EmidiEndLoopValue = 127, EmidiAllCards = 127, EmidiIncludeTrack = 110,
        EmidiExcludeTrack = 111, EmidiProgramChange = 112, EmidiVolumeChange = 113, EmidiContextStart = 114,
        EmidiContextEnd = 115, EmidiLoopStart = 116, EmidiLoopEnd = 117, EmidiSongLoopStart = 118,
        EmidiSongLoopEnd = 119, EmidiAdLib = 7, EmidiNumContexts = 7;
    const int GenmidiDefaultVolume = 90, TimePrecision = 16;
    static readonly int[] CommandLengths = { 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2, 1, 1, 2, 0 };

    sealed class Context
    {
        public int Pos = -1, LoopStart = -1, LoopCount, RunningStatus, FpSecondsPerTick, Tick, Beat, Measure,
            BeatsPerMeasure, TicksPerBeat, TimeBase, Delay;
        public uint Time;
        public bool Active;
        public Context Copy() => (Context)MemberwiseClone();
    }

    sealed class Track
    {
        public int Start, Pos, Delay, RunningStatus, CurrentContext;
        public bool Active, IncludeTrack, EmidiProgramChange, EmidiVolumeChange;
        public readonly Context[] Ctx = new Context[EmidiNumContexts];
    }

    byte[] _song = Array.Empty<byte>();
    Track[] _tracks = Array.Empty<Track>();
    bool _loop, _active;
    int _division, _tick, _beat = 1, _measure = 1, _beatsPerMeasure, _ticksPerBeat, _timeBase, _fpSecondsPerTick;
    uint _time;
    int _context, _activeTracks;
    int _renderTempo = -1, _renderTimer;

    static int ReadNumber(byte[] b, int at, int size)
    {
        int value = 0;
        for (int i = 0; i < Math.Min(size, 4) && at + i < b.Length; i++)
            value = (value << 8) + b[at + i];
        return value;
    }

    int Next(Track t) => t.Pos < _song.Length ? _song[t.Pos++] : 0xff;

    int ReadDelta(Track t)
    {
        int value = Next(t);
        if ((value & 0x80) != 0)
        {
            value &= 0x7f;
            int c;
            do
            {
                c = Next(t);
                value = (value << 7) + (c & 0x7f);
            }
            while ((c & 0x80) != 0 && t.Pos < _song.Length);
        }
        return value;
    }

    /// <summary>MIDI_PlaySong: starts a standard MIDI file (format 0 or 1), looping or not. False if it isn't one.</summary>
    public bool Play(byte[] song, bool loop)
    {
        if (song.Length < 14 || song[0] != 'M' || song[1] != 'T' || song[2] != 'h' || song[3] != 'd')
            return false;
        int headerSize = ReadNumber(song, 4, 4);
        int format = ReadNumber(song, 8, 2);
        int numTracks = ReadNumber(song, 10, 2);
        int division = (short)ReadNumber(song, 12, 2);
        if (division < 0)
            division = 96;
        if (format > 1 || numTracks == 0)
            return false;
        int ptr = 8 + headerSize;
        var tracks = new Track[numTracks];
        for (int i = 0; i < numTracks; i++)
        {
            if (ptr + 8 > song.Length || song[ptr] != 'M' || song[ptr + 1] != 'T' || song[ptr + 2] != 'r' || song[ptr + 3] != 'k')
                return false;
            int len = ReadNumber(song, ptr + 4, 4);
            ptr += 8;
            tracks[i] = new Track { Start = ptr };
            for (int c = 0; c < EmidiNumContexts; c++)
                tracks[i].Ctx[c] = new Context();
            ptr += len;
        }
        _song = song;
        _tracks = tracks;
        _loop = loop;
        _division = division;
        InitEmidi();
        ResetTracks();
        MidiReset();
        AlShutdown();          // AdLibDrv_MIDI_StartPlayback: AL_Init
        SetTempo(120);
        _active = true;
        return true;
    }

    public void Stop()
    {
        _active = false;
        AlShutdown();
    }

    void SetTempo(int tempo)
    {
        _renderTempo = tempo * _division / 60;     // AdLibDrv_MIDI_SetTempo
        _renderTimer = 0;
        int ticksPerSecond = Math.Max(1, tempo * _division / 60);
        _fpSecondsPerTick = (1 << TimePrecision) / ticksPerSecond;
    }

    void ResetTracks()
    {
        _tick = 0;
        _beat = 1;
        _measure = 1;
        _time = 0;
        _beatsPerMeasure = 4;
        _ticksPerBeat = _division;
        _timeBase = 4;
        _activeTracks = 0;
        _context = 0;
        foreach (var t in _tracks)
        {
            t.Pos = t.Start;
            t.Delay = ReadDelta(t);
            t.Active = t.IncludeTrack;
            t.RunningStatus = 0;
            t.CurrentContext = 0;
            t.Ctx[0].LoopStart = t.Start;
            t.Ctx[0].LoopCount = 0;
            if (t.Active)
                _activeTracks++;
        }
    }

    void AdvanceTick()
    {
        _time += (uint)_fpSecondsPerTick;
        _tick++;
        while (_tick > _ticksPerBeat)
        {
            _tick -= _ticksPerBeat;
            _beat++;
        }
        while (_beat > _beatsPerMeasure)
        {
            _beat -= _beatsPerMeasure;
            _measure++;
        }
    }

    void SysEx(Track t) => t.Pos += ReadDelta(t);

    void MetaEvent(Track t)
    {
        int command = Next(t);
        int length = Next(t);
        switch (command)
        {
            case 0x2F:      // end of track
                t.Active = false;
                _activeTracks--;
                break;
            case 0x51:      // tempo
            {
                int us = ReadNumber(_song, t.Pos, 3);
                if (us > 0)
                    SetTempo(60000000 / us);
                break;
            }
            case 0x58:      // time signature
            {
                if (_tick > 0 || _beat > 1)
                    _measure++;
                _tick = 0;
                _beat = 1;
                _timeBase = 1;
                _beatsPerMeasure = t.Pos < _song.Length ? _song[t.Pos] : 4;
                int denominator = t.Pos + 1 < _song.Length ? _song[t.Pos + 1] : 2;
                while (denominator-- > 0)
                    _timeBase += _timeBase;
                _ticksPerBeat = _division * 4 / Math.Max(1, _timeBase);
                break;
            }
        }
        t.Pos += length;
    }

    void SaveContext(Context c, Track t)
    {
        c.RunningStatus = t.RunningStatus;
        c.Time = _time;
        c.FpSecondsPerTick = _fpSecondsPerTick;
        c.Tick = _tick;
        c.Beat = _beat;
        c.Measure = _measure;
        c.BeatsPerMeasure = _beatsPerMeasure;
        c.TicksPerBeat = _ticksPerBeat;
        c.TimeBase = _timeBase;
    }

    void RestoreTime(Context c)
    {
        _time = c.Time;
        _fpSecondsPerTick = c.FpSecondsPerTick;
        _tick = c.Tick;
        _beat = c.Beat;
        _measure = c.Measure;
        _beatsPerMeasure = c.BeatsPerMeasure;
        _ticksPerBeat = c.TicksPerBeat;
        _timeBase = c.TimeBase;
    }

    bool InterpretController(Track t, bool timeSet, int channel, int c1, int c2)
    {
        switch (c1)
        {
            case MidiMonoModeOn:
                t.Pos++;
                break;
            case MidiVolume:
                if (!t.EmidiVolumeChange)
                    MidiSetChannelVolume(channel, c2);
                break;
            case EmidiIncludeTrack:
            case EmidiExcludeTrack:
            case EmidiContextStart:
                break;
            case EmidiProgramChange:
                if (t.EmidiProgramChange)
                    AlProgramChange(channel, c2 & 0x7f);
                break;
            case EmidiVolumeChange:
                if (t.EmidiVolumeChange)
                    MidiSetChannelVolume(channel, c2);
                break;
            case EmidiContextEnd:
            {
                if (t.CurrentContext == _context || _context < 0 || t.Ctx[_context].Pos < 0)
                    break;
                var c = t.Ctx[_context];
                t.CurrentContext = _context;
                t.Ctx[0].LoopStart = c.LoopStart;
                t.Ctx[0].LoopCount = c.LoopCount;
                t.Pos = c.Pos;
                t.RunningStatus = c.RunningStatus;
                if (timeSet)
                    break;
                RestoreTime(c);
                timeSet = true;
                break;
            }
            case EmidiLoopStart:
            case EmidiSongLoopStart:
            {
                int loopcount = c2 == 0 ? EmidiInfinite : c2;
                IEnumerable<Track> which = c1 == EmidiSongLoopStart ? _tracks : new[] { t };
                foreach (var tr in which)
                {
                    var c = tr.Ctx[0];
                    c.LoopCount = loopcount;
                    c.Pos = tr.Pos;
                    c.LoopStart = tr.Pos;
                    c.Active = tr.Active;
                    c.Delay = tr.Delay;
                    SaveContext(c, tr);
                }
                break;
            }
            case EmidiLoopEnd:
            case EmidiSongLoopEnd:
            {
                if (c2 != EmidiEndLoopValue || t.Ctx[0].LoopStart < 0 || t.Ctx[0].LoopCount == 0)
                    break;
                IEnumerable<Track> which;
                if (c1 == EmidiSongLoopEnd)
                {
                    which = _tracks;
                    _activeTracks = 0;
                }
                else
                {
                    which = new[] { t };
                    _activeTracks--;
                }
                foreach (var tr in which)
                {
                    var c = tr.Ctx[0];
                    if (c.LoopCount != EmidiInfinite)
                        c.LoopCount--;
                    tr.Pos = c.LoopStart;
                    tr.RunningStatus = c.RunningStatus;
                    tr.Delay = c.Delay;
                    tr.Active = c.Active;
                    if (tr.Active)
                        _activeTracks++;
                    if (!timeSet)
                    {
                        RestoreTime(c);
                        timeSet = true;
                    }
                }
                break;
            }
            default:
                AlControlChange(channel, c1, c2);
                break;
        }
        return timeSet;
    }

    /// <summary>MIDI_ServiceRoutine: one MIDI tick of every track.</summary>
    void ServiceRoutine()
    {
        if (!_active)
            return;
        bool timeSet = false;
        for (int n = 0; n < _tracks.Length; n++)
        {
            var t = _tracks[n];
            while (t.Active && t.Delay == 0)
            {
                int ev = Next(t);
                if ((ev >> 4) == 0xF)
                {
                    if (ev == 0xF0 || ev == 0xF7)
                        SysEx(t);
                    else if (ev == 0xFF)
                        MetaEvent(t);
                    if (t.Active)
                        t.Delay = ReadDelta(t);
                    continue;
                }
                if ((ev & 0x80) != 0)
                    t.RunningStatus = ev;
                else
                {
                    ev = t.RunningStatus;
                    t.Pos--;
                }
                int channel = ev & 0xf, command = ev >> 4, c1 = 0, c2 = 0;
                if (CommandLengths[command] > 0)
                {
                    c1 = Next(t);
                    if (CommandLengths[command] > 1)
                        c2 = Next(t);
                }
                switch (command)
                {
                    case 0x8: AlNoteOff(channel, c1); break;
                    case 0x9: AlNoteOn(channel, c1, c2); break;
                    case 0xB: timeSet = InterpretController(t, timeSet, channel, c1, c2); break;
                    case 0xC:
                        if (!t.EmidiProgramChange)
                            AlProgramChange(channel, c1 & 0x7f);
                        break;
                    case 0xE: AlPitchBend(channel, c1, c2); break;
                }
                t.Delay = ReadDelta(t);
            }
            t.Delay--;
            if (_activeTracks == 0)
            {
                ResetTracks();
                if (_loop)
                {
                    n = -1;         // tracknum = 0 (the loop's n++ makes it 0)
                    continue;
                }
                _active = false;
                break;
            }
        }
        AdvanceTick();
    }

    void MidiSetChannelVolume(int channel, int volume) => AlControlChange(channel, MidiVolume, volume);

    /// <summary>MIDI_Reset: every channel's controllers to their start.</summary>
    void MidiReset()
    {
        for (int c = 0; c < 16; c++)
        {
            AlControlChange(c, MidiHold1, 0);
            AlControlChange(c, MidiSostenuto, 0);
            AlControlChange(c, MidiAllNotesOff, 0);
            AlControlChange(c, MidiAllSoundsOff, 0);
        }
        for (int c = 0; c < 16; c++)
        {
            AlControlChange(c, MidiResetAllControllers, 0);
            AlControlChange(c, MidiRpnMsb, MidiPitchbendMsb);
            AlControlChange(c, MidiRpnLsb, MidiPitchbendLsb);
            AlControlChange(c, MidiDataEntryMsb, 2);
            AlControlChange(c, MidiDataEntryLsb, 0);
            AlControlChange(c, MidiPan, 64);
            AlControlChange(c, MidiReverb, 40);
            AlControlChange(c, MidiChorus, 0);
            AlControlChange(c, MidiBankSelectMsb, 0);
            AlControlChange(c, MidiBankSelectLsb, 0);
            AlProgramChange(c, 0);
        }
        for (int c = 0; c < 16; c++)
            MidiSetChannelVolume(c, GenmidiDefaultVolume);
    }

    /// <summary>_MIDI_InitEMIDI: which tracks play on this card, and the loop and context starts.</summary>
    void InitEmidi()
    {
        ResetTracks();
        foreach (var t in _tracks)
        {
            _tick = 0;
            _beat = 1;
            _measure = 1;
            _time = 0;
            _beatsPerMeasure = 4;
            _ticksPerBeat = _division;
            _timeBase = 4;
            _activeTracks = 0;
            _context = -1;
            t.RunningStatus = 0;
            t.Active = true;
            t.EmidiProgramChange = false;
            t.EmidiVolumeChange = false;
            t.IncludeTrack = true;
            for (int c = 0; c < EmidiNumContexts; c++)
                t.Ctx[c] = new Context();
            while (t.Delay > 0)
            {
                AdvanceTick();
                t.Delay--;
            }
            bool includeFound = false;
            while (t.Active && t.Pos < _song.Length)
            {
                int ev = Next(t);
                if ((ev >> 4) == 0xF)
                {
                    if (ev == 0xF0 || ev == 0xF7)
                        SysEx(t);
                    else if (ev == 0xFF)
                        MetaEvent(t);
                    if (t.Active)
                    {
                        t.Delay = ReadDelta(t);
                        while (t.Delay > 0)
                        {
                            AdvanceTick();
                            t.Delay--;
                        }
                    }
                    continue;
                }
                if ((ev & 0x80) != 0)
                    t.RunningStatus = ev;
                else
                {
                    ev = t.RunningStatus;
                    t.Pos--;
                }
                int command = ev >> 4;
                int length = CommandLengths[command];
                if (command == 0xB)
                {
                    if (t.Pos < _song.Length && _song[t.Pos] == MidiMonoModeOn)
                        length++;
                    int c1 = Next(t), c2 = Next(t);
                    length -= 2;
                    switch (c1)
                    {
                        case EmidiLoopStart:
                        case EmidiSongLoopStart:
                        {
                            var c = t.Ctx[0];
                            c.LoopCount = c2 == 0 ? EmidiInfinite : c2;
                            c.Pos = t.Pos;
                            c.LoopStart = t.Pos;
                            SaveContext(c, t);
                            break;
                        }
                        case EmidiLoopEnd:
                        case EmidiSongLoopEnd:
                            if (c2 == EmidiEndLoopValue)
                            {
                                t.Ctx[0].LoopStart = -1;
                                t.Ctx[0].LoopCount = 0;
                            }
                            break;
                        case EmidiIncludeTrack:
                            if (c2 == EmidiAllCards || c2 == EmidiAdLib)
                            {
                                includeFound = true;
                                t.IncludeTrack = true;
                            }
                            else if (!includeFound)
                            {
                                includeFound = true;
                                t.IncludeTrack = false;
                            }
                            break;
                        case EmidiExcludeTrack:
                            if (c2 == EmidiAllCards || c2 == EmidiAdLib)
                                t.IncludeTrack = false;
                            break;
                        case EmidiProgramChange:
                            t.EmidiProgramChange = true;
                            break;
                        case EmidiVolumeChange:
                            t.EmidiVolumeChange = true;
                            break;
                        case EmidiContextStart:
                            if (c2 > 0 && c2 < EmidiNumContexts)
                            {
                                var c = t.Ctx[c2];
                                c.Pos = t.Pos;
                                c.LoopStart = t.Ctx[0].LoopStart;
                                c.LoopCount = t.Ctx[0].LoopCount;
                                SaveContext(c, t);
                            }
                            break;
                    }
                }
                t.Pos += length;
                t.Delay = ReadDelta(t);
                while (t.Delay > 0)
                {
                    AdvanceTick();
                    t.Delay--;
                }
            }
        }
        ResetTracks();
    }

    // ---------------------------------------------------------------- output
    /// <summary>AdLibDrv_MIDI_Service: count stereo frames into buffer (interleaved left, right; -1..1), the MIDI
    /// ticks run as the samples pass.</summary>
    public void Render(float[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            while (_renderTimer >= _rate)
            {
                if (_renderTempo >= 0)
                    ServiceRoutine();
                _renderTimer -= _rate;
            }
            if (_renderTempo >= 0)
                _renderTimer += _renderTempo;
            _chip.GenerateResampled(out short l, out short r);
            buffer[i * 2] = Math.Clamp(l * PostAmp, -32768f, 32767f) / 32768f;
            buffer[i * 2 + 1] = Math.Clamp(r * PostAmp, -32768f, 32767f) / 32768f;
        }
    }

    public bool Active => _active;
}
