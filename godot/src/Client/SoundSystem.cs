using System;
using System.Collections.Generic;
using Chosen.Core;
using Chosen.Data;
using Chosen.Sim;
using Godot;

namespace Chosen.Client;

/// <summary>
/// The world's sounds, mixed as Blood's audiolib mixes them (NBlood's sfx.cpp and multivoc): one output stream
/// (an AudioStreamGenerator) this class fills each frame. A sound on an object follows it on one of its channels (a
/// new sound on a busy channel replaces the old one; flags 2 and 4 of sfxPlay3DSound are kept by the game). A sound in
/// the world is two voices, one for each ear (sfxPlay3DSound with gStereo): each its own volume (Calc3DValues: relVol *
/// 80 / max(3/8 distance, 64), Vol3d of the angle to the ear's side, 15 degrees off ahead), each started that many
/// samples in as its ear is nearer (the time between the ears: lPhase, rPhase) and each its own Doppler pitch
/// (sfxUpdate3DSounds every game frame: the ear's and the source's speed along the line between them against 5853);
/// a sound heard everywhere (sndStartSample), the title's thunder and the ambient sprites one voice on both sides. A
/// voice plays its sample as MV_MixMono does: the sample at position >> 16, held (no interpolation: the bright edge
/// Blood sounds with; snd_smooth 1 interpolates), its pan volumes on the audiolib's 0..255 eased towards their goal
/// (MV_VolumeSmoothFactor). Heard from the player as Blood hears it (not the camera: an eye past a water plane views
/// from the room beyond the link, which in Build's map is elsewhere). Files come from Blood's extracted sounds
/// (game/blood3d/sound).
/// </summary>
public partial class SoundSystem : Node3D
{
    static readonly Cvar Volume = Cvars.Register("volume", "0.7", "sound volume (0..1; 1 is Blood's FXVolume 255)", saved: true);
    static readonly Cvar Smooth = Cvars.Register("snd_smooth", "0",
        "1: sounds interpolated between their samples (softer); 0: each sample held as the audiolib mixes (NBlood's crisp sound)", saved: true);

    /// <summary>kEarDist / 2: each ear 43 Build units (8.5 cm) to the side, in Quake units.</summary>
    const float EarOffset = (int)((32 << 4) * 0.17) / 2 / 16f;
    /// <summary>Vol3d's ears: the side each hears best, 85 of Build's 2048 (15 degrees) off ahead.</summary>
    static readonly float EarTurn = 85f / 2048f * Mathf.Tau;
    /// <summary>Calc3DValues' Doppler: speeds in Build units a game frame against this.</summary>
    const float SoundSpeed = 5853f;
    const float GameFrameRate = 30f;
    /// <summary>Faster than this (Build units a game frame) is a jump, not a speed: through a link, a teleport, a
    /// respawn (Blood's sfxCorrectListenerPos): no Doppler for it.</summary>
    const float Jump = 2000f;

    /// <summary>A sound's samples as the RAW resource: 16-bit signed (an 8-bit one shifted up), its rate.</summary>
    sealed class Sample
    {
        public short[] Data;
        public int Rate;
        public bool Loop;
        public double Seconds => Data.Length / (double)Rate;
    }

    /// <summary>One of the audiolib's voices: a sample from a position (16.16), its frequency, its pan volumes (0..1 of
    /// 255) and their goals.</summary>
    sealed class Tap
    {
        public Sample S;
        public long Pos;
        public double Freq;
        public float L, R, GoalL, GoalR;
        public bool Done;
    }

    /// <summary>A sound (a BONKLE): its one voice (heard everywhere) or its two (an ear each), what it is on, where it
    /// was, how loud.</summary>
    sealed class Voice
    {
        public Tap Left, Right;
        public XObject Owner;
        public Vector3 At;            // Quake position of a sound not on an object
        public Vector3 Was;           // where it was a moment ago (its speed: Doppler)
        public int Channel = -1;      // the object's channel (sfxPlay3DSound's chanId as is), -1: none
        public int SoundId;
        public int RelVol;
        public double Pitch;          // Hz: the sample's rate by the sfx's pitch
        public bool Positional;
        public float Fixed;           // a voice heard everywhere: its volume (0..1 of 255)
        public bool Playing => Left is { Done: false } || Right is { Done: false };
    }

    World _world;
    readonly List<Voice> _voices = new();
    readonly Dictionary<string, Sample> _samples = new();
    readonly Dictionary<Sprite, Voice> _ambient = new();
    Vector3 _listener, _earL, _earR, _earLWas, _earRWas;
    Vector3 _forward = new(1, 0, 0), _right = new(0, -1, 0);
    ulong _lastUpdate;
    bool _paused;

    // the output: a generator filled each frame
    AudioStreamPlayer _out;
    AudioStreamGeneratorPlayback _playback;
    int _mixRate, _capacity;
    float _queue = 0.06f;     // seconds queued ahead (more after a frame that let it run dry)
    Vector2[] _buffer = Array.Empty<Vector2>();
    Voice _solo;          // sndrec: only this one heard
    static readonly Voice Silence = new();

    public override void _Ready()
    {
        // the audiolib clamps a mix past full scale; a limiter keeps many sounds at once (a level's thunder) from
        // crackling the way a clamp does
        int master = AudioServer.GetBusIndex("Master");
        bool limited = false;
        for (int i = 0; i < AudioServer.GetBusEffectCount(master); i++)
            limited |= AudioServer.GetBusEffect(master, i) is AudioEffectHardLimiter;
        if (!limited)
            AudioServer.AddBusEffect(master, new AudioEffectHardLimiter { CeilingDb = -0.3f });
        // the mixer's own bus (sndrec records it)
        if (AudioServer.GetBusIndex("Sfx") < 0)
        {
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount - 1, "Sfx");
            AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
        }
        _mixRate = (int)AudioServer.GetMixRate();
        _out = new AudioStreamPlayer
        {
            Name = "SfxMixer", Bus = "Sfx",
            Stream = new AudioStreamGenerator { MixRate = _mixRate, BufferLength = 0.25f },
        };
        AddChild(_out);
        _out.Play();
        _playback = (AudioStreamGeneratorPlayback)_out.GetStreamPlayback();
        _capacity = _playback.GetFramesAvailable();
    }

    public void Setup(World world)
    {
        StopAll();
        if (_world == world)
            return;
        if (_world != null)
        {
            _world.SoundPlayed -= OnSound;
            _world.SoundAtPlayed -= OnSoundAt;
            _world.SoundKilled -= OnKill;
            _world.SoundPlayedCP -= OnSoundCP;
            _world.SoundPlayingQuery = null;
        }
        _world = world;
        world.SoundPlayed += OnSound;
        world.SoundAtPlayed += OnSoundAt;
        world.SoundKilled += OnKill;
        world.SoundPlayedCP += OnSoundCP;
        world.SoundPlayingQuery = IsPlaying;
    }

    /// <summary>Is one of these sounds playing (on an object, or anywhere when null)? A modern map's custom dudes ask
    /// (a sound not started again while it plays).</summary>
    bool IsPlaying(Sprite e, int[] ids)
    {
        foreach (var v in _voices)
            if (v.Playing && (e == null || v.Owner == e) && Array.IndexOf(ids, v.SoundId) >= 0)
                return true;
        return false;
    }

    public void StopAll()
    {
        _voices.Clear();
        _ambient.Clear();
    }


    /// <summary>sfxFileName: a looping sound's file is its _loop variant; played once, the plain one.</summary>
    static string FileFor(SfxData.Sfx sfx, bool looped)
    {
        if (!looped && sfx.Loop && sfx.File.EndsWith("_loop.wav"))
            return sfx.File[..^9] + ".wav";
        return sfx.File;
    }

    Sample SampleOf(string file)
    {
        // by its path: an add-on's own file of a name before the base game's
        string path = BloodData.Find("sound/" + file);
        if (_samples.TryGetValue(path, out var s))
            return s;
        s = Load(path);
        if (s != null)
        {
            s.Loop = file.EndsWith("_loop.wav");
            Core.Perf.SoundLoads++;
        }
        _samples[path] = s;
        return s;
    }

    /// <summary>A plain PCM WAV's samples (8 or 16-bit; a stereo one's left), null if it isn't one.</summary>
    static Sample Load(string path)
    {
        byte[] f;
        try
        {
            if (!System.IO.File.Exists(path))
                return null;
            f = System.IO.File.ReadAllBytes(path);
        }
        catch (System.IO.IOException)
        {
            return null;
        }
        if (f.Length < 12 || f[0] != 'R' || f[1] != 'I' || f[2] != 'F' || f[3] != 'F')
            return null;
        int rate = 0, bits = 0, channels = 0, dataAt = -1, dataLen = 0;
        for (int p = 12; p + 8 <= f.Length;)
        {
            int len = BitConverter.ToInt32(f, p + 4);
            string id = System.Text.Encoding.ASCII.GetString(f, p, 4);
            if (id == "fmt " && p + 24 <= f.Length)
            {
                if (BitConverter.ToInt16(f, p + 8) != 1)
                    return null;            // not PCM
                channels = BitConverter.ToInt16(f, p + 10);
                rate = BitConverter.ToInt32(f, p + 12);
                bits = BitConverter.ToInt16(f, p + 22);
            }
            else if (id == "data")
            {
                dataAt = p + 8;
                dataLen = Math.Min(len, f.Length - dataAt);
                break;
            }
            p += 8 + len + (len & 1);
        }
        if (dataAt < 0 || rate <= 0 || bits is not (8 or 16) || channels is not (1 or 2))
            return null;
        int frameBytes = bits / 8 * channels;
        int frames = dataLen / frameBytes;
        if (frames == 0)
            return null;
        var data = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            int at = dataAt + i * frameBytes;
            // (an 8-bit WAV's samples are unsigned: the audiolib's CONVERT_LE_SAMPLE_TO_SIGNED)
            data[i] = bits == 8 ? (short)((f[at] - 128) << 8) : BitConverter.ToInt16(f, at);
        }
        return new Sample { Data = data, Rate = rate };
    }

    // ---------------------------------------------------------------- playing

    /// <summary>A sound not in the world (sndStartSample: the title screen's thunder), file under the sound folder.</summary>
    public void PlayUi(string file)
    {
        var s = SampleOf(file);
        if (s == null)
            return;
        _voices.Add(Fixed(s, s.Rate, 1f));
    }

    /// <summary>One voice on both sides at a volume (0..1 of 255).</summary>
    static Voice Fixed(Sample s, double freq, float vol)
    {
        var t = new Tap { S = s, Freq = freq, L = vol, R = vol, GoalL = vol, GoalR = vol };
        return new Voice { Left = t, Fixed = vol, Pitch = freq };
    }

    /// <summary>A world sound's two voices (an ear each), started as sfxPlay3DSound starts them: Calc3DValues for
    /// their volumes and pitches, each ear's voice that many samples in as the ear is nearer.</summary>
    void StartPositional(Voice v, Sample s)
    {
        v.Positional = true;
        var src = SourceOf(v);
        v.Was = src;
        v.Left = new Tap { S = s };
        v.Right = new Tap { S = s };
        Calc3D(v, src, 0f, true);
        v.Left.L = v.Left.GoalL;
        v.Right.R = v.Right.GoalR;
        // the time between the ears: mulscale16r(approxDist to the ear, 4114 at 11025 Hz, else 8228) samples
        int k = s.Rate == 11025 ? 4114 : 8228;
        double phaseL = Math.Round(ApproxDist(src - _earL) * 16 * k / 65536.0);
        double phaseR = Math.Round(ApproxDist(src - _earR) * 16 * k / 65536.0);
        double min = Math.Min(phaseL, phaseR);
        v.Left.Pos = (long)(phaseR - min) << 16;
        v.Right.Pos = (long)(phaseL - min) << 16;
        _voices.Add(v);
    }

    /// <summary>approxDist (Build's: the longer side and 3/8 of the shorter) over x and y.</summary>
    static float ApproxDist(Vector3 d)
    {
        float x = Math.Abs(d.X), y = Math.Abs(d.Y);
        return x > y ? x + y * 3 / 8 : y + x * 3 / 8;
    }

    /// <summary>
    /// Calc3DValues for a world sound at src (Quake units): each ear's goal volume (scale(vol, 80, distance), distance
    /// max(3/8 of it, 64), by Vol3d of the angle from the ear's side: 1 - (1 - cos) / 8 of it, at most 255) and, dt
    /// seconds after the last, each ear's pitch (Doppler: the sound's rate by (the ear's speed along the line + 5853) /
    /// (the source's + 5853), Build units a game frame, 1000..50000 Hz).
    /// </summary>
    void Calc3D(Voice v, Vector3 src, float dt, bool start)
    {
        var d = src - _listener;
        float distance = Math.Max(d.Length() * 0.375f, 64f);
        float vol = v.RelVol * 80f / distance;
        // the line to the source, flat (getangle of dx, dy); ahead when it is right here
        var flat = new Vector3(d.X, d.Y, 0);
        var dir = flat.LengthSquared() > 1e-6f ? flat.Normalized() : _forward;
        var left = -_right;
        var sideL = (_forward * Mathf.Cos(EarTurn) + left * Mathf.Sin(EarTurn)).Normalized();
        var sideR = (_forward * Mathf.Cos(EarTurn) - left * Mathf.Sin(EarTurn)).Normalized();
        float lVol = Math.Min(vol * (1f - (1f - dir.Dot(sideL)) / 8f), 255f) / 255f;
        float rVol = Math.Min(vol * (1f - (1f - dir.Dot(sideR)) / 8f), 255f) / 255f;
        v.Left.GoalL = lVol;
        v.Left.GoalR = 0;
        v.Right.GoalL = 0;
        v.Right.GoalR = rVol;
        double lPitch = v.Pitch, rPitch = v.Pitch;
        if (!start && dt > 0)
        {
            // speeds in Build units a game frame (Quake units are 16 of them)
            float perFrame = 16f / (dt * GameFrameRate);
            float Speed(Vector3 moved) => moved.Length() * perFrame > Jump ? 0 : moved.Dot(dir) * perFrame;
            float srcV = Speed(src - v.Was);
            float earLV = Speed(_earL - _earLWas);
            float earRV = Speed(_earR - _earRWas);
            lPitch = Math.Clamp(v.Pitch * (earLV + SoundSpeed) / (srcV + SoundSpeed), 1000, 50000);
            rPitch = Math.Clamp(v.Pitch * (earRV + SoundSpeed) / (srcV + SoundSpeed), 1000, 50000);
        }
        else if (start)
        {
            lPitch = Math.Clamp(v.Pitch, 1000, 50000);
            rPitch = lPitch;
        }
        v.Left.Freq = lPitch;
        v.Right.Freq = rPitch;
    }

    void OnSound(XObject e, int soundId, int chanId, int flags) => OnSoundCP(e, soundId, chanId, flags, 0, 0);

    /// <summary>sfxPlay3DSoundCP: a world sound with its own pitch (a 16.16 factor of the sample's rate; 0 or less: the
    /// sound's) and volume (1..255; 0: the sound's; -1: silent).</summary>
    void OnSoundCP(XObject e, int soundId, int chanId, int flags, int pitch, int volume)
    {
        if (e == null || soundId < 0 || !SfxData.TryGet(soundId, out var sfx))
            return;
        int relVol = volume == 0 ? sfx.RelVol : volume < 0 ? 0 : Math.Min(volume, 255);
        int sfxPitch = pitch > 0 ? pitch : sfx.Pitch;
        // NBlood's channels as they are: folded into 1..7 (DarkPlaces' sndMapChannel) a landing blood drip's (385 on 30,
        // anyone's: flag 1) was the player's shot's (2), and each drip a shotgun blast threw cut the shot off
        int ch = chanId >= 0 ? chanId : -1;
        // sfxPlay3DSound's flags: what already plays on the channel (1: anyone's) is kept, the new sound dropped, if
        // it is any sound (4) or the same one (2): standing in lava (352 each frame) sizzles, not a restart a frame
        if (ch >= 0 && (flags & 6) != 0)
            foreach (var v0 in _voices)
                if (v0.Channel == ch && (v0.Owner == e || (flags & 1) != 0)
                    && ((flags & 4) != 0 || v0.SoundId == soundId) && v0.Playing)
                    return;
        var s = SampleOf(FileFor(sfx, chanId >= 0));
        if (s == null)
            return;
        bool local = chanId == -2;           // sndStartSample: heard everywhere
        double freq = s.Rate * (sfxPitch > 0 ? sfxPitch / 65536.0 : 1.0);
        if (ch >= 0)
            for (int i = _voices.Count - 1; i >= 0; i--)
                if ((_voices[i].Owner == e || (flags & 1) != 0) && _voices[i].Channel == ch)
                    _voices.RemoveAt(i);
        Voice v;
        if (local)
        {
            // sndStartSample: relVol * 80, at most 255
            float vol = Math.Min(relVol * 80f / 255f, 1f);
            if (vol <= 0)
                return;
            v = Fixed(s, freq, vol);
            _voices.Add(v);
        }
        else
        {
            v = new Voice { Owner = e, RelVol = relVol, Pitch = freq };
            if (v.RelVol <= 0)
                return;
            StartPositional(v, s);
        }
        v.Owner = e;
        v.Channel = ch;
        v.SoundId = soundId;
    }

    void OnSoundAt(Vector3 at, int soundId)
    {
        if (soundId < 0 || !SfxData.TryGet(soundId, out var sfx))
            return;
        var s = SampleOf(FileFor(sfx, false));
        if (s == null || sfx.RelVol <= 0)
            return;
        var v = new Voice { At = at, SoundId = soundId, RelVol = sfx.RelVol, Pitch = s.Rate * (sfx.Pitch > 0 ? sfx.Pitch / 65536.0 : 1.0) };
        StartPositional(v, s);
    }

    /// <summary>sfxKill3DSound(e, channel, sound id): an object's sounds on a channel (-1 any), or of one id.</summary>
    void OnKill(XObject e, int chanId, int soundId)
    {
        int ch = chanId >= 0 ? chanId : -1;
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var v = _voices[i];
            if (v.Owner != e)
                continue;
            if (ch >= 0 ? v.Channel == ch && (soundId < 0 || v.SoundId == soundId) : (soundId < 0 || v.SoundId == soundId))
                _voices.RemoveAt(i);
        }
    }

    /// <summary>Where a sound is (Quake units): on its object (a moving one carries it), or where it was made.</summary>
    static Vector3 SourceOf(Voice v) => v.Owner switch
    {
        Sprite s => s.Origin,
        Sector sec => sec.Bounds.GetCenter() + (sec.Origin - sec.BaseOrg),
        Wall w => w.Bounds.GetCenter(),
        _ => v.At,
    };

    /// <summary>The game is paused (the console is open): every sound holds where it is.</summary>
    public void SetPaused(bool paused) => _paused = paused;

    /// <summary>Each drawn frame, with the listener (Quake units) and the view's facing: the ears placed (Blood's
    /// earL / earR, the player's sides), the sounds following their objects (Calc3DValues: volumes, Doppler), finished
    /// ones gone, the ambient sounds run.</summary>
    public void Update(Vector3 listener, Basis facing)
    {
        if (_paused)
            return;
        ulong now = Time.GetTicksUsec();
        float dt = _lastUpdate == 0 ? 0 : (float)((now - _lastUpdate) / 1e6);
        _lastUpdate = now;
        _listener = listener;
        // the view's ahead and right, flat (Blood's ears turn with the player's angle only)
        var ahead = Coords.GodotToQuake(-facing.Z);
        ahead.Z = 0;
        if (ahead.LengthSquared() > 1e-6f)
        {
            _forward = ahead.Normalized();
            _right = new Vector3(_forward.Y, -_forward.X, 0);
        }
        _earLWas = _earL;
        _earRWas = _earR;
        _earL = listener - _right * EarOffset;
        _earR = listener + _right * EarOffset;
        if (dt == 0 || dt > 0.5f)
        {
            // (a first frame or a jump: no speed)
            _earLWas = _earL;
            _earRWas = _earR;
        }
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var v = _voices[i];
            if (!v.Playing || (v.Owner is Sprite s && s.Removed))
            {
                _voices.RemoveAt(i);
                continue;
            }
            if (!v.Positional)
                continue;
            var src = SourceOf(v);
            Calc3D(v, src, dt > 0.5f ? 0 : dt, false);
            v.Was = src;
        }
        UpdateAmbient();
    }

    /// <summary>ambientProcess: sound_ambient sprites play their loop while on, louder towards the inner distance (one
    /// voice, the same on both sides: FX_SetPan(voice, vol, vol, vol)).</summary>
    void UpdateAmbient()
    {
        if (_world == null)
            return;
        foreach (var e in _world.Sprites)
        {
            if (e.Type != K.kSoundAmbient)
                continue;
            float vol = 0;
            if (!e.Removed && e.XState != 0 && e.Data1 < e.Data2)
            {
                var d = e.Origin - _listener;
                double nDist = Math.Floor(Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z));
                double vs = Blood.ClipHigh(Blood.MulScale16(e.Data4, e.XBusy), 127);
                double t = Blood.ClipRange(Blood.Scale3(nDist - e.Data1, -vs, e.Data2 - e.Data1) + vs, 0, vs);
                vol = (float)(t / 255);
            }
            _ambient.TryGetValue(e, out var v);
            if (vol > 0 && SfxData.TryGet(e.Data3, out var sfx))
            {
                if (v == null)
                {
                    var s = SampleOf(sfx.File);
                    if (s == null)
                        continue;
                    v = Fixed(s, s.Rate, vol);
                    _ambient[e] = v;
                }
                v.Left.GoalL = v.Left.GoalR = vol;
            }
            else if (v != null)
                _ambient.Remove(e);
        }
    }

    // ---------------------------------------------------------------- the mix

    public override void _Process(double delta)
    {
        if (_playback == null)
            return;
        // about 60 ms queued: enough for a slow frame, little delay; run dry, a little more (at most 150 ms)
        int free = _playback.GetFramesAvailable();
        _capacity = Math.Max(_capacity, free);
        int buffered = _capacity - free;
        if (buffered <= 0 && _voices.Count > 0)
            _queue = Math.Min(_queue * 1.25f, 0.15f);
        int want = Math.Min(free, Math.Max(0, (int)(_mixRate * _queue) - buffered));
        if (want <= 0)
            return;
        if (_buffer.Length < want)
            _buffer = new Vector2[want];
        Array.Clear(_buffer, 0, want);
        if (!_paused)
        {
            float global = Mathf.Clamp(Volume.Float, 0f, 1f) / 32768f;
            bool smooth = Smooth.Bool;
            // MV_VolumeSmoothFactor: 1 - 0.1^(30 / the mix rate) of the way to the goal each sample
            float ease = 1f - Mathf.Pow(0.1f, 30f / _mixRate);
            if (_solo != null)
                MixVoice(_solo, want, global, smooth, ease);
            else
            {
                foreach (var v in _voices)
                    MixVoice(v, want, global, smooth, ease);
                foreach (var v in _ambient.Values)
                    MixVoice(v, want, global, smooth, ease);
            }
        }
        _playback.PushBuffer(want == _buffer.Length ? _buffer : _buffer[..want]);
    }

    void MixVoice(Voice v, int n, float global, bool smooth, float ease)
    {
        if (v.Left != null)
            Mix(v.Left, n, global, smooth, ease);
        if (v.Right != null)
            Mix(v.Right, n, global, smooth, ease);
    }

    /// <summary>MV_MixStereo: n samples of a voice into the buffer, its position a 16.16 step a sample (the frequency by
    /// the mix rate), the sample there held (smooth: towards the next), the pan volumes eased to their goals; a looping
    /// sample goes round, another ends.</summary>
    void Mix(Tap t, int n, float global, bool smooth, float ease)
    {
        if (t.Done)
            return;
        var data = t.S.Data;
        long len = (long)data.Length << 16;
        long step = (long)(t.Freq * 65536.0 / _mixRate);
        if (step <= 0)
            return;
        long pos = t.Pos;
        float l = t.L, r = t.R, gl = t.GoalL, gr = t.GoalR;
        for (int i = 0; i < n; i++)
        {
            if (pos >= len)
            {
                if (!t.S.Loop)
                {
                    t.Done = true;
                    break;
                }
                pos %= len;
            }
            int k = (int)(pos >> 16);
            float x = data[k];
            if (smooth)
            {
                int k2 = k + 1 < data.Length ? k + 1 : t.S.Loop ? 0 : k;
                x += (data[k2] - x) * ((pos & 0xffff) / 65536f);
            }
            x *= global;
            _buffer[i].X += x * l;
            _buffer[i].Y += x * r;
            l += (gl - l) * ease;
            r += (gr - r) * ease;
            pos += step;
        }
        t.Pos = pos;
        t.L = l;
        t.R = r;
    }

    // ---------------------------------------------------------------- sndrec

    /// <summary>sndrec: a sound through the mixer alone (the level's others silent), recorded off its bus: crisp and
    /// smooth heard everywhere, then crisp in the world 256 units to the player's right (the ears: louder and earlier
    /// on the right), into prefix_crisp.wav, prefix_smooth.wav, prefix_right.wav.</summary>
    public async void Record(string file, string prefix)
    {
        var s = SampleOf(file);
        if (s == null)
        {
            GameConsole.Print($"sndrec: no {BloodData.Find("sound/" + file)}");
            return;
        }
        int bus = AudioServer.GetBusIndex("Sfx");
        var rec = new AudioEffectRecord();
        AudioServer.AddBusEffect(bus, rec);
        int at = AudioServer.GetBusEffectCount(bus) - 1;
        string keep = Smooth.Value;
        foreach (var (name, smooth, world) in new[] { ("crisp", "0", false), ("smooth", "1", false), ("right", "0", true) })
        {
            Smooth.Set(smooth);
            Voice v;
            if (world)
            {
                v = new Voice { At = _listener + _right * 256, RelVol = 80, Pitch = s.Rate };
                StartPositional(v, s);
                _voices.Remove(v);
                GameConsole.Print($"sndrec: right: listener {_listener}, ahead {_forward}, right {_right}; left ear {v.Left.GoalL:0.000} at {v.Left.Pos >> 16}, "
                    + $"right ear {v.Right.GoalR:0.000} at {v.Right.Pos >> 16}");
            }
            else
                v = Fixed(s, s.Rate, 1f);
            // (silence first, until what was queued of the level's sounds has played out)
            _solo = Silence;
            await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
            rec.SetRecordingActive(true);
            _solo = v;
            await ToSignal(GetTree().CreateTimer(s.Seconds + 0.2), SceneTreeTimer.SignalName.Timeout);
            _solo = null;
            rec.SetRecordingActive(false);
            string outPath = $"{prefix}_{name}.wav";
            rec.GetRecording()?.SaveToWav(outPath);
            GameConsole.Print($"sndrec: {name} recorded at {_mixRate} Hz into {outPath}");
        }
        Smooth.Set(keep);
        AudioServer.RemoveBusEffect(bus, at);
    }
}
