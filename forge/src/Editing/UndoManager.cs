// Chosen core: original code.
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Chosen.Forge.Document;

namespace Chosen.Forge.Editing;

/// <summary>
/// Undo and redo by whole-map snapshots (the .MAPX of the map before each change, Deflate-compressed): Build's data is
/// index-based and an insert shifts every index after it, so a snapshot is the safe unit. 100 levels.
/// </summary>
public sealed class UndoManager
{
    public const int Levels = 100;

    sealed record Snap(string Description, byte[] Data);

    readonly List<Snap> _undo = new(), _redo = new();
    readonly MapDocument _doc;
    readonly int _levels;

    public UndoManager(MapDocument doc, int levels = Levels)
    {
        _doc = doc;
        _levels = levels;
    }

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string NextUndo => _undo.Count > 0 ? _undo[^1].Description : null;
    public string NextRedo => _redo.Count > 0 ? _redo[^1].Description : null;

    /// <summary>The newest undo level as a token (null: none): a run of small changes kept as one (the preview's
    /// wheel) adds to the level it made while that is still the newest.</summary>
    public object Top => _undo.Count > 0 ? _undo[^1] : null;

    static byte[] Pack(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true))
            z.Write(raw);
        return ms.ToArray();
    }

    static byte[] Unpack(byte[] packed)
    {
        using var z = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>The map as it is now, kept before a change; returns the level (for WithdrawUndo).</summary>
    public int CreateUndo(string description)
    {
        _undo.Add(new Snap(description, Pack(_doc.Snapshot())));
        if (_undo.Count > _levels)
            _undo.RemoveAt(0);
        _redo.Clear();
        return _undo.Count;
    }

    /// <summary>The last CreateUndo taken back (a change cancelled before it was made, or put back by the caller).</summary>
    public void WithdrawUndo()
    {
        if (_undo.Count > 0)
            _undo.RemoveAt(_undo.Count - 1);
    }

    /// <summary>Puts the map back as before the last change (and drops that undo level): a cancelled drag.</summary>
    public void Revert()
    {
        if (_undo.Count == 0)
            return;
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _doc.Restore(Unpack(s.Data));
    }

    public bool PerformUndo()
    {
        if (_undo.Count == 0)
            return false;
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(new Snap(s.Description, Pack(_doc.Snapshot())));
        _doc.Restore(Unpack(s.Data));
        return true;
    }

    public bool PerformRedo()
    {
        if (_redo.Count == 0)
            return false;
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(new Snap(s.Description, Pack(_doc.Snapshot())));
        _doc.Restore(Unpack(s.Data));
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
