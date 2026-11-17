// Chosen core: original code (the hooks follow UDB's EditMode, its design, not its code).
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>
/// An editing mode (vertices, walls, sectors, sprites, drawing): it gets the view's input through ClassicMode's mouse
/// handling and these hooks (UDB's names), highlights what is under the cursor, and draws its overlay.
/// </summary>
public abstract class EditMode
{
    public MainWindow Window { get; internal set; }
    protected MapView2D View => Window.View;
    protected MapDocument Doc => Window.Doc;

    /// <summary>Its name in the menus and the status bar ("Vertices").</summary>
    public abstract string Name { get; }
    /// <summary>The keys.cfg action that switches to it.</summary>
    public abstract string Action { get; }

    public virtual void OnEngage() { }
    public virtual void OnDisengage() { }
    public virtual void OnMouseMove(Vector2 map) { }
    public virtual void OnMouseLeave() { }
    public virtual void OnSelectBegin(Vector2 map) { }
    public virtual void OnSelectEnd(Vector2 map) { }
    public virtual void OnEditBegin(Vector2 map) { }
    public virtual void OnEditEnd(Vector2 map) { }
    public virtual void OnDragStart(Vector2 map, bool select) { }
    public virtual void OnDragMove(Vector2 map) { }
    public virtual void OnDragStop(Vector2 map) { }
    public virtual void OnUpdateMultiSelection(Vector2 from, Vector2 to) { }
    public virtual void OnEndMultiSelection(Vector2 from, Vector2 to, bool subtract) { }
    public virtual void OnInsert() { }
    public virtual void OnDelete() { }
    /// <summary>Tab: the next of the candidates under the cursor (sectors sharing a place).</summary>
    public virtual void OnCycle() { }
    public virtual void OnCancel() { }
    /// <summary>The document changed under the mode (undo, a dialog): refresh what it shows.</summary>
    public virtual void OnDocumentChanged() { }
    public virtual void OnRedraw(MapView2D v) { }
    /// <summary>A key the mode handles itself (draw modes' Enter, Backspace); true if it did.</summary>
    public virtual bool OnKey(InputEventKey e) => false;
}
