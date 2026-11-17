using Chosen.Core;
using Chosen.Data;
using Godot;

namespace Chosen.Client;

/// <summary>
/// Blood's music, looping: each level's, and the title menu's, from blood.ini (Track: a CD track, Song: a MIDI song).
/// CD music (b3d_cdmusic, NBlood's Redbook audio) plays Refreshed Supply's CD tracks (sound/cdtracks/trackNNN.ogg);
/// otherwise, or when the install has no CD tracks (DOS Blood), the MIDI song plays on the chosen device
/// (b3d_mididevice): Refreshed Supply's SC-55 recordings (sound/sc55/NAME.ogg), or an emulated OPL3 FM card with
/// Blood's timbre bank, as NBlood's OPL3 driver (MidiFm; sound/midi/NAME.mid, gmtimbre.tmb). A missing recording falls
/// back to the FM synth. bgmvolume sets the music's volume (DarkPlaces' name).
/// </summary>
public partial class MusicPlayer : Node
{
    static readonly Cvar Volume = Cvars.Register("bgmvolume", "0.7", "music volume (0-1)", saved: true);
    public static readonly Cvar CdMusic = Cvars.Register("b3d_cdmusic", "1",
        "1: CD music (Refreshed Supply's tracks) when the install has it; 0: the MIDI songs", saved: true);
    public static readonly Cvar MidiDevice = Cvars.Register("b3d_mididevice", "0",
        "the MIDI songs on: 0 Refreshed Supply's SC-55 recordings (FM when missing), 1 an emulated OPL3 FM card (as DOS Blood's Sound Blaster)",
        saved: true);

    const int FmRate = 44100;

    AudioStreamPlayer _player;
    string _playing = "";
    int _track;
    float _start;
    string _song = "";
    MidiFm _fm;
    AudioStreamGeneratorPlayback _gen;
    float[] _render = System.Array.Empty<float>();
    Vector2[] _frames = System.Array.Empty<Vector2>();

    public override void _Ready()
    {
        _player = new AudioStreamPlayer();
        AddChild(_player);
        // a changed option replays what is playing on the new source
        CdMusic.Changed += Replay;
        MidiDevice.Changed += Replay;
    }

    void Replay()
    {
        _playing = "";
        Start();
    }

    /// <summary>Plays a CD track from start (seconds), or the MIDI song when CD music is off or the track is missing;
    /// what is already playing goes on. Track 0 and no song: silence.</summary>
    public void Play(int track, float start = 0, string song = "")
    {
        _track = track;
        _start = start;
        _song = song ?? "";
        Start();
    }

    void Start()
    {
        string cd = _track > 0 ? BloodData.Find($"sound/cdtracks/track{_track:D3}.ogg") : null;
        string sc55 = _song != "" ? BloodData.Find($"sound/sc55/{_song}.ogg") : null;
        string mid = _song != "" ? BloodData.Find($"sound/midi/{_song}.mid") : null;
        string key;
        if (cd != null && CdMusic.Bool && System.IO.File.Exists(cd))
            key = "cd:" + cd;
        else if (sc55 != null && MidiDevice.Int == 0 && System.IO.File.Exists(sc55))
            key = "sc55:" + sc55;
        else if (mid != null && System.IO.File.Exists(mid))
            key = "fm:" + mid;
        else if (cd != null && System.IO.File.Exists(cd))
            key = "cd:" + cd;       // no MIDI song for it: the CD track after all
        else
            key = "";
        if (key == _playing && (_player.Playing || key == ""))
            return;
        _playing = key;
        _player.Stop();
        _fm = null;
        _gen = null;
        if (key == "")
            return;
        string path = key[(key.IndexOf(':') + 1)..];
        if (key.StartsWith("fm:"))
        {
            var bank = BloodData.Folder.PathJoin("sound/midi/gmtimbre.tmb");
            if (!System.IO.File.Exists(bank))
            {
                GD.PushWarning($"music: no {bank}");
                return;
            }
            _fm = new MidiFm(FmRate, System.IO.File.ReadAllBytes(bank));
            if (!_fm.Play(System.IO.File.ReadAllBytes(path), true))
            {
                GD.PushWarning($"music: {path} is not a MIDI file");
                _fm = null;
                return;
            }
            _player.Stream = new AudioStreamGenerator { MixRate = FmRate, BufferLength = 0.25f };
            _player.Play();
            _gen = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
            Fill();
            GameConsole.Print($"music: {_song} (OPL3 FM)");
            return;
        }
        var stream = AudioStreamOggVorbis.LoadFromFile(path);
        stream.Loop = true;
        _player.Stream = stream;
        bool isCd = key.StartsWith("cd:");
        _player.Play(isCd ? _start : 0);
        GameConsole.Print(isCd ? $"music: track {_track} ({stream.GetLength():0} s) from {_start:0.#} s"
            : $"music: {_song} (SC-55)");
    }

    public void PlayLevel(string map) => Play(LevelInfo.Track(map), 0, LevelInfo.Song(map));

    public void PlayMenu()
    {
        var (t, s) = LevelInfo.MenuTrack();
        Play(t, s, LevelInfo.MenuSong());
    }

    /// <summary>The FM synth's samples for the room in the generator's buffer.</summary>
    void Fill()
    {
        if (_fm == null || _gen == null)
            return;
        int n = _gen.GetFramesAvailable();
        if (n <= 0)
            return;
        if (_render.Length < n * 2)
        {
            _render = new float[n * 2];
            _frames = new Vector2[n];
        }
        else if (_frames.Length != n)
            _frames = new Vector2[n];
        _fm.Render(_render, n);
        for (int i = 0; i < n; i++)
            _frames[i] = new Vector2(_render[i * 2], _render[i * 2 + 1]);
        _gen.PushBuffer(_frames);
    }

    public override void _Process(double delta)
    {
        float v = Mathf.Clamp(Volume.Float, 0, 1);
        _player.VolumeDb = v <= 0.001f ? -80 : Mathf.LinearToDb(v);
        Fill();
    }
}
