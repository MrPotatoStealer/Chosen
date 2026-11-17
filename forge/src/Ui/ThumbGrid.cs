// Chosen core: original code (a grid of tile pictures loaded as they come into view, their size a slider's or
// Ctrl+wheel's).
using System;
using System.Collections.Generic;
using Chosen.Forge.Config;
using Chosen.Forge.Data;
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>
/// An ItemList of tile pictures (the tile picker's, the sprite browser's): each item's picture made when it comes into
/// view (a few dozen a frame: thousands at once would hold the window up), found from the items' laid-out rectangles
/// against the scroll position (nothing while the list is still being laid out). The pictures' size: a slider (Slider)
/// or Ctrl+wheel over the list, kept in the config under a key; the selected item stays in view through a change
/// (centred), else the first item in view stays where it was.
/// </summary>
public sealed class ThumbGrid
{
    public const int MinSize = 32, MaxSize = 192;
    public readonly ItemList List;
    public readonly HSlider Slider;
    readonly Func<int, int> _tileOf;
    readonly int _extraWidth;
    readonly HashSet<int> _loaded = new();
    readonly ForgeConfig _config;
    readonly string _key;
    ImageTexture _blank;
    // a size change's item to keep in view (the selected one, centred; else the first in view, where it was) and the
    // frames to wait for the list's new layout
    int _anchor = -1, _anchorWait;
    float _anchorAt;

    /// <summary>tileOf: an item's metadata to its tile (-1: no picture); extraWidth: the column's width past the picture.</summary>
    public ThumbGrid(ItemList list, Func<int, int> tileOf, ForgeConfig config, string key, int size, int extraWidth)
    {
        List = list;
        _tileOf = tileOf;
        _config = config;
        _key = key;
        _extraWidth = extraWidth;
        Slider = new HSlider
        {
            MinValue = MinSize, MaxValue = MaxSize, Step = 8, CustomMinimumSize = new Vector2(110, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "the pictures' size (or Ctrl+wheel over them)",
        };
        Size = Math.Clamp(config?.GetInt(key, size) ?? size, MinSize, MaxSize);
        Slider.SetValueNoSignal(Size);
        Slider.ValueChanged += v => SetSize((int)v);
        list.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, CtrlPressed: true } mb && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                SetSize(Size + (mb.ButtonIndex == MouseButton.WheelUp ? 16 : -16));
                list.AcceptEvent();
            }
        };
        Apply();
    }

    public int Size { get; private set; }

    /// <summary>The empty picture an item shows until its own is made (the list's icon size).</summary>
    public ImageTexture Blank => ForgeData.Ready ? _blank : null;

    public void SetSize(int size)
    {
        size = Math.Clamp(size, MinSize, MaxSize);
        if (size == Size)
            return;
        // (a change while one waits for its layout keeps that one's item)
        if (_anchor < 0 && List.ItemCount > 0 && List.GetItemRect(List.ItemCount - 1).Size.Y > 0)
        {
            float top = (float)List.GetVScrollBar().Value;
            var sel = List.GetSelectedItems();
            if (sel.Length > 0)
            {
                _anchor = sel[0];
                _anchorAt = -1;
            }
            else
            {
                _anchor = FirstReaching(top);
                _anchorAt = List.GetItemRect(_anchor).Position.Y - top;
            }
        }
        _anchorWait = 2;
        Size = size;
        Slider.SetValueNoSignal(size);
        if (_config != null)
        {
            _config.Set(_key, size);
            _config.Save();
        }
        Apply();
        // the pictures again at this size, the one chosen kept in view
        for (int i = 0; i < List.ItemCount; i++)
            List.SetItemIcon(i, Blank);
        _loaded.Clear();
    }

    /// <summary>The first item whose rectangle reaches below y (the rectangles go down the list in order).</summary>
    int FirstReaching(float y)
    {
        int lo = 0, hi = List.ItemCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (List.GetItemRect(mid).End.Y < y)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    void Apply()
    {
        List.FixedIconSize = new Vector2I(Size, Size);
        List.FixedColumnWidth = Size + _extraWidth;
        _blank = ImageTexture.CreateFromImage(Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8));
    }

    /// <summary>The list was filled again: no item has its picture yet.</summary>
    public void Reset()
    {
        _loaded.Clear();
        _anchor = -1;
    }

    /// <summary>Each frame: the pictures of the items in view (and a row or two past), up to a budget.</summary>
    public void Tick()
    {
        if (!GodotObject.IsInstanceValid(List) || !List.IsVisibleInTree() || !ForgeData.Ready)
            return;
        int n = List.ItemCount;
        if (n == 0)
            return;
        // (laid out when drawn: a list just filled has no rectangles yet)
        if (List.GetItemRect(n - 1).Size.Y <= 0)
            return;
        // after a size change, once laid out anew: its item where it should be
        if (_anchor >= 0)
        {
            if (--_anchorWait > 0)
                return;
            if (_anchor < n)
            {
                var rc = List.GetItemRect(_anchor);
                float want = _anchorAt < 0 ? rc.GetCenter().Y - List.Size.Y / 2 : rc.Position.Y - _anchorAt;
                List.GetVScrollBar().Value = Math.Max(0, want);
            }
            _anchor = -1;
            return;
        }
        float top = (float)List.GetVScrollBar().Value, bottom = top + List.Size.Y;
        float margin = Size + 32;
        // the first item reaching into view
        int lo = FirstReaching(top - margin);
        int budget = 48;
        for (int i = lo; i < n && budget > 0; i++)
        {
            if (List.GetItemRect(i).Position.Y > bottom + margin)
                break;
            if (!_loaded.Add(i))
                continue;
            int tile = _tileOf((int)List.GetItemMetadata(i));
            if (tile >= 0 && TileImages.Thumb(tile, Size) is { } tex)
                List.SetItemIcon(i, tex);
            budget--;
        }
    }
}
