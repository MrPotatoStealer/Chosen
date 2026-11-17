// Chosen Blood module (GPL v2): Blood's sounds for Forge (SOUNDS.RFF's SFX and RAW resources through Chosen.Blood.Files,
// played as NBlood plays them: sfx.cpp's rate from the format, scaled by the sound's pitch).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chosen.Forge.Data;
using Chosen.Shared.Blood.Files;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge.Blood;

/// <summary>
/// The sounds of the Blood folder (SOUNDS.RFF): each id's sample, length and loop, playable in the editor; which
/// sprite fields hold a sound (objtypes.ini's captions: On Snd, Sound, Sector SFX's four); what Blood's own levels use
/// each sound for (BLOOD.RFF's maps, read once in the background: the numbered voice and creature sounds say little by
/// their names).
/// </summary>
public static class SoundLibrary
{
    public sealed class Sound
    {
        public int Id;
        /// <summary>The sample's name (its RAW resource, lower case).</summary>
        public string Name = "";
        public double Seconds;
        public bool Loops;
        public int Rate, RelVol, Pitch, PitchRange;
        internal RffEntry Raw;
        internal int LoopStart;
    }

    static string _folder;
    static Rff _rff;
    static SortedDictionary<int, Sound> _sounds = new();
    static readonly Dictionary<int, AudioStreamWav> _streams = new();

    /// <summary>Every sound by id (empty without a Blood folder or its SOUNDS.RFF).</summary>
    public static IReadOnlyDictionary<int, Sound> All
    {
        get
        {
            Load();
            return _sounds;
        }
    }

    public static Sound Get(int id) => All.TryGetValue(id, out var s) ? s : null;

    /// <summary>A sound in a few words: "doorslid, 1.3 s, loops" (null: no such sound).</summary>
    public static string Describe(int id) => Get(id) is { } s
        ? $"{s.Name}, {s.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s" + (s.Loops ? ", loops" : "")
        : null;

    static void Load()
    {
        if (_folder == ForgeData.Folder)
            return;
        _folder = ForgeData.Folder;
        _sounds = new SortedDictionary<int, Sound>();
        _streams.Clear();
        _rff = null;
        _uses = null;
        _scan = null;
        string path = string.IsNullOrEmpty(_folder) ? null : BloodInstall.FindFile(_folder, "SOUNDS.RFF");
        if (path == null)
            return;
        try
        {
            _rff = new Rff(path);
            foreach (var (id, s) in Assets.Sfxs(_rff))
            {
                if (_rff.Find(s.Raw, "RAW") is not { } e)
                    continue;
                // (NBlood's sfx.cpp: the format's rate times pitch / 65536)
                int rate = Math.Max(1000, (int)((long)Sfx.Rate(s.Format) * (s.Pitch > 0 ? s.Pitch : 65536) / 65536));
                _sounds[id] = new Sound
                {
                    Id = id, Name = s.Raw.ToLowerInvariant(), Rate = rate, Seconds = e.Size / (double)rate, Loops = s.LoopStart >= 0,
                    RelVol = s.RelVol, Pitch = s.Pitch, PitchRange = s.PitchRange, Raw = e, LoopStart = s.LoopStart,
                };
            }
        }
        catch (Exception)
        {
            _sounds = new SortedDictionary<int, Sound>();
            _rff = null;
        }
    }

    /// <summary>A sound as Godot plays it (8-bit unsigned samples made signed; looping from its loop start), or null.</summary>
    public static AudioStreamWav Stream(int id)
    {
        if (_streams.TryGetValue(id, out var st))
            return st;
        if (Get(id) is not { } s || _rff == null)
            return null;
        var pcm = _rff.Read(s.Raw);
        if (pcm == null || pcm.Length == 0)
            return null;
        var data = new byte[pcm.Length];
        for (int i = 0; i < pcm.Length; i++)
            data[i] = (byte)(pcm[i] ^ 0x80);
        st = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format8Bits, MixRate = s.Rate, Stereo = false, Data = data };
        if (s.Loops && s.LoopStart < pcm.Length)
        {
            st.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            st.LoopBegin = s.LoopStart;
            st.LoopEnd = pcm.Length;
        }
        _streams[id] = st;
        return st;
    }

    // ---------------------------------------------------------------- playing

    static AudioStreamPlayer _player;

    /// <summary>The sound playing (-1: none).</summary>
    public static int Playing => _player != null && GodotObject.IsInstanceValid(_player) && _player.Playing ? _playing : -1;
    static int _playing = -1;

    /// <summary>Plays a sound (stopping the one before); false when there is no such sound.</summary>
    public static bool Play(Node anywhere, int id)
    {
        Stop();
        if (Stream(id) is not { } st || anywhere?.GetTree()?.Root is not { } root)
            return false;
        if (_player == null || !GodotObject.IsInstanceValid(_player))
        {
            _player = new AudioStreamPlayer { Name = "ForgeSound" };
            root.AddChild(_player);
        }
        _player.Stream = st;
        // (relvol: 80 is Blood's usual, 255 the loudest)
        _player.VolumeDb = Mathf.LinearToDb(Math.Clamp(Get(id).RelVol / 255f * 2.5f, 0.05f, 1.5f));
        _player.Play();
        _playing = id;
        return true;
    }

    public static void Stop()
    {
        if (_player != null && GodotObject.IsInstanceValid(_player))
            _player.Stop();
        _playing = -1;
    }

    // ---------------------------------------------------------------- which fields hold sounds

    /// <summary>The data fields (1..4) of a sprite type that hold a sound id: those objtypes.ini captions as one
    /// ("On Snd", "Sound", "SFX"), and all four of a Sector SFX (709: off to on, stopping, on to off, stopping).</summary>
    public static int[] Fields(int type)
    {
        if (type == 709)
            return new[] { 1, 2, 3, 4 };
        var f = new List<int>();
        for (int n = 1; n <= 4; n++)
            if (BloodNames.SpriteData(type, n) is { } c && (c.Contains("Snd", StringComparison.OrdinalIgnoreCase)
                || c.Contains("Sound", StringComparison.OrdinalIgnoreCase) || c.Contains("SFX", StringComparison.OrdinalIgnoreCase)))
                f.Add(n);
        return f.ToArray();
    }

    /// <summary>The sound ids a map's sprites hold, with what for ("Sector SFX: Off->On").</summary>
    public static IEnumerable<(int id, string what)> UsesIn(IEnumerable<BSprite> sprites)
    {
        foreach (var sp in sprites)
        {
            if (sp.Xsp is not { } x)
                continue;
            int[] data = { x.Data1, x.Data2, x.Data3, x.Data4 };
            foreach (int n in Fields(sp.Type))
                if (data[n - 1] > 0)
                    yield return (data[n - 1], $"{BloodNames.Sprite(sp.Type)}: {BloodNames.SpriteData(sp.Type, n) ?? $"Data {n}"}");
        }
    }

    // ---------------------------------------------------------------- what Blood's levels use them for

    /// <summary>Per sound: what Blood's levels use it for and how often, and in which levels.</summary>
    public sealed class Use
    {
        public readonly Dictionary<string, int> What = new();
        public readonly SortedSet<string> Levels = new(StringComparer.OrdinalIgnoreCase);

        public override string ToString()
        {
            var top = What.OrderByDescending(p => p.Value).Take(2).Select(p => p.Value > 1 ? $"{p.Key} x{p.Value}" : p.Key);
            string levels = Levels.Count <= 3 ? string.Join(", ", Levels) : $"{string.Join(", ", Levels.Take(3))} +{Levels.Count - 3}";
            return string.Join("; ", top) + (What.Count > 2 ? "; ..." : "") + $" ({levels})";
        }
    }

    static Dictionary<int, Use> _uses;
    static Task _scan;

    /// <summary>The levels' uses once read (null until then: the first call starts reading BLOOD.RFF's maps in the
    /// background).</summary>
    public static IReadOnlyDictionary<int, Use> LevelUses
    {
        get
        {
            Load();
            if (_uses == null && _scan == null && !string.IsNullOrEmpty(_folder))
            {
                string folder = _folder;
                _scan = Task.Run(() =>
                {
                    var uses = Scan(folder);
                    // (the folder still the one read)
                    if (folder == _folder)
                        _uses = uses;
                });
            }
            return _uses;
        }
    }

    static Dictionary<int, Use> Scan(string folder)
    {
        var uses = new Dictionary<int, Use>();
        try
        {
            string path = BloodInstall.FindFile(folder, "BLOOD.RFF");
            if (path == null)
                return uses;
            var rff = new Rff(path);
            foreach (var e in rff.Entries.Where(e => e.Type.Equals("MAP", StringComparison.OrdinalIgnoreCase)))
            {
                BloodMapFile map;
                try
                {
                    map = BloodMapFile.Parse(rff.Read(e));
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (var (id, what) in UsesIn(map.Sprites))
                {
                    if (!uses.TryGetValue(id, out var u))
                        uses[id] = u = new Use();
                    u.What[what] = u.What.GetValueOrDefault(what) + 1;
                    u.Levels.Add(e.Name.ToUpperInvariant());
                }
            }
        }
        catch (Exception)
        {
        }
        return uses;
    }
}
