// Chosen core: original code (the window laid out as UDB's MainForm).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Data;
using Chosen.Forge.Document;
using Chosen.Forge.Editing;
using Chosen.Forge.Ui;
using Chosen.Forge.Ui.InfoPanels;
using Chosen.Forge.View;
using Godot;

namespace Chosen.Forge;

/// <summary>
/// Chosen Forge's window, as UDB's: the menus, the main toolbar, the mode buttons on the left, the 2D view, the docker
/// on the right (Statistics), the info panel at the bottom (what is highlighted) and the status bar (status, cursor
/// coordinates in Build units, grid, zoom, the map and its format).
/// </summary>
public partial class MainWindow : Control
{
	public MapDocument Doc { get; private set; }
	public MapView2D View { get; private set; }
	public EditingManager Editing { get; private set; }
	public ForgeConfig Config { get; private set; }
	public UndoManager Undo { get; private set; }
	public KeyMap Keys { get; private set; }

	InfoPanel _info;
	Preview3D _preview;
	Blood.SpriteBrowser _sprites;

	/// <summary>The type new sprites get (the docker's sprite browser).</summary>
	public int SpriteType
	{
		get => _replaySprite ?? Config.GetInt("sprite.type", 1);
		set
		{
			// a replay's settings are its own: the user's aren't touched
			if (Replaying)
				_replaySprite = value;
			else if (value != SpriteType)
			{
				Config.Set("sprite.type", value);
				Config.Save();
			}
		}
	}
	int? _replaySprite;
	bool? _replayEditNew;

	/// <summary>The picture new decorations get (the sprite browser's Decorations: a tile Blood's maps use as a
	/// decoration; -1: none, a decoration's picture is picked when it is placed).</summary>
	public int SpriteTile
	{
		get => _replaySpriteTile ?? Config.GetInt("sprite.tile", -1);
		set
		{
			if (Replaying)
				_replaySpriteTile = value;
			else if (value != SpriteTile)
			{
				Config.Set("sprite.tile", value);
				Config.Save();
			}
		}
	}
	int? _replaySpriteTile;

	/// <summary>A placed sprite's properties open (UDB's "edit new things").</summary>
	public bool EditNewSprite
	{
		get => _replayEditNew ?? Config.GetBool("sprite.editnew", true);
		set
		{
			if (Replaying)
				_replayEditNew = value;
			else if (value != EditNewSprite)
			{
				Config.Set("sprite.editnew", value);
				Config.Save();
			}
		}
	}
	Label _status, _coords, _grid, _zoom, _mapName, _format;
	TabContainer _docker;
	Label _stats;
	readonly Dictionary<EditMode, Button> _modeButtons = new();
	readonly Dictionary<int, string> _menuActions = new();
	readonly Dictionary<string, (PopupMenu menu, int id)> _menuItems = new();
	PopupMenu _recentMenu;
	int _nextId = 1;
	int _overlaps;

	readonly Dictionary<ElementKind, HashSet<int>> _selection = new()
	{
		[ElementKind.Point] = new(), [ElementKind.Line] = new(), [ElementKind.Sector] = new(), [ElementKind.Sprite] = new(),
		[ElementKind.Light] = new(),
	};

	public HashSet<int> SelectionOf(ElementKind k) => _selection[k];

	/// <summary>The selections without the items the map no longer has (after an undo); true when any went.</summary>
	bool PruneSelection()
	{
		int n = 0;
		n += _selection[ElementKind.Point].RemoveWhere(i => i < 0 || i >= Doc.Points.Count);
		n += _selection[ElementKind.Line].RemoveWhere(i => i < 0 || i >= Doc.Lines.Count);
		n += _selection[ElementKind.Sector].RemoveWhere(i => i < 0 || i >= Doc.Sectors.Count);
		n += _selection[ElementKind.Sprite].RemoveWhere(i => i < 0 || i >= Doc.Sprites.Count);
		n += _selection[ElementKind.Light].RemoveWhere(i => i < 0 || i >= Doc.Lights.Count);
		return n > 0;
	}

	public override void _Ready()
	{
		var args = OS.GetCmdlineUserArgs();
		if (args.Length > 0 && (args[0] == "--roundtrip" || args[0] == "--selftest"))
		{
			int code = args[0] == "--roundtrip" ? HeadlessTests.RoundTrip(args.Skip(1).ToArray()) : HeadlessTests.SelfTest(args.Skip(1).ToArray());
			GetTree().Quit(code);
			return;
		}
		GetTree().AutoAcceptQuit = false;
		ForgeInput.TakeFocus();
		Config = ForgeConfig.Load();
		Units.Game = Config.Get("units") != "build";
		ClassicMode.HighlightRange = Config.GetInt("highlightrange", 10);
		ColorSettings.Load(Config);
		Keys = KeyMap.Load(Config);
		BuildUi();
		// every session is recorded (not a screenshot, a replay or a test run of one)
		if (!args.Any(a => a.StartsWith("--shot=") || a.StartsWith("--replay=") || a.StartsWith("--addtest=") || a == "--regress"))
			StartRecording();
		string folder = Config.Get("blood.folder");
		if (folder == "")
		{
			folder = Shared.Blood.Files.BloodInstall.Find() ?? "";
			if (folder != "")
			{
				Config.Set("blood.folder", folder);
				Config.Save();
			}
		}
		if (!ForgeData.Use(folder))
			DisplayStatus("No Blood folder (Edit > Preferences): tiles show as numbers. " + ForgeData.Error);
		else
			DisplayStatus($"Blood folder: {folder} ({ForgeData.Tiles.Count} tiles)");
		_sprites.Refresh();
		SetDocument(MapDocument.New());
		if (RunRecordingOptions(args))
			return;
		if (args.Contains("--uitest"))
		{
			// the modes driven by mouse and key events (UiTest.cs), then quit with its result
			UiTest().ContinueWith(t => Callable.From(() => GetTree().Quit(t.Result)).CallDeferred());
			return;
		}
		var file = args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
		var shot = args.FirstOrDefault(a => a.StartsWith("--shot="))?[7..];
		_shotRun = shot != null;
		if (shot != null)
		{
			// dialogs drawn inside the window, so the picture has them
			GetViewport().GuiEmbedSubwindows = true;
			// a picture of the window with the map open (testing; off the screen with Godot's --position), then quit:
			// --mode=<action> the mode, --do=<action> an action, --hover=x,y the cursor there (zoomed in)
			if (file != null)
				Open(file, false);
			foreach (var a in args.Where(a => a.StartsWith("--mode=")))
				Editing.Switch(a[7..]);
			foreach (var a in args.Where(a => a.StartsWith("--do=")))
				Do(a[5..]);
			// --preview=x,y[,Build angle 0..2047[,pitch degrees]]: the 3D preview from that map point (Build units)
			if (args.FirstOrDefault(a => a.StartsWith("--preview=")) is { } pv)
			{
				var c = pv[10..].Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
				Toggle3D(new Vector2(c[0], c[1]));
				var pose = _preview.CameraPose;
				// (a fifth number: the camera's height, game units up)
				var at = c.Length > 4 ? new Vector3(pose.at.X, c[4], pose.at.Z) : pose.at;
				_preview.SetCamera(at, c.Length > 2 ? Preview3D.YawOf(c[2]) : 0, c.Length > 3 ? Mathf.DegToRad(c[3]) : 0);
				// the same view in the game: its --cam (Quake units: x east, y north, z up; yaw counter-clockwise)
				var p = _preview.CameraPose.at;
				GD.Print(string.Format(System.Globalization.CultureInfo.InvariantCulture, "preview camera: --cam={0:0.##},{1:0.##},{2:0.##},{3:0.##},{4:0.##}",
					p.X, -p.Z, p.Y, c.Length > 2 ? -c[2] * 360 / 2048 : 0, c.Length > 3 ? -c[3] : 0));
			}
			// --select3d=f12,c12,w40u: the preview's selection (a floor, a ceiling, a wall's upper / lower / middle part)
			if (args.FirstOrDefault(a => a.StartsWith("--select3d=")) is { } sel3)
				foreach (var t in sel3[11..].Split(',', StringSplitOptions.RemoveEmptyEntries))
					if (Preview3D.Parse(Doc, t) is { } vt)
						_preview.Selected.Add(vt);
			// --doafter=<action>: after the preview is up (playmovers: Play from there)
			foreach (var a in args.Where(a => a.StartsWith("--doafter=")))
				Do(a[10..]);
			// --tab=<docker tab>, --edit=<point|line|sector|sprite>:<index> (its properties dialog)
			if (args.FirstOrDefault(a => a.StartsWith("--tab=")) is { } tab && _docker.FindChild(tab[6..], false, false) is Control tc)
				_docker.CurrentTab = _docker.GetTabIdxFromControl(tc);
			// (a third part: the dialog's tab, 1 the second)
			if (args.FirstOrDefault(a => a.StartsWith("--edit=")) is { } ed && ed[7..].Split(':') is [var kind, var idx, ..] parts)
				GetTree().CreateTimer(0.2).Timeout += () =>
				{
					// (atmosphere:<n>: an atmosphere's settings)
					if (kind == "atmosphere")
					{
						EditAtmosphere(int.Parse(idx));
						return;
					}
					Blood.EditDialogs.StartTab = parts.Length > 2 && int.TryParse(parts[2], out int tab) ? tab : 0;
					EditProperties(kind switch
					{
						"point" => ElementKind.Point, "line" => ElementKind.Line, "sector" => ElementKind.Sector, _ => ElementKind.Sprite,
					}, new List<int> { int.Parse(idx) });
				};
			// --picktile=<tile>[,<kind>]: the tile picker at that tile (on the kind's tab: wall, floor, sky...)
			if (args.FirstOrDefault(a => a.StartsWith("--picktile=")) is { } pt && pt[11..].Split(',') is var ptk)
				GetTree().CreateTimer(0.2).Timeout += () => TilePickerDialog.Show(this, View, int.Parse(ptk[0]), _ => { }, kind: ptk.Length > 1 ? ptk[1] : null);
			GetTree().CreateTimer(0.4).Timeout += () =>
			{
				if (args.FirstOrDefault(a => a.StartsWith("--hover=")) is { } h)
				{
					var c = h[8..].Split(',');
					var m = new Vector2(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture),
						float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture));
					View.Zoom = 0.15f;
					View.CentreOn(m);
					Editing.Mode?.OnMouseMove(m);
				}
				// --dolater=<action>: after the hover (contextmenu: the menu for what it highlights)
				foreach (var a in args.Where(a => a.StartsWith("--dolater=")))
					Do(a[10..]);
			};
			// --shotdelay=<seconds>: later than 1.2 s (what moves: Play, panning floors)
			double delay = args.FirstOrDefault(a => a.StartsWith("--shotdelay=")) is { } sd
				&& double.TryParse(sd[12..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : 1.2;
			GetTree().CreateTimer(delay).Timeout += () =>
			{
				GetViewport().GetTexture().GetImage().SavePng(shot);
				GetTree().Quit();
			};
			return;
		}
		if (file != null)
			CallDeferred(MethodName.OpenFile, file);
	}

	// ---------------------------------------------------------------- layout
	void BuildUi()
	{
		// one compact theme for the window and every dialog (the root window's theme reaches its child windows)
		GetTree().Root.Theme = ForgeTheme.Make();
		RenderingServer.SetDefaultClearColor(ForgeTheme.Back);
		SetAnchorsPreset(LayoutPreset.FullRect);
		var bg = new Panel();
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);
		var root = new VBoxContainer();
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddThemeConstantOverride("separation", 0);
		AddChild(root);

		Editing = new EditingManager(this);
		Editing.Add(new VerticesMode());
		Editing.Add(new WallsMode());
		Editing.Add(new SectorsMode());
		Editing.Add(new SpritesMode());
		Editing.Add(new LightsMode());
		Editing.Add(new AtmosphereMode());
		Editing.Add(new DrawGeometryMode());
		Editing.Add(new DrawRectangleMode());
		Editing.Add(new DrawCircleMode());
		Editing.Add(new DrawGridMode());
		Editing.Add(new AlignGridMode());
		Editing.Add(_pasteMode);
		root.AddChild(BuildMenus());
		root.AddChild(BuildToolbar());

		// resizable: the view and the docker side by side, the info panel under them (positions kept in forge.cfg)
		var vsplit = new VSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		root.AddChild(vsplit);
		var top = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		top.AddThemeConstantOverride("separation", 0);
		vsplit.AddChild(top);
		top.AddChild(BuildModeBar());
		var hsplit = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		top.AddChild(hsplit);

		View = new MapView2D { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		View.GridSize = Config.GetInt("grid", NewMapGrid);
		View.Snap = Config.GetBool("snap", true);
		View.ShowGrid = Config.GetBool("showgrid", true);
		View.GridOverFill = Config.GetBool("gridoverfill", false);
		// (by default floors filled and sprites as their pictures, until chosen otherwise)
		View.Mode = Enum.TryParse<ViewMode>(Config.Get("viewmode"), out var vm) ? vm : ViewMode.Floors;
		View.SpritePictures = Config.GetBool("spritepictures", true);
		View.AllLinks = Config.GetBool("alllinks", false);
		SyncSelection = Config.GetBool("syncselection", true);
		UpdateViewModeMenu();
		// the lines between linked elements (Blood's channels, links, markers: Blood.MapLinks), made again when the map
		// changes; shown for the highlighted element and the selection
		// (of the map drawn: a tool's result while its dialog is open has walls and sprites of its own)
		View.Links = () =>
		{
			var d = View.Doc ?? Doc;
			if (_links == null || _linksOf != d)
			{
				_links = d == null ? new List<MapLink>() : Blood.MapLinks.All(d);
				_linksOf = d;
			}
			return _links;
		};
		View.LinkFocus = LinkFocus;
		View.DrawOverlay = v =>
		{
			Editing.Mode?.OnRedraw(v);
			DrawToolPreview(v);
		};
		View.SpriteColor = i => ColorSettings.Things.GetValueOrDefault(Blood.BloodNames.Category(View.Doc.Sprites[i]), Colors.White);
		View.SectorLabel = s => View.Zoom > 0.12f && View.Doc.Sectors[s].Type != 0
			? $"{s} {Blood.BloodNames.Sector(View.Doc.Sectors[s].Type)}" : s.ToString();
		View.SectorCaption = s => Blood.SectorCaption.Of(View.Doc, s);
		View.CursorMoved += m =>
		{
			_coords.Text = $"{Units.Xy(m.X)}, {Units.Xy(m.Y)}";
		};
		View.MapInput += e =>
		{
			// the moving sectors shown away from where they are stored: no clicking or dragging the map (Edit)
			if (Posed && e is InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } held)
			{
				if (held.Pressed)
					DisplayStatus(ShownLocked);
				View.Accept();
				return;
			}
			if (e is InputEventKey { Keycode: Key.Space } sp)
			{
				// held: pans; tapped (let go without the view dragged): the context menu
				if (!sp.Echo && View.SetPanKey(sp.Pressed) && !sp.Pressed)
					Do("contextmenu");
				View.Accept();
			}
			else if (e is InputEventKey k && k.Pressed && HandleKey(k))
				View.Accept();
			else if (e is InputEventMouseButton { Pressed: true, CtrlPressed: true, ShiftPressed: true } wb
				&& (wb.ButtonIndex == MouseButton.WheelUp || wb.ButtonIndex == MouseButton.WheelDown))
			{
				// Ctrl+Shift+wheel turns the sprites (UDB's rotate)
				(Editing.Mode as SpritesMode)?.Rotate(wb.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
				View.Accept();
			}
			else
				Editing.Input(e);
		};
		View.MouseExited += () =>
		{
			Rec?.Leave();
			Editing.Mode?.OnMouseLeave();
		};
		View.GuiEvent += e => Rec?.Input(e, "v");
		View.ViewChanged += UpdateStatusFields;
		// the 2D view and the 3D preview share the place (Q switches)
		var stack = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		stack.AddChild(View);
		_preview = new Preview3D { Leave = () => UiDo("visualmode"), OtherKey = PreviewKey };
		_preview.ShowInvisible = Config.GetBool("showinvisible", true);
		_preview.FullBright = View.FullBright = Config.GetBool("fullbright", false);
		_preview.ShowLights = Config.GetBool("showlights3d", true);
		_preview.LightShadows = Config.GetBool("lightshadows", true);
		_preview.ShowAtmosphere = Config.GetBool("showatmosphere3d", true);
		View.ShowSectorEffects = Config.GetBool("showsectoreffects", true);
		View.LinkChains = Config.GetBool("linkchains", true);
		_preview.ShowSectorEffects = Config.GetBool("showsectoreffects3d", true);
		// the preview's editing (UDB's visual mode): the info panel on what the crosshair is on; the wheel's heights as a
		// command (recorded and replayed as any other)
		_preview.TargetChanged = t => _info.ShowElement(t is not { } v ? Element.None
			: v.IsLight ? new Element(ElementKind.Light, v.Light)
			: v.IsSprite ? new Element(ElementKind.Sprite, v.Sprite)
			: v.IsPlane ? new Element(ElementKind.Sector, v.Sector)
			: v.Wall >= 0 && v.Wall < Doc.LineOf.Length ? new Element(ElementKind.Line, Doc.LineOf[v.Wall]) : Element.None);
		_preview.Click = t => UiDo("visualselect " + Preview3D.Code(t));
		_preview.Raise = (planes, dz) => UiDo($"visualz {dz} " + string.Join(" ", planes.Select(Preview3D.Code)));
		// (a light's bulb held and moved, let go: Lights mode)
		_preview.MoveLights = (items, dx, dy, dz) => UiDo($"lightmove {dx} {dy} {dz} {string.Join(",", items)}");
		// slope edges: the edge named by its ends (raising one renumbers its sector's walls)
		_preview.SlopeEdge = (plane, wall, dz, hinge) =>
		{
			var (x1, y1, x2, y2) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, wall);
			UiDo($"visualslope {dz} {Preview3D.Code(plane)} {x1} {y1} {x2} {y2}" + (hinge ? " tilt" : ""));
		};
		_preview.FirstWall = (plane, wall) =>
		{
			var (x1, y1, x2, y2) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, wall);
			UiDo($"visualslope 0 {Preview3D.Code(plane)} {x1} {y1} {x2} {y2} first");
		};
		_preview.Says = s => DisplayStatus(s);
		// (a right click: Texture Settings, its pictures picked from there; on a light its properties)
		_preview.Menu = PreviewMenu;
		stack.AddChild(_preview);
		hsplit.AddChild(stack);

		// the docker: its width is the divider's, whatever its tabs hold (long lines clip, lists scroll)
		_docker = new TabContainer { CustomMinimumSize = new Vector2(200, 0), ClipContents = true };
		_stats = new Label { Name = "Stats", AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Top,
			CustomMinimumSize = new Vector2(1, 0) };
		_docker.AddChild(_stats);
		_sprites = new Blood.SpriteBrowser(this);
		_docker.AddChild(_sprites);
		_layers = new Ui.LayersPanel(this);
		_docker.AddChild(_layers);
		_atmospheres = new Ui.AtmospherePanel(this);
		_docker.AddChild(_atmospheres);
		_docker.AddChild(BuildSpriteFilter());
		_docker.AddChild(BuildErrors());
		hsplit.AddChild(_docker);
		hsplit.SplitOffsets = new[] { Config.GetInt("split.docker", -60) };
		hsplit.Dragged += o => { Config.Set("split.docker", (int)o); Config.Save(); };

		// the info panel: a fixed height (the divider's), whatever is highlighted
		_info = new InfoPanel();
		_info.Visible = Config.GetBool("infopanel", true);
		vsplit.AddChild(_info);
		vsplit.SplitOffsets = new[] { Config.GetInt("split.info", 0) };
		vsplit.Dragged += o => { Config.Set("split.info", (int)o); Config.Save(); };
		root.AddChild(BuildStatusBar());

		Editing.ModeChanged += () =>
		{
			foreach (var (m, b) in _modeButtons)
				b.SetPressedNoSignal(m == Editing.Mode);
			DisplayStatus(Editing.Mode.Name + " mode");
		};
		Editing.Switch(Editing.Modes[0]);
	}

	Control BuildMenus()
	{
		var bar = new MenuBar();
		var file = Menu(bar, "File");
		Item(file, "New Map", "newmap");
		Item(file, "Open Map...", "openmap");
		_recentMenu = new PopupMenu();
		_recentMenu.IdPressed += id => { var r = Config.Recent(); if (id < r.Count) OpenFile(r[(int)id]); };
		file.AddSubmenuNodeItem("Open Recent", _recentMenu);
		file.AboutToPopup += FillRecent;
		file.AddSeparator();
		Item(file, "Save Map", "savemap");
		Item(file, "Save Map As...", "savemapas");
		Item(file, "Save As Extended (.MAPX)...", "savemapx");
		Item(file, "Export Compatibility .MAP...", "exportmap");
		file.AddSeparator();
		Item(file, "Map Options...", "mapoptions");
		file.AddSeparator();
		Item(file, "Exit", "exit");

		var edit = Menu(bar, "Edit");
		Item(edit, "Undo", "undo");
		Item(edit, "Redo", "redo");
		edit.AddSeparator();
		Item(edit, "Cut", "cutselection");
		Item(edit, "Copy", "copyselection");
		Item(edit, "Paste", "pasteselection");
		edit.AddSeparator();
		Item(edit, "Select All", "selectall");
		Item(edit, "Clear Selection", "clearselection");
		edit.AddSeparator();
		Item(edit, "Copy Properties", "copyproperties");
		Item(edit, "Paste Properties", "pasteproperties");
		Item(edit, "Point Sprites at the Cursor", "thinglookatcursor");
		edit.AddSeparator();
		Item(edit, "Preferences...", "preferences");

		var view = Menu(bar, "View");
		Item(view, "Zoom In", "zoomin");
		Item(view, "Zoom Out", "zoomout");
		Item(view, "Zoom to Fit", "zoomtofit");
		Item(view, "Centre on Selection", "centerinscreen");
		view.AddSeparator();
		Item(view, "Toggle Grid", "togglegrid");
		Item(view, "Grid Over Floors / Ceilings (2D)", "gridoverfill");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["gridoverfill"].id), true);
		Item(view, "Smaller Grid", "griddec");
		Item(view, "Larger Grid", "gridinc");
		Item(view, "Snap to Grid", "togglesnap");
		view.AddSeparator();
		Item(view, "Go To Coordinates...", "gotocoords");
		Item(view, "Toggle Info Panel", "toggleinfopanel");
		view.AddSeparator();
		Item(view, "3D Preview", "visualmode");
		view.AddSeparator();
		// UDB's view modes (the 2D view's sectors: lines, brightness, floor or ceiling pictures), sprites as pictures
		foreach (var (title, action, _) in ViewModes)
		{
			Item(view, title, action);
			view.SetItemAsRadioCheckable(view.GetItemIndex(_menuItems[action].id), true);
		}
		Item(view, "Next View Mode", "nextviewmode");
		Item(view, "Previous View Mode", "previousviewmode");
		Item(view, "Sprites as Pictures", "spritepictures");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["spritepictures"].id), true);
		// the lines between linked elements: the highlighted and selected ones' always, every link with this
		Item(view, "Show All Links", "showalllinks");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["showalllinks"].id), true);
		Item(view, "Sync Selection (2D and 3D)", "syncselection");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["syncselection"].id), true);
		// the lighting left out (UDB's Toggle Full Brightness), 2D and 3D
		Item(view, "Full Brightness", "togglebrightness");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["togglebrightness"].id), true);
		// the map's dynamic lights in the 3D preview (MainWindow.Lights.cs), and their shadows
		Item(view, "Dynamic Lights (3D)", "showlights3d");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["showlights3d"].id), true);
		Item(view, "Dynamic Light Shadows (3D)", "lightshadows");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["lightshadows"].id), true);
		// the map's atmospheres in the 3D preview (fog, ground mist, weather: MainWindow.Atmosphere.cs)
		Item(view, "Atmosphere (3D)", "showatmosphere3d");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["showatmosphere3d"].id), true);
		Item(view, "Show Invisible Sprites (3D)", "showinvisible");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["showinvisible"].id), true);
		Item(view, "Follow Link Chains", "linkchains");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["linkchains"].id), true);
		Item(view, "Show Sector Effects (2D)", "showsectoreffects");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["showsectoreffects"].id), true);
		Item(view, "Show Sector Effects (3D)", "showsectoreffects3d");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["showsectoreffects3d"].id), true);
		view.AddSeparator();
		Item(view, "Moving Sectors as Stored (Edit)", "showedit");
		Item(view, "Moving Sectors Off", "showoff");
		Item(view, "Moving Sectors On", "showon");
		foreach (var a in new[] { "showedit", "showoff", "showon" })
			view.SetItemAsRadioCheckable(view.GetItemIndex(_menuItems[a].id), true);
		Item(view, "Play Moving Sectors (3D Preview)", "playmovers");
		view.SetItemAsCheckable(view.GetItemIndex(_menuItems["playmovers"].id), true);
		UpdateViewModeMenu();

		var mode = Menu(bar, "Mode");
		foreach (var m in Editing.Modes.Where(m => m is not PasteMode))
			Item(mode, m.Name + " Mode", m.Action);
		mode.AddSeparator();
		Item(mode, "Set First Wall", "setfirstwall");
		Item(mode, "Join Sectors", "joinsectors");
		Item(mode, "Detach Wall", "dissolveitem");
		Item(mode, "Separate Sectors", "separatesectors");

		var tools = Menu(bar, "Tools");
		Item(tools, "Map Analysis", "showerrors");
		Item(tools, "Gradient...", "gradienttool");
		Item(tools, "Generate Lights (from the Sprites)", "generatelights");
		Item(tools, "Generate Atmosphere (the Game's Guesses)", "generateatmosphere");
		Item(tools, "Test Map", "testmap");
		Item(tools, "Sounds...", "soundbrowser");

		var help = Menu(bar, "Help");
		Item(help, "Report a Problem...", "reportproblem");
		Item(help, "Open the Sessions Folder", "opensessions");
		help.AddSeparator();
		Item(help, "About Chosen Forge", "about");
		return bar;
	}

	Ui.LayersPanel _layers;
	Ui.AtmospherePanel _atmospheres;

	/// <summary>
	/// The layers' commands (Document/Layers.cs; the Layers tab): layershow i 0|1, layercurrent i (shown too),
	/// layernew, layername i name, layerdelete [i], layermove i (the selected sectors into it; in Lights mode the selected lights), layersauto (made again
	/// from the map). Showing and the current layer are the view (not undone); the others are edits (one undo step).
	/// </summary>
	void LayerCommand(string action)
	{
		var a = action.Split(' ', 3);
		int Arg(int i) => a.Length > i && int.TryParse(a[i], out int v) ? v : -1;
		var d = Doc;
		int layer = Arg(1);
		bool known = layer >= 0 && layer < d.Layers.Count;
		switch (a[0])
		{
			case "layershow" when known:
				d.Layers[layer].Visible = Arg(2) != 0;
				LayersChanged(false, $"layer {d.Layers[layer].Name} {(d.Layers[layer].Visible ? "shown" : "hidden")}");
				break;
			case "layercurrent" when known:
				d.CurrentLayer = layer;
				d.Layers[layer].Visible = true;
				LayersChanged(false, $"editing layer {d.Layers[layer].Name}");
				break;
			case "layernew":
				Undo.CreateUndo("New layer");
				d.AddLayer($"Layer {d.Layers.Count}");
				LayersChanged(true, $"new layer {d.Layers[d.CurrentLayer].Name}, editing it");
				break;
			case "layername" when known && a.Length > 2:
				Undo.CreateUndo("Rename layer");
				d.Layers[layer].Name = a[2];
				d.Extras.LayersAuto = false;
				LayersChanged(true, $"layer {layer} named {a[2]}");
				break;
			case "layerdelete":
			{
				int i = known ? layer : d.CurrentLayer;
				string name = d.Layers[i].Name;
				Undo.CreateUndo("Delete layer");
				if (!d.DeleteLayer(i))
				{
					Undo.WithdrawUndo();
					DisplayStatus("delete layer: the last layer stays");
					return;
				}
				LayersChanged(true, $"layer {name} deleted: its sectors are in {d.Layers[0].Name}");
				break;
			}
			case "layermove" when known && Editing.Mode is LightsMode:
			{
				// Lights mode (2D or the 3D preview's): the selected lights (a point light into that layer's room at its
				// place, an attached one with its sprite)
				var lights = (_preview.Visible ? _preview.Selected.Where(t => t.IsLight).Select(t => t.Light) : SelectionOf(ElementKind.Light))
					.Where(i => i >= 0 && i < d.Lights.Count).Distinct().ToList();
				if (lights.Count == 0)
				{
					DisplayStatus("move to layer: select lights first");
					return;
				}
				Undo.CreateUndo($"Move {lights.Count} light{(lights.Count == 1 ? "" : "s")} to a layer");
				var (moved, attached, left) = d.MoveLightsToLayer(lights, layer);
				if (moved + attached == 0)
				{
					Undo.WithdrawUndo();
					DisplayStatus($"move to layer: layer {d.Layers[layer].Name} has no room where the light{(lights.Count == 1 ? " is" : "s are")}");
					return;
				}
				string lwhat = $"{moved + attached} light{(moved + attached == 1 ? "" : "s")}";
				// (the 3D preview's selection of lights now in a layer not edited dropped, as the 2D view's is)
				_preview.Selected.RemoveWhere(t => t.IsLight && (t.Light >= d.Lights.Count || !d.LightEditable(t.Light)));
				LayersChanged(true, $"{lwhat} moved to layer {d.Layers[layer].Name}"
					+ (attached > 0 ? $" ({attached} with {(attached == 1 ? "its sprite" : "their sprites")})" : "")
					+ (left > 0 ? $"; {left} left where {(left == 1 ? "it was" : "they were")}: no room of that layer there" : ""));
				break;
			}
			case "layermove" when known:
			{
				// the selected sectors (every sprite in them with them) and the selected sprites
				var sel = SelectionOf(ElementKind.Sector).ToList();
				var sprites = SelectionOf(ElementKind.Sprite).ToList();
				if (sel.Count == 0 && sprites.Count == 0)
				{
					DisplayStatus("move to layer: select sectors (Sectors mode) or sprites (Sprites mode) first");
					return;
				}
				string what = string.Join(" and ", new[] { (sel.Count, "sector"), (sprites.Count, "sprite") }
					.Where(t => t.Item1 > 0).Select(t => $"{t.Item1} {t.Item2}{(t.Item1 == 1 ? "" : "s")}"));
				Undo.CreateUndo($"Move {what} to a layer");
				d.MoveToLayer(sel, sprites, layer);
				LayersChanged(true, $"{what} moved to layer {d.Layers[layer].Name}");
				break;
			}
			case "layersauto":
				Undo.CreateUndo("Layers from overlaps");
				Blood.AutoLayers.Make(d);
				d.Extras.LayersAuto = false;
				LayersChanged(true, "layers: " + Blood.AutoLayers.Summary(d));
				break;
			default:
				DisplayStatus($"layers: no such command or layer ({action})");
				break;
		}
	}

	/// <summary>After a layer command: the selection cleared (it may lie in a layer no longer edited), the view, the
	/// 3D preview and the Layers tab made again; an edit's undo step closed.</summary>
	void LayersChanged(bool edit, string status)
	{
		ClearSelection();
		if (edit)
		{
			Doc.NotifyChanged();
			UndoChanged();
		}
		else
		{
			View.MapChanged();
			_preview.MapChanged();
		}
		_layers?.Refresh();
		Editing.Mode?.OnMouseMove(View.Cursor);
		View.QueueRedraw();
		DisplayStatus(status);
	}

	/// <summary>UDB's view modes: the menu's title, the action, the mode.</summary>
	static readonly (string title, string action, ViewMode mode)[] ViewModes =
	{
		("View Wireframe", "viewmodenormal", ViewMode.Wireframe), ("View Brightness Levels", "viewmodebrightness", ViewMode.Brightness),
		("View Floor Textures", "viewmodefloors", ViewMode.Floors), ("View Ceiling Textures", "viewmodeceilings", ViewMode.Ceilings),
	};

	/// <summary>The 2D view's mode (UDB's View Wireframe, Brightness Levels, Floor or Ceiling Textures), kept in
	/// forge.cfg.</summary>
	public void SetViewMode(ViewMode m)
	{
		View.Mode = m;
		Config.Set("viewmode", m.ToString());
		UpdateViewModeMenu();
		View.QueueRedraw();
		DisplayStatus(ViewModes[(int)m].title);
	}

	void UpdateViewModeMenu()
	{
		// (the menus are made before the view)
		if (View == null)
			return;
		foreach (var (_, action, mode) in ViewModes)
			if (_menuItems.TryGetValue(action, out var it))
				it.menu.SetItemChecked(it.menu.GetItemIndex(it.id), View.Mode == mode);
		if (_menuItems.TryGetValue("spritepictures", out var sp))
			sp.menu.SetItemChecked(sp.menu.GetItemIndex(sp.id), View.SpritePictures);
		if (_menuItems.TryGetValue("showalllinks", out var al))
			al.menu.SetItemChecked(al.menu.GetItemIndex(al.id), View.AllLinks);
		if (_menuItems.TryGetValue("syncselection", out var ss))
			ss.menu.SetItemChecked(ss.menu.GetItemIndex(ss.id), SyncSelection);
		if (_menuItems.TryGetValue("linkchains", out var lc))
			lc.menu.SetItemChecked(lc.menu.GetItemIndex(lc.id), View.LinkChains);
		if (_menuItems.TryGetValue("gridoverfill", out var gf))
			gf.menu.SetItemChecked(gf.menu.GetItemIndex(gf.id), View.GridOverFill);
		if (_menuItems.TryGetValue("showsectoreffects", out var se))
			se.menu.SetItemChecked(se.menu.GetItemIndex(se.id), View.ShowSectorEffects);
		if (_menuItems.TryGetValue("showsectoreffects3d", out var se3))
			se3.menu.SetItemChecked(se3.menu.GetItemIndex(se3.id), _preview?.ShowSectorEffects ?? true);
		if (_menuItems.TryGetValue("showinvisible", out var si))
			si.menu.SetItemChecked(si.menu.GetItemIndex(si.id), _preview?.ShowInvisible ?? true);
		if (_menuItems.TryGetValue("togglebrightness", out var fb))
			fb.menu.SetItemChecked(fb.menu.GetItemIndex(fb.id), View.FullBright);
		if (_menuItems.TryGetValue("showlights3d", out var sl))
			sl.menu.SetItemChecked(sl.menu.GetItemIndex(sl.id), _preview?.ShowLights ?? true);
		if (_menuItems.TryGetValue("lightshadows", out var ls))
			ls.menu.SetItemChecked(ls.menu.GetItemIndex(ls.id), _preview?.LightShadows ?? true);
		if (_menuItems.TryGetValue("showatmosphere3d", out var sa))
			sa.menu.SetItemChecked(sa.menu.GetItemIndex(sa.id), _preview?.ShowAtmosphere ?? true);
	}

	PopupMenu Menu(MenuBar bar, string title)
	{
		var m = new PopupMenu { Name = title };
		m.IdPressed += id => { if (_menuActions.TryGetValue((int)id, out var a)) UiDo(a); };
		bar.AddChild(m);
		return m;
	}

	void Item(PopupMenu m, string label, string action, bool enabled = true)
	{
		int id = _nextId++;
		m.AddItem(label, id);
		int idx = m.GetItemIndex(id);
		if (Keys.Shortcut(action) is { } sc)
			m.SetItemShortcut(idx, sc, true);
		m.SetItemDisabled(idx, !enabled);
		_menuActions[id] = action;
		_menuItems[action] = (m, id);
	}

	/// <summary>Turns a menu item (by action) on or off: the stages to come enable theirs.</summary>
	public void EnableAction(string action, bool on)
	{
		if (_menuItems.TryGetValue(action, out var it))
			it.menu.SetItemDisabled(it.menu.GetItemIndex(it.id), !on);
	}

	void FillRecent()
	{
		_recentMenu.Clear();
		var r = Config.Recent();
		for (int i = 0; i < r.Count; i++)
			_recentMenu.AddItem(r[i], i);
		if (r.Count == 0)
		{
			_recentMenu.AddItem("(none)", 99);
			_recentMenu.SetItemDisabled(0, true);
		}
	}

	Control BuildToolbar()
	{
		var bar = new HBoxContainer();
		bar.AddThemeConstantOverride("separation", 2);
		void B(string text, string action, string tip)
		{
			var b = new Button { Text = text, TooltipText = tip + (Keys.Text(action) is { Length: > 0 } k ? $" ({k})" : ""), ThemeTypeVariation = "FlatButton", FocusMode = FocusModeEnum.None };
			b.Pressed += () => UiDo(action);
			bar.AddChild(b);
		}
		void Sep() => bar.AddChild(new VSeparator());
		B("New", "newmap", "New map");
		B("Open", "openmap", "Open map");
		B("Save", "savemap", "Save map");
		Sep();
		B("Undo", "undo", "Undo");
		B("Redo", "redo", "Redo");
		Sep();
		B("Cut", "cutselection", "Cut");
		B("Copy", "copyselection", "Copy");
		B("Paste", "pasteselection", "Paste");
		Sep();
		B("Snap", "togglesnap", "Snap to grid");
		B("Grid", "togglegrid", "Show the grid");
		B("Grid -", "griddec", "Smaller grid");
		B("Grid +", "gridinc", "Larger grid");
		{
			// Align Grid: a mode, on while it's the mode (pressed again: back to the mode it came from)
			var align = Editing.Modes.First(m => m is AlignGridMode);
			var ab = new Button { Text = "Align Grid", ToggleMode = true, ThemeTypeVariation = "FlatButton", FocusMode = FocusModeEnum.None,
				TooltipText = "Align the grid: click a vertex (its origin there), a wall (along it) or empty space (back to the map's)"
					+ (Keys.Text(align.Action) is { Length: > 0 } k ? $" ({k})" : "") };
			ab.Toggled += on => UiDo(on ? align.Action : (Editing.LastClassic ?? Editing.Modes[0]).Action);
			bar.AddChild(ab);
			_modeButtons[align] = ab;
		}
		Sep();
		B("Zoom +", "zoomin", "Zoom in");
		B("Zoom -", "zoomout", "Zoom out");
		B("Fit", "zoomtofit", "Zoom to fit the map");
		Sep();
		B("3D", "visualmode", "3D preview: fly through the map");
		Sep();
		// the moving sectors: as stored (edited), at their off or on places, played in the 3D preview
		foreach (var (text, action, tip) in new[]
		{
			("Edit", "showedit", "The map as stored: the moving sectors where the map has them (editing)"),
			("Off", "showoff", "The moving sectors at their off places (doors shut, lifts down, sliders back)"),
			("On", "showon", "The moving sectors at their on places (doors open, lifts up, sliders across)"),
			("Play", "playmovers", "Play the moving sectors in the 3D preview: off, on, off again with their own times"),
		})
		{
			var b = new Button { Text = text, ToggleMode = true, TooltipText = tip + (Keys.Text(action) is { Length: > 0 } k ? $" ({k})" : ""),
				ThemeTypeVariation = "FlatButton", FocusMode = FocusModeEnum.None };
			b.Pressed += () => UiDo(action);
			bar.AddChild(b);
			_shownButtons[action] = b;
		}
		return bar;
	}

	Control BuildModeBar()
	{
		var bar = new VBoxContainer { CustomMinimumSize = new Vector2(96, 0) };
		var group = new ButtonGroup();
		foreach (var m in Editing.Modes.Where(m => m is not PasteMode and not AlignGridMode))
		{
			if (m is DrawGeometryMode && m.GetType() == typeof(DrawGeometryMode))
			{
				var h = new Label { Text = "Drawing", HorizontalAlignment = HorizontalAlignment.Center };
				h.AddThemeColorOverride("font_color", ForgeTheme.Dim);
				h.AddThemeFontSizeOverride("font_size", 12);
				bar.AddChild(new HSeparator());
				bar.AddChild(h);
			}
			var b = new Button
			{
				Text = m.Name, ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None,
				TooltipText = $"{m.Name} mode ({Keys.Text(m.Action)})",
			};
			var mode = m;
			b.Pressed += () => UiDo(mode.Action);
			_modeButtons[m] = b;
			bar.AddChild(b);
		}
		return bar;
	}

	Control BuildStatusBar()
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = ForgeTheme.Back, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2 });
		var bar = new HBoxContainer();
		panel.AddChild(bar);
		bar.AddThemeConstantOverride("separation", 16);
		_status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
		_coords = new Label { CustomMinimumSize = new Vector2(120, 0) };
		_grid = new Label { CustomMinimumSize = new Vector2(80, 0) };
		_zoom = new Label { CustomMinimumSize = new Vector2(80, 0) };
		_mapName = new Label { CustomMinimumSize = new Vector2(120, 0) };
		_format = new Label { CustomMinimumSize = new Vector2(50, 0) };
		foreach (var l in new[] { _status, _coords, _grid, _zoom, _mapName, _format })
			bar.AddChild(l);
		return panel;
	}

	// ---------------------------------------------------------------- the status bar, info panel (UDB's IMainForm)
	public void DisplayStatus(string text)
	{
		Rec?.Status(text);
		if (_status != null)
			_status.Text = text;
	}

	void UpdateStatusFields()
	{
		if (_grid == null)
			return;
		_grid.Text = $"grid {Units.Xy(View.GridSize)}{(View.Snap ? "" : " (no snap)")}";
		_zoom.Text = $"zoom {View.Zoom * 100:0.##}%";
		_mapName.Text = Doc == null ? "" : (Doc.Path == "" ? "(new map)" : Path.GetFileName(Doc.Path)) + (Doc.Dirty ? " *" : "");
		_format.Text = Doc == null ? "" : Doc.Format == MapFormat.Map ? "MAP" : "MAPX";
		GetWindow().Title = $"Chosen Forge {Chosen.Shared.Engine.ChosenVersion.Number} - " + _mapName.Text;
	}

	public void ShowInfo(Element e)
	{
		_info.ShowElement(e);
		if (!e.IsNone && _overlaps > 1)
			DisplayStatus($"{_overlaps} {e.Kind switch { ElementKind.Point => "points", ElementKind.Line => "walls", ElementKind.Sector => "sectors", ElementKind.Light => "lights", _ => "sprites" }} here (Tab: the next)");
	}

	public void SetOverlapCount(int n) => _overlaps = n;

	public void SelectionChanged()
	{
		int n = _selection.Values.Sum(s => s.Count);
		DisplayStatus(n > 0 ? $"{n} selected" : Editing.Mode.Name + " mode");
		View.QueueRedraw();
	}

	public void ClearSelection()
	{
		foreach (var s in _selection.Values)
			s.Clear();
		SelectionChanged();
	}

	/// <summary>The browser's decoration picture, when the type new sprites get has no picture of its own (-1: none).</summary>
	public int DecorationTile() => SpriteTile >= 0 && Blood.BloodNames.Types.Look(SpriteType) is not { Tile: >= 0 } ? SpriteTile : -1;

	/// <summary>A new sprite of the docker's type at (x, y), on the floor of the sector there (the caller took the undo
	/// level); its index.</summary>
	public int NewSprite(int x, int y)
	{
		int i = Doc.AddSprite(x, y, 0);
		Blood.SpriteTypes.Apply(Doc, i, SpriteType);
		// a decoration from the browser: its picture as Blood's maps have it (size; flat on the floor where they lie so)
		if (DecorationTile() is int dt and >= 0)
			Blood.SpriteTypes.Decorate(Doc, i, dt, "floor");
		// centred, its bottom on the floor, as Blood's maps have sprites (it settles there in the game)
		Blood.SpriteExtents.StandOnFloor(Doc, i);
		Doc.NotifyChanged();
		return i;
	}

	/// <summary>The properties dialog of items (one undo step).</summary>
	public void EditProperties(ElementKind kind, List<int> items, bool isNew = false)
	{
		if (kind == ElementKind.Light)
			Blood.LightDialog.Show(this, items, isNew);
		else
			Blood.EditDialogs.Show(this, kind, items, isNew);
	}

	/// <summary>A dialog shown centred, freed once it closes; from: the dialog it is opened from (a window can have
	/// only one exclusive dialog: a picker opened from a properties dialog belongs to that dialog).</summary>
	public void Popup(Window d, Vector2I size = default, Node from = null)
	{
		_openDialogs++;
		bool open = true;
		d.VisibilityChanged += () =>
		{
			if (!d.Visible && open)
			{
				open = false;
				_openDialogs--;
				d.QueueFree();
				Callable.From(ResumePreview).CallDeferred();
			}
		};
		d.TreeExiting += () =>
		{
			if (open)
			{
				open = false;
				_openDialogs--;
			}
		};
		Node parent = from?.GetWindow() is Window owner && owner != GetWindow() ? owner : this;
		parent.AddChild(d);
		if (Replaying)
		{
			// a replay doesn't show it: the session's "dlg" line fills and applies it
			PendingDialog?.QueueFree();
			PendingDialog = d;
			return;
		}
		Rec?.DialogOpened(d);
		LastDialog = d;
		// (from the 3D preview: the mouse it holds let go, for the dialog; taken back when it closes, ResumePreview)
		if (Input.MouseMode == Input.MouseModeEnum.Captured)
			Input.MouseMode = Input.MouseModeEnum.Visible;
		// never bigger than the screen it opens on (its usable part), or the window when dialogs are drawn inside it
		var room = ScreenRoom();
		d.MaxSize = room;
		d.MinSize = new Vector2I(Math.Min(d.MinSize.X, room.X), Math.Min(d.MinSize.Y, room.Y));
		if (size != default)
			size = new Vector2I(Math.Min(size.X, room.X), Math.Min(size.Y, room.Y));
		if (size == default)
			d.PopupCentered();
		else
			d.PopupCentered(size);
	}

	/// <summary>The most room a dialog may take: the usable part of the screen the window is on (less a title bar and a
	/// margin), or the window itself when dialogs are drawn inside it.</summary>
	public Vector2I ScreenRoom()
	{
		// (headless, the tests: no screen, no limit)
		if (DisplayServer.GetName() == "headless")
			return new Vector2I(16384, 16384);
		if (GetViewport().GuiEmbedSubwindows)
			return (Vector2I)GetViewport().GetVisibleRect().Size;
		var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen(GetWindow().GetWindowId())).Size;
		return new Vector2I(Math.Max(320, usable.X - 32), Math.Max(240, usable.Y - 64));
	}

	/// <summary>
	/// A geometry operation as one undo step: op returns null when done, else why it was refused (the map is put back).
	/// The map rules are checked after it: an error more than before is reported in the status bar.
	/// </summary>
	public bool Operation(string what, Func<string> op)
	{
		int before = MapErrors();
		Doc.Warning = null;
		Undo.CreateUndo(what);
		string error;
		try
		{
			error = op();
		}
		catch (Exception e)
		{
			error = "failed: " + e.Message;
		}
		if (error != null)
		{
			Undo.Revert();
			DisplayStatus($"{what}: {error}");
			UndoChanged();
			return false;
		}
		foreach (var s in _selection.Values)
			s.Clear();
		Doc.NotifyChanged();
		UndoChanged();
		int after = MapErrors();
		DisplayStatus(after > before ? $"{what}: done, but the map rules find {after - before} new error(s) (Tools > Map Analysis)"
			: Doc.Warning != null ? $"{what}: done; {Doc.Warning}" : what + ": done");
		return true;
	}

	public int MapErrors() => Shared.Blood.Map.MapValidator.Check(Doc.Map, Shared.Blood.Map.Limits.Extended)
		.Count(i => i.Severity == Shared.Engine.Geometry.Severity.Error);

	/// <summary>The undo levels changed: the Edit menu says what Undo and Redo would do.</summary>
	public void UndoChanged()
	{
		// after every edit: a header start in no sector follows player 1's start (Map Analysis' "start sector" warning)
		if (Doc != null && Doc.RepairStart())
			Doc.Dirty = true;
		// the map's fingerprint after every edit: a replay checks it got the same
		if (Doc != null)
			Rec?.Check(Doc, Undo?.NextUndo);
		// what an undo took away (a paste's sectors, selected) is no longer selected
		if (Doc != null && PruneSelection())
			View?.QueueRedraw();
		if (!_menuItems.TryGetValue("undo", out var u) || Undo == null)
			return;
		u.menu.SetItemText(u.menu.GetItemIndex(u.id), Undo.NextUndo is { } a ? "Undo " + a : "Undo");
		u.menu.SetItemDisabled(u.menu.GetItemIndex(u.id), Undo.UndoCount == 0);
		var r = _menuItems["redo"];
		r.menu.SetItemText(r.menu.GetItemIndex(r.id), Undo.NextRedo is { } b ? "Redo " + b : "Redo");
		r.menu.SetItemDisabled(r.menu.GetItemIndex(r.id), Undo.RedoCount == 0);
		UpdateStatusFields();
	}

	// ---------------------------------------------------------------- the sprite filter (UDB's things filter)
	readonly HashSet<string> _hiddenCategories = new();

	Control BuildSpriteFilter()
	{
		var box = new VBoxContainer { Name = "Filter" };
		box.AddChild(new Label { Text = "Sprites shown:" });
		foreach (var cat in ColorSettings.Things.Keys)
		{
			var c = new CheckBox { Text = cat, ButtonPressed = true };
			c.AddThemeColorOverride("font_color", ColorSettings.Things[cat]);
			string name = cat;
			c.Toggled += on =>
			{
				if (on)
					_hiddenCategories.Remove(name);
				else
					_hiddenCategories.Add(name);
				View.QueueRedraw();
			};
			box.AddChild(c);
		}
		View.SpriteHidden = i => _hiddenCategories.Count > 0 && i < View.Doc.Sprites.Count && _hiddenCategories.Contains(Blood.BloodNames.Category(View.Doc.Sprites[i]));
		return box;
	}

	// ---------------------------------------------------------------- Map Analysis (UDB's error checks)
	ItemList _errors;
	Label _errorsSummary;
	Button _fixOne, _fixAll;
	List<Blood.Finding> _findings = new();

	Control BuildErrors()
	{
		var box = new VBoxContainer { Name = "Errors" };
		var check = new Button { Text = "Check the map" };
		check.Pressed += () => UiDo("showerrors");
		box.AddChild(check);
		// what Forge can mend: the selected finding, or every one it can
		var fixes = new HBoxContainer();
		_fixOne = new Button { Text = "Fix", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = "Fix the selected finding as its tooltip says" };
		_fixOne.Pressed += () =>
		{
			if (_errors.GetSelectedItems() is { Length: > 0 } sel)
				UiDo($"fixfinding {sel[0]}");
		};
		_fixAll = new Button { Text = "Fix All", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = "Repair what Forge can be sure of (sprites' sectors, references, markers nobody owns); one undo step. What a type needs is Fix, one at a time" };
		_fixAll.Pressed += () => UiDo("fixfindings");
		fixes.AddChild(_fixOne);
		fixes.AddChild(_fixAll);
		box.AddChild(fixes);
		_errorsSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_errorsSummary);
		_errors = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, AutoHeight = false, TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming };
		_errors.ItemSelected += i =>
		{
			Rec?.Line("goto", ((int)i).ToString(Inv));
			GoTo(_findings[(int)i]);
			_fixOne.Disabled = _findings[(int)i].Fix == null;
		};
		box.AddChild(_errors);
		return box;
	}

	void RunAnalysis()
	{
		if (Doc.RepairStart())
		{
			Doc.Dirty = true;
			UpdateStatusFields();
		}
		_findings = Blood.MapAnalysis.Run(Doc).OrderByDescending(f => f.Severity).ToList();
		_errors.Clear();
		foreach (var f in _findings)
		{
			int i = _errors.AddItem((f.Fix != null ? "[Fix] " : "") + (f.Severity == Shared.Engine.Geometry.Severity.Error ? "error: " : "") + f.Text);
			_errors.SetItemTooltip(i, f.Text + (f.Fix != null ? $"\nFix: {f.FixText}" : ""));
			_errors.SetItemCustomFgColor(i, f.Severity == Shared.Engine.Geometry.Severity.Error ? new Color(1, 0.4f, 0.4f) : new Color(1, 0.85f, 0.5f));
		}
		int e = _findings.Count(f => f.Severity == Shared.Engine.Geometry.Severity.Error);
		int fixable = _findings.Count(f => f.Fix != null), repairs = _findings.Count(f => f.Repair);
		_errorsSummary.Text = $"{e} error(s), {_findings.Count - e} warning(s){(fixable > 0 ? $", {fixable} with a [Fix] (select one, then Fix; Fix All makes the {repairs} repair(s))" : "")}. Click one to go there.";
		_fixOne.Disabled = true;
		_fixAll.Disabled = repairs == 0;
		_docker.CurrentTab = _docker.GetTabIdxFromControl(_errors.GetParent<Control>());
		DisplayStatus($"Map Analysis: {e} error(s), {_findings.Count - e} warning(s)");
	}

	/// <summary>Map Analysis' Fix (one finding: its number in the list as the map is now) or Fix All (-1), one undo
	/// step; the list made again.</summary>
	void FixFindings(int which)
	{
		if (Doc.RepairStart())
			Doc.Dirty = true;
		var found = Blood.MapAnalysis.Run(Doc).OrderByDescending(f => f.Severity).ToList();
		int fixedCount = 0;
		List<string> refused = new();
		if (which >= 0)
		{
			if (which >= found.Count || found[which].Fix is not { } fix)
			{
				DisplayStatus("fix: that finding has no fix");
				return;
			}
			Operation("Fix: " + found[which].Text, () => fix(Doc));
		}
		else
			Operation("Fix the map's findings", () =>
			{
				(fixedCount, refused) = Blood.MapAnalysis.FixAll(Doc);
				return fixedCount == 0 ? "nothing Forge can repair (what types need: select it and Fix)" : null;
			});
		string said = _status.Text;
		RunAnalysis();
		DisplayStatus(which >= 0 ? said : $"Fix All: {fixedCount} fixed" + (refused.Count > 0 ? $", {refused.Count} refused ({refused[0]})" : "")
			+ $"; {_findings.Count} finding(s) left");
	}

	/// <summary>A finding: the view centred on it, its mode on, the item selected.</summary>
	void GoTo(Blood.Finding f)
	{
		if (f.Kind == ElementKind.None)
			return;
		View.CentreOn(f.At);
		Editing.Switch(f.Kind switch
		{
			ElementKind.Point => "verticesmode", ElementKind.Line => "linedefsmode", ElementKind.Sector => "sectorsmode", _ => "thingsmode",
		});
		foreach (var s in _selection.Values)
			s.Clear();
		if (f.Index >= 0)
			_selection[f.Kind].Add(f.Index);
		SelectionChanged();
	}

	// ---------------------------------------------------------------- Test Map (UDB's F9)
	int _gamePid = -1;
	string _testFile;

	/// <summary>Chosen.exe in a folder beside Forge's own: the one named Chosen, else the most recently written one
	/// holding it (an unpacked release, Chosen-0.1.0-windows-x64); null when there is none.</summary>
	static string SiblingGame(string forgeDir)
	{
		try
		{
			var parent = Directory.GetParent(forgeDir);
			if (parent == null)
				return null;
			var games = parent.GetDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "Chosen.exe"))).ToList();
			var pick = games.FirstOrDefault(d => d.Name.Equals("Chosen", StringComparison.OrdinalIgnoreCase))
				?? games.OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d.FullName, "Chosen.exe"))).FirstOrDefault();
			return pick == null ? null : Path.Combine(pick.FullName, "Chosen.exe");
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>
	/// The map as it is now into a .MAPX in the temporary folder, played in Chosen: Chosen.exe beside Forge when there
	/// is one (a distribution), else the repository's game through Godot (development). A game still running from the
	/// last test just reloads it (it watches the file).
	/// </summary>
	void TestMap()
	{
		string name = Doc.Map.Name is { Length: > 0 } n ? n : "UNTITLED";
		string dir = Path.Combine(Path.GetTempPath(), "ChosenForge");
		Directory.CreateDirectory(dir);
		_testFile = Path.Combine(dir, name + ".mapx");
		try
		{
			File.WriteAllBytes(_testFile + ".tmp", Doc.TestBytes());
			File.Move(_testFile + ".tmp", _testFile, true);
		}
		catch (Exception e)
		{
			Alert("Test Map", "Can't write the test map: " + e.Message);
			return;
		}
		if (_gamePid > 0 && OS.IsProcessRunning(_gamePid))
		{
			DisplayStatus("Test Map: the running game reloads the map");
			return;
		}
		string file = _testFile.Replace('\\', '/');
		string exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
		// Chosen.exe beside Forge, or in a folder beside Forge's (the two downloads unpacked side by side: "Chosen", or
		// the zip's own name, Chosen-0.1.0-windows-x64, as Windows' Extract All names it; the newest of those)
		string chosen = Path.Combine(exeDir, "Chosen.exe");
		if (!File.Exists(chosen))
			chosen = SiblingGame(exeDir) ?? chosen;
		string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
		string godot = Path.Combine(root, "tools", "godot-4.7-mono", "Godot_v4.7.2-stable_mono_win64", "Godot_v4.7.2-stable_mono_win64.exe");
		// a map of a mod's folder: played with that mod (installed in the game: ChosenLoader's Add a Mod Folder)
		var gameArgs = new List<string> { "--", "--file=" + file };
		if (Data.ForgeData.ModFolder != "")
		{
			try
			{
				gameArgs.Add("--addon=" + new Shared.Blood.Files.Addon(Data.ForgeData.ModFolder, true).Id);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
			{
			}
		}
		if (File.Exists(chosen))
			_gamePid = OS.CreateProcess(chosen, gameArgs.ToArray());
		else if (File.Exists(godot) && Directory.Exists(Path.Combine(root, "godot")))
			_gamePid = OS.CreateProcess(godot, new[] { "--path", Path.Combine(root, "godot") }.Concat(gameArgs).ToArray());
		else
		{
			Alert("Test Map", $"Chosen isn't found (no Chosen.exe beside Forge, no repository game). The map is saved as\n{_testFile}");
			return;
		}
		DisplayStatus(_gamePid > 0 ? $"Test Map: playing {name} in Chosen (F9 again sends it the changes)" : "Test Map: the game didn't start");
	}

	// ---------------------------------------------------------------- copy, cut and paste (sectors and sprites)
	Blood.MapClip _clip;
	readonly PasteMode _pasteMode = new();

	/// <summary>Ctrl+C (cut: Ctrl+X, the copy then deleted): the selection's sectors (and the rooms inside them, their
	/// sprites and markers) in Sectors mode, its sprites in Sprites mode, its walls in Walls mode (laid down again as a
	/// drawing: MapPaste.PasteWalls); Ctrl+V places them.</summary>
	void CopySelection(bool cut)
	{
		if (Editing.Mode is not ClassicMode c)
			return;
		var items = c.CopyItems();
		Blood.MapClip clip;
		if (c is WallsMode)
			clip = Blood.MapPaste.CopyWalls(Doc, items);
		else
		{
			var (secs, sprs) = c.CopySet(items);
			clip = Blood.MapPaste.Copy(Doc, secs, sprs, c.CopyLights(items));
		}
		if (clip == null)
		{
			DisplayStatus(items.Count == 0 ? "copy: nothing selected" : "copy: sectors in Sectors mode, sprites in Sprites mode, walls in Walls mode, lights in Lights mode");
			return;
		}
		_clip = clip;
		if (cut)
			c.OnDelete();
		DisplayStatus($"{(cut ? "cut" : "copied")} {clip.What} (Ctrl+V places them)");
	}

	// ---------------------------------------------------------------- copy and paste properties
	(ElementKind kind, int index)? _copied;
	byte[] _copiedSnapshot;

	void CopyProperties()
	{
		if (Editing.Mode is not ClassicMode c)
			return;
		var e = c.Highlighted;
		if (e.IsNone)
		{
			var sel = _selection.FirstOrDefault(kv => kv.Value.Count == 1);
			if (sel.Value == null)
				return;
			e = new Element(sel.Key, sel.Value.First());
		}
		if (e.Kind == ElementKind.Point)
			return;
		_copied = (e.Kind, e.Index);
		_copiedSnapshot = Doc.Snapshot();
		DisplayStatus($"copied the properties of {e.Kind.ToString().ToLowerInvariant()} {e.Index}");
	}

	void PasteProperties()
	{
		if (_copied is not { } src || Editing.Mode is not ClassicMode c)
			return;
		var targets = _selection[src.kind].Count > 0 ? _selection[src.kind].ToList()
			: c.Highlighted.Kind == src.kind && !c.Highlighted.IsNone ? new List<int> { c.Highlighted.Index } : new List<int>();
		if (targets.Count == 0)
			return;
		var (from, _) = Shared.Blood.Map.MapXFile.Read(_copiedSnapshot);
		Undo.CreateUndo("Paste properties");
		foreach (int t in targets)
			Blood.Properties.Paste(Doc, from, src.kind, src.index, t);
		Doc.NotifyChanged();
		UndoChanged();
		DisplayStatus($"pasted onto {targets.Count} item(s)");
	}

	void UpdateStats()
	{
		if (Doc == null)
			return;
		var m = Doc.Map;
		int xs = m.Sectors.Count(s => s.Extra > 0), xw = m.Walls.Count(w => w.Extra > 0), xsp = m.Sprites.Count(s => s.Extra > 0);
		// (a modern map is for NBlood and Chosen: their limits; any other may be for Blood itself)
		int modern = Blood.ModernTypes.Revision(m);
		var lim = modern != 0 ? Shared.Blood.Map.Limits.NBlood : Shared.Blood.Map.Limits.Vanilla;
		_stats.Text =
			$"Sectors: {m.Sectors.Count} ({(modern != 0 ? "NBlood" : "vanilla")} max {lim.Sectors})\n" +
			$"Walls: {m.Walls.Count} ({lim.Walls})\n" +
			$"Sprites: {m.Sprites.Count} ({lim.Sprites})\n" +
			$"X-sectors: {xs} ({lim.XSectors})\nX-walls: {xw} ({lim.XWalls})\nX-sprites: {xsp} ({lim.XSprites})\n\n" +
			$"Points: {Doc.Points.Count}\nLines: {Doc.Lines.Count} ({Doc.Lines.Count(l => l.Back >= 0)} two-sided)\n\n" +
			$"Version: 0x{m.Version:x}{(modern != 0 ? " (modern features)" : "")}\nRevision: {m.MapRev}\n" +
			(ForgeData.ModFolder != "" ? $"Mod folder: {Path.GetFileName(ForgeData.ModFolder)} ({ForgeData.ModTiles.Count} tiles of its own)\n" : "") +
			(Doc.PatchApplied ? "Refreshed Supply's fixes applied\n" : "");
	}

	// ---------------------------------------------------------------- documents
	void SetDocument(MapDocument d)
	{
		if (d.Path == "")
		{
			Rec?.NewMap();
			// a new map starts on a 16 grid (not saved: the grid chosen stays the setting; a replay takes the recorded
			// one), seen from further out (MapDocument.Bounds of an empty map)
			if (!Replaying)
				View.GridSize = NewMapGrid;
		}
		Doc = d;
		Undo = new UndoManager(d);
		_copied = null;
		View.Doc = d;
		_info.Doc = d;
		foreach (var s in _selection.Values)
			s.Clear();
		_preview.Doc = d;
		_preview.MapChanged();
		View.MapChanged();
		_layers?.Refresh();
		_atmospheres?.Refresh();
		_links = null;
		// (the map's own art over Blood's: another map, another list)
		SyncMapArt();
		d.Changed += () =>
		{
			// (the atmospheres alone: the 3D preview's atmosphere made again, its meshes kept; the sectors' tints)
			if (d.AtmosphereOnly && !Posed)
			{
				_preview.AtmosphereChanged();
				View.TintChanged();
				_atmospheres?.Refresh();
				UpdateStatusFields();
				Editing.Mode?.OnDocumentChanged();
				View.QueueRedraw();
				return;
			}
			// (the lights alone: the 3D preview's lights made again, its meshes kept; the moving sectors shown off or
			// on: the shown copy made again as for any change)
			if (d.LightsOnly && !Posed)
			{
				_preview.LightsChanged();
				UpdateStats();
				UpdateStatusFields();
				Editing.Mode?.OnDocumentChanged();
				View.QueueRedraw();
				return;
			}
			_links = null;
			// (an art command or its undo: the list another)
			SyncMapArt();
			// (the moving sectors shown off or on: posed again from the map as it is now)
			if (Posed)
				RefreshShown();
			_preview.MapChanged();
			View.MapChanged();
			_layers?.Refresh();
			_atmospheres?.Refresh();
			UpdateStats();
			UpdateStatusFields();
			Editing.Mode?.OnDocumentChanged();
			View.QueueRedraw();
		};
		UpdateStats();
		UndoChanged();
		View.FitPending = true;
		View.CallDeferred(MapView2D.MethodName.ZoomToFit);
		// (another map: its moving sectors shown as the state shown)
		if (Posed)
			RefreshShown();
		Editing.Mode?.OnDocumentChanged();
		UpdateStatusFields();
		View.QueueRedraw();
	}

	/// <summary>Opens a map file (a .MAP with a .patch beside it: asks whether to take Refreshed Supply's fixes).</summary>
	public void OpenFile(string path)
	{
		ConfirmDiscard(() =>
		{
			string patch = Path.ChangeExtension(path, ".patch");
			if (!Shared.Blood.Map.MapXFile.IsMapX(ReadHead(path)) && File.Exists(patch))
			{
				var c = new ConfirmationDialog
				{
					Title = "Refreshed Supply's fixes",
					DialogText = $"{Path.GetFileName(patch)} beside this map holds Refreshed Supply's fixes (sector heights, voxel models).\nApply them? The heights go into the map; the model fixes are kept in a .MAPX.",
					OkButtonText = "Apply", CancelButtonText = "Keep the map as stored",
				};
				c.Confirmed += () => Open(path, true);
				c.Canceled += () => Open(path, false);
				Popup(c);
			}
			else
				Open(path, false);
		});
	}

	static byte[] ReadHead(string path)
	{
		using var f = File.OpenRead(path);
		var b = new byte[4];
		f.Read(b, 0, 4);
		return b;
	}

	bool _shotRun;

	void Open(string path, bool applyPatch)
	{
		try
		{
			var d = MapDocument.Open(path, _ => applyPatch);
			// a .MAP has no layers: its rooms over rooms get their own (Blood.AutoLayers)
			if (d.Format == MapFormat.Map)
				Blood.AutoLayers.Make(d);
			// the level as it was before any command: its file (and patch) copied into the session
			Rec?.Opened(path, applyPatch);
			SetDocument(d);
			AskGenerateLights();
			// (a picture taken off the screen or a replay leaves the user's recent files and folder as they were)
			if (!_shotRun && !Replaying)
			{
				Config.AddRecent(path);
				Config.Set("lastfolder", Path.GetDirectoryName(path));
				Config.Save();
			}
			DisplayStatus($"Opened {path}: {d.Sectors.Count} sectors, {d.Walls.Count} walls, {d.Sprites.Count} sprites"
				+ (d.Format == MapFormat.MapX && d.Extras is { Migrated: true } ox
					? $" (an older .MAPX, version {ox.FileVersion}: brought up to version {Shared.Blood.Map.MapXFile.FormatVersion}, which saving writes; older Forges won't open it then)"
					: ""));
		}
		catch (Exception e)
		{
			Alert("Can't open the map", $"{path}:\n{e.Message}");
		}
	}

	void SaveTo(string path, MapFormat format, bool confirmed = false, bool ruled = false)
	{
		// (a replay writes no file: its "save" and "saved" lines redo what saving does to the map)
		if (Replaying)
			return;
		Doc.RepairStart();
		// what the map rules find wrong said first (Blood may not load a map with errors: NBlood stops on some)
		if (!ruled && AskAboutFindings(path, format, confirmed))
			return;
		Rec?.Line("save", path, format.ToString());
		// a .MAP holds nothing Chosen-only: what would be lost is said first
		if (format == MapFormat.Map && !confirmed)
		{
			Doc.SaveBytes(MapFormat.Map, false, out var lose);
			if (lose.Count > 0)
			{
				var c = new ConfirmationDialog
				{
					Title = "Saving as a Blood .MAP",
					DialogText = "A .MAP can't hold:\n  " + string.Join("\n  ", lose) + "\n\nSave as .MAP anyway, or as an extended .MAPX?",
					OkButtonText = "Save .MAP", CancelButtonText = "Cancel",
				};
				c.AddButton("Save .MAPX instead", true, "mapx");
				c.Confirmed += () => SaveTo(path, MapFormat.Map, true, true);
				c.CustomAction += a => { c.Hide(); AskSavePath(MapFormat.MapX); };
				Popup(c);
				return;
			}
		}
		try
		{
			Doc.Save(path, format);
			Rec?.Line("saved", format.ToString());
			if (format == MapFormat.Map && Doc.Extras.SpriteExtras.Count > 0)
			{
				// the model fixes as the .patch Chosen reads beside a .MAP
				File.WriteAllLines(Path.ChangeExtension(path, ".patch"), Shared.Blood.Map.MapPatch.SpriteLines(Doc.Extras.SpriteExtras));
			}
			Config.AddRecent(path);
			Config.Save();
			UpdateStatusFields();
			// (saved all the same: but Blood may refuse what the rules find wrong, NBlood stop on it)
			int errors = MapErrors();
			DisplayStatus($"Saved {path} (revision {Doc.Map.MapRev})"
				+ (errors > 0 ? $"; the map rules find {errors} error(s): Blood may not load it (Tools > Map Analysis)" : ""));
		}
		catch (Exception e)
		{
			Alert("Can't save the map", $"{path}:\n{e.Message}");
		}
	}

	/// <summary>Before saving: the map's errors and warnings (Map Analysis) shown with Save anyway, Fix and save (what
	/// Forge can fix), Show them (in the docker) and Cancel; warnings only can be let through for good (forge.cfg's
	/// "savewarnings"). True: asked (the save goes on from the dialog).</summary>
	bool AskAboutFindings(string path, MapFormat format, bool confirmed)
	{
		var found = Blood.MapAnalysis.Run(Doc).OrderByDescending(f => f.Severity).ToList();
		int errors = found.Count(f => f.Severity == Shared.Engine.Geometry.Severity.Error);
		if (found.Count == 0 || (errors == 0 && !Config.GetBool("savewarnings", true)))
			return false;
		int fixable = found.Count(f => f.Repair);
		var c = new ConfirmationDialog { Title = errors > 0 ? "The map has errors" : "The map has warnings", OkButtonText = "Save anyway", CancelButtonText = "Cancel" };
		var box = new VBoxContainer();
		var list = string.Join("\n", found.Take(8).Select(f => (f.Severity == Shared.Engine.Geometry.Severity.Error ? "  error: " : "  ") + f.Text
			+ (f.Fix != null ? "  [Fix]" : ""))) + (found.Count > 8 ? $"\n  ... and {found.Count - 8} more" : "");
		box.AddChild(new Label
		{
			Text = $"Map Analysis finds {errors} error(s) and {found.Count - errors} warning(s)"
				+ (errors > 0 ? " (Blood may not load a map with errors)" : "") + ":\n" + list
				+ (fixable > 0 ? $"\n\nForge can repair {fixable} of them (Fix and save)." : ""),
		});
		var never = new CheckBox { Text = "Don't ask again when there are only warnings", Visible = errors == 0 };
		box.AddChild(never);
		c.AddChild(box);
		void Remember()
		{
			if (never.ButtonPressed)
			{
				Config.Set("savewarnings", false);
				Config.Save();
			}
		}
		if (fixable > 0)
			c.AddButton("Fix and save", true, "fix");
		c.AddButton("Show them", false, "show");
		c.Confirmed += () =>
		{
			Remember();
			SaveTo(path, format, confirmed, true);
		};
		c.CustomAction += a =>
		{
			c.Hide();
			Remember();
			if (a == "fix")
			{
				UiDo("fixfindings");
				SaveTo(path, format, confirmed, true);
			}
			else
				RunAnalysis();
		};
		Popup(c);
		return true;
	}

	void AskSavePath(MapFormat format)
	{
		var dlg = MakeFileDialog(FileDialog.FileModeEnum.SaveFile, format == MapFormat.Map ? "*.map ; Blood map" : "*.mapx ; Chosen extended map");
		string baseName = Doc.Path != "" ? Path.GetFileNameWithoutExtension(Doc.Path) : "untitled";
		dlg.CurrentFile = baseName + (format == MapFormat.Map ? ".map" : ".mapx");
		dlg.FileSelected += p =>
		{
			if (Path.GetExtension(p) == "")
				p += format == MapFormat.Map ? ".map" : ".mapx";
			SaveTo(p, format);
		};
		dlg.PopupCentered(new Vector2I(900, 600));
	}

	FileDialog MakeFileDialog(FileDialog.FileModeEnum mode, string filter)
	{
		var d = new FileDialog
		{
			FileMode = mode, Access = Godot.FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
			Filters = new[] { filter },
		};
		string last = Config.Get("lastfolder");
		if (last != "" && Directory.Exists(last))
			d.CurrentDir = last;
		AddChild(d);
		return d;
	}

	/// <summary>Runs then, after asking when the map has unsaved changes.</summary>
	void ConfirmDiscard(Action then)
	{
		if (Doc == null || !Doc.Dirty)
		{
			then();
			return;
		}
		var c = new ConfirmationDialog { Title = "Unsaved changes", DialogText = "The map has unsaved changes. Discard them?", OkButtonText = "Discard" };
		c.Confirmed += then;
		Popup(c);
	}

	public void Alert(string title, string text)
	{
		var a = new AcceptDialog { Title = title, DialogText = text };
		Popup(a);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
			Do("exit");
		// with focus: a frame every refresh (vsync), so a variable-refresh monitor (G-Sync, FreeSync) in a window keeps
		// one rate instead of following the editor's bursts of frames (stutter while panning or dragging); without
		// focus: frames only when something changes
		else if (what == NotificationApplicationFocusIn)
			OS.LowProcessorUsageMode = false;
		else if (what == NotificationApplicationFocusOut)
		{
			OS.LowProcessorUsageMode = true;
			// Space released in another window never reaches this one
			View?.SetPanKey(false);
		}
	}

	// ---------------------------------------------------------------- actions
	public override void _UnhandledKeyInput(InputEvent e)
	{
		if (e is InputEventKey)
			Rec?.Input(e, "u");
		UnhandledKey(e);
	}

	/// <summary>A key no control took: the pan key, else the actions (recorded and replayed through here).</summary>
	void UnhandledKey(InputEvent e)
	{
		if (e is InputEventKey { Keycode: Key.Space } sp)
		{
			// the pan key: held over the view (a release anywhere ends it)
			if (!sp.Echo && (!sp.Pressed || View.GetGlobalRect().HasPoint(View.GetGlobalMousePosition())))
			{
				if (View.SetPanKey(sp.Pressed) && !sp.Pressed)
					Do("contextmenu");
				Handled();
			}
			return;
		}
		if (e is InputEventKey { Keycode: Key.Print } pk && LonePrint(pk) is { } press)
			e = press;
		if (e is InputEventKey k && k.Pressed && !k.Echo && HandleKey(k))
			Handled();
	}

	/// <summary>PrtScr held down (its press seen).</summary>
	bool _printDown;

	/// <summary>Windows gives PrtScr's press to the system and sends only its release: a release with no press
	/// before it stands for the press (the copy returned), else null.</summary>
	InputEventKey LonePrint(InputEventKey k)
	{
		if (k.Echo)
			return null;
		if (k.Pressed)
		{
			_printDown = true;
			return null;
		}
		bool lone = !_printDown;
		_printDown = false;
		if (!lone)
			return null;
		var press = (InputEventKey)k.Duplicate();
		press.Pressed = true;
		return press;
	}

	/// <summary>A key press: the mode's own keys first, then keys.cfg's actions.</summary>
	bool HandleKey(InputEventKey k)
	{
		if (Editing?.Mode?.OnKey(k) == true)
			return true;
		if (Keys.Match(k) is { } a)
		{
			Do(a);
			return true;
		}
		switch (k.Keycode)
		{
			case Key.Left: View.Pan(new Vector2(-64, 0)); return true;
			case Key.Right: View.Pan(new Vector2(64, 0)); return true;
			case Key.Up: View.Pan(new Vector2(0, -64)); return true;
			case Key.Down: View.Pan(new Vector2(0, 64)); return true;
		}
		return false;
	}

	public void Do(string action)
	{
		// a replay never saves, opens files or starts the game, by a key (Ctrl+S) no more than by a "do" line: it
		// would write the session's own copy of the map (or a level of the data folder) with what it replays
		if (Replaying && NotReplayed.Contains(action))
			return;
		// the moving sectors shown away from where they are stored: the map's shape isn't edited (Edit does)
		if (Posed && (ShapeActions.Contains(action) || action.StartsWith("splitlines ", StringComparison.Ordinal)
			|| action.StartsWith("curvelines ", StringComparison.Ordinal) || action.StartsWith("snapgrid ", StringComparison.Ordinal)
			|| action == "snaptogrid"))
		{
			DisplayStatus(ShownLocked);
			return;
		}
		// a click in the 3D preview: "visualselect f12" (w40u, c3...: a target toggled), the 2D view's selection synced
		if (action.StartsWith("visualselect ", StringComparison.Ordinal))
		{
			if (Preview3D.Parse(Doc, action[13..]) is { } vt)
			{
				_preview.ToggleSelected(vt);
				if (SyncSelection)
					SyncTo2D();
			}
			return;
		}
		// the tools' commands (Tools/BuiltinTools.cs)
		if (action.StartsWith("splitlines ", StringComparison.Ordinal))
		{
			SplitLines(action);
			return;
		}
		if (action.StartsWith("curvelines ", StringComparison.Ordinal))
		{
			CurveLines(action);
			return;
		}
		if (action.StartsWith("snapgrid ", StringComparison.Ordinal))
		{
			SnapGrid(action);
			return;
		}
		// the 3D preview's texture keys and menu items (MainWindow.Textures.cs): "visualtex <op> ..." commands
		if (action.StartsWith("visualtex ", StringComparison.Ordinal))
		{
			VisualTex(action);
			return;
		}
		if (action.StartsWith("visualmove ", StringComparison.Ordinal))
		{
			VisualMove(action);
			return;
		}
		if (action.StartsWith("visualplace ", StringComparison.Ordinal))
		{
			VisualPlace(action);
			return;
		}
		if (action.StartsWith("visualdelete ", StringComparison.Ordinal))
		{
			VisualDelete(action);
			return;
		}
		// Play: the player entering a sector (its enter trigger; the camera's own moves do this in the 3D preview)
		if (action.StartsWith("playenter ", StringComparison.Ordinal))
		{
			if (_play != null && int.TryParse(action[10..], out int into) && into >= 0 && into < Doc.Sectors.Count)
			{
				_play.Sim.PlayerMoved(_playSector, into);
				_playSector = into;
				DisplayStatus($"Play: sector {into} entered");
			}
			return;
		}
		// the grid's frame (Align Grid's clicks): "aligngrid point 5", "aligngrid line 12", "aligngrid reset"
		if (action.StartsWith("aligngrid ", StringComparison.Ordinal))
		{
			var p = action.Split(' ');
			DisplayStatus(AlignGridMode.Align(View, Doc, p[1], p.Length > 2 && int.TryParse(p[2], out int gi) ? gi : -1));
			return;
		}
		// Map Analysis' fixes: one finding (its number in the list as the map is now), or all Forge can fix
		if (action == "fixfindings" || action.StartsWith("fixfinding ", StringComparison.Ordinal))
		{
			FixFindings(action == "fixfindings" ? -1 : int.Parse(action[11..], Inv));
			return;
		}
		// the copy placed: moved by (dx, dy), one undo step, what was pasted selected
		if (action.StartsWith("pastehere ", StringComparison.Ordinal))
		{
			var p = action.Split(' ');
			if (_clip is { Lines.Count: > 0 } && p.Length == 3 && int.TryParse(p[1], out int wdx) && int.TryParse(p[2], out int wdy))
			{
				// walls: laid down as a drawing, the parts outside the sectors cut off
				List<int> walls = null;
				int made = 0;
				string note = null;
				if (Operation($"Paste {_clip.What}", () =>
					{
						(walls, made, note) = Blood.MapPaste.PasteWalls(Doc, _clip, wdx, wdy);
						return walls.Count == 0 && made == 0 ? note ?? "the walls lie outside the sectors or along their walls: nothing to draw" : null;
					}))
				{
					foreach (int l in walls.Select(w => w < Doc.LineOf.Length ? Doc.LineOf[w] : -1).Where(l => l >= 0).Distinct())
						SelectionOf(ElementKind.Line).Add(l);
					SelectionChanged();
					if (!_status.Text.Contains("new error", StringComparison.Ordinal))
						DisplayStatus($"pasted {_clip.What}: {made} sector(s) split or made" + (note != null ? $" ({note})" : ""));
				}
			}
			else if (_clip != null && p.Length == 3 && int.TryParse(p[1], out int pdx) && int.TryParse(p[2], out int pdy))
			{
				List<int> secs = null, sprs = null, lts = null;
				if (Operation($"Paste {_clip.What}", () =>
					{
						var (a, b, why) = Blood.MapPaste.Paste(Doc, _clip, pdx, pdy, out var pl);
						(secs, sprs, lts) = (a, b, pl);
						return why;
					}))
				{
					// (what was pasted selected: the sectors, else the sprites, else the lights; in Lights mode its lights)
					var kind = lts.Count > 0 && (Editing.Mode is LightsMode || secs.Count + sprs.Count == 0) ? ElementKind.Light
						: secs.Count > 0 ? ElementKind.Sector : ElementKind.Sprite;
					foreach (int i in kind == ElementKind.Sector ? secs : kind == ElementKind.Sprite ? sprs : lts)
						SelectionOf(kind).Add(i);
					SelectionChanged();
					if (_preview.Visible && SyncSelection)
						SyncTo3D();
					// (the operation's word kept when the rules found something wrong)
					if (!_status.Text.Contains("new error", StringComparison.Ordinal))
						DisplayStatus($"pasted {_clip.What}");
				}
			}
			return;
		}
		// a selection set (tests, scripted runs): "select sector 3,4", "select line 12", "select point 5", "select sprite 7"
		if (action.StartsWith("select ", StringComparison.Ordinal))
		{
			var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var kind = p.Length > 1 ? p[1] switch
			{
				"sector" => ElementKind.Sector, "line" => ElementKind.Line, "point" => ElementKind.Point, "sprite" => ElementKind.Sprite,
				"light" => ElementKind.Light, _ => ElementKind.None,
			} : ElementKind.None;
			if (kind != ElementKind.None)
			{
				var sel = SelectionOf(kind);
				sel.Clear();
				if (p.Length > 2)
					foreach (var t in p[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
						if (int.TryParse(t, out int i))
							sel.Add(i);
				SelectionChanged();
			}
			return;
		}
		// "sectorgradient <floor|ceiling|both|shade> <from> <to> <equal 1|0> <round 1|0> <shade parts> <sectors...>":
		// the Gradient tool's (SectorGradient), one undo step
		if (action.StartsWith("sectorgradient ", StringComparison.Ordinal))
		{
			var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			int I(int k) => k < p.Length && int.TryParse(p[k], out int v) ? v : 0;
			var secs = p.Skip(7).Select(t => int.TryParse(t, out int v) ? v : -1).Where(v => v >= 0 && v < Doc.Sectors.Count).ToList();
			if (p.Length < 9 || secs.Count < 2 || !Tools.SectorGradient.Whats.Contains(p[1]))
			{
				DisplayStatus("gradient: two sectors or more");
				return;
			}
			Undo.CreateUndo(p[1] == "shade" ? "Brightness gradient" : "Height gradient");
			int n = Tools.SectorGradient.Apply(Doc, secs, p[1], I(2), I(3), I(4) != 0, I(5) != 0, I(6));
			Doc.NotifyChanged();
			UndoChanged();
			DisplayStatus(p[1] == "shade"
				? $"gradient: {n} sectors' brightness from shade {I(2)} (sector {secs[0]}) to {I(3)} (sector {secs[^1]})"
				: $"gradient: {n} sectors' {(p[1] == "both" ? "floors and ceilings" : p[1] + "s")} from {Units.Z(I(2))} (sector {secs[0]}) to {Units.Z(I(3))} (sector {secs[^1]})");
			return;
		}
		// "properties <sector|line|sprite|point> 3,4,...": their properties dialog (the context menu's Properties...)
		if (action.StartsWith("properties ", StringComparison.Ordinal))
		{
			var p = action.Split(' ');
			if (p.Length >= 3)
			{
				var (kind, count) = p[1] switch
				{
					"sector" => (ElementKind.Sector, Doc.Sectors.Count),
					"line" => (ElementKind.Line, Doc.Lines.Count),
					"point" => (ElementKind.Point, Doc.Points.Count),
					"light" => (ElementKind.Light, Doc.Lights.Count),
					_ => (ElementKind.Sprite, Doc.Sprites.Count),
				};
				var items = p[2].Split(',', StringSplitOptions.RemoveEmptyEntries)
					.Select(t => int.TryParse(t, out int i) ? i : -1).Where(i => i >= 0 && i < count).Distinct().ToList();
				if (items.Count > 0)
					EditProperties(kind, items);
			}
			return;
		}
		// a properties dialog's Fix: what a sector, wall or sprite needs and Forge can make (its markers, an x-structure),
		// one undo step; then its dialog again on that tab
		if (action.StartsWith("fixneeds ", StringComparison.Ordinal))
		{
			var p = action.Split(' ');
			if (p.Length >= 3 && int.TryParse(p[2], out int at))
			{
				var needs = p[1] switch
				{
					"sector" when at < Doc.Sectors.Count => Blood.Requirements.Sector(Doc, at),
					"wall" when at < Doc.Walls.Count => Blood.Requirements.Wall(Doc, at),
					"sprite" when at < Doc.Sprites.Count => Blood.Requirements.Sprite(Doc, at),
					_ => new List<Blood.Need>(),
				};
				Undo.CreateUndo($"Fix {p[1]} {at}");
				int changes = 0;
				foreach (var n in needs.Where(n => !n.Met && n.Fix != null))
					changes += n.Fix(Doc);
				if (changes == 0)
					Undo.WithdrawUndo();
				else
					Doc.NotifyChanged();
				UndoChanged();
				DisplayStatus(changes == 0 ? $"{p[1]} {at}: nothing to fix" : $"{p[1]} {at}: fixed ({changes} change(s))");
				if (p.Length >= 4 && int.TryParse(p[3], out int tab))
				{
					Blood.EditDialogs.StartTab = tab;
					EditProperties(p[1] switch { "sector" => ElementKind.Sector, "wall" => ElementKind.Line, _ => ElementKind.Sprite },
						new List<int> { p[1] == "wall" ? Doc.LineOf[at] : at });
				}
			}
			return;
		}
		// Play: a sprite (s87), a wall (w12) or a sector (64) used as E uses it (tests, replays of scripted runs)
		if (action.StartsWith("playuse ", StringComparison.Ordinal))
		{
			string arg = action[8..].Trim();
			if (_play != null && int.TryParse(arg.TrimStart('s', 'w'), out int n) && n >= 0)
			{
				var used = arg[0] == 's' ? _play.Sim.Use(-1, -1, n, -1) : arg[0] == 'w' ? _play.Sim.Use(n, -1, -1, -1) : _play.Sim.Use(-1, n, -1, n);
				DisplayStatus(used != null ? $"used {used}" : NothingToUse);
			}
			return;
		}
		if (action.StartsWith("standonfloor ", StringComparison.Ordinal))
		{
			StandOnFloor(action);
			return;
		}
		if (KeyMap.PreviewOnly(action) && _preview.Visible && PreviewTextureAction(action))
			return;
		if (action.StartsWith("tool ", StringComparison.Ordinal))
		{
			RunTool(action[5..]);
			return;
		}
		// the map's own art (Map Options > Art): "mapart add|remove|kind|move|pack|reload ..." (MainWindow.Art.cs)
		if (action.StartsWith("mapart ", StringComparison.Ordinal))
		{
			MapArtCommand(action[7..]);
			return;
		}
		// rooms over rooms, wall portals and doors: "addfloor ...", "makeportal ...", "makewallportal ...", "makemirror ...",
		// "makedoor ..." (MainWindow.Links.cs; makewallportal and makemirror were left out here and answered "not there
		// yet": Forge report 20261105-214020)
		if (action.Split(' ', 2)[0] is "addfloor" or "makeportal" or "makewallportal" or "makemirror" or "makedoor" or "adjustdoor" && LinkCommand(action))
			return;
		// the map's dynamic lights: "lightset ...", "generatelights" (MainWindow.Lights.cs)
		if (action.Split(' ', 2)[0] is "lightset" or "generatelights" or "lightmove" or "lightadd" or "copylights" && LightCommand(action))
			return;
		// the map's atmospheres: "atmoassign ...", "atmonew ...", "atmodelete ...", "atmoset ...", "generateatmosphere"
		// (MainWindow.Atmosphere.cs)
		if (action.Split(' ', 2)[0] is "atmoassign" or "atmonew" or "atmodelete" or "atmoset" or "generateatmosphere" && AtmosphereCommand(action))
			return;
		// Ctrl+C in the 3D preview: "visualcopy f12" (the picture of what the crosshair is on, for Ctrl+V)
		if (action.StartsWith("visualcopy ", StringComparison.Ordinal))
		{
			if (Preview3D.Parse(Doc, action[11..]) is { } ct)
			{
				TextureClip = TileOf(ct);
				_textureClipFields = Tools.TextureOps.Clip(Doc, ct);
				DisplayStatus($"copied tile {TextureClip} and its settings ({DescribeTarget(ct)}): Ctrl+V puts them on the selection (another kind of surface: the picture)");
			}
			return;
		}
		// the 3D preview's menu: "visualtile <tile> f12 w40u s7 ..." (the picture of each)
		if (action.StartsWith("visualtile ", StringComparison.Ordinal))
		{
			VisualTile(action);
			return;
		}
		// slope edges: "visualslope <dz> <f12|c12> <x1> <y1> <x2> <y2> [tilt|first]": that floor's (ceiling's) edge from
		// (x1, y1) to (x2, y2) raised by dz, its far corner kept (Tools.SlopeOps); tilt: the plane tilted about that edge
		// (the far corner moved by dz); first: the edge made the first wall, the plane kept as near as it can (Shift+click);
		// a run of notches on one edge one undo step
		if (action.StartsWith("visualslope ", StringComparison.Ordinal))
		{
			var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			int I(int k) => k < p.Length && int.TryParse(p[k], out int v) ? v : 0;
			if (p.Length < 7 || Preview3D.Parse(Doc, p[2]) is not { IsPlane: true } plane)
				return;
			if (Posed)
			{
				DisplayStatus("slopes are changed in Edit");
				return;
			}
			bool ceiling = plane.Part == VisualPart.Ceiling;
			int wall = Tools.SlopeOps.FindEdge(Doc, plane.Sector, I(3), I(4), I(5), I(6));
			// (whichever layer the plane is in: the 3D preview edits what it shows, layers or not)
			if (wall < 0)
			{
				DisplayStatus("slope: that edge isn't the sector's any more");
				return;
			}
			string how = p.Length > 7 ? p[7] : "";
			bool first = how == "first", tilt = how == "tilt";
			string key = $"slope {p[2]} {p[3]} {p[4]} {p[5]} {p[6]} {how}";
			if (first || key != _visualKey || Undo.Top == null || Undo.Top != _visualTop)
			{
				Undo.CreateUndo(first ? $"First wall of sector {plane.Sector}" : $"Slope {(ceiling ? "ceiling" : "floor")} of sector {plane.Sector}");
				_visualKey = first ? null : key;
				_visualTop = Undo.Top;
			}
			bool wasFirst = Doc.Sectors[plane.Sector].WallPtr == wall;
			if ((tilt ? Tools.SlopeOps.Tilt(Doc, plane.Sector, ceiling, wall, I(1)) : Tools.SlopeOps.RaiseEdge(Doc, plane.Sector, ceiling, wall, first ? 0 : I(1))) is { } why)
			{
				DisplayStatus("slope: " + why);
				return;
			}
			Doc.NotifyChanged();
			UndoChanged();
			var sec = Doc.Sectors[plane.Sector];
			int h = ceiling ? sec.CeilingHeinum : sec.FloorHeinum;
			string what = $"sector {plane.Sector}'s {(ceiling ? "ceiling" : "floor")}";
			if (first)
				DisplayStatus($"slope: {what}: that wall is the first wall {(wasFirst ? "already" : "now")}; the far edge (cyan) locked: the wheel tilts the plane about it");
			else if (tilt)
				DisplayStatus($"slope: {what} tilted about its first wall (at {Units.Z(ceiling ? sec.CeilingZ : sec.FloorZ)})" + (h == 0 ? ", flat" : $", slope {h} ({Math.Atan(h / 4096.0) * 180 / Math.PI:0.#}°)"));
			else
				DisplayStatus($"slope: {what}'s first wall at {Units.Z(ceiling ? sec.CeilingZ : sec.FloorZ)}, its far edge kept" + (h == 0 ? ", flat" : $", slope {h} ({Math.Atan(h / 4096.0) * 180 / Math.PI:0.#}°)"));
			return;
		}
		// the 3D preview's wheel: "visualz <dz> f12 c40 ..." (floors and ceilings, Build z, up negative)
		if (action.StartsWith("visualz ", StringComparison.Ordinal))
		{
			VisualHeight(action);
			return;
		}
		// the Layers tab's commands carry their layer (and a name) after the action: "layershow 2 0"
		if (action.StartsWith("layer", StringComparison.Ordinal))
		{
			LayerCommand(action);
			return;
		}
		switch (action)
		{
			case "newmap": ConfirmDiscard(() => SetDocument(MapDocument.New())); break;
			case "openmap":
			{
				var d = MakeFileDialog(Godot.FileDialog.FileModeEnum.OpenFile, "*.map, *.mapx ; Maps");
				d.FileSelected += OpenFile;
				d.PopupCentered(new Vector2I(900, 600));
				break;
			}
			case "savemap":
				if (Doc.Path == "")
					AskSavePath(Doc.Format);
				else
					SaveTo(Doc.Path, Doc.Format);
				break;
			case "savemapas": AskSavePath(Doc.Format); break;
			case "savemapx": AskSavePath(MapFormat.MapX); break;
			case "exportmap": AskSavePath(MapFormat.Map); break;
			case "mapoptions": MapOptionsDialog.Show(this); break;
			case "preferences": PreferencesDialog.Show(this); break;
			case "exit": ConfirmDiscard(() => GetTree().Quit()); break;
			case "selectall": SelectAll(); break;
			// (in the 3D preview its own selection)
			case "clearselection":
				if (_preview.Visible)
				{
					_preview.ClearSelection();
					if (SyncSelection)
						SyncTo2D();
				}
				else
					ClearSelection();
				break;
			// invisible sprites (markers, a wall crack's target) faint in the 3D preview, or not drawn as in the game
			// what each sector does (xmapedit's captions): boxed in the 2D view, floating in the 3D preview's rooms
			case "showsectoreffects":
				View.ShowSectorEffects = !View.ShowSectorEffects;
				Config.Set("showsectoreffects", View.ShowSectorEffects);
				Config.Save();
				UpdateViewModeMenu();
				View.QueueRedraw();
				DisplayStatus(View.ShowSectorEffects ? "sector effects shown in the 2D view" : "sector effects hidden in the 2D view");
				break;
			// the focus's channels followed through every receiver that sends on (and back to what sends to it)
			case "linkchains":
				View.LinkChains = !View.LinkChains;
				Config.Set("linkchains", View.LinkChains);
				Config.Save();
				UpdateViewModeMenu();
				View.QueueRedraw();
				DisplayStatus(View.LinkChains ? "links: chains followed (what each receiver sends on, and what sends to it)" : "links: the focus's own only");
				break;
			case "showsectoreffects3d":
				_preview.ShowSectorEffects = !_preview.ShowSectorEffects;
				Config.Set("showsectoreffects3d", _preview.ShowSectorEffects);
				Config.Save();
				UpdateViewModeMenu();
				_preview.MapChanged();
				DisplayStatus(_preview.ShowSectorEffects ? "sector effects shown in the 3D preview" : "sector effects hidden in the 3D preview");
				break;
			case "showlights3d":
				_preview.ShowLights = !_preview.ShowLights;
				Config.Set("showlights3d", _preview.ShowLights);
				Config.Save();
				UpdateViewModeMenu();
				_preview.MapChanged();
				DisplayStatus(_preview.ShowLights ? "dynamic lights shown in the 3D preview" : "dynamic lights off in the 3D preview (Build's shading alone, as r_dynamiclights 0)");
				break;
			case "showatmosphere3d":
				_preview.ShowAtmosphere = !_preview.ShowAtmosphere;
				Config.Set("showatmosphere3d", _preview.ShowAtmosphere);
				Config.Save();
				UpdateViewModeMenu();
				_preview.MapChanged();
				DisplayStatus(_preview.ShowAtmosphere ? "the atmosphere shown in the 3D preview (fog, ground mist, weather)"
					: "the atmosphere off in the 3D preview (Build's darkening with distance)");
				break;
			case "lightshadows":
				_preview.LightShadows = !_preview.LightShadows;
				Config.Set("lightshadows", _preview.LightShadows);
				Config.Save();
				UpdateViewModeMenu();
				_preview.MapChanged();
				DisplayStatus(_preview.LightShadows ? "the nearest dynamic lights cast shadows in the 3D preview" : "dynamic lights without shadows in the 3D preview (faster)");
				break;
			case "togglebrightness":
				View.FullBright = _preview.FullBright = !View.FullBright;
				Config.Set("fullbright", View.FullBright);
				Config.Save();
				UpdateViewModeMenu();
				_preview.MapChanged();
				View.QueueRedraw();
				DisplayStatus(View.FullBright ? "full brightness: the lighting left out (shades, darkening with distance), 2D and 3D; the map keeps its shades"
					: "full brightness off: shaded as in the game");
				break;
			case "showinvisible":
				_preview.ShowInvisible = !_preview.ShowInvisible;
				Config.Set("showinvisible", _preview.ShowInvisible);
				Config.Save();
				UpdateViewModeMenu();
				_preview.MapChanged();
				DisplayStatus(_preview.ShowInvisible ? "invisible sprites shown faint in the 3D preview" : "invisible sprites not drawn (as in the game)");
				break;
			case "syncselection":
				SyncSelection = !SyncSelection;
				Config.Set("syncselection", SyncSelection);
				UpdateViewModeMenu();
				if (SyncSelection && _preview.Visible)
					SyncTo2D();
				DisplayStatus(SyncSelection ? "the 2D view's and the 3D preview's selections in step" : "the 2D view's and the 3D preview's selections apart");
				break;
			case "cancelmode": Editing.Mode?.OnCancel(); break;
			case "zoomin": View.ZoomAt(View.Size * 0.5f, 1.25f); break;
			case "zoomout": View.ZoomAt(View.Size * 0.5f, 0.8f); break;
			case "zoomtofit": View.ZoomToFit(); break;
			case "centerinscreen": CentreOnSelection(); break;
			case "viewmodenormal": SetViewMode(ViewMode.Wireframe); break;
			case "viewmodebrightness": SetViewMode(ViewMode.Brightness); break;
			case "viewmodefloors": SetViewMode(ViewMode.Floors); break;
			case "viewmodeceilings": SetViewMode(ViewMode.Ceilings); break;
			case "nextviewmode": SetViewMode((ViewMode)(((int)View.Mode + 1) % ViewModes.Length)); break;
			case "previousviewmode": SetViewMode((ViewMode)(((int)View.Mode + ViewModes.Length - 1) % ViewModes.Length)); break;
			// the context menu (Space tapped, in the 2D view or the 3D preview): the tools for what is selected
			case "contextmenu": ContextMenu(); break;
			// Snap to Grid (UDB's Shift+G): the 2D selection's points (a wall's ends, a sector's corners) and sprites
			case "snaptogrid":
				if (ToolContext() is { In3D: false } sc)
					SnapToGrid(sc);
				break;
			case "showedit": SetShown(ShownState.Edit); break;
			// (F3, F4 again: back to Edit, as xmapedit's show off / show on)
			case "showoff": SetShown(_shown == ShownState.Off ? ShownState.Edit : ShownState.Off); break;
			case "showon": SetShown(_shown == ShownState.On ? ShownState.Edit : ShownState.On); break;
			case "playmovers": TogglePlay(); break;
			case "showalllinks":
				View.AllLinks = !View.AllLinks;
				Config.Set("alllinks", View.AllLinks);
				UpdateViewModeMenu();
				View.QueueRedraw();
				break;
			case "spritepictures":
				View.SpritePictures = !View.SpritePictures;
				Config.Set("spritepictures", View.SpritePictures);
				UpdateViewModeMenu();
				View.QueueRedraw();
				break;
			case "togglegrid":
				View.ShowGrid = !View.ShowGrid;
				Config.Set("showgrid", View.ShowGrid);
				View.QueueRedraw();
				break;
			// the grid over the filled sectors (the Floors, Ceilings and Brightness modes) instead of under them
			case "gridoverfill":
				View.GridOverFill = !View.GridOverFill;
				Config.Set("gridoverfill", View.GridOverFill);
				Config.Save();
				UpdateViewModeMenu();
				View.QueueRedraw();
				DisplayStatus(View.GridOverFill ? "the grid drawn over the floors and ceilings in the 2D view" : "the grid drawn under the floors and ceilings in the 2D view");
				break;
			case "togglesnap":
				View.Snap = !View.Snap;
				Config.Set("snap", View.Snap);
				UpdateStatusFields();
				break;
			case "griddec": SetGrid(Math.Max(1, View.GridSize / 2)); break;
			case "gridinc": SetGrid(Math.Min(8192, View.GridSize * 2)); break;
			case "gotocoords": GoToDialog.Show(this); break;
			case "visualmode": Toggle3D(); break;
			case "toggleinfopanel":
				_info.Visible = !_info.Visible;
				Config.Set("infopanel", _info.Visible);
				break;
			case "cycleoverlap": Editing.Mode?.OnCycle(); break;
			case "undo":
				if (Undo.PerformUndo())
					DisplayStatus("undone");
				UndoChanged();
				break;
			case "redo":
				if (Undo.PerformRedo())
					DisplayStatus("redone");
				UndoChanged();
				break;
			case "showerrors": RunAnalysis(); break;
			// the Gradient tool over the selected sectors (Tools > Gradient..., the context menu)
			case "gradienttool":
				if (ToolContext() is { } gc)
					Tools.BuiltinTools.Gradient(gc);
				break;
			case "testmap": TestMap(); break;
			case "copyproperties": CopyProperties(); break;
			case "pasteproperties": PasteProperties(); break;
			case "thinglookatcursor": (Editing.Mode as SpritesMode)?.LookAtCursor(); break;
			case "insertitem": Editing.Mode?.OnInsert(); break;
			case "deleteitem": Editing.Mode?.OnDelete(); break;
			case "dissolveitem":
				if (Editing.Mode is WallsMode wm)
					wm.Detach();
				else if (Editing.Mode is VerticesMode)
					Editing.Mode.OnDelete();
				else if (Editing.Mode is SectorsMode dm and not AtmosphereMode)
					dm.Dissolve();
				break;
			case "setfirstwall": (Editing.Mode as WallsMode)?.MakeFirstWall(); break;
			// the selected sectors' red walls to the sectors round them made white: they move on their own
			case "separatesectors":
				if (Editing.Mode is SectorsMode sm and not AtmosphereMode)
					sm.Separate();
				else
					DisplayStatus("separate: in Sectors mode, select the sectors to separate");
				break;
			// Build has one join (UDB's merge: the walls between go); both of UDB's keys do it
			case "joinsectors" or "mergesectors":
				if (Editing.Mode is WallsMode walls)
					walls.JoinWalls();
				else
					(Editing.Mode is AtmosphereMode ? null : Editing.Mode as SectorsMode)?.Join();
				break;
			case "reportproblem": ReportProblem(); break;
			case "copyselection": CopySelection(false); break;
			case "cutselection": CopySelection(true); break;
			case "pasteselection":
				if (_clip == null)
					DisplayStatus("paste: nothing copied (Ctrl+C sectors in Sectors mode, sprites in Sprites mode, walls in Walls mode or lights in Lights mode)");
				else
				{
					_pasteMode.Clip = _clip;
					Editing.Switch(_pasteMode);
				}
				break;
			case "rotateselection": (Editing.Mode as ClassicMode)?.StartRotate(); break;
			case "moveselection": (Editing.Mode as ClassicMode)?.StartMove(); break;
			case "scaleselection": (Editing.Mode as ClassicMode)?.StartScale(); break;
			case "soundbrowser": Ui.SoundPickerDialog.Show(this, null, 0, null); break;
			case "playuse": PlayUse2D(); break;
			case "opensessions": OS.ShellOpen(Recording.SessionRecorder.Root); break;
			case "about":
				Alert("About Chosen Forge", $"Chosen Forge {Chosen.Shared.Engine.ChosenVersion.Full}: a map editor for Blood's .MAP and Chosen's .MAPX,\nin the manner of UltimateDoomBuilder. Free software under the GPL v2 (LICENSE.md).");
				break;
			default:
				if (Editing.Modes.Any(m => m.Action == action))
					Editing.Switch(action);
				else
					DisplayStatus($"{action}: not there yet");
				break;
		}
	}

	/// <summary>The grid a new map starts on, and the grid before one is chosen.</summary>
	const int NewMapGrid = 16;

	public void SetGrid(int g)
	{
		View.GridSize = g;
		Config.Set("grid", g);
		View.QueueRedraw();
		UpdateStatusFields();
	}

	void SelectAll()
	{
		if (Editing.Mode is not ClassicMode)
			return;
		var kind = Editing.Mode switch
		{
			VerticesMode => ElementKind.Point, WallsMode => ElementKind.Line, SectorsMode => ElementKind.Sector, LightsMode => ElementKind.Light,
			_ => ElementKind.Sprite,
		};
		int n = kind switch
		{
			ElementKind.Point => Doc.Points.Count, ElementKind.Line => Doc.Lines.Count, ElementKind.Sector => Doc.Sectors.Count,
			ElementKind.Light => Doc.Lights.Count, _ => Doc.Sprites.Count,
		};
		var s = _selection[kind];
		for (int i = 0; i < n; i++)
			s.Add(i);
		SelectionChanged();
	}

	void CentreOnSelection()
	{
		var pts = new List<Vector2>();
		foreach (int i in _selection[ElementKind.Point])
			pts.Add(new Vector2(Doc.Points[i].X, Doc.Points[i].Y));
		foreach (int i in _selection[ElementKind.Sprite])
			pts.Add(new Vector2(Doc.Sprites[i].X, Doc.Sprites[i].Y));
		foreach (int i in _selection[ElementKind.Sector])
			pts.Add(Doc.Labels[i]);
		foreach (int i in _selection[ElementKind.Light])
			if (Doc.LightPlace(i) is { } lp)
				pts.Add(new Vector2((float)lp.x, (float)lp.y));
		foreach (int i in _selection[ElementKind.Line])
		{
			var w = Doc.Walls[Doc.Lines[i].Front];
			pts.Add(new Vector2(w.X, w.Y));
		}
		if (pts.Count == 0)
			return;
		var c = Vector2.Zero;
		foreach (var p in pts)
			c += p;
		View.CentreOn(c / pts.Count);
	}

	List<MapLink> _links;
	MapDocument _linksOf;

	// ---------------------------------------------------------------- the moving sectors: Edit, Off, On, Play

	/// <summary>What the 2D view and the 3D preview show of the moving sectors: as stored (the map edited), at their
	/// off places, at their on places (Blood.SectorMotion; a copy of the map, the map itself untouched).</summary>
	public enum ShownState { Edit, Off, On }

	ShownState _shown = ShownState.Edit;
	/// <summary>The map shown while the moving sectors are posed (Off, On, Play): a copy; null in Edit.</summary>
	MapDocument _shownDoc;
	readonly Dictionary<string, Button> _shownButtons = new();

	MapDocument ShownDoc => _shownDoc ?? Doc;

	const string ShownLocked = "the moving sectors are shown off or on: the map's shape is edited in Edit (the toolbar, F3 / F4 again)";

	/// <summary>The actions that change the map's shape: not while the moving sectors are shown off or on.</summary>
	static readonly HashSet<string> ShapeActions = new()
	{
		"deleteitem", "dissolveitem", "insertitem", "joinsectors", "mergesectors", "cutselection", "pasteselection", "separatesectors",
		"setfirstwall", "drawlinesmode", "drawrectanglemode", "drawcirclemode", "drawgridmode", "thinglookatcursor",
		"moveselection", "rotateselection", "scaleselection",
	};

	/// <summary>Play's clock while playing (the 2D view or the 3D preview, whichever shows), else null.</summary>
	MoverPlay _play;

	/// <summary>The moving sectors are shown away from where they are stored (Off, On, playing): the map's shape
	/// isn't edited.</summary>
	public bool Posed => _shown != ShownState.Edit || _play != null;

	/// <summary>Off, On, Play's colours (xmapedit's: blue off, red on), the frame and label round the views.</summary>
	static readonly Color OffColor = new(0.35f, 0.6f, 1f), OnColor = new(1f, 0.4f, 0.3f), PlayColor = new(0.4f, 0.95f, 0.45f);

	void SetShown(ShownState s)
	{
		_play = null;
		_shown = s;
		if (s != ShownState.Edit)
			Editing.Mode?.OnCancel();
		RefreshShown();
		int n = Blood.SectorMotion.Movers(Doc).Count;
		DisplayStatus(s switch
		{
			ShownState.Off => $"Off: the {n} moving sectors at their off places (the map's shape is edited in Edit)",
			ShownState.On => $"On: the {n} moving sectors at their on places (the map's shape is edited in Edit)",
			_ => "Edit: the map as stored",
		});
	}

	/// <summary>The shown map made again: a copy posed at Off or On, or the map itself in Edit (Play stops).</summary>
	void RefreshShown()
	{
		_play = null;
		if (_shown == ShownState.Edit)
			_shownDoc = null;
		else
		{
			_shownDoc = Doc.Copy();
			Blood.SectorMotion.Pose(Doc, _shownDoc, _ => _shown == ShownState.On ? 1.0 : 0.0);
			// (slopes aligned as the game aligns them every frame)
			Blood.SectorMotion.AlignSlopes(_shownDoc);
			_shownDoc.NotifyChanged();
		}
		View.Doc = ShownDoc;
		_preview.Doc = ShownDoc;
		// (Blood's panning that pans when on: moving in On)
		_preview.PanOn = _shown == ShownState.On;
		_links = null;
		View.MapChanged();
		_preview.MapChanged();
		UpdateShownUi();
	}

	/// <summary>P: the moving sectors played in the view showing (the 2D view or the 3D preview), or stopped where
	/// they were shown.</summary>
	void TogglePlay()
	{
		if (_play != null)
		{
			RefreshShown();
			DisplayStatus("Play stopped");
			return;
		}
		Editing.Mode?.OnCancel();
		// (a fresh copy run from the level's start: the map itself stays as stored)
		_shownDoc = Doc.Copy();
		View.Doc = _shownDoc;
		_preview.Doc = _shownDoc;
		_play = new MoverPlay(Doc, _shownDoc);
		// (the camera starts where it is: no sector entered yet)
		_playSector = _preview.Visible ? _preview.CameraSectorNow : -1;
		_shownDoc.NotifyChanged();
		View.MapChanged();
		if (_preview.Visible)
			_preview.Rebuild();
		else
			_preview.MapChanged();
		UpdateShownUi();
		DisplayStatus("Play: the level from its start, run by Blood's events (level start, switches, timers, links); "
			+ "in the 3D preview flying into a sector triggers it as the player does, E uses what the crosshair is on; P stops");
	}

	/// <summary>The sector Play's player (the 3D preview's camera) is in.</summary>
	int _playSector = -1;

	/// <summary>E in Play: what the crosshair is on used as the player uses it (Space in the game).</summary>
	void PlayUse()
	{
		if (_play == null)
		{
			DisplayStatus("E uses things in Play (P): doors, switches, what the crosshair is on");
			return;
		}
		var t = _preview.Target;
		int wall = -1, sector = -1, sprite = -1;
		if (t is { } v)
		{
			if (v.IsSprite)
				sprite = v.Sprite;
			else if (v.Part is VisualPart.Floor or VisualPart.Ceiling)
				sector = v.Sector;
			else
				wall = v.Wall;
		}
		var used = _play.Sim.Use(wall, sector, sprite, _preview.CameraSectorNow);
		DisplayStatus(used != null ? $"used {used}" : NothingToUse);
	}

	const string NothingToUse = "nothing to use there (a wall, sector or sprite set to be pushed, a thing an explosion breaks)";

	/// <summary>E in Play in the 2D view: what the mouse is on (the mode's highlight; else the sector under it) used
	/// as the player uses it; a sector nothing pushes but entering sets off: entered.</summary>
	void PlayUse2D()
	{
		if (_play == null)
		{
			DisplayStatus("E uses things in Play (P): doors, switches, cracks, what the mouse is on");
			return;
		}
		var d = View.Doc;
		var cur = View.Cursor;
		int here = -1;
		double best = double.MaxValue;
		for (int s = 0; s < d.Sectors.Count; s++)
			if (d.SectorShown(s) && Chosen.Shared.Engine.Geometry.BuildGeom.Inside(d, s, cur.X, cur.Y) && s < d.Areas.Length && d.Areas[s] < best)
			{
				best = d.Areas[s];
				here = s;
			}
		var e = (Editing.Mode as ClassicMode)?.Highlighted ?? Element.None;
		int wall = -1, sector = -1, sprite = -1;
		switch (e.Kind)
		{
			case ElementKind.Sprite:
				sprite = e.Index;
				break;
			case ElementKind.Line when e.Index < d.Lines.Count:
				// (the side that pushes, else the one facing the mouse's sector)
				var line = d.Lines[e.Index];
				bool Pushes(int w) => w >= 0 && (d.Walls[w].Xw is { TriggerPush: not 0 }
					|| (d.Walls[w].NextSector >= 0 && d.Sectors[d.Walls[w].NextSector].Xs is { Wallpush: not 0 }));
				wall = Pushes(line.Front) ? line.Front : Pushes(line.Back) ? line.Back
					: line.Back >= 0 && d.Walls[line.Back].Sector == here ? line.Back : line.Front;
				break;
			case ElementKind.Sector:
				sector = here = e.Index;
				break;
			default:
				sector = here;
				break;
		}
		var used = _play.Sim.Use(wall, sector, sprite, here);
		if (used == null && sector >= 0 && d.Sectors[sector].Xs is { Enter: not 0 })
		{
			_play.Sim.PlayerMoved(-1, sector);
			used = $"sector {sector} (entered)";
		}
		DisplayStatus(used != null ? $"used {used}" : NothingToUse);
	}

	public override void _Process(double delta)
	{
		if (_play == null || _shownDoc == null)
			return;
		// the player (the camera) entering and leaving sectors
		if (_preview.Visible && _preview.CameraSectorNow != _playSector)
		{
			_play.Sim.PlayerMoved(_playSector, _preview.CameraSectorNow);
			_playSector = _preview.CameraSectorNow;
		}
		var moved = _play.Step(_shownDoc, delta);
		if (moved.Count == 0)
			return;
		if (_preview.Visible)
			_preview.Moved(_play, moved);
		else
		{
			// (the 3D preview made whole when it next shows)
			_preview.MapChanged();
			View.SectorsMoved(_play.Touched(moved).sectors);
		}
	}

	/// <summary>
	/// The 3D preview's wheel on floors and ceilings: in Edit their heights as stored; in Off or On the moving
	/// sectors' heights for that state (the x-sector's Off / On floor or ceiling: xmapedit's capture, by moving the
	/// plane itself; a plane not moving yet starts from its height as stored, both states). A sector that doesn't
	/// move keeps its heights there (Edit changes them).
	/// </summary>
	void VisualHeight(string action)
	{
		var parts = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 3 || !int.TryParse(parts[1], out int dz))
			return;
		if (_play != null)
		{
			DisplayStatus("Play is on: stop it (P) to change heights");
			return;
		}
		var planes = new List<(bool ceiling, int s)>();
		var sprites = new List<int>();
		var lights = new List<int>();
		foreach (var p in parts.Skip(2))
			if (p.Length > 1 && int.TryParse(p[1..], out int s) && s >= 0)
			{
				if (p[0] == 'l' && s < Doc.Lights.Count)
					lights.Add(s);
				else if (p[0] == 's' && s < Doc.Sprites.Count)
					sprites.Add(s);
				else if (p[0] is 'c' or 'f' && s < Doc.Sectors.Count)
					planes.Add((p[0] == 'c', s));
			}
		var stops = _shown != ShownState.Edit;
		// lights up and down (a run on the same lights one undo step)
		lights = lights.Where(Doc.LightEditable).Distinct().ToList();
		if (lights.Count > 0)
		{
			MergeUndo("lightz " + string.Join(" ", lights.OrderBy(i => i)), lights.Count == 1 ? "Move light up or down" : $"Move {lights.Count} lights up or down");
			foreach (int i in lights)
			{
				Doc.Lights[i].Z += dz;
				Doc.SettleLight(i);
			}
			Doc.NotifyLightsChanged();
			UndoChanged();
			int lh = Blood.LightDialog.Height(Doc, lights[0]);
			DisplayStatus($"light {lights[0]}: {(Units.Game ? (lh / Units.PerZ).ToString("0.##", Inv) : lh.ToString(Inv))} over "
				+ (Doc.Lights[lights[0]].Attached ? "its spot on its sprite" : "the floor") + (lights.Count > 1 ? $" (and {lights.Count - 1} more)" : ""));
			if (sprites.Count == 0 && planes.Count == 0)
				return;
		}
		// sprites up and down (as stored: in Edit only)
		if (sprites.Count > 0 && planes.Count == 0)
		{
			if (stops)
			{
				DisplayStatus("sprites are moved up and down in Edit");
				return;
			}
			string skey = "Edit " + string.Join(" ", sprites.OrderBy(i => i).Select(i => "s" + i));
			if (skey != _visualKey || Undo.Top == null || Undo.Top != _visualTop)
			{
				Undo.CreateUndo(sprites.Count == 1 ? "Move sprite up or down" : $"Move {sprites.Count} sprites up or down");
				_visualKey = skey;
				_visualTop = Undo.Top;
			}
			foreach (int i in sprites)
				Doc.Sprites[i].Z += dz;
			Doc.NotifyChanged();
			UndoChanged();
			DisplayStatus($"sprite {sprites[0]}: z {Units.Z(Doc.Sprites[sprites[0]].Z)}" + (sprites.Count > 1 ? $" (and {sprites.Count - 1} more)" : ""));
			return;
		}
		var moving = new HashSet<int>(Blood.SectorMotion.HeightMovers(Doc));
		var skipped = stops ? planes.Where(p => !moving.Contains(p.s)).Select(p => p.s).Distinct().ToList() : new List<int>();
		planes.RemoveAll(p => skipped.Contains(p.s));
		if (planes.Count == 0)
		{
			DisplayStatus(skipped.Count > 0 ? $"sector {skipped[0]} doesn't move: its heights are changed in Edit (Off / On set a moving sector's)" : "");
			return;
		}
		string what = planes.Count == 1 ? (planes[0].ceiling ? "ceiling" : "floor") : $"{planes.Count} planes";
		// one undo level for a run of notches on the same planes in the same state, while it is the newest level
		// (another change, an undo or other planes start a new one)
		string key = $"{_shown} " + string.Join(" ", planes.OrderBy(p => p.s).ThenBy(p => p.ceiling).Select(p => (p.ceiling ? "c" : "f") + p.s));
		if (key != _visualKey || Undo.Top == null || Undo.Top != _visualTop)
		{
			Undo.CreateUndo("Change " + what + " height" + (stops ? $" ({_shown})" : ""));
			_visualKey = key;
			_visualTop = Undo.Top;
		}
		foreach (var (ceiling, s) in planes)
		{
			var sec = Doc.Sectors[s];
			if (!stops)
			{
				if (ceiling)
					sec.CeilingZ += dz;
				else
					sec.FloorZ += dz;
				continue;
			}
			var x = sec.Xs;
			bool on = _shown == ShownState.On;
			if (ceiling)
			{
				if (x.OnCeilZ == x.OffCeilZ)
					x.OnCeilZ = x.OffCeilZ = sec.CeilingZ;
				if (on) x.OnCeilZ += dz; else x.OffCeilZ += dz;
			}
			else
			{
				if (x.OnFloorZ == x.OffFloorZ)
					x.OnFloorZ = x.OffFloorZ = sec.FloorZ;
				if (on) x.OnFloorZ += dz; else x.OffFloorZ += dz;
			}
		}
		Doc.NotifyChanged();
		UndoChanged();
		var (c0, s0) = planes[0];
		var sx = Doc.Sectors[s0];
		int z = !stops ? (c0 ? sx.CeilingZ : sx.FloorZ)
			: _shown == ShownState.On ? (c0 ? sx.Xs.OnCeilZ : sx.Xs.OnFloorZ) : (c0 ? sx.Xs.OffCeilZ : sx.Xs.OffFloorZ);
		DisplayStatus($"{(stops ? _shown + " " : "")}{(c0 ? "ceiling" : "floor")} of sector {s0}: {Units.Z(z)}"
			+ (planes.Count > 1 ? $" (and {planes.Count - 1} more)" : "")
			+ (skipped.Count > 0 ? $"; {skipped.Count} sectors that don't move kept" : ""));
	}

	string _visualKey;
	object _visualTop;

	int _openDialogs;

	/// <summary>A dialog is open (Popup): the 3D preview leaves the mouse alone until it closes.</summary>
	public bool DialogOpen => _openDialogs > 0;

	/// <summary>The 3D preview takes the mouse back (a menu or the last dialog closed): no click needed.</summary>
	public void ResumePreview()
	{
		if (_preview.Visible && !DialogOpen && !Replaying)
		{
			ForgeInput.Capture();
			_preview.GrabFocus();
		}
	}

	/// <summary>The 3D preview's targets: its selection when what the crosshair is on is in it (or always, when
	/// selectionFirst and there is one), else what the crosshair is on.</summary>
	List<VisualTarget> PreviewTargets(bool selectionFirst, bool lights = false)
	{
		var t = _preview.Target;
		List<VisualTarget> r;
		if (_preview.Selected.Count > 0 && (selectionFirst || t == null || _preview.Selected.Contains(t.Value)))
			r = _preview.Selected.ToList();
		else
			r = t is { } v ? new List<VisualTarget> { v } : new List<VisualTarget>();
		// (the lights only where asked for: the surfaces' tools and textures have nothing for them; in Lights mode the
		// lights alone: the rest isn't clicked there)
		if (_preview.LightMarkers)
			return lights ? r.Where(x => x.IsLight).ToList() : new List<VisualTarget>();
		return lights ? r : r.Where(x => !x.IsLight).ToList();
	}

	/// <summary>The context menu (Space): the tools (Tools/ContextTools.cs) for the 3D preview's targets, or the 2D
	/// view's selection (the mode's kind; else what is highlighted).</summary>
	void ContextMenu()
	{
		if (Replaying || ToolContext() is not { } c)
			return;
		// (Atmosphere mode's menu in the 2D view, as its right click: the sectors' tools there edited what it doesn't)
		if (!c.In3D && Editing.Mode is AtmosphereMode atmo)
		{
			atmo.OnEditEnd(default);
			return;
		}
		Func<Vector2> at;
		if (c.In3D)
		{
			// the mouse let go at the crosshair and the menu there (the mouse's position isn't known again until it
			// moves: read back after the warp it is where it was)
			var centre = _preview.GlobalPosition + _preview.Size * 0.5f;
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
				if (!ForgeInput.NoInput)
					Input.WarpMouse(centre);
			}
			at = () => _preview.GlobalPosition + _preview.Size * 0.5f;
		}
		else
			at = () => GetViewport().GetMousePosition();
		Tools.ContextTools.Show(c, at, ResumePreview);
	}

	/// <summary>"tool <name>": a context menu tool run on what the menu would offer it for (its name without case or
	/// "...").</summary>
	void RunTool(string name)
	{
		// (the selection first: a command, not a click on something)
		if (Replaying || ToolContext(true) is not { } c)
			return;
		string Plain(string s) => s.Replace("...", "").Trim().ToLowerInvariant();
		var t = Tools.ContextTools.All.FirstOrDefault(x => Plain(x.Name) == Plain(name));
		if (t == null)
			DisplayStatus($"tool: no tool called \"{name}\"");
		else if (t.Shape && Posed)
			DisplayStatus(ShownLocked);
		else if (!t.Applies(c))
			DisplayStatus($"{t.Name}: not for {c.Describe()}");
		else
		{
			if (c.In3D && Input.MouseMode == Input.MouseModeEnum.Captured)
				Input.MouseMode = Input.MouseModeEnum.Visible;
			t.Run(c);
			Callable.From(ResumePreview).CallDeferred();
		}
	}

	/// <summary>What the context menu's tools work on: the 3D preview's targets, or the 2D view's selection of the
	/// mode's kind (else what is highlighted); null in a mode with no items (drawing).</summary>
	Tools.ToolContext ToolContext(bool selectionFirst = false)
	{
		var c = new Tools.ToolContext { Window = this, In3D = _preview.Visible };
		if (c.In3D)
		{
			c.Targets = PreviewTargets(selectionFirst);
			c.UnderCrosshair = _preview.Target;
			// (Lights mode: the lights its tools work on)
			c.LightsMode = _preview.LightMarkers;
			if (c.LightsMode)
				c.Lights = PreviewTargets(selectionFirst, true).Select(t => t.Light).Distinct().OrderBy(i => i).ToList();
		}
		else
		{
			if (Editing.Mode is not ClassicMode cm)
				return null;
			var kind = cm switch
			{
				VerticesMode => ElementKind.Point,
				WallsMode => ElementKind.Line,
				SectorsMode => ElementKind.Sector,
				SpritesMode => ElementKind.Sprite,
				LightsMode => ElementKind.Light,
				_ => ElementKind.None,
			};
			if (kind == ElementKind.None)
				return null;
			c.LightsMode = kind == ElementKind.Light;
			var items = SelectionOf(kind).OrderBy(i => i).ToList();
			if (items.Count == 0 && cm.Highlighted is { IsNone: false } h && h.Kind == kind)
				items.Add(h.Index);
			switch (kind)
			{
				case ElementKind.Point: c.Points = items; break;
				case ElementKind.Line: c.Lines = items; break;
				case ElementKind.Sector: c.Sectors = items; break;
				case ElementKind.Light: c.Lights = items; break;
				default: c.Sprites = items; break;
			}
		}
		return c;
	}

	/// <summary>The tile picker for the targets (at the first one's picture): a sprite just placed.</summary>
	public void PickTexture(List<VisualTarget> targets)
	{
		if (targets.Count == 0)
			return;
		if (Input.MouseMode == Input.MouseModeEnum.Captured)
			Input.MouseMode = Input.MouseModeEnum.Visible;
		var codes = string.Join(" ", targets.Select(Preview3D.Code));
		// the tile looked at shown on the surfaces in the preview (a copy of the map) until Use or Cancel
		int looking = -1;
		bool pending = false;
		void Look(int tile)
		{
			looking = tile;
			if (tile < 0)
			{
				ShowToolResult(null);
				return;
			}
			if (pending)
				return;
			pending = true;
			GetTree().CreateTimer(0.06).Timeout += () =>
			{
				pending = false;
				if (looking < 0)
					return;
				var copy = Doc.Copy();
				foreach (var t in targets)
					SetTile(copy, t, looking);
				ShowToolResult(copy);
			};
		}
		TilePickerDialog.Show(this, _preview, TileOf(targets[0]), tile => UiDo($"visualtile {tile} {codes}"),
			_preview.Visible && !Replaying ? Look : null, PictureKind(targets[0]));
	}

	/// <summary>The picture Ctrl+C took in the 3D preview (-1: none yet).</summary>
	public int TextureClip { get; private set; } = -1;
	/// <summary>The settings Ctrl+C took with it (TextureOps.Clip): Ctrl+V puts them on surfaces of the same kind.</summary>
	Dictionary<string, int> _textureClipFields;

	public void PasteTexture(List<VisualTarget> targets)
	{
		if (TextureClip < 0)
			DisplayStatus("paste: no texture copied yet (Ctrl+C on a surface in the 3D preview)");
		else if (targets.Count > 0)
			UiDo(_textureClipFields != null
				? $"visualtex paste {Tools.TextureOps.Encode(_textureClipFields)} " + string.Join(" ", targets.Select(Preview3D.Code))
				: $"visualtile {TextureClip} " + string.Join(" ", targets.Select(Preview3D.Code)));
	}

	/// <summary>The 2D view's property dialog for 3D targets (one kind: the first target's), through the recorded
	/// "properties" action.</summary>
	public void PreviewProperties(List<VisualTarget> targets)
	{
		if (targets.Count == 0)
			return;
		if (targets[0].IsPlane)
			UiProperties(ElementKind.Sector, targets.Where(t => t.IsPlane).Select(t => t.Sector).Distinct().ToList());
		else if (targets[0].IsSprite)
			UiProperties(ElementKind.Sprite, targets.Where(t => t.IsSprite).Select(t => t.Sprite).Distinct().ToList());
		else
			UiProperties(ElementKind.Line, targets.Where(t => t.IsWall && t.Wall < Doc.LineOf.Length)
				.Select(t => Doc.LineOf[t.Wall]).Distinct().ToList());
	}

	/// <summary>A properties dialog the context menu opens: recorded as "properties sector 3,4" (a replay opens it
	/// and fills it as the dialog line says), then opened.</summary>
	public void UiProperties(ElementKind kind, List<int> items)
	{
		if (items.Count == 0)
			return;
		string k = kind switch { ElementKind.Sector => "sector", ElementKind.Line => "line", ElementKind.Point => "point", ElementKind.Light => "light", _ => "sprite" };
		UiDo($"properties {k} {string.Join(",", items)}");
	}

	/// <summary>"splitlines line...": each line split at its middle (one undo step).</summary>
	void SplitLines(string action)
	{
		var lines = action.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
			.Select(x => int.TryParse(x, out int l) ? l : -1).Where(l => l >= 0 && l < Doc.Lines.Count && Doc.LineEditable(Doc.Lines[l]))
			.Distinct().ToList();
		if (lines.Count == 0)
			return;
		var walls = lines.Select(l => Doc.Walls[Doc.Lines[l].Front]).ToList();
		Operation(lines.Count == 1 ? "Split wall" : $"Split {lines.Count} walls", () =>
		{
			int done = 0;
			foreach (var wall in walls)
			{
				int w = Doc.Walls.IndexOf(wall);
				if (w < 0)
					continue;
				var (x1, y1, x2, y2) = Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, w);
				if (Doc.InsertPoint(w, (int)Math.Round((x1 + x2) / 2.0), (int)Math.Round((y1 + y2) / 2.0)))
					done++;
			}
			return done > 0 ? null : "the walls are too short to split";
		});
	}

	/// <summary>"curvelines count shape depth reverse asone line...": Curve Walls (one undo step).</summary>
	void CurveLines(string action)
	{
		if (Tools.BuiltinTools.ParseCurve(action) is not { } a)
			return;
		var plan = Doc.PlanCurve(a.lines, a.shape, a.count, a.depth, a.reverse, a.asOne);
		if (Operation(a.lines.Count == 1 ? "Curve wall" : $"Curve {a.lines.Count} walls", () => Doc.ApplyCurve(plan)) && plan.NotAsOne > 0)
			DisplayStatus($"Curve walls: done; {plan.NotAsOne} run(s) close or branch: each wall curved on its own");
	}

	/// <summary>A tool's preview (ToolDialog): paths in map coordinates, drawn over the 2D view while it is open.</summary>
	public List<Vector2[]> ToolPreview;

	/// <summary>
	/// A popup's (menu's, dialog's) position for a point of the window (its viewport's coordinates): the screen's when
	/// popups are windows of their own (the editor), the window's when drawn inside it (test pictures, --shot).
	/// </summary>
	public Vector2I PopupPosition(Vector2 at) =>
		GetViewport().GuiEmbedSubwindows ? (Vector2I)at : (Vector2I)at + DisplayServer.WindowGetPosition(GetWindow().GetWindowId());

	/// <summary>The 3D preview is showing (a tool's dialog shows its result there).</summary>
	public bool PreviewShown => _preview.Visible;

	/// <summary>The view showing: the 3D preview or the 2D view.</summary>
	public Control ShownView => _preview.Visible ? _preview : View;

	/// <summary>The 3D preview shows a tool's result (a changed copy of the map; null: the map again). Its selection's
	/// marks are hidden meanwhile (the copy's walls are numbered differently).</summary>
	public void ShowToolResult(MapDocument result)
	{
		if (result == null && _preview.Doc == ShownDoc && View.Doc == ShownDoc)
			return;
		// (the moving sectors shown off or on: the result posed the same way)
		if (result != null && _shown != ShownState.Edit)
		{
			var posed = result.Copy();
			Blood.SectorMotion.Pose(result, posed, _ => _shown == ShownState.On ? 1.0 : 0.0);
			Blood.SectorMotion.AlignSlopes(posed);
			posed.NotifyChanged();
			result = posed;
		}
		_preview.Doc = result ?? ShownDoc;
		_preview.MarksHidden = result != null;
		_preview.MapChanged();
		// the 2D view too (a sprite moved or turned, a floor's picture in the floors view mode)
		View.Doc = result ?? ShownDoc;
		View.MapChanged();
	}

	void DrawToolPreview(MapView2D v)
	{
		if (ToolPreview == null)
			return;
		var col = new Color(1f, 0.75f, 0.2f);
		foreach (var path in ToolPreview)
		{
			var pts = path.Select(p => v.MapToScreen(p)).ToArray();
			if (pts.Length > 1)
				v.DrawPolyline(pts, col, 2f, true);
			foreach (var p in pts)
				v.DrawRect(new Rect2(p - new Vector2(3, 3), new Vector2(6, 6)), col);
		}
	}

	public string DescribeTarget(VisualTarget t) => t.Part switch
	{
		VisualPart.Floor => $"floor of sector {t.Sector}",
		VisualPart.Ceiling => $"ceiling of sector {t.Sector}",
		VisualPart.Upper => $"upper wall {t.Wall}",
		VisualPart.Lower => $"lower wall {t.Wall}",
		VisualPart.Sprite => $"sprite {t.Sprite}",
		VisualPart.Light => $"light {t.Light}",
		_ => $"wall {t.Wall}",
	};

	/// <summary>The picture a target shows: a floor's or ceiling's, a wall's (a lower part with swapped bottoms
	/// the next wall's, as Build draws it; a masked middle its mask), a sprite's.</summary>
	/// <summary>The kind of picture a 3D target shows (the tile picker's tab: wall, masked, floor, ceiling, sky,
	/// sprite).</summary>
	string PictureKind(VisualTarget t) => t.Part switch
	{
		VisualPart.Floor => "floor",
		VisualPart.Ceiling => (Doc.Sectors[t.Sector].CeilingStat & 1) != 0 ? "sky" : "ceiling",
		VisualPart.Sprite => "sprite",
		VisualPart.Middle when Doc.Walls[t.Wall].NextSector >= 0 => "masked",
		_ => "wall",
	};

	int TileOf(VisualTarget t)
	{
		switch (t.Part)
		{
			case VisualPart.Floor: return Doc.Sectors[t.Sector].FloorPicnum;
			case VisualPart.Ceiling: return Doc.Sectors[t.Sector].CeilingPicnum;
			case VisualPart.Sprite: return Doc.Sprites[t.Sprite].Picnum;
		}
		var w = Doc.Walls[t.Wall];
		if (t.Part == VisualPart.Middle && w.NextSector >= 0)
			return w.OverPicnum;
		if (t.Part == VisualPart.Lower && (w.Cstat & 2) != 0 && w.NextWall >= 0 && w.NextWall < Doc.Walls.Count)
			return Doc.Walls[w.NextWall].Picnum;
		return w.Picnum;
	}

	/// <summary>"visualtile <tile> <targets>": each target's picture (where TileOf reads it), one undo step.</summary>
	void VisualTile(string action)
	{
		var parts = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 3 || !int.TryParse(parts[1], out int tile) || tile < 0)
			return;
		var targets = parts.Skip(2).Select(c => Preview3D.Parse(Doc, c)).Where(t => t != null).Select(t => t.Value).ToList();
		if (targets.Count == 0)
			return;
		Undo.CreateUndo(targets.Count == 1 ? "Change picture" : $"Change {targets.Count} pictures");
		foreach (var t in targets)
			SetTile(Doc, t, tile);
		Doc.NotifyChanged();
		UndoChanged();
		DisplayStatus($"tile {tile} on {(targets.Count == 1 ? DescribeTarget(targets[0]) : $"{targets.Count} surfaces")}");
	}

	/// <summary>A target's picture set (where TileOf reads it) in map d.</summary>
	static void SetTile(MapDocument d, VisualTarget t, int tile)
	{
		switch (t.Part)
		{
			case VisualPart.Floor: d.Sectors[t.Sector].FloorPicnum = tile; break;
			case VisualPart.Ceiling: d.Sectors[t.Sector].CeilingPicnum = tile; break;
			// (standing where it stood: its bottom kept, a wall sprite's middle)
			case VisualPart.Sprite: Blood.SpriteExtents.SetPicture(d.Sprites[t.Sprite], tile); break;
			default:
			{
				var w = d.Walls[t.Wall];
				if (t.Part == VisualPart.Middle && w.NextSector >= 0)
					w.OverPicnum = tile;
				else if (t.Part == VisualPart.Lower && (w.Cstat & 2) != 0 && w.NextWall >= 0 && w.NextWall < d.Walls.Count)
					d.Walls[w.NextWall].Picnum = tile;
				else
					w.Picnum = tile;
				break;
			}
		}
	}

	/// <summary>The 2D view's and the 3D preview's selections kept in step (View > Sync Selection).</summary>
	public bool SyncSelection = true;

	/// <summary>The 2D selection's sectors and lines into the preview: a sector its floor and ceiling, a line its
	/// walls' parts there are.</summary>
	void SyncTo3D()
	{
		var t = new List<VisualTarget>();
		foreach (int s in _selection[ElementKind.Sector])
			if (s >= 0 && s < Doc.Sectors.Count)
			{
				t.Add(new VisualTarget(VisualPart.Floor, s, -1));
				t.Add(new VisualTarget(VisualPart.Ceiling, s, -1));
			}
		foreach (int l in _selection[ElementKind.Line])
			if (l >= 0 && l < Doc.Lines.Count)
			{
				t.AddRange(Preview3D.PartsOf(Doc, Doc.Lines[l].Front));
				t.AddRange(Preview3D.PartsOf(Doc, Doc.Lines[l].Back));
			}
		foreach (int i in _selection[ElementKind.Sprite])
			if (i >= 0 && i < Doc.Sprites.Count)
				t.Add(new VisualTarget(VisualPart.Sprite, Doc.Sprites[i].SectNum, -1, i));
		foreach (int i in _selection[ElementKind.Light])
			if (i >= 0 && i < Doc.Lights.Count)
				t.Add(Preview3D.LightTarget(Doc, i));
		_preview.SetSelected(t);
	}

	/// <summary>The preview's selection into the 2D view's: a floor or ceiling its sector, a wall's part its line, a
	/// sprite itself (the points selected stay).</summary>
	void SyncTo2D()
	{
		var sectors = _selection[ElementKind.Sector];
		var lines = _selection[ElementKind.Line];
		var sprites = _selection[ElementKind.Sprite];
		var lights = _selection[ElementKind.Light];
		sectors.Clear();
		lines.Clear();
		sprites.Clear();
		lights.Clear();
		foreach (var t in _preview.Selected)
			if (t.IsLight)
				lights.Add(t.Light);
			else if (t.IsSprite)
				sprites.Add(t.Sprite);
			else if (t.IsPlane)
				sectors.Add(t.Sector);
			else if (t.Wall >= 0 && t.Wall < Doc.LineOf.Length && Doc.LineOf[t.Wall] >= 0)
				lines.Add(Doc.LineOf[t.Wall]);
		SelectionChanged();
	}

	void UpdateShownUi()
	{
		bool On(string a) => a switch
		{
			"showedit" => _shown == ShownState.Edit,
			"showoff" => _shown == ShownState.Off,
			"showon" => _shown == ShownState.On,
			_ => _play != null,
		};
		foreach (var (action, b) in _shownButtons)
		{
			b.SetPressedNoSignal(On(action));
			// (the pressed one in its colour: blue Off, red On, green Play)
			var c = action switch { "showoff" => OffColor, "showon" => OnColor, "playmovers" => PlayColor, _ => Colors.White };
			b.Modulate = On(action) && action != "showedit" ? c : Colors.White;
		}
		foreach (var a in new[] { "showedit", "showoff", "showon", "playmovers" })
			if (_menuItems.TryGetValue(a, out var it))
				it.menu.SetItemChecked(it.menu.GetItemIndex(it.id), On(a));
		// the frame and label round both views: what is shown besides the map as stored
		(string, Color)? banner = _play != null ? ("PLAY: the level running (E uses, P stops)", PlayColor)
			: _shown == ShownState.Off ? ("OFF: the moving sectors at their off places (F3 back to Edit)", OffColor)
			: _shown == ShownState.On ? ("ON: the moving sectors at their on places (F4 back to Edit)", OnColor)
			: null;
		View.Banner = banner;
		View.Mover = s => Blood.SectorMotion.IsMover(View.Doc, s);
		_preview.SetBanner(banner);
		View.QueueRedraw();
	}

	/// <summary>The elements whose links the 2D view draws: the highlighted one and every selected one.</summary>
	IEnumerable<Element> LinkFocus()
	{
		if (Editing.Mode is ClassicMode { Highlighted: { IsNone: false } h })
			yield return h;
		foreach (var (kind, set) in _selection)
			foreach (int i in set)
				yield return new Element(kind, i);
	}

	/// <summary>The sector the 3D preview starts in at map point p where rooms share it: the highlighted sector, else
	/// a selected one, else one of the current layer's (-1: any).</summary>
	int PreviewSector(Vector2 p)
	{
		var here = Doc.SectorsAt(p.X, p.Y);
		if (Editing.Mode is ClassicMode { Highlighted: { Kind: ElementKind.Sector } h } && here.Contains(h.Index))
			return h.Index;
		var sel = SelectionOf(ElementKind.Sector);
		foreach (int s in here)
			if (sel.Contains(s))
				return s;
		foreach (int s in here)
			if (Doc.SectorEditable(s))
				return s;
		return -1;
	}

	/// <summary>Q: the 3D preview from the 2D cursor (or from a map point), or back to 2D.</summary>
	void Toggle3D(Vector2? at = null)
	{
		if (_preview.Visible)
		{
			_preview.Exit();
			// the 2D view centred where the camera flew to (preview space: x, -z / 256, y as game units of 16)
			var cam = _preview.CameraPose.at;
			View.CentreOn(new Vector2(cam.X * 16, cam.Z * 16));
			View.Visible = true;
			View.GrabFocus();
			DisplayStatus(Editing.Mode.Name + " mode");
			return;
		}
		if (Doc.Sectors.Count == 0)
		{
			DisplayStatus("3D preview: the map has no sectors yet (draw one first)");
			return;
		}
		Editing.Mode?.OnCancel();
		View.Visible = false;
		_preview.Doc = ShownDoc;
		var p = at ?? View.Cursor;
		_preview.Enter(p, PreviewSector(p));
		if (SyncSelection)
			SyncTo3D();
		DisplayStatus(Editing.Mode is LightsMode
			? "3D preview, Lights mode (K: back to the mode before): click a bulb selects, hold it to move it (wheel: nearer / farther), wheel raises / lowers, arrows move, right click: its settings, I: place a light, Delete, Ctrl+C / Ctrl+V; Q or Esc: back to 2D"
			: "3D preview: mouse looks, W A S D move (where you look), Shift faster; click selects, wheel raises / lowers, C clears, right click: texture, Ctrl+C / Ctrl+V: copy / paste texture, arrows: move it, keypad: repeats and shade, T: settings, Ctrl+A: align, I: place a sprite, H: slope edges, K: Lights mode, Delete: delete sprites, Space: menu; Q or Esc: back to 2D");
	}

	/// <summary>Actions that work in the 3D preview too (the others are the 2D modes').</summary>
	static readonly HashSet<string> PreviewActions = new()
	{
		"undo", "redo", "savemap", "savemapas", "savemapx", "exportmap", "testmap", "preferences", "mapoptions", "showerrors",
		"toggleinfopanel", "reportproblem", "showedit", "showoff", "showon", "playmovers", "clearselection", "contextmenu", "showinvisible",
		"togglebrightness", "showlights3d", "lightshadows", "generatelights", "showsectoreffects", "showsectoreffects3d", "linkchains",
		"showatmosphere3d", "generateatmosphere",
	};

	bool PreviewKey(InputEventKey k)
	{
		// PrtScr: its release alone stands for the press (Windows)
		if (k.Keycode == Key.Print)
		{
			var press = LonePrint(k);
			if (!k.Pressed && press == null)
				return true;
			k = press ?? k;
		}
		// a held key: only the nudging keys (arrows, keypad) repeat, each repeat merged into the same undo step
		if (k.Echo)
			return PreviewTextureKey(k);
		// Space: the context menu (as tapped in the 2D view)
		if (k.Keycode == Key.Space && !k.CtrlPressed && !k.AltPressed && !k.ShiftPressed)
		{
			Do("contextmenu");
			return true;
		}
		// the arrows, the keypad: panning, repeats, shade
		if (PreviewTextureKey(k))
			return true;
		if (Keys.Match(k, true) is not { } a)
			return false;
		if (KeyMap.PreviewOnly(a))
		{
			Do(a);
			return true;
		}
		// Ctrl+C, Ctrl+V: the picture of what the crosshair is on, onto the selection (else what the crosshair is on)
		if (a == "copyselection")
		{
			// (lights: the lights themselves, as Lights mode's copy)
			if (PreviewLightsCopied())
				return true;
			if (_preview.LightMarkers)
				DisplayStatus("copy: aim at a light (or select some): in Lights mode only the lights are copied");
			else if (_preview.Target is { } t)
				UiDo("visualcopy " + Preview3D.Code(t));
			return true;
		}
		if (a == "pasteselection")
		{
			if (PreviewLightsPasted())
				return true;
			var targets = PreviewTargets(false);
			if (targets.Count > 0)
				PasteTexture(targets);
			return true;
		}
		if (!PreviewActions.Contains(a))
			return false;
		UiDo(a);
		return true;
	}

	/// <summary>The Blood folder changed (preferences): its tiles reloaded.</summary>
	public void UseBloodFolder(string folder)
	{
		Config.Set("blood.folder", folder);
		Config.Save();
		DisplayStatus(ForgeData.Use(folder) ? $"Blood folder: {folder} ({ForgeData.Tiles.Count} tiles)" : ForgeData.Error);
		SyncMapArt();
		_sprites.Refresh();
		_preview.ClearMaterials();
		View.MapChanged();
		_preview.MapChanged();
		_info.ShowElement((Editing.Mode as ClassicMode)?.Highlighted ?? Element.None);
		View.QueueRedraw();
	}

	/// <summary>Preferences: game or Build units; everything showing a position or height again.</summary>
	public void SetUnits(bool game)
	{
		Units.Game = game;
		Config.Set("units", game ? "game" : "build");
		Config.Save();
		UpdateStatusFields();
		_coords.Text = $"{Units.Xy(View.Cursor.X)}, {Units.Xy(View.Cursor.Y)}";
		_info.ShowElement((Editing.Mode as ClassicMode)?.Highlighted ?? Element.None);
		View.QueueRedraw();
	}

	public void ColoursChanged()
	{
		ColorSettings.Save(Config);
		Config.Save();
		View.QueueRedraw();
	}
}
