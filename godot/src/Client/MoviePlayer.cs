using System;
using System.Collections.Generic;
using Chosen.Core;
using Godot;

namespace Chosen.Client;

/// <summary>
/// Full-screen videos (NBlood's credPlaySmk): the intro logos and the episode cutscenes. A Smacker video (Smacker) is
/// drawn frame by frame in its palette at its frame rate, its sound played from its WAV (NBlood never plays the audio
/// inside); a Theora video (Refreshed Supply's remastered cutscenes) plays through Godot's VideoStreamPlayer, sound
/// included. Videos up to 320x200 fill the screen at 4:3 like Blood's 320x200 modes; larger ones (GTI.SMK, 320x320)
/// show 1:1 in a 640x480 view, as DOS Blood 1.11 did. Escape skips a video at once, any other key or button after its
/// first second; the queue then goes on. Subtitles (Refreshed Supply's .srt, the same timing for both versions) show
/// at the bottom while their lines are due.
/// </summary>
public partial class MoviePlayer : Control
{
    public readonly struct Movie
    {
        // absolute paths; Sound may be null (a Theora video has its own), Subtitles null (none)
        public readonly string Video, Sound, Subtitles;
        public Movie(string video, string sound = null, string subtitles = null)
        {
            Video = video;
            Sound = sound;
            Subtitles = subtitles;
        }
    }

    readonly List<(double from, double to, string text)> _lines = new();
    Label _subtitle;

    readonly Queue<Movie> _queue = new();
    Action _done;
    Smacker _smk;
    ImageTexture _tex;
    Image _img;
    byte[] _rgba;
    double _time;
    int _shown = -1;
    AudioStreamPlayer _sound;
    VideoStreamPlayer _video;

    public bool Playing => _done != null;

    public override void _Ready()
    {
        Render.HdrOutput.Ui(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        _sound = new AudioStreamPlayer();
        AddChild(_sound);
        _subtitle = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 1,
        };
        _subtitle.AddThemeColorOverride("font_color", Colors.White);
        _subtitle.AddThemeColorOverride("font_outline_color", Colors.Black);
        AddChild(_subtitle);
    }

    /// <summary>An .srt file's lines (number, "hh:mm:ss,mmm --> hh:mm:ss,mmm", text lines, a blank line).</summary>
    void LoadSubtitles(string path)
    {
        _lines.Clear();
        if (path == null || !System.IO.File.Exists(path))
            return;
        static double T(string s)
        {
            var p = s.Trim().Replace(',', '.').Split(':');
            return p.Length == 3 ? int.Parse(p[0]) * 3600 + int.Parse(p[1]) * 60
                + double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
        var blocks = System.IO.File.ReadAllText(path).TrimStart((char)0xFEFF).Replace("\r", "").Split("\n\n");
        foreach (var block in blocks)
        {
            var rows = block.Trim().Split('\n');
            int at = Array.FindIndex(rows, r => r.Contains("-->"));
            if (at < 0)
                continue;
            var span = rows[at].Split("-->");
            _lines.Add((T(span[0]), T(span[1]), string.Join("\n", rows[(at + 1)..])));
        }
    }

    void UpdateSubtitle(double t)
    {
        string text = "";
        foreach (var (from, to, line) in _lines)
            if (t >= from && t < to)
                text = line;
        if (_subtitle.Text != text)
            _subtitle.Text = text;
        var view = GetViewportRect().Size;
        int size = Math.Max(12, (int)(view.Y / 24));
        _subtitle.AddThemeFontSizeOverride("font_size", size);
        _subtitle.AddThemeConstantOverride("outline_size", Math.Max(2, size / 6));
        _subtitle.Position = new Vector2(view.X * 0.1f, view.Y * 0.72f);
        _subtitle.Size = new Vector2(view.X * 0.8f, view.Y * 0.22f);
    }

    /// <summary>Plays the videos one after another (missing or broken ones are passed over), then calls done.</summary>
    public void Play(IEnumerable<Movie> movies, Action done)
    {
        Stop();
        foreach (var m in movies)
            _queue.Enqueue(m);
        _done = done ?? (() => { });
        Visible = true;
        Mouse.Set(Input.MouseModeEnum.Hidden);
        Next();
    }

    void Next()
    {
        StopCurrent();
        while (_queue.Count > 0)
        {
            var m = _queue.Dequeue();
            if (Start(m))
                return;
        }
        Visible = false;
        var done = _done;
        _done = null;
        done?.Invoke();
    }

    bool Start(Movie m)
    {
        _time = 0;
        _shown = -1;
        LoadSubtitles(m.Subtitles);
        _subtitle.Text = "";
        if (!System.IO.File.Exists(m.Video))
        {
            GameConsole.Print($"movie: no {m.Video}");
            return false;
        }
        float volume = Mathf.Clamp(Cvars.Find("volume")?.Float ?? 0.7f, 0f, 1f);
        if (m.Video.EndsWith(".ogv", StringComparison.OrdinalIgnoreCase))
        {
            _video = new VideoStreamPlayer
            {
                Stream = new VideoStreamTheora { File = m.Video },
                Expand = true,
                MouseFilter = MouseFilterEnum.Ignore,
                VolumeDb = Mathf.LinearToDb(Mathf.Max(volume, 0.0001f)),
            };
            AddChild(_video);
            _video.Finished += Next;
            _video.Play();
            QueueRedraw();
            return true;
        }
        try
        {
            _smk = new Smacker(m.Video);
        }
        catch (Exception e)
        {
            GameConsole.Print($"movie: {m.Video}: {e.Message}");
            _smk = null;
            return false;
        }
        _img = Image.CreateEmpty(_smk.Width, _smk.Height, false, Image.Format.Rgba8);
        _rgba = new byte[_smk.Width * _smk.Height * 4];
        _tex = ImageTexture.CreateFromImage(_img);
        if (m.Sound != null && System.IO.File.Exists(m.Sound))
        {
            _sound.Stream = LoadWav(m.Sound);
            _sound.VolumeDb = Mathf.LinearToDb(Mathf.Max(volume, 0.0001f));
            _sound.Play();
        }
        ShowFrame();
        return true;
    }

    /// <summary>A PCM WAV (Blood's: 8-bit unsigned mono, a data chunk of odd length without its pad byte, which Godot's
    /// loader refuses) as a stream; null if it isn't one.</summary>
    static AudioStreamWav LoadWav(string path)
    {
        var b = System.IO.File.ReadAllBytes(path);
        if (b.Length < 12 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF")
            return null;
        int channels = 1, rate = 22050, bitsPer = 8, pos = 12;
        byte[] data = null;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int size = (int)Math.Min(BitConverter.ToUInt32(b, pos + 4), (uint)(b.Length - pos - 8));
            if (id == "fmt ")
            {
                channels = BitConverter.ToUInt16(b, pos + 10);
                rate = (int)BitConverter.ToUInt32(b, pos + 12);
                bitsPer = BitConverter.ToUInt16(b, pos + 22);
            }
            else if (id == "data")
            {
                data = new byte[size];
                Array.Copy(b, pos + 8, data, 0, size);
                if (bitsPer == 8)
                    for (int i = 0; i < data.Length; i++)
                        data[i] = (byte)(data[i] - 128);     // unsigned -> Godot's signed 8 bits
            }
            pos += 8 + size + (size & 1);
        }
        if (data == null)
            return null;
        return new AudioStreamWav
        {
            Data = data,
            Format = bitsPer == 16 ? AudioStreamWav.FormatEnum.Format16Bits : AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = rate,
            Stereo = channels == 2,
        };
    }

    void StopCurrent()
    {
        _sound.Stop();
        _lines.Clear();
        _subtitle.Text = "";
        _smk = null;
        if (_video != null)
        {
            _video.Finished -= Next;
            _video.Stop();
            _video.QueueFree();
            _video = null;
        }
    }

    /// <summary>Stops everything without calling done.</summary>
    public void Stop()
    {
        _queue.Clear();
        StopCurrent();
        _done = null;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Playing)
            return;
        _time += delta;
        if (_video != null)
        {
            LayoutVideo();
            UpdateSubtitle(_video.StreamPosition);
            return;
        }
        UpdateSubtitle(_time);
        if (_smk == null)
            return;
        // the frame due now (frames are decoded in order; a slow frame catches up)
        int due = (int)(_time * _smk.FrameRate);
        if (due >= _smk.FrameCount)
        {
            Next();         // NBlood stops its sound with the last frame
            return;
        }
        if (due > _shown)
            ShowFrame(due);
    }

    void ShowFrame(int upTo = 0)
    {
        while (_shown < upTo && _smk.CurrentFrame < _smk.FrameCount)
        {
            _smk.NextFrame();
            _shown++;
        }
        var pal = _smk.Palette;
        var pic = _smk.Picture;
        for (int i = 0, o = 0; i < pic.Length; i++, o += 4)
        {
            int c = pic[i] * 3;
            _rgba[o] = pal[c];
            _rgba[o + 1] = pal[c + 1];
            _rgba[o + 2] = pal[c + 2];
            _rgba[o + 3] = 255;
        }
        _img.SetData(_smk.Width, _smk.Height, false, Image.Format.Rgba8, _rgba);
        _tex.Update(_img);
        QueueRedraw();
    }

    /// <summary>The picture's place on screen (credPlaySmk's scale): up to 320x200 the 4:3 screen, larger 1:1 in a
    /// 640x480 view; a Theora video keeps its own aspect.</summary>
    Rect2 Fit(Vector2 size, float aspect)
    {
        var view = GetViewportRect().Size;
        float h = view.Y, w = h * aspect;
        if (w > view.X)
        {
            w = view.X;
            h = w / aspect;
        }
        return new Rect2((view - new Vector2(w, h)) / 2, new Vector2(w, h));
    }

    void LayoutVideo()
    {
        var t = _video.GetVideoTexture();
        float aspect = t != null && t.GetHeight() > 0 ? (float)t.GetWidth() / t.GetHeight() : 16f / 9f;
        var r = Fit(GetViewportRect().Size, aspect);
        _video.Position = r.Position;
        _video.Size = r.Size;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, GetViewportRect().Size), Colors.Black);
        if (_smk == null || _tex == null)
            return;
        var screen = Fit(GetViewportRect().Size, 4f / 3f);
        Rect2 r;
        if (_smk.Width <= 320 && _smk.Height <= 200)
            r = screen;
        else
        {
            // 1:1 in a 640x480 view (taller ones in a view as many times 240 rows high as they need)
            float viewH = 480f * ((Math.Max(_smk.Height, 241) + 239) / 240) / 2f;
            float s = screen.Size.Y / viewH;
            var size = new Vector2(_smk.Width, _smk.Height) * s;
            r = new Rect2(screen.Position + (screen.Size - size) / 2, size);
        }
        DrawTextureRect(_tex, r, false);
    }

    public override void _Input(InputEvent e)
    {
        if (!Playing)
            return;
        bool press = e is InputEventKey { Pressed: true, Echo: false } || e is InputEventMouseButton { Pressed: true }
            || e is InputEventJoypadButton { Pressed: true };
        if (!press)
            return;
        GetViewport().SetInputAsHandled();
        bool escape = e is InputEventKey k && k.Keycode == Key.Escape;
        float rate = _smk?.FrameRate ?? 30f;
        if (escape || _time * rate > rate)        // NBlood: Escape, or any input after the first second
            Next();
    }
}
