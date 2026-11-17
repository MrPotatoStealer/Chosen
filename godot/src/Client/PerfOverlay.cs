using System;
using Chosen.Core;
using Godot;

namespace Chosen.Client;

/// <summary>
/// r_perfoverlay (F11 cycles it, Options > Video > Performance): 1 shows the frame rate and where the frame goes,
/// 2 adds a graph of the last frames. Numbers: frames per second, the frame time (average, worst, 1% low over the
/// graph's frames), the game's CPU time (the Blood ticks run this frame, and drawing: movers, portals, sprites,
/// HUD), the main view's render time on the CPU and on the GPU (Godot's measure), draw calls and primitives. The
/// graph: a bar per frame as tall as its frame time, the game's tick time orange and the rest of its CPU time blue
/// at the bottom, the GPU time as a green line; the dashed lines are the screen's refresh time and 33 ms (30 fps).
/// A tall lone bar is a hitch, uneven bars are jitter.
/// </summary>
public partial class PerfOverlay : CanvasLayer
{
    public static readonly Cvar Mode = Cvars.Register("r_perfoverlay", "0", "performance overlay: 0 off, 1 numbers, 2 numbers and graph", saved: true);

    const int History = 300;
    readonly float[] _frame = new float[History], _tick = new float[History], _cpu = new float[History], _gpu = new float[History];
    int _head, _count;
    float _renderCpu, _renderGpu;
    long _draws, _prims;
    bool _measuring;
    Control _panel;

    public override void _Ready()
    {
        Layer = 120;
        _panel = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.Draw += DrawPanel;
        AddChild(_panel);
        Render.HdrOutput.Ui(_panel);
    }

    /// <summary>F11: off, numbers, numbers and graph.</summary>
    public static void Cycle() => Mode.Set(((Mode.Int + 1) % 3).ToString());

    /// <summary>A drawn frame: its time (ms), the Blood ticks' CPU time in it and the game's whole CPU time.</summary>
    public void Record(double frameMs, double tickMs, double cpuMs)
    {
        bool on = Mode.Int > 0;
        Visible = on;
        var rid = GetViewport().GetViewportRid();
        if (on != _measuring)
        {
            RenderingServer.ViewportSetMeasureRenderTime(rid, on);
            _measuring = on;
        }
        if (!on)
        {
            _count = 0;
            return;
        }
        _renderCpu = (float)RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
        _renderGpu = (float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
        _draws = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        _prims = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        _frame[_head] = (float)frameMs;
        _tick[_head] = (float)tickMs;
        _cpu[_head] = (float)cpuMs;
        _gpu[_head] = _renderGpu;
        _head = (_head + 1) % History;
        _count = Math.Min(_count + 1, History);
        _panel.QueueRedraw();
    }

    float At(float[] a, int i) => a[(_head - _count + i + History) % History];      // i: 0 = the oldest kept

    void DrawPanel()
    {
        if (_count == 0)
            return;
        var size = _panel.Size;
        float ui = Mathf.Max(1f, size.Y / 1080f);
        var font = ThemeDB.FallbackFont;
        int fs = (int)(15 * ui);
        float avg = 0, worst = 0;
        var sorted = new float[_count];
        for (int i = 0; i < _count; i++)
        {
            float f = At(_frame, i);
            avg += f;
            worst = Mathf.Max(worst, f);
            sorted[i] = f;
        }
        avg /= _count;
        Array.Sort(sorted);
        float p99 = sorted[Math.Min(_count - 1, (int)(_count * 0.99f))];
        float last = At(_frame, _count - 1), lastTick = At(_tick, _count - 1), lastCpu = At(_cpu, _count - 1);
        float tickAvg = 0, cpuAvg = 0;
        for (int i = 0; i < _count; i++)
        {
            tickAvg += At(_tick, i);
            cpuAvg += At(_cpu, i);
        }
        tickAvg /= _count;
        cpuAvg /= _count;
        string[] lines =
        {
            $"FPS {Engine.GetFramesPerSecond():0}   frame {last:0.0} ms   avg {avg:0.0}   worst {worst:0.0}   1% low {1000 / Mathf.Max(p99, 0.01f):0} fps",
            $"CPU game ticks {lastTick:0.0} ms (avg {tickAvg:0.00})   game total {lastCpu:0.0} ms (avg {cpuAvg:0.00})",
            $"render CPU {_renderCpu:0.0} ms   GPU {_renderGpu:0.0} ms",
            $"draw calls {_draws}   primitives {_prims / 1000.0:0.0}k",
        };
        float pad = 8 * ui, lh = fs * 1.3f, w = 0;
        foreach (var l in lines)
            w = Mathf.Max(w, font.GetStringSize(l, HorizontalAlignment.Left, -1, fs).X);
        float gh = Mode.Int >= 2 ? 120 * ui : 0, gw = Mathf.Max(w, History * 1.5f * ui);
        var box = new Rect2(pad, pad, gw + pad * 2, lines.Length * lh + pad * 2 + (gh > 0 ? gh + pad + lh : 0));
        _panel.DrawRect(box, new Color(0, 0, 0, 0.6f));
        float y = pad * 2 + fs;
        foreach (var l in lines)
        {
            _panel.DrawString(font, new Vector2(pad * 2, y), l, HorizontalAlignment.Left, -1, fs, Colors.White);
            y += lh;
        }
        if (gh <= 0)
            return;
        // the graph: 0 at the bottom, its top over the worst recent frame (at least 2.5 refresh times, at most 100 ms)
        float refresh = (float)DisplayServer.ScreenGetRefreshRate();
        float budget = refresh > 1 ? 1000f / refresh : 16.7f;
        float top = Mathf.Clamp(Mathf.Max(worst * 1.15f, budget * 2.5f), 10f, 100f);
        var g = new Rect2(pad * 2, y - fs + pad, gw, gh);
        _panel.DrawRect(g, new Color(1, 1, 1, 0.05f));
        float bw = g.Size.X / History;
        float Y(float ms) => g.End.Y - Mathf.Min(ms, top) / top * g.Size.Y;
        for (int i = 0; i < _count; i++)
        {
            float x = g.Position.X + (History - _count + i) * bw;
            float f = At(_frame, i), t = At(_tick, i), c = At(_cpu, i);
            var frameCol = f > budget * 1.5f ? new Color(1, 0.2f, 0.2f, 0.9f) : new Color(1, 1, 1, 0.35f);
            _panel.DrawRect(new Rect2(x, Y(f), Mathf.Max(bw - 0.5f, 1), g.End.Y - Y(f)), frameCol);
            _panel.DrawRect(new Rect2(x, Y(c), Mathf.Max(bw - 0.5f, 1), g.End.Y - Y(c)), new Color(0.3f, 0.6f, 1f, 0.9f));
            _panel.DrawRect(new Rect2(x, Y(t), Mathf.Max(bw - 0.5f, 1), g.End.Y - Y(t)), new Color(1f, 0.6f, 0.1f, 0.95f));
            if (i > 0)
            {
                float x0 = x - bw;
                _panel.DrawLine(new Vector2(x0 + bw / 2, Y(At(_gpu, i - 1))), new Vector2(x + bw / 2, Y(At(_gpu, i))), new Color(0.3f, 1f, 0.3f), 1.5f * ui);
            }
        }
        foreach (var (ms, label) in new[] { (budget, $"{budget:0.0} ms ({refresh:0} Hz)"), (33.3f, "33.3 ms (30 fps)") })
        {
            if (ms > top)
                continue;
            _panel.DrawDashedLine(new Vector2(g.Position.X, Y(ms)), new Vector2(g.End.X, Y(ms)), new Color(1, 1, 0, 0.6f), ui, 6 * ui);
            float lw = font.GetStringSize(label, HorizontalAlignment.Left, -1, (int)(11 * ui)).X;
            _panel.DrawString(font, new Vector2(g.End.X - lw - 4 * ui, Y(ms) - 3 * ui), label, HorizontalAlignment.Left, -1, (int)(11 * ui), new Color(1, 1, 0, 0.8f));
        }
        float lx = g.Position.X, ly = g.End.Y + lh * 0.9f;
        foreach (var (col, label) in new[] { (new Color(1, 1, 1, 0.8f), "frame"), (new Color(0.3f, 0.6f, 1f), "game CPU"), (new Color(1f, 0.6f, 0.1f), "ticks"), (new Color(0.3f, 1f, 0.3f), "GPU") })
        {
            _panel.DrawRect(new Rect2(lx, ly - 9 * ui, 9 * ui, 9 * ui), col);
            _panel.DrawString(font, new Vector2(lx + 13 * ui, ly), label, HorizontalAlignment.Left, -1, (int)(12 * ui), Colors.White);
            lx += 13 * ui + font.GetStringSize(label, HorizontalAlignment.Left, -1, (int)(12 * ui)).X + 14 * ui;
        }
    }
}
