using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chosen.Core;
using Godot;

namespace Chosen.Render;

/// <summary>
/// HDR output to the monitor (Options > Video > HDR): the window asks Godot for HDR (it falls back to SDR by itself, and
/// follows the screen and the system's settings). The world is drawn with its white at r_hdrwhite nits (paper white: how
/// bright ordinary white is, not the most the screen can do); what is brighter than white (dynamic lights, the fog's glow,
/// fullbright sprites by r_hdrhighlight) goes on up to r_hdrpeak, at most what the screen reports. The UI (status bar,
/// messages, menus, console, movies: registered with Ui) is scaled to r_hdrui nits. Test runs stay in SDR, so that
/// baselines don't depend on the settings.
/// </summary>
public partial class HdrOutput : Node
{
    public static readonly Cvar Mode = Cvars.Register("r_hdr", "1", "1: HDR output to the monitor, where it can show it", saved: true);
    public static readonly Cvar White = Cvars.Register("r_hdrwhite", "200", "HDR: how bright the world's ordinary white is, in nits (paper white)", saved: true);
    public static readonly Cvar Peak = Cvars.Register("r_hdrpeak", "10000", "HDR: the brightest it may go, in nits (at most what the screen reports)", saved: true);
    public static readonly Cvar Highlight = Cvars.Register("r_hdrhighlight", "6", "HDR: fullbright sprites (fire, lamps, explosions) times this over white; 1 at white", saved: true);
    public static readonly Cvar UiLevel = Cvars.Register("r_hdrui", "260", "HDR: the UI's white level in nits", saved: true);

    /// <summary>A test run: no HDR whatever r_hdr says (Game; the hdr command can lift it).</summary>
    public static bool Suspended;

    static readonly StringName LinearName = "hdr_linear_screen", HighlightName = "hdr_highlight";
    static float _highlightSent = -1f;

    /// <summary>Are highlights in use now (HDR on, over 1)?</summary>
    public static bool Highlighting => _highlightSent > 1f;

    /// <summary>A 2D colour multiplying a picture by highlights in light (1 without HDR).</summary>
    public static float HighlightCanvas => Canvas(Mathf.Max(_highlightSent, 1f));

    /// <summary>The 2D colour that multiplies by k in light: 2D colours are sRGB, decoded with the picture when the 2D is
    /// drawn in HDR (1.5 given made 2.5 times as bright; a highlight of 6 made 50).</summary>
    public static float Canvas(float k)
    {
        if (k == 1f)
            return 1f;
        return k <= 0.0031308f ? k * 12.92f : 1.055f * Mathf.Pow(k, 1f / 2.4f) - 0.055f;
    }

    /// <summary>The most this screen can show in nits, as the system reports it (0: not known).</summary>
    public static float ScreenPeak { get; private set; }
    static readonly List<CanvasItem> _ui = new();
    static float _uiScale = 1f;
    static float _linearSent = -1f;
    bool _dirty = true;
    float _poll;

    /// <summary>Is HDR output on now (asked for and the window really showing it)?</summary>
    public static bool Active => DisplayServer.WindowIsHdrOutputEnabled();

    /// <summary>Can the graphics API running show HDR (Godot's Vulkan on Windows can't: RenderDriver)? If not, nothing asks.</summary>
    static bool DriverHasHdr => RenderingServer.GetRenderingDevice()?.HasFeature(RenderingDevice.Features.HdrOutput) == true;

    /// <summary>What the options page shows.</summary>
    public static string Status => !Mode.Bool ? "HDR IS OFF" : !DriverHasHdr ? "HDR NEEDS DIRECT3D 12 (VIDEO > RENDERER)" : !Active ? "HDR NOT AVAILABLE HERE"
        : ScreenPeak > 0 ? $"HDR IS ON, SCREEN PEAK {ScreenPeak:0} NITS" : "HDR IS ON";

    /// <summary>The peak slider's top: the screen's, if it says.</summary>
    public static float PeakLimit
    {
        get
        {
            ReadScreenPeak();
            return ScreenPeak > 0 ? ScreenPeak : 2000f;
        }
    }

    // what the system reports: the window's current peak with no limit of ours on it
    static void ReadScreenPeak()
    {
        if (!DriverHasHdr)
            return;
        float set = DisplayServer.WindowGetHdrOutputMaxLuminance();
        if (set >= 0)
            DisplayServer.WindowSetHdrOutputMaxLuminance(-1f);
        float p = DisplayServer.WindowGetHdrOutputCurrentMaxLuminance();
        if (p > 0)
            ScreenPeak = p;
        if (set >= 0)
            DisplayServer.WindowSetHdrOutputMaxLuminance(set);
    }

    /// <summary>A canvas item drawn at the UI's level (keeps its own alpha).</summary>
    public static void Ui(CanvasItem item)
    {
        _ui.Add(item);
        Apply(item);
    }

    static void Apply(CanvasItem item)
    {
        var m = item.Modulate;
        float k = Canvas(_uiScale);
        item.Modulate = new Color(k, k, k, m.A);
    }

    public override void _Ready()
    {
        Mode.Changed += MarkDirty;
        White.Changed += MarkDirty;
        Peak.Changed += MarkDirty;
        Highlight.Changed += MarkDirty;
        UiLevel.Changed += MarkDirty;
    }

    public override void _ExitTree()
    {
        Mode.Changed -= MarkDirty;
        White.Changed -= MarkDirty;
        Peak.Changed -= MarkDirty;
        Highlight.Changed -= MarkDirty;
        UiLevel.Changed -= MarkDirty;
    }

    void MarkDirty() => _dirty = true;

    public override void _Notification(int what)
    {
        // on or off, the screen or the system's brightness changed
        if (what == NotificationWMOutputMaxLinearValueChanged)
            _dirty = true;
    }

    public override void _Process(double delta)
    {
        // (a window moved to another screen: the notification covers it; looked at now and then all the same)
        _poll -= (float)delta;
        if (!_dirty && _poll > 0)
            return;
        _poll = 1f;
        _dirty = false;
        var win = GetWindow();
        bool want = Mode.Bool && !Suspended && DriverHasHdr;
        if (win.HdrOutputRequested != want)
            win.HdrOutputRequested = want;
        float white = Mathf.Clamp(White.Float, 40f, 2000f);
        if (want)
        {
            DisplayServer.WindowSetHdrOutputReferenceLuminance(white);
            // the peak: at the screen's or above it, whatever the system says (and follows it); else our limit
            ReadScreenPeak();
            float peak = Peak.Float;
            DisplayServer.WindowSetHdrOutputMaxLuminance(ScreenPeak > 0 && peak >= ScreenPeak ? -1f : Mathf.Max(peak, white));
        }
        float highlight = want && Active ? Mathf.Clamp(Highlight.Float, 1f, 12f) : 1f;
        if (highlight != _highlightSent)
        {
            _highlightSent = highlight;
            RenderingServer.GlobalShaderParameterSet(HighlightName, highlight);
        }
        float scale = want && Active ? Mathf.Clamp(UiLevel.Float, 40f, 2000f) / white : 1f;
        if (scale != _uiScale)
        {
            _uiScale = scale;
            _ui.RemoveAll(i => !IsInstanceValid(i));
            foreach (var i in _ui)
                Apply(i);
        }
        // the screen as a canvas shader reads it: linear whenever the root draws its 2D in HDR (asked for, shown or not)
        float linear = win.UseHdr2D || want ? 1f : 0f;
        if (linear != _linearSent)
        {
            _linearSent = linear;
            RenderingServer.GlobalShaderParameterSet(LinearName, linear);
        }
    }

    /// <summary>What the hdr command prints.</summary>
    public string Describe()
    {
        var rd = RenderingServer.GetRenderingDevice();
        var win = GetWindow();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "hdr: r_hdr {0} suspended {1} requested {2} enabled {3} window supports {4} display server {5} rendering device {6} driver {7} use_hdr_2d {8} reference {9:0} nits max {10:0} nits max linear {11:0.00} ui scale {12:0.00}",
            Mode.Value, Suspended, win.HdrOutputRequested, Active, DisplayServer.WindowIsHdrOutputSupported(), DisplayServer.HasFeature(DisplayServer.Feature.HdrOutput),
            rd != null && rd.HasFeature(RenderingDevice.Features.HdrOutput), RenderingServer.GetCurrentRenderingDriverName(), win.UseHdr2D,
            DisplayServer.WindowGetHdrOutputCurrentReferenceLuminance(), DisplayServer.WindowGetHdrOutputCurrentMaxLuminance(), DisplayServer.WindowGetOutputMaxLinearValue(), _uiScale);
    }

    /// <summary>Test: look again now (the hdr command changed something).</summary>
    public void Refresh() => _dirty = true;

    /// <summary>A viewport's picture as an ordinary image: with HDR it comes linear and in floats (a screenshot, a report's:
    /// sRGB, what is brighter than white clipped).</summary>
    public static Image Picture(Viewport viewport)
    {
        var img = viewport.GetTexture().GetImage();
        var f = img.GetFormat();
        if (f != Image.Format.Rgbah && f != Image.Format.Rgbaf && f != Image.Format.Rgbh && f != Image.Format.Rgbf)
            return img;
        img.Convert(Image.Format.Rgbaf);
        int w = img.GetWidth(), h = img.GetHeight();
        var src = MemoryMarshal.Cast<byte, float>(img.GetData());
        var dst = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            for (int c = 0; c < 3; c++)
                dst[i * 4 + c] = ToSrgb(src[i * 4 + c]);
            dst[i * 4 + 3] = 255;
        }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, dst);
    }

    /// <summary>The picture's values as drawn (debugging, the hdr command: 1 is the white level): the share of the view
    /// over white, and the luminance at some percentiles.</summary>
    public static string Stats(Viewport viewport)
    {
        var img = viewport.GetTexture().GetImage();
        string format = img.GetFormat().ToString();
        img.Convert(Image.Format.Rgbaf);
        var src = MemoryMarshal.Cast<byte, float>(img.GetData());
        int n = src.Length / 4;
        var lum = new float[n / 16 + 1];
        int k = 0;
        double sum = 0;
        for (int i = 0; i < n; i += 16)
        {
            float l = 0.2126f * src[i * 4] + 0.7152f * src[i * 4 + 1] + 0.0722f * src[i * 4 + 2];
            lum[k++] = l;
            sum += l;
        }
        Array.Resize(ref lum, k);
        Array.Sort(lum);
        int over1 = 0, over2 = 0;
        foreach (float l in lum)
        {
            if (l > 1)
                over1++;
            if (l > 2)
                over2++;
        }
        float P(double q) => lum[(int)((k - 1) * q)];
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "hdrstats: format {0} mean {1:0.000} p50 {2:0.000} p90 {3:0.000} p99 {4:0.000} max {5:0.00} over white {6:0.0}% over 2x {7:0.0}%",
            format, sum / k, P(0.5), P(0.9), P(0.99), lum[k - 1], 100.0 * over1 / k, 100.0 * over2 / k);
    }

    static byte ToSrgb(float v)
    {
        v = Mathf.Clamp(v, 0f, 1f);
        v = v <= 0.0031308f ? v * 12.92f : 1.055f * Mathf.Pow(v, 1f / 2.4f) - 0.055f;
        return (byte)(v * 255f + 0.5f);
    }
}
