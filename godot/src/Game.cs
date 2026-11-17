using System.Collections.Generic;
using System.Globalization;
using Chosen.Core;
using Chosen.Map;
using Chosen.Replay;
using Chosen.Sim;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen;

/// <summary>
/// Main node. Command line options (after "--"):
///   --map=e1m2                  the level &lt;name&gt;'s Blood .MAP (game/blood3d/maps; an add-on's in game/blood3d/addons/&lt;id&gt;/maps)
///   --file=&lt;path&gt;               any .map or .mapx on disk
///   --addon=&lt;id&gt;                 with that add-on (a mod's art, sounds, custom dudes) when no add-on lists the level
///   --replay=&lt;path or bug id&gt;   play a DarkPlaces bug recording's player path back as the camera
///   --play=&lt;path or bug id&gt;     play a recording's inputs into the C# game (the world runs on its own)
///   --check=&lt;bug id&gt;            strict check of the C# game against a recording (headless), then quit
///   --inputcheck=&lt;bug id&gt;       input replay of a recording without drawing, report how far the player drifts
///   --speed=N --frame=N         replay speed, recorded frame to stop at
///   --cam=x,y,z,yaw,pitch       camera at a Quake position
///   --shot=&lt;file.png&gt;           save a screenshot (at the end of the replay, or once the level is up) and quit
///   --wait=&lt;seconds&gt;           with --shot: wait this long first (the game runs on)
///   --atshot="cmd; cmd"         with --shot: console commands to run just before it is taken
///   --exec="cmd; cmd"           console commands to run at startup
///   --atend="cmd; cmd"          with --play: console commands to run where the replay stops
///   --report=&lt;text&gt;            with --play: file a bug report where the replay stops, then quit
/// The level is rebuilt when its .map file changes (hot reload). You play the level (Blood3D's controls, see
/// Client.PlayerControls); "fly" frees the camera (mouse, WASD, Space/C, Shift; again: the player drops in there). Escape frees the mouse, F12 saves a
/// screenshot, backtick or F1 opens the console.
/// </summary>
public partial class Game : Node3D
{
	readonly Dictionary<string, string> _args = new();
	readonly GameClock _clock = new();
	readonly World _world = new();
	Render.SpriteRenderer _sprites;
	Render.Hud _hud;
	Render.HdrOutput _hdr;
	Client.SoundSystem _sound;
	Client.MusicPlayer _music;
	Client.Menu _menu;
	World.PlayerCarry _carry;       // the inventory taken to the next level
	double _endTime = -1;           // when the end-of-level screen came up
	bool _endKeysReleased;
	Render.PortalRenderer _portals;
	Render.DynamicLights _lights;
	Render.GroundMist _mist;
	Render.Atmosphere _atmosphere;
	Render.Weather _weather;
	Client.PerfOverlay _perfOverlay;
	long _tickTicks;      // this frame's Blood ticks (Stopwatch ticks), for the performance overlay
	readonly Client.PlayerControls _controls = new();
	Player _player;
	bool _fly;
	float _flyEye = 48;            // the eye over the feet when flying began (fly off puts the feet under the camera)
	bool _paused;               // the pause command (the console also pauses while open)
	InputReplay _play;          // a recording's inputs driving the player
	Recording _playRec;
	BugRecorder _recorder;      // the level as played, for bug reports (reportbug)
	LevelLoader _level;
	Camera3D _cam;
	GhostPlayback _ghost;
	int _stopFrame = -1;
	bool _quitAfterShot;
	string _shotPath;

	static readonly Cvar MonstersOn = Cvars.Register("b3d_monsters", "1", "0: levels start without monsters (testing; nomonsters removes them from the running level)", saved: true);
	static readonly Cvar FlySpeed = Cvars.Register("cl_flyspeed", "400", "fly camera speed (Quake units per second)", saved: true);
	static readonly StringName TickLerpName = "tick_lerp";
	// the camera for the fog shaders (they know none: GroundMist's portal clip)
	static readonly StringName CameraWorldName = "camera_world", EyeName = "b3d_eye";
	static readonly Cvar Fov = Cvars.Register("fov", "90", "horizontal field of view at 4:3 (DarkPlaces' convention, as the game has always had it)", saved: true);

	public override void _Ready()
	{
		UserDataMigration.MigrateFromBlood3D();
		Cvars.LoadConfig();
		Cvars.RegisterAll();
		Render.RenderDriver.Init();
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			var kv = a.TrimPrefix("--").Split('=', 2);
			_args[kv[0]] = kv.Length > 1 ? kv[1] : "";
		}
		// a distribution started before ChosenLoader made its data folder: the loader instead
		if (!Data.TileArt.Present && !OS.HasFeature("editor"))
		{
			string loader = OS.GetExecutablePath().GetBaseDir().PathJoin("ChosenLoader.exe");
			if (System.IO.File.Exists(loader))
				OS.CreateProcess(loader, System.Array.Empty<string>());
			else
				OS.Alert($"Chosen's data folder ({Data.BloodData.Folder}) is missing: run ChosenLoader first.", "Chosen");
			GetTree().Quit();
			return;
		}

		AddChild(new GameConsole());
		GameConsole.Print("Chosen " + Shared.Engine.ChosenVersion.Full);
		if (Data.BloodData.Outdated != "")
			GameConsole.Print($"{Data.BloodData.Folder}: {Data.BloodData.Outdated.ToLowerInvariant()}");
		RegisterCommands();
		// the auto aim option takes effect at once, as in NBlood; recorded so a replay changes it on the same frame
		Client.Menu.AutoAim.Changed += () =>
		{
			if (_world == null || _playRec != null)
				return;
			_world.AutoAim = Mathf.Clamp(Client.Menu.AutoAim.Int, 0, 3);
			_recorder?.Command("setautoaim " + _world.AutoAim);
		};
		// see-through water at once: its links drawn as portals or not, the water flats blended or not
		Render.Enhanced.Init();
		Render.EnhancedFog.Init();
		Render.WaterFx.Reflect.Changed += () => _native?.RefreshWater();
		Render.Enhanced.WaterPortals.Changed += () =>
		{
			if (_world == null || _level?.Board == null)
				return;
			SetupNativeWorld();
			_lights.PortalsChanged();
			_sprites.PortalsChanged();
		};
		// v1.0x weapons take effect at once in single player (NBlood's SetWeaponsV10X)
		Client.Menu.WeaponsV10x.Changed += () =>
		{
			if (_world == null || _playRec != null || _world.GameType != Data.K.kGameTypeSinglePlayer)
				return;
			_world.WeaponsV10x = Client.Menu.WeaponsV10x.Bool;
			_recorder?.Command("setweaponsv10x " + (_world.WeaponsV10x ? 1 : 0));
		};
		Client.Menu.FixBlood.Changed += () =>
		{
			if (_world == null || _playRec != null)
				return;
			_world.FixBlood = Client.Menu.FixBlood.Bool;
			_recorder?.Command("setfixblood " + (_world.FixBlood ? 1 : 0));
		};
		// --addon=<id>: a level no add-on lists is played with that add-on's art, sounds and tables
		if (_args.TryGetValue("addon", out var addonId) && addonId != "")
		{
			Data.Addons.Forced = addonId.ToLowerInvariant();
			if (Data.Addons.ById(Data.Addons.Forced) == null)
				GameConsole.Print($"add-on {addonId}: not installed (ChosenLoader: Add a Mod Folder, or maptool extract --mod)");
		}
		if (_args.TryGetValue("check", out var checkId))
		{
			// headless check of the C# game against a recording (no level drawn), then quit
			GameConsole.Execute("check " + checkId);
			GetTree().Quit();
			return;
		}
		if (_args.TryGetValue("inputcheck", out var icId))
		{
			GameConsole.Execute("inputcheck " + icId);
			GetTree().Quit();
			return;
		}

		_cam = new Camera3D { Near = Client.PlayerView.NearClip };
		AddChild(_cam);
		ApplyFov();

		_level = new LevelLoader();
		AddChild(_level);
		_sprites = new Render.SpriteRenderer { Name = "Sprites" };
		AddChild(_sprites);
		var hudLayer = new CanvasLayer { Layer = 1 };
		AddChild(hudLayer);
		_hud = new Render.Hud();
		hudLayer.AddChild(_hud);
		_sound = new Client.SoundSystem();
		AddChild(_sound);
		AddChild(new Render.Brightness { Name = "Brightness" });
		// (test runs in SDR: their baselines don't depend on the setting)
		Render.HdrOutput.Suspended = _args.ContainsKey("shot") || _args.ContainsKey("views") || _args.ContainsKey("bugs") || _args.ContainsKey("scripted");
		_hdr = new Render.HdrOutput { Name = "Hdr" };
		AddChild(_hdr);
		_music = new Client.MusicPlayer { Name = "Music" };
		AddChild(_music);
		var menuLayer = new CanvasLayer { Layer = 50 };
		AddChild(menuLayer);
		_menu = new Client.Menu { Name = "Menu" };
		menuLayer.AddChild(_menu);
		var movieLayer = new CanvasLayer { Layer = 100 };
		AddChild(movieLayer);
		_movies = new Client.MoviePlayer { Name = "Movies" };
		movieLayer.AddChild(_movies);
		_menu.NewGame += map =>
		{
			_carry = null;
			_playRec = null;
			_play = null;
			_fly = false;
			// levelPlayIntroScene: the episode's cutscene A on entering its CutSceneALevel
			var (e, l) = Data.LevelInfo.Find(map);
			var ep = e >= 0 ? Data.LevelInfo.AllEpisodes[e] : null;
			if (ep != null && ep.CutA != "" && l == ep.CutALevel)
				PlayMovies(new[] { Cutscene(ep.CutA, ep.CutWavA) }, () =>
				{
					LoadMap(MapResPath(map));
					Client.Mouse.Capture();
					_music.PlayLevel(MapName);
				});
			else
				LoadMap(MapResPath(map));
		};
		// leaving the title screen for the level loaded behind it: its music
		_menu.Closed += () => _music.PlayLevel(MapName);
		_menu.Quit += () => GetTree().Quit();
		_portals = new Render.PortalRenderer { Name = "Portals" };
		AddChild(_portals);
		_lights = new Render.DynamicLights { Name = "DynamicLights" };
		AddChild(_lights);
		_mist = new Render.GroundMist { Name = "GroundMist" };
		AddChild(_mist);
		_atmosphere = new Render.Atmosphere { Name = "Atmosphere" };
		AddChild(_atmosphere);
		Render.Atmosphere.Thunder = () => _sound.PlayUi("blood/thunder2.wav");
		GameConsole.Register("lightning", "lightning", "a flash of lightning now", _ => _atmosphere.Bolt());
		GameConsole.Register("boom", "boom [napalm]", "an explosion's aftermath in front of the camera, with napalm a burst's light (debugging)", b =>
		{
			_atmosphere.Boom(_cam, Render.StackedRooms.Sector);
			if (b.Length > 1)
				_lights.AddBurst(Coords.GodotToQuake(_cam.GlobalPosition - _cam.GlobalBasis.Z * 5f));
		});
		GameConsole.Register("fires", "fires", "where the level's fires are (embers and smoke start there; debugging)", _ => _atmosphere.ListFires());
		GameConsole.Register("sndrec", "sndrec <sound file> <out prefix>", "a sound played crisp and smooth, the output recorded into <prefix>_crisp.wav and _smooth.wav (testing)",
			a => { if (a.Length >= 3) _sound.Record(a[1], a[2]); else GameConsole.Print("sndrec <sound file, as blood/dooropen.wav> <out prefix>"); });
		GameConsole.Register("moonstat", "moonstat", "the light from the sky where the camera is (debugging)", _ => GameConsole.Print(_atmosphere.MoonStatus(Render.StackedRooms.Sector)));
		GameConsole.Register("r_compare", "r_compare", "all enhanced options off for now, or back as they were (F10; never saved off)", _ =>
		{
			Render.Atmosphere.ToggleComparison();
			string msg = Render.Atmosphere.ComparingOff ? "Enhanced options off" : "Enhanced options on";
			if (_player != null)
				_world.ViewSetMessage(_player, msg);
			else
				GameConsole.Print(msg);
		});
		GameConsole.Register("r_fxall", "r_fxall 0|1", "all of Options > Enhanced's effects off or on", a => Render.Atmosphere.SetAll(a.Length > 1 && a[1] != "0"));
		_weather = new Render.Weather { Name = "Weather" };
		AddChild(_weather);
		_perfOverlay = new Client.PerfOverlay { Name = "PerfOverlay" };
		AddChild(_perfOverlay);
		// a (re)built level respawns the world's sprites from its entities (also on hot reload)
		_level.LevelBuilt += StartLevel;

		// no level, replay or test run asked for: the title screen, with no level behind it until one is picked
		bool title = !_args.ContainsKey("map") && !_args.ContainsKey("file") && !_args.ContainsKey("shot")
			&& !_args.ContainsKey("views") && !_args.ContainsKey("bugs");
		if (_args.ContainsKey("replay"))
			StartReplay(_args["replay"], F(Arg("speed", "1")), int.Parse(Arg("frame", "-1")));
		else if (_args.ContainsKey("play"))
			StartPlay(_args["play"], F(Arg("speed", "1")), int.Parse(Arg("frame", "-1")));
		else if (!title)
			LoadMap(_args.ContainsKey("file") ? _args["file"] : MapResPath(Arg("map", "e1m2")));
		else
			Data.Tiles.Load();      // the menu's pictures and fonts

		if (_args.TryGetValue("cam", out var cam))
			PlaceCamera(cam.Split(','));
		else if (_ghost == null)
			PlaceCamera("-938.9,-2699.5,312.8,-104.9,0.6".Split(','));

		if (_args.TryGetValue("exec", out var exec))
			GameConsole.Execute(exec);

		if (_args.TryGetValue("views", out var views))
			ViewBatch(views, _args.TryGetValue("viewshots", out var dir) ? dir : "user://views");
		else if (_args.TryGetValue("bugs", out var bugList))
			BugBatch(bugList, _args.TryGetValue("bugshots", out var bugDir) ? bugDir : "user://bugshots");
		else if (_args.ContainsKey("shot"))
		{
			_shotPath = _args["shot"];
			_quitAfterShot = true;
			if (_ghost == null && _playRec == null)
				SaveShotSoon();       // a replay takes it where it stops
		}
		else if (_args.ContainsKey("scripted"))
		{
			// a test run with no shot of its own (bugs.py motion): the mouse stays the user's
		}
		else if (_ghost == null && _playRec == null && title && _level.MapPath == "")
		{
			// credLogosDos: Monolith's and GT Interactive's logos, then the title screen with thunder
			if (SkipIntro.Bool)
				ShowTitle(false);
			else
				PlayMovies(new[] { Original("logo", "logo"), Original("gti", "gti") }, () => ShowTitle(true));
		}
		else
		{
			Client.Mouse.Capture();
			_music.PlayLevel(MapName);
		}
	}

	// ---------------------------------------------------------------- intro and cutscenes (credits.cpp, levels.cpp)
	static readonly Cvar CutsceneVersion = Cvars.Register("b3d_cutscenes", "1",
		"the episode cutscenes: 0 the original Smacker videos (as NBlood), 1 Refreshed Supply's remastered ones", saved: true);
	static readonly Cvar Subtitles = Cvars.Register("b3d_subtitles", "1",
		"1: the cutscenes' subtitles (Refreshed Supply's, English)", saved: true);
	static readonly Cvar SkipIntro = Cvars.Register("b3d_skipintro", "1",
		"1: start on the title screen without the Monolith and GT Interactive logos", saved: true);
	Client.MoviePlayer _movies;

	static string MovieFolder => Data.BloodData.Folder.PathJoin("movie");

	/// <summary>A file of the movie folder, the active add-on's own first (Data.Addons).</summary>
	static string MovieFile(string rel) => Data.BloodData.Find("movie/" + rel);

	/// <summary>A DOS Smacker video with its sound (movie/&lt;name&gt;.smk, movie/&lt;wav&gt;.wav).</summary>
	static Client.MoviePlayer.Movie Original(string name, string wav) =>
		new(MovieFile(name + ".smk"), wav != "" ? MovieFile(wav + ".wav") : null);

	/// <summary>An episode cutscene in the chosen version (the other when that one is missing: an add-on's come only
	/// remastered).</summary>
	static Client.MoviePlayer.Movie Cutscene(string name, string wav)
	{
		string remastered = MovieFile("remastered/" + name + ".ogv");
		string srt = MovieFile("remastered/" + name + ".srt");
		srt = Subtitles.Bool && System.IO.File.Exists(srt) ? srt : null;
		if ((CutsceneVersion.Bool || !System.IO.File.Exists(MovieFile(name + ".smk"))) && System.IO.File.Exists(remastered))
			return new(remastered, null, srt);
		var o = Original(name, wav);
		return new(o.Video, o.Sound, srt);
	}

	/// <summary>Plays videos over everything (the game paused, the music stopped), then calls done.</summary>
	void PlayMovies(IEnumerable<Client.MoviePlayer.Movie> movies, System.Action done)
	{
		_music.Play(0);
		_sound.StopAll();
		_movies.Play(movies, done);
	}

	/// <summary>The title screen's main menu (after the intro: Blood's thunder over it).</summary>
	void ShowTitle(bool thunder)
	{
		_menu.Open(title: true);
		_music.PlayMenu();
		if (thunder)
			_sound.PlayUi("blood/thunder2.wav");
	}

	string Arg(string key, string fallback) => _args.TryGetValue(key, out var v) && v != "" ? v : fallback;
	static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
	static string MapResPath(string name) => Data.Addons.MapRes(name);

	/// <summary>The room-over-room portals, and water surfaces drawn see-through (Enhanced.WaterPortals: their links
	/// drawn as portals too).</summary>
	Render.Native.NativeWorld _native;

	/// <summary>A level from its Blood .MAP: the native world drawn from its Build data (the converted level's renderers
	/// left empty), its light effects and panning from the game's sectors.</summary>
	void SetupNativeWorld()
	{
		if (_native == null)
		{
			_native = new Render.Native.NativeWorld { Name = "NativeWorld" };
			AddChild(_native);
			// a gib wall broken (or whole again on a restart): its sectors built again
			_world.WallVisibilityChanged += w => _native.WallChanged(w.BWall);
		}
		_native.Visible = true;
		// the links drawn as portals: Build's (tile 504), and with see-through water (Enhanced.WaterPortals) the water
		// links, their surfaces blended over the far room
		bool water = Render.Enhanced.WaterPortals.Bool;
		foreach (var l in _world.Collision.Links)
			l.Portal = l.ConvertedPortal || water && Render.Enhanced.IsWaterLink(l.Type);
		bool Side(int s, bool ceiling, bool waterSide)
		{
			foreach (var l in _world.Collision.Links)
				if (l.Portal && l.ConvertedPortal != waterSide && l.Sector == s && l.Dir == (ceiling ? -1 : 1))
					return true;
			return false;
		}
		_native.IsPortal = (s, ceiling) => Side(s, ceiling, false);
		_native.IsPortalInPlace = (s, ceiling) =>
		{
			foreach (var l in _world.Collision.Links)
				if (l.Portal && l.ConvertedPortal && l.Sector == s && l.Dir == (ceiling ? -1 : 1))
					return l.Offset.LengthSquared() < 0.25f;
			return false;
		};
		_native.IsWater = (s, ceiling) => Side(s, ceiling, true);
		// wall stacks' windows and mirrors (PortalRenderer.WallPortals)
		var wallPortals = Render.PortalRenderer.WallPortalWalls(_level.Board);
		_native.IsWallPortal = wallPortals.Contains;
		_native.SharesGround = (a, b) =>
		{
			var overlaps = _world.Map.OverlapsOf(a);
			for (int i = 0; i < overlaps.Count; i++)
				if (overlaps[i] == b)
					return true;
			return false;
		};
		// rooms sharing a place told apart for the dynamic lights (a light in one keeps out of the others)
		Render.DynamicLights.SetRooms(_world.Map);
		_native.RoomColour = Render.DynamicLights.RoomColourOf;
		_native.SectorShadeFx = _world.SectorShadeFxNow;
		_native.SectorPan = _world.SectorPanNow;
		_native.WallPan = _world.WallPanNow;
		_native.SkyFlags = _world.NativeSkyFlags();
		_native.FollowWater(_world.Collision.Links, _level.Board);
		Render.WaterFx.SetLevel(_world.Collision.Links, _level.Board);
		_native.Setup(_level.Board);
		Render.StackedRooms.Setup(_level.Shade, _world.Collision.Links);
		_portals.SetupNative(_world.Collision.Links, _level.Board, _native, s => _world.XSectorOf(s)?.SUnderwater is > 0);
	}

	/// <summary>A world without drawing (inputcheck, check) on a level's Build data; false if it has no Blood map.</summary>
	static bool LoadHeadless(World world, string map)
	{
		string name = System.IO.Path.GetFileNameWithoutExtension(map);
		Data.Addons.Activate(Data.Addons.ForMap(name));
		string bp = Data.Addons.BuildMapPath(name);
		if (bp == null)
		{
			GameConsole.Print($"{name}: no Blood map (pipeline/export_maps.py)");
			return false;
		}
		var board = new Sim.Build.Board(BloodMapFile.Load(bp));
		world.LoadNative(board, name.ToLowerInvariant(), Sim.SectorMap.FromBoard(board));
		return true;
	}

	string MapName => _level?.MapPath != null ? System.IO.Path.GetFileNameWithoutExtension(_level.MapPath).ToLowerInvariant() : "";

	/// <summary>The send console command on a world (the game's, or inputcheck's headless one: walk tests opening a door
	/// first).</summary>
	static void Send(World world, Player player, string[] a)
	{
		if (world == null || a.Length < 2)
			return;
		int cmd = a.Length < 3 ? Data.K.kCmdToggle : a[2] switch
		{
			"off" => Data.K.kCmdOff,
			"on" => Data.K.kCmdOn,
			"toggle" => Data.K.kCmdToggle,
			_ => int.TryParse(a[2], out int c) ? c : Data.K.kCmdToggle,
		};
		if (a[1].StartsWith('s') && int.TryParse(a[1][1..], out int bs))
		{
			int n = 0;
			foreach (var sec in world.Sectors)
				if (sec.BSector == bs + 1 && sec.IsMover)
				{
					world.TrTriggerSector(sec, cmd, player);      // every part (a lift's floor and ceiling)
					n++;
				}
			GameConsole.Print(n > 0 ? $"sent {cmd} to sector {bs}" : $"sector {bs} has no mover");
			return;
		}
		if (!int.TryParse(a[1], out int rx))
			return;
		world.EvSend(null, rx, cmd, player);
		GameConsole.Print($"sent {cmd} on channel {rx}");
	}

	/// <summary>(Re)starts the level's game: sprites, sectors, the player at its start (also on hot reload).</summary>
	/// <summary>Not 0: every level starts with this random seed (bug regression shots, so they repeat).</summary>
	static long FixedSeed;

	void StartLevel()
	{
		long seed = _playRec != null ? _playRec.Seed : FixedSeed != 0 ? FixedSeed : 1 + (long)(GD.Randi() % 16000000);
		_world.Seed(seed);
		if (_playRec == null)
		{
			_world.Skill = Mathf.Clamp(Client.Menu.Skill.Int, 0, 4);
			_world.Monsters = MonstersOn.Bool;
			_world.AutoAim = Mathf.Clamp(Client.Menu.AutoAim.Int, 0, 3);
			_world.WeaponsV10x = Client.Menu.WeaponsV10x.Bool;
			_world.FixBlood = Client.Menu.FixBlood.Bool;
		}
		if (_playRec != null)
			ApplyRecordingSettings(_world, _playRec);
		// the level's Build data as stored (a restart: the last game changed it)
		_level.FreshBoard();
		_world.LoadNative(_level.Board, MapName, _level.Shade);
		_player = _world.AddPlayer();
		if (_playRec != null)
			_carry = BugRecorder.CarryFromJson(_playRec.Carry);
		if (_carry != null && !World.ApplyCarry(_player, _carry))
			_carry = null;
		_recorder = new BugRecorder(System.IO.Path.GetFileNameWithoutExtension(_level.MapPath), seed, _world, BugRecorder.CarryToJson(_carry));
		_carry = null;
		_endTime = -1;
		_controls.Sync(_player);
		_controls.Pitch = 0;
		_sprites.Setup(_world, _level.Shade);
		_hud?.Setup(_world);
		_sound?.Setup(_world);
		if (_menu != null && !_menu.Title)
			_music.PlayLevel(MapName);
		_sprites.ViewSprite = _ghost == null && !_fly ? _player : null;
		SetupNativeWorld();
		// (only looked at: drawing never moves the game's sectors on)
		Render.StackedRooms.SpriteSectorOf = s => _world.PeekSpriteBSector(s, false);
		_lights.Setup(_world);
		// the atmosphere: the map's own atmospheres (a .MAPX's ATMO), else the game's guesses from the Build data (weather
		// by episode, mist over the open sectors); each area's mist, the weather and wind by place, the fog by the camera's
		Render.AreaAtmosphere.Setup(_world, MapName);
		var mistAreas = new List<(List<int>, float, float, Color?)>();
		foreach (var (secs, a) in Render.AreaAtmosphere.MistAreas(_world))
			mistAreas.Add((secs, a.MistHeight / 256f, a.MistDensity, Render.AreaAtmosphere.MistColourOf(a)));
		_mist.SetupNative(_level.Shade, mistAreas, _world.NativeWaterGroups(), Render.AreaAtmosphere.FogAreas(_world));
		_weather.Setup(_world);
		_atmosphere.Setup(_world, Render.AreaAtmosphere.AnyRain, Render.AreaAtmosphere.AnySky);
		GameConsole.Print($"{_world.Sectors.FindAll(s => s.FxAmp != 0).Count} light sectors");
		if (_playRec != null)
			_play = new InputReplay(_playRec) { OnCommand = c => _recorder?.Command(c) };
		else
			_clock.Reset();
		GameConsole.Print($"{_world.Sprites.Count} sprites, {_world.Sectors.Count} sectors, {_world.Walls.Count} walls");
		// the last level's materials and pictures let go now, not whenever the collector next runs: they hold the graphics
		// driver's bindings, and level after level it ran out of them (D3D12's descriptor heap: what came next wasn't drawn)
		ulong freeFrom = Time.GetTicksMsec();
		System.GC.Collect();
		System.GC.WaitForPendingFinalizers();
		GameConsole.Print($"last level freed in {Time.GetTicksMsec() - freeFrom} ms");
	}

	static void ApplyRecordingSettings(World w, Recording rec)
	{
		if (rec.Cvars.TryGetValue("b3d_skill", out var sk)) w.Skill = int.Parse(sk);
		if (rec.Cvars.TryGetValue("b3d_monsters", out var mo)) w.Monsters = mo != "0";
		// recordings from before the option: always (Blood's default)
		w.AutoAim = rec.Cvars.TryGetValue("cl_autoaim", out var aa) ? int.Parse(aa) : 1;
		// recordings from before the option: off (the 1.21 weapons)
		w.WeaponsV10x = rec.Cvars.TryGetValue("b3d_weaponsv10x", out var wv) && wv != "0";
		// recordings from before the option: off (Blood as it was)
		w.FixBlood = rec.Cvars.TryGetValue("b3d_fixblood", out var fb) && fb != "0";
	}

	/// <summary>A recording's inputs drive the player from the level start.</summary>
	void StartPlay(string id, float speed, int stopFrame)
	{
		_playRec = Recording.Load(RecordingPath(id));
		_ghost = null;
		_fly = false;
		_clock.Reset();
		_clock.Speed = speed;
		_stopFrame = stopFrame;
		GameConsole.Print($"play {id}: {_playRec.Frames.Count} frames on {_playRec.Map}");
		_level.Load(MapResPath(_playRec.Map));
	}

	void ApplyFov()
	{
		// Blood3D's fov is horizontal at 4:3 (DarkPlaces' convention); Godot's is vertical, the same at any aspect
		float h = Mathf.DegToRad(Mathf.Clamp(Fov.Float, 10f, 170f));
		_cam.Fov = _fovBase = Mathf.RadToDeg(2f * Mathf.Atan(Mathf.Tan(h / 2f) * 0.75f));
	}
	float _fovBase = 75f;

	static readonly Cvar HorizonCvar = Cvars.Register("r_horizcenter", "0",
		"1: the horizon at the screen's middle; 0: a little above it, as Blood draws it (NBlood's default)", saved: true);
	float _projNear = -1, _projFov = -1, _projShift = -1;
	// shotburst (debugging): drawn frames still to save, and the label of the one drawn last
	int _burstLeft, _burstNo;
	string _burstPrefix, _burstLabel;
	bool _burstQuit;
	int _burstFrom;

	/// <summary>shotburst: saves the frame drawn last (the viewport's image now is the previous draw's), labelled with
	/// the game frame and blend it was drawn at.</summary>
	// motionshots: the steps left, each a blend between the game frames drawn the normal way and exact
	string _motionPrefix;
	float[] _motionLerps;
	int _motionStep, _motionWait;
	int _motionUntil;
	string _motionBlend;
	string _motionCull = "1";
	bool _motionQuit;

	/// <summary>The rendering steps between two game frames (bugs.py motion): the game held at each blend, the view saved
	/// as the renderer draws it (prefix_i_blend.png), all of those first, on the meshes the game itself built; then at each
	/// with every moving sector built exactly for that blend (prefix_i_exact.png). A few frames are drawn at each before
	/// the shot (a change of mesh shows on the next), more at the first (a room's turn reaches the camera over several).</summary>
	void MotionShots()
	{
		if (_motionPrefix == null)
			return;
		Render.Hud.HideMessages = true;      // (a message fading by the clock is no part of what is held)
		// (counted in game frames: the same moment in every run)
		if ((_world?.Frame ?? 0) < _motionUntil)
			return;
		if (_motionWait-- > 0)
			return;
		if (_motionStep >= 0)
		{
			int n = _motionLerps.Length;
			string path = $"{_motionPrefix}_{_motionStep % n}_{(_motionStep < n ? "blend" : "exact")}.png";
			Render.HdrOutput.Picture(GetViewport()).SavePng(path);
			GD.Print($"motionshot {path} game frame {_world?.Frame ?? 0} blend {_clock.Lerp:0.000}");
		}
		_motionStep++;
		if (_motionStep >= _motionLerps.Length * 2)
		{
			_motionPrefix = null;
			_clock.Hold = -1;
			Render.Native.NativeWorld.MeshBlend.Set(_motionBlend);
			Render.SectorVisibility.CullCvar.Set(_motionCull);
			if (_motionQuit)
				GetTree().Quit();
			return;
		}
		_clock.Hold = _motionLerps[_motionStep % _motionLerps.Length];
		Render.Native.NativeWorld.MeshBlend.Set(_motionStep < _motionLerps.Length ? _motionBlend : "0");
		// (the exact picture culled by nothing: what the walk cuts from a blend, E3M5's door, shows against it)
		Render.SectorVisibility.CullCvar.Set(_motionStep < _motionLerps.Length ? _motionCull : "0");
		_motionWait = _motionStep == 0 ? 30 : 3;
	}

	void BurstShot()
	{
		if (_burstLeft <= 0 || (_world?.Frame ?? 0) < _burstFrom)
			return;
		if (_burstLabel != null)
		{
			string path = $"{_burstPrefix}_{_burstNo:000}.png";
			Render.HdrOutput.Picture(GetViewport()).SavePng(path);
			GD.Print($"burst {_burstNo:000} {_burstLabel}");
			_burstNo++;
			if (--_burstLeft == 0)
			{
				if (_burstQuit)
					GetTree().Quit();
				return;
			}
		}
		// (with the sector the view was walked from and the player's: a frame drawn from the wrong room)
		_burstLabel = string.Format(CultureInfo.InvariantCulture, "frame {0} lerp {1:0.000} sector {2} player {3}", _world?.Frame ?? 0,
			_clock.Lerp, Render.StackedRooms.Sector, _player != null && _world != null ? _world.PeekSpriteBSector(_player, false) : -1);
	}

	/// <summary>Blood draws the view's horizon 10 of its 200 rows above the middle (its horiz 90; NBlood's
	/// r_horizcenter 0), which NBlood's polymost shows as a level look tipped atan(10 / 160) down (measured over the
	/// comparison views). The camera's frustum is shifted down that much instead (a share of the view's height); it
	/// still turns to look up and down as before. Set each frame, as the near plane moves (PlayerView).</summary>
	void ApplyHorizon()
	{
		float shift = HorizonCvar.Bool ? 0f : 10f / 160f / (2f * Mathf.Tan(Mathf.DegToRad(_cam.Fov) / 2f));
		if (shift == 0f)
		{
			if (_cam.Projection != Camera3D.ProjectionType.Perspective)
				_cam.Projection = Camera3D.ProjectionType.Perspective;
			_projShift = 0;
			return;
		}
		if (_cam.Projection == Camera3D.ProjectionType.Frustum && _cam.Near == _projNear && _cam.Fov == _projFov && shift == _projShift)
			return;
		(_projNear, _projFov, _projShift) = (_cam.Near, _cam.Fov, shift);
		// the frustum's height at the near plane (kept as the fov's: the size is the height, as Fov is)
		float size = 2f * _cam.Near * Mathf.Tan(Mathf.DegToRad(_cam.Fov) / 2f);
		_cam.SetFrustum(size, new Vector2(0, -shift * size), _cam.Near, _cam.Far);
	}

	/// <summary>Half the camera's angle of view across the screen (degrees).</summary>
	float HalfFovAcross()
	{
		var size = GetViewport().GetVisibleRect().Size;
		float aspect = size.Y > 0 ? size.X / size.Y : 16f / 9f;
		return Mathf.RadToDeg(Mathf.Atan(Mathf.Tan(Mathf.DegToRad(_cam.Fov) / 2f) * aspect));
	}

	void LoadMap(string path)
	{
		_ghost = null;
		_level.Load(path);
	}

	void StartReplay(string id, float speed, int stopFrame)
	{
		var rec = Recording.Load(RecordingPath(id));
		_level.Load(MapResPath(rec.Map));
		_ghost = new GhostPlayback(rec);
		_clock.Reset();
		_clock.Speed = speed;
		_stopFrame = stopFrame;
		GameConsole.Print($"replay {id}: {rec.Frames.Count} frames on {rec.Map}");
	}

	/// <summary>A path, or a bug id: bugs/&lt;id&gt;/recording.rec in the repository.</summary>
	static string RecordingPath(string s)
	{
		if (s.EndsWith(".rec"))
			return s;
		return ProjectSettings.GlobalizePath("res://").PathJoin("../bugs").PathJoin(s).PathJoin("recording.rec");
	}

	void PlaceCamera(string[] v)
	{
		var pos = Coords.QuakeToGodot(new Vector3(F(v[0]), F(v[1]), F(v[2])));
		_cam.GlobalPosition = pos;
		float yaw = v.Length > 3 ? F(v[3]) : 0f, pitch = v.Length > 4 ? F(v[4]) : 0f;
		_cam.LookAt(pos + Coords.QuakeAnglesToGodotDir(pitch, yaw), Vector3.Up);
	}

	/// <summary>A level loaded from the console: the menu (the title screen's too) closes on it.</summary>
	void CloseMenuForLevel()
	{
		if (_menu != null && _menu.IsOpen && _level?.MapPath != "")
			_menu.Close();
	}

	void RegisterCommands()
	{
		GameConsole.Register("map", "map <name or file> [skill]", "load the level <name> (its Blood .MAP: game/blood3d/maps, an add-on's addons/<id>/maps), or a .map / .mapx file; skill 0 Still Kicking .. 4 Extra Crispy (b3d_skill; default: the current one)", a =>
		{
			if (a.Length > 2 && int.TryParse(a[2], out int skill))
				Client.Menu.Skill.Set(Mathf.Clamp(skill, 0, 4).ToString());
			LoadMap(System.IO.File.Exists(a[1]) ? a[1] : MapResPath(a[1]));
			CloseMenuForLevel();
		});
		GameConsole.Register("reload", "reload", "rebuild the level from its .map (.mapx)", _ => { if (_level.Source != "") _level.Load(_level.Source); });
		GameConsole.Register("replay", "replay <bug id or .rec> [speed] [frame]",
			"play a DarkPlaces bug recording's player path back as the camera",
			a => StartReplay(a[1], a.Length > 2 ? F(a[2]) : 1f, a.Length > 3 ? int.Parse(a[3]) : -1));
		GameConsole.Register("stop", "stop", "stop the replay (the camera flies from there)", _ => { _ghost = null; _fly = true; });
		GameConsole.Register("fly", "fly", "free the camera from the player (again: the player drops in where the camera is)", _ =>
		{
			bool playing = _player != null && _ghost == null && _playRec == null && _player.XHealth > 0;
			if (!_fly && playing)
			{
				// the eye's height over the feet, to put the feet back under the camera when flying stops
				_flyEye = Coords.GodotToQuake(_cam.GlobalPosition).Z - (_player.Origin.Z + _player.Mins.Z);
			}
			else if (_fly && playing)
			{
				var q = Coords.GodotToQuake(_cam.GlobalPosition);
				var d = -_cam.GlobalBasis.Z;
				float yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)), pitch = -Mathf.RadToDeg(Mathf.Asin(d.Y));
				GameConsole.Execute(string.Format(CultureInfo.InvariantCulture, "warp {0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##}",
					q.X, q.Y, q.Z - _flyEye, yaw, pitch));
			}
			_fly = !_fly;
			if (_sprites != null)
				_sprites.ViewSprite = _fly ? null : _player;
			GameConsole.Print(_fly ? "fly on" : "fly off");
		});
		// cheats (World.CheatCommand); their messages show in the console
		foreach (var (name, usage, help) in new[]
		{
			("give", "give [all|weapons|ammo|armor|keys|items]", "give yourself things"),
			("god", "god", "toggle god mode"), ("mpkfa", "mpkfa", "god mode, all weapons, armor and keys"),
			("testkit", "testkit", "god mode and every weapon with full ammo (no keys)"),
			("noclip", "noclip", "toggle walking through walls"), ("kill", "kill", "die"),
			("nomonsters", "nomonsters", "remove every monster"), ("trigger", "trigger <channel>", "toggle a channel, as a switch would"),
			("idaho", "idaho", "all weapons"), ("griswold", "griswold", "full armor"), ("montana", "montana", "everything"),
			("lara", "lara", "all weapons"), ("cousteau", "cousteau", "toggle walking through walls"), ("kevorkian", "kevorkian", "die"),
		})
			GameConsole.Register(name, usage, help, RunCheat);
		GameConsole.Register("warp", "warp <x> <y> <z> [yaw] [pitch] [sector]", "move the player (Quake units, z: the feet; sector: the Build sector, where rooms share space)", a =>
		{
			RunCheat(a);
			if (a.Length > 5)
				_controls.Pitch = F(a[5]);
			_controls.Sync(_player);
		});
		// DarkPlaces' cheats were server commands ("cmd mpkfa"): the same line works here
		GameConsole.Register("cmd", "cmd <command>", "run a command (DarkPlaces' cmd prefix for server commands and cheats)", a =>
		{
			if (a.Length > 1)
				GameConsole.Execute(string.Join(' ', a[1..]));
		});
		GameConsole.Register("sprinfo", "sprinfo <picnum>", "the collision state of every sprite using a tile (debugging)", a =>
		{
			int pic = int.Parse(a[1]);
			foreach (var s in _world.Sprites)
				if (!s.Removed && s.Picnum == pic)
					GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "{0} edict {1} org {2} cstat {3} solid {4} mins {5} maxs {6} boxyaw {7} ang {8}",
						s, s.Edict, s.Origin, s.Cstat, s.Solid, s.Mins, s.Maxs, s.BoxYaw, s.Ang));
		});
		GameConsole.Register("spawndude", "spawndude <type> [distance] [height]", "spawn a monster (Blood dude type, e.g. 206 a flesh gargoyle) in front of the player, distance and height in Quake units (debugging)", a =>
		{
			if (_world == null || _player == null || a.Length < 2)
				return;
			double dist = a.Length > 2 ? F(a[2]) : 64, up = a.Length > 3 ? F(a[3]) : 0;
			var e = _world.ActSpawnDude(_player, int.Parse(a[1]), dist * 16, -up * Blood.ZScale);
			GameConsole.Print($"spawned {e} at {e.Origin}");
		});
		GameConsole.Register("sprnear", "sprnear <x> <y> <r>", "every sprite within r of x y: type, tile, status, box, health, state and data (debugging)", a =>
		{
			var c = new Vector2(F(a[1]), F(a[2]));
			float r = F(a[3]);
			foreach (var s in _world.Sprites)
				if (!s.Removed && new Vector2(s.Origin.X, s.Origin.Y).DistanceTo(c) <= r)
					GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "{0} edict {1} type {2} pic {3} stat {4} org {5} solid {6} mins {7} maxs {8} health {9} state {10} data {11} {12}",
						s, s.Edict, s.Type, s.Picnum, s.StatNum, s.Origin, s.Solid, s.Mins, s.Maxs, s.XHealth, s.XState, s.Data1, s.Data2));
		});
		GameConsole.Register("reportbug", "reportbug <what is wrong>",
			"save a bug report: screenshot, where you are and what you aim at, and the level's input recording", a =>
			{
				if (a.Length < 2)
					GameConsole.Print("reportbug <what is wrong>");
				else
					_ = ReportBug(string.Join(' ', a[1..]));
			});
		GameConsole.Register("gotobug", "gotobug <id>", "go to where a bug was reported (on its map)", a => GotoBug(a[1]));
		_world.MessageShown += GameConsole.Print;
		GameConsole.Register("play", "play <bug id or .rec> [speed] [frame]",
			"play a recording's inputs into the C# game from the level start",
			a => StartPlay(a[1], a.Length > 2 ? F(a[2]) : 1f, a.Length > 3 ? int.Parse(a[3]) : -1));
		GameConsole.Register("inputcheck", "inputcheck <bug id or .rec> [from-to|slow]",
			"run a recording's inputs through the C# game without drawing; report how far the player drifts", a =>
			{
				var rec = Recording.Load(RecordingPath(a[1]));
				var world = new World();
				ApplyRecordingSettings(world, rec);
				world.Seed(rec.Seed);
				if (!LoadHeadless(world, rec.Map))
					return;
				var p = world.AddPlayer();
				World.ApplyCarry(p, BugRecorder.CarryFromJson(rec.Carry));
				var ir = new InputReplay(rec);
				if (a.Length > 2 && a[2].Contains('-'))
				{
					var r = a[2].Split('-');
					ir.TraceFrom = int.Parse(r[0]);
					ir.TraceTo = int.Parse(r[1]);
				}
				ulong t = Time.GetTicksMsec();
				var sw = new System.Diagnostics.Stopwatch();
				double slowest = 0;
				while (!ir.Finished)
				{
					ir.Prepare(world, p, c => { var w = c.Split(' ', System.StringSplitOptions.RemoveEmptyEntries); if (w.Length > 0 && w[0] == "send") Send(world, p, w); });
					int f = ir.FrameNumber;
					sw.Restart();
					world.Tick();
					double ms = sw.Elapsed.TotalMilliseconds;
					slowest = System.Math.Max(slowest, ms);
					if (ms > 8 && a.Length > 2 && a[2] == "slow")
						GameConsole.Print($"frame {f}: {ms:0.0} ms, {world.Sprites.Count} sprites");
					ir.Finish(p);
					if (f + 1 >= ir.TraceFrom && f + 1 <= ir.TraceTo)
						GameConsole.Print($"sector {f + 1}: {world.SpriteBSector(p)}");
				}
				GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "end: origin {0:0.##} {1:0.##} {2:0.##} sector {3} health {4} medium {5}",
					p.Origin.X, p.Origin.Y, p.Origin.Z, world.SpriteBSector(p), System.Math.Floor(p.XHealth / 16), p.Medium));
				GameConsole.Print($"slowest game frame {slowest:0.0} ms");
				GameConsole.Print($"{a[1]} on {rec.Map}: {Time.GetTicksMsec() - t} ms");
				GameConsole.Print(ir.Report());
			});
		GameConsole.Register("restart", "restart", "restart the level", _ => StartLevel());
		GameConsole.Register("pause", "pause", "pause or resume the game", _ =>
		{
			_paused = !_paused;
			GameConsole.Print(_paused ? "paused" : "resumed");
		});
		GameConsole.Register("lerphold", "lerphold [0..1|off]", "stop the game and draw everything that far between the last two game frames (a replay's --atend=\"lerphold 0.5\" holds the frame it stopped on); off resumes", a =>
		{
			if (a.Length > 1)
				_clock.Hold = a[1] == "off" ? -1 : Mathf.Clamp(F(a[1]), 0f, 1f);
			GameConsole.Print(_clock.Hold < 0 ? "lerphold off" : string.Format(CultureInfo.InvariantCulture, "lerphold {0:0.###} at game frame {1}", _clock.Hold, _world?.Frame ?? 0));
		});
		GameConsole.Register("driver", "driver [restart]", "the graphics API running and chosen (r_driver); restart: start again on the chosen one", a =>
		{
			if (a.Length > 1 && a[1] == "restart")
				Render.RenderDriver.Restart();
			GameConsole.Print($"driver: running {RenderingServer.GetCurrentRenderingDriverName()}, r_driver {Render.RenderDriver.Choice.Value}");
		});
		GameConsole.Register("hdr", "hdr [on|2d]", "HDR output's state; on: this run too if it is a test run; 2d: the 2D drawn in HDR with no HDR asked for (testing the picture)", a =>
		{
			if (a.Length > 1 && a[1] == "on")
				Render.HdrOutput.Suspended = false;
			if (a.Length > 1 && a[1] == "2d")
				GetWindow().UseHdr2D = true;
			_hdr.Refresh();
			GameConsole.Print(_hdr.Describe());
			if (a.Length > 1 && a[1] == "stats")
				GameConsole.Print(Render.HdrOutput.Stats(GetViewport()));
		});
		GameConsole.Register("version", "version", "Chosen's version (VERSION; -dev: not a release build), and the data folder's", _ =>
		{
			GameConsole.Print("Chosen " + Shared.Engine.ChosenVersion.Full);
			int dv = Shared.Blood.Files.Extractor.FolderDataVersion(Data.BloodData.Folder);
			GameConsole.Print($"data folder {Data.BloodData.Folder}: data version {(dv < 0 ? "none (no data/extracted.txt)" : dv.ToString())}, "
				+ $"this game needs {Shared.Blood.Files.Extractor.DataVersion}");
		});
		GameConsole.Register("later", "later <seconds> <command>", "run a console command that many seconds from now (scripted tests of settings changed while playing)", a =>
		{
			if (a.Length < 3)
				return;
			string command = string.Join(" ", a[2..]);
			GetTree().CreateTimer(F(a[1])).Timeout += () => GameConsole.Execute(command);
		});
		GameConsole.Register("motionshots", "motionshots <file prefix> <blend,blend,...> [quit] [after=<game seconds>]",
			"hold the game at each blend between the last two game frames and save the view as drawn and exact (bugs.py motion); after: once that long has passed", a =>
			{
				if (a.Length < 3)
					return;
				_motionPrefix = a[1];
				_motionLerps = System.Array.ConvertAll(a[2].Split(','), v => float.Parse(v, CultureInfo.InvariantCulture));
				_motionBlend = Render.Native.NativeWorld.MeshBlend.Value;
				_motionCull = Render.SectorVisibility.CullCvar.Value;
				_motionQuit = System.Array.IndexOf(a, "quit", 3) >= 0;
				_motionUntil = 0;
				foreach (var arg in a)
					if (arg.StartsWith("after="))
						_motionUntil = (_world?.Frame ?? 0) + (int)(double.Parse(arg[6..], CultureInfo.InvariantCulture) * 30);
				_motionStep = -1;
				_motionWait = 0;
			});
		GameConsole.Register("hidemesh", "hidemesh <name part> [x]", "hide the meshes whose node or parent name has it (x: all the others), to see which renderer draws what", a =>
		{
			int n = 0;
			var all = new System.Collections.Generic.List<MeshInstance3D>();
			void Walk(Node at)
			{
				foreach (var c in at.GetChildren())
				{
					if (c is MeshInstance3D m)
						all.Add(m);
					Walk(c);
				}
			}
			Walk(GetTree().Root);
			foreach (var mi in all)
			{
				string nm = mi.Name + "/" + mi.GetParent()?.Name;
				if (nm.Contains(a[1], System.StringComparison.OrdinalIgnoreCase) == (a.Length < 3))
				{
					mi.Visible = false;
					n++;
				}
			}
			GameConsole.Print($"hidemesh {a[1]}: {n} hidden");
			if (n == 0)
				GameConsole.Print("  meshes: " + string.Join(", ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(all, m => m.Name + "/" + m.GetParent()?.Name), 12)));
		});
		GameConsole.Register("cam", "cam <x> <y> <z> [yaw] [pitch]", "put the camera at a Quake position (flying)",
			a => { _ghost = null; _fly = true; PlaceCamera(a[1..]); });
		Sim.Build.BuildDebug.Register();
		GameConsole.Register("bsec", "bsec [sector|near [radius]]", "a level from its Blood .MAP: Build sectors (heights, type) and their x-sectors' state",
			a =>
			{
				var b = _world.Board;
				if (b == null)
				{
					GameConsole.Print("bsec: no level");
					return;
				}
				var list = new System.Collections.Generic.List<int>();
				if (a.Length > 1 && int.TryParse(a[1], out int one))
					list.Add(one);
				else
				{
					var c = Coords.GodotToQuake(_cam.GlobalPosition);
					float r = a.Length > 2 && float.TryParse(a[2], CultureInfo.InvariantCulture, out float rr) ? rr : 256;
					for (int i = 0; i < b.SectorCount; i++)
					{
						var box = _world.Map.SectorBox(i);
						if (box.Grow(r).HasPoint(new Vector2(c.X, c.Y)))
							list.Add(i);
					}
				}
				foreach (int i in list)
				{
					if (!b.Valid(i))
						continue;
					var s = b.Sectors[i];
					var xs = _world.XSectorOf(i);
					GameConsole.Print($"bsec {i} type {s.Type} walls {s.WallNum} floor {s.FloorZ} ceil {s.CeilingZ} heinum {s.FloorHeinum}/{s.CeilingHeinum} pics {s.FloorPicnum}/{s.CeilingPicnum} stat {s.FloorStat}/{s.CeilingStat} shade {s.FloorShade}/{s.CeilingShade} pal {s.FloorPal}/{s.CeilingPal} vis {s.Visibility} wallptr {s.WallPtr} shape {b.ShapeVersion[i]} planes {b.PlaneVersion[i]}"
						+ " overlaps " + string.Join(",", _world.Map.OverlapsOf(i)) + " pts" + string.Concat(System.Linq.Enumerable.Select(b.SectorWalls(i), w => $" ({b.Walls[w].X},{b.Walls[w].Y})"))
						+ (xs == null ? "" : $" x: rx {xs.RxID} tx {xs.TxID} cmd {xs.XCommand} state {xs.XState} busy {xs.XBusy} active {xs.BusyActive} proc {xs.BusyProc} delta {xs.BusyDelta} off/on floor {xs.OffFloorZ}/{xs.OnFloorZ} ceil {xs.OffCeilZ}/{xs.OnCeilZ} markers {xs.SMarker0}/{xs.SMarker1} drag {xs.SDrag} bob {xs.BobSpeed}"));
				}
			});
		GameConsole.Register("cdudes", "cdudes [near <radius>]", "a modern map's custom dudes: where, their descriptor, state, health, target and weapons (debugging)",
			a =>
			{
				float r = a.Length > 2 && float.TryParse(a[2], CultureInfo.InvariantCulture, out float rr) ? rr : -1;
				int n = 0;
				foreach (var line in _world.CdDescribe(Coords.GodotToQuake(_cam.GlobalPosition), r))
				{
					GameConsole.Print(line);
					n++;
				}
				GameConsole.Print($"{n} custom dudes");
			});
		GameConsole.Register("patrols", "patrols", "a modern map's patrolling dudes: where, their state, the marker they head for and how far they have spotted a player (debugging)",
			a =>
			{
				int n = 0;
				foreach (var line in _world.PatrolDescribe())
				{
					GameConsole.Print(line);
					n++;
				}
				GameConsole.Print($"{n} dudes with the patrol flag");
			});
		GameConsole.Register("bwall", "bwall <wall>", "a level from its Blood .MAP: a Build wall (flags, tiles, the sectors on both sides) and its x-wall's state (debugging)",
			a =>
			{
				var b = _world.Board;
				if (b == null || a.Length < 2 || !int.TryParse(a[1], out int w) || w < 0 || w >= b.WallCount)
				{
					GameConsole.Print("bwall: no such wall");
					return;
				}
				var bw = b.Walls[w];
				var xw = _world.XWallOf(w);
				GameConsole.Print($"bwall {w} sector {bw.Sector} next {bw.NextSector} (wall {bw.NextWall}) cstat {bw.Cstat} type {bw.Type} pic {bw.Picnum} over {bw.OverPicnum} shade {bw.Shade} pal {bw.Pal} at ({bw.X},{bw.Y})..({b.Walls[bw.Point2].X},{b.Walls[bw.Point2].Y})"
					+ (xw == null ? " no x-wall" : $" x: rx {xw.RxID} tx {xw.TxID} cmd {xw.XCommand} state {xw.XState} vector {xw.XVector} gib {xw.GibType} broken {xw.Broken}"));
			});
		GameConsole.Register("where", "where", "print the camera's and the player's Quake position and angles", _ =>
		{
			if (_player != null)
				GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "player origin {0:0.#} {1:0.#} {2:0.#} yaw {3:0.##} health {4} posture {5} medium {6} underwater {7} water at origin {8}",
					_player.Origin.X, _player.Origin.Y, _player.Origin.Z, _controls.Yaw, System.Math.Floor(_player.XHealth / 16),
					_player.Posture, _player.Medium, _player.IsUnderwater, _world.PointIsWater(_player.Origin)));
			if (_player != null)
			{
				var sec = _world.SpriteSectorUpdate(_player);
				GameConsole.Print($"player Build sector {_world.SpriteBSector(_player)}, view {Client.PlayerView.EyeSector(_world, _player)}");
				GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "player now / last game frame: origin {0} / {1}, zview {2} / {3}, bz {4} / {5}, bob {6} / {7}",
					_player.Origin, _player.PrevOrigin, _player.ZView, _player.PrevZView, _player.BZ, _player.PrevBZ, _player.BobHeight, _player.PrevBobHeight));
				GameConsole.Print(sec == null ? "player sector: none"
					: $"player sector: {sec.Index} (Build {sec.BSector - 1}) type {sec.Type}{(sec.TargetName != "" ? " " + sec.TargetName : "")}");
				GameConsole.Print($"player floor: {_player.FlorHit} {_player.FlorHitEnt}");
			}
			var q = Coords.GodotToQuake(_cam.GlobalPosition);
			var d = -_cam.GlobalBasis.Z;
			float yaw = Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)), pitch = -Mathf.RadToDeg(Mathf.Asin(d.Y));
			GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "cam {0:0.#} {1:0.#} {2:0.#} {3:0.#} {4:0.#}", q.X, q.Y, q.Z, yaw, pitch));
		});
		GameConsole.Register("screenshot", "screenshot [file.png]", "save a screenshot", a =>
		{
			_shotPath = a.Length > 1 ? a[1] : $"user://shot_{Time.GetTicksMsec()}.png";
			_quitAfterShot = false;
			SaveShotSoon();
		});
		GameConsole.Register("shotburst", "shotburst <frames> <file prefix> [quit] [from=<game frame>]",
			"save every drawn frame for that many frames (prefix_000.png...; each printed with its game frame and blend), to catch a glitch of one drawn frame; quit: quit after; from: start at that game frame (a replay's --exec, the player still moving)", a =>
			{
				if (a.Length < 3)
					return;
				_burstLeft = (int)F(a[1]);
				_burstPrefix = a[2];
				_burstQuit = System.Array.IndexOf(a, "quit", 3) >= 0;
				_burstFrom = 0;
				foreach (var arg in a)
					if (arg.StartsWith("from="))
						_burstFrom = int.Parse(arg[5..]);
				_burstNo = 0;
				_burstLabel = null;
			});
		GameConsole.Register("quit", "quit", "quit the game", _ => GetTree().Quit());
		GameConsole.Register("check", "check <bug id or .rec> [probe frames...] [diffs=N]",
			"check the C# player against a DarkPlaces recording, frame by frame (strict replay)", a =>
			{
				var rec = Recording.Load(RecordingPath(a[1]));
				Data.Tiles.Load();
				Data.SeqData.Load();
				ulong t = Time.GetTicksMsec();
				var world = new World();
				ApplyRecordingSettings(world, rec);
				world.Seed(rec.Seed);
				if (!LoadHeadless(world, rec.Map))
					return;
				ulong t2 = Time.GetTicksMsec();
				var sr = new StrictReplay(rec, world);
				int maxDiffs = 12;
				for (int i = 2; i < a.Length; i++)
				{
					if (a[i].StartsWith("diffs="))
						maxDiffs = int.Parse(a[i][6..]);
					else if (a[i] == "rngtrace")
						sr.TraceRng = true;
					else if (a[i].StartsWith("dump="))
					{
						foreach (var f in a[i][5..].Split(','))
							sr.DumpFrames.Add(int.Parse(f));
						sr.DumpPattern = ProjectSettings.GlobalizePath("res://").PathJoin($"tools/dumps/{a[1]}-{{0}}-cs.txt");
						System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sr.DumpPattern));
					}
					else
						sr.Probe.Add(int.Parse(a[i]));
				}
				var report = sr.Run(maxDiffs);
				GameConsole.Print($"{a[1]} on {rec.Map}: load {t2 - t} ms, check {Time.GetTicksMsec() - t2} ms");
				GameConsole.Print(report.TrimEnd());
			});
		GameConsole.Register("sprites", "sprites [radius]", "list the sprites near the camera (default 256 units)", a =>
		{
			var q = Coords.GodotToQuake(_cam.GlobalPosition);
			float r = a.Length > 1 ? F(a[1]) : 256;
			foreach (var s in _world.Sprites)
			{
				if (s.Removed || (s.Origin - q).Length() > r)
					continue;
				GameConsole.Print(string.Format(CultureInfo.InvariantCulture,
					"{0} edict {1} type {2} pic {3} view {4} stat {5} cstat {6} pal {7} rep {8}x{9} org {10:0.#} {11:0.#} {12:0.#}{13}{14}{15}",
					s, s.Edict, s.Type, s.Picnum, Data.Tiles.View(s.Picnum), s.StatNum, s.Cstat, s.Pal, s.XRepeat, s.YRepeat,
					s.Origin.X, s.Origin.Y, s.Origin.Z, (s.Visible ? "" : " hidden") + (s.HasX ? $" xstate {s.XState} busy {s.XBusy} rx {s.RxID} tx {s.TxID} triggered {s.IsTriggered}" : ""), s.OutsideHome ? " outside-home" : "",
					Render.Voxels.TryGet(s.Picnum, out var vi) ? " voxel " + vi.Model : ""));
			}
		});
		GameConsole.Register("fx", "fx [radius]", "list the light-effect sectors near the camera and their shade now", a =>
		{
			var q = Coords.GodotToQuake(_cam.GlobalPosition);
			float r = a.Length > 1 ? F(a[1]) : 512;
			GameConsole.Print($"build_visibility {RenderingServer.GlobalShaderParameterGet("build_visibility")}");
			foreach (var s in _world.Sectors)
			{
				if (s.FxAmp == 0 || s.Bounds.GetCenter().DistanceTo(q) > r)
					continue;
				GameConsole.Print($"{s} bsector {s.BSector} amp {s.FxAmp} wave {s.FxWave} freq {s.FxFreq} always {s.FxAlways} busy {s.XBusy} mover {s.IsMover} shade {s.FxShade}");
			}
		});
		GameConsole.Register("walk", "walk <frames> [delay frames]", "run forward for that many game frames (testing triggers you walk into)", a =>
		{
			Client.PlayerControls.WalkFrames = a.Length > 1 ? (int)F(a[1]) : 30;
			Client.PlayerControls.WalkDelay = a.Length > 2 ? (int)F(a[2]) : 0;
		});
		GameConsole.Register("after", "after <seconds> <command>", "run a console command that many seconds later (testing: onto a moving platform when it is there)", a =>
		{
			if (a.Length < 3)
				return;
			string cmd = string.Join(" ", a[2..]);
			GetTree().CreateTimer(F(a[1])).Timeout += () => GameConsole.Execute(cmd);
		});
		GameConsole.Register("use", "use", "press use for a game frame (testing doors and switches)", _ =>
			Client.PlayerControls.UseFrames = 2);
		GameConsole.Register("timescale", "timescale [speed]", "run the game slower or faster (0.1: a tenth of real time, to catch a frame's glitch; 1: normal); no value prints it", a =>
		{
			if (a.Length > 1 && float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sp) && sp > 0)
				_clock.Speed = Mathf.Clamp(sp, 0.01f, 50f);
			GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "timescale {0:0.###}", _clock.Speed));
		});
		GameConsole.Register("busy", "busy", "list the sectors busy now (moving through their busy procedure): Build sector, type, busy, drag (debugging)", _ =>
		{
			foreach (var s in _world.Sectors)
				if (s.BusyActive)
					GameConsole.Print($"busy: sector {s.BSector - 1} type {s.Type} busy {s.BusyVal} delta {s.BusyDelta} state {s.XState} drag {s.SDrag} zpart '{s.ZPart}' origin {s.Origin}");
		});
		GameConsole.Register("movers", "movers [radius]", "list the movers near the camera: pose, bob, slope, state", a =>
		{
			var q = Coords.GodotToQuake(_cam.GlobalPosition);
			float r = a.Length > 1 ? F(a[1]) : 512;
			foreach (var s in _world.Sectors)
				if (s.IsMover && s.AbsBounds.GetCenter().DistanceTo(q) <= r)
					GameConsole.Print($"{s} bsector {s.BSector} type {s.Type} part {s.ZPart} offset {s.Origin - s.BaseOrg} yaw {s.Yaw - s.BaseYaw:0.#} bob {s.BobZ:0.##} slope {s.Shear:0.###} state {s.XState} busy {s.XBusy} place {s.PlaceF}{(s.AlignEnt != null ? " aligned to " + s.AlignEnt : "")}");
		});
		GameConsole.Register("node", "node <name> [hide|show]", "show a level node's state (visibility, position, meshes; debugging)", a =>
		{
			if (FindChild(a[1], true, false) is not Node3D n)
			{
				GameConsole.Print($"no node {a[1]}");
				return;
			}
			if (a.Length > 2)
				n.Visible = a[2] != "hide";
			GameConsole.Print($"{n.GetPath()} visible {n.IsVisibleInTree()} at {n.GlobalPosition} basis {n.GlobalBasis}");
			foreach (var c in n.FindChildren("*", "MeshInstance3D", true, false))
			{
				var mi = (MeshInstance3D)c;
				GameConsole.Print($"  {mi.Name} visible {mi.IsVisibleInTree()} layers {mi.Layers} at {mi.GlobalPosition} surfaces {mi.Mesh?.GetSurfaceCount()} aabb {mi.GetAabb()}");
			}
		});
		GameConsole.Register("debugdraw", "debugdraw <n>", "Godot's viewport debug draw (0 off, 1 unshaded, 3 overdraw, 4 wireframe)",
			a => GetViewport().DebugDraw = (Viewport.DebugDrawEnum)int.Parse(a[1]));
		// Blood's view size keys: = grows the view (less HUD), - shrinks it (b3d_hudsize 0 none .. 3 the status bar)
		GameConsole.Register("sizeup", "sizeup", "a bigger view: less HUD (the = key)", _ => AddHudSize(-1));
		GameConsole.Register("sizedown", "sizedown", "a smaller view: more HUD (the - key)", _ => AddHudSize(1));
		GameConsole.Register("perf", "perf [vsync 0|1]", "print the frame rate, draw calls and portals rendered (vsync: turn vsync off/on)", a =>
		{
			if (a.Length > 2 && a[1] == "vsync")
				DisplayServer.WindowSetVsyncMode(a[2] == "0" ? DisplayServer.VSyncMode.Disabled : DisplayServer.VSyncMode.Enabled);
			// the main view's render time (measured from the first perf on)
			var rid = GetViewport().GetViewportRid();
			RenderingServer.ViewportSetMeasureRenderTime(rid, true);
			GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "{0} fps, {1} draw calls, {2} primitives, {3} portals rendered, render CPU {4:0.00} ms GPU {5:0.00} ms",
				Engine.GetFramesPerSecond(), RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
				RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame), _portals?.Drawn ?? 0,
				RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid)));
		});
		GameConsole.Register("portals", "portals", "list the room-over-room portals and their copies (debugging)", _ =>
		{
			foreach (var line in _portals.Describe())
				GameConsole.Print(line);
		});
		GameConsole.Register("spriteresend", "spriteresend", "send every sprite node's shader parameters again (debugging)", _ => _sprites.Resend());
		GameConsole.Register("hitscan", "hitscan [up] [fire]", "run the game's hitscan from the player's weapon along the aim and print what it hits (debugging)", a =>
		{
			if (_player == null)
				return;
			int hit = _world.DebugHitscan(_player, out var what, out var at, System.Array.IndexOf(a, "up") > 0, System.Array.IndexOf(a, "fire") > 0);
			GameConsole.Print($"hitscan: type {hit} {what} at {at} (Build wall {_world.HitWallB}, sector {_world.HitSectB})");
		});
		GameConsole.Register("togglemenu", "togglemenu", "open or close the menu", _ =>
		{
			if (_menu.IsOpen)
				_menu.Close();
			else
				_menu.Open();
		});
		GameConsole.Register("menu_page", "menu_page <main|episodes|difficulty|options|game|help> [title]", "open a menu page (debugging)",
			a => _menu.OpenPage(a.Length > 1 ? a[1] : "main", a.Length > 2 && a[2] == "title"));
		GameConsole.Register("movie", "movie <name> [wav]", "play movie/<name>.smk (with movie/<wav>.wav) or movie/<name>.ogv, e.g. logo logo, cs1 cs1822m, remastered/cs1", a =>
		{
			string name = a[1];
			string srt = MovieFolder.PathJoin("remastered").PathJoin(System.IO.Path.GetFileName(name) + ".srt");
			srt = Subtitles.Bool && System.IO.File.Exists(srt) ? srt : null;
			var m = System.IO.File.Exists(MovieFolder.PathJoin(name + ".ogv")) ? new Client.MoviePlayer.Movie(MovieFolder.PathJoin(name + ".ogv"), null, srt)
				: Original(name, a.Length > 2 ? a[2] : "");
			if (m.Subtitles == null && srt != null)
				m = new Client.MoviePlayer.Movie(m.Video, m.Sound, srt);
			PlayMovies(new[] { m }, () => { if (_player != null) _music.PlayLevel(MapName); });
		});
		GameConsole.Register("fmrender", "fmrender <song> <seconds> <file.wav>", "render a MIDI song on the OPL3 FM synth to a WAV (debugging)", a =>
		{
			var dir = Data.BloodData.Folder.PathJoin("sound/midi");
			var fm = new Client.MidiFm(44100, System.IO.File.ReadAllBytes(dir.PathJoin("gmtimbre.tmb")));
			if (!fm.Play(System.IO.File.ReadAllBytes(dir.PathJoin(a[1] + ".mid")), true))
				return;
			int n = (int)(F(a[2]) * 44100);
			var buf = new float[n * 2];
			ulong t0 = Time.GetTicksMsec();
			fm.Render(buf, n);
			using var f = new System.IO.BinaryWriter(System.IO.File.Create(a[3]));
			f.Write("RIFF"u8); f.Write(36 + n * 4); f.Write("WAVEfmt "u8); f.Write(16); f.Write((short)1); f.Write((short)2);
			f.Write(44100); f.Write(44100 * 4); f.Write((short)4); f.Write((short)16); f.Write("data"u8); f.Write(n * 4);
			foreach (var s in buf)
				f.Write((short)Mathf.Clamp(s * 32767f, -32768f, 32767f));
			GameConsole.Print($"fmrender: {a[1]} {a[2]} s in {Time.GetTicksMsec() - t0} ms");
		});
		GameConsole.Register("cansee","cansee <x> <y> <z> <x> <y> <z>", "the AI's line of sight between two Quake points (debugging)", a =>
			GameConsole.Print("cansee: " + _world.QCanSee(new Vector3(F(a[1]), F(a[2]), F(a[3])), new Vector3(F(a[4]), F(a[5]), F(a[6])))));
		GameConsole.Register("stacks", "stacks", "the camera's sector and the side of each pair of stacked rooms (debugging)", _ =>
		{
			foreach (var line in Render.StackedRooms.Describe())
				GameConsole.Print(line);
		});
		GameConsole.Register("seen", "seen [sector]", "how many Build sectors the camera may see this frame (r_cullmoving), and whether one of them is (debugging)", a =>
		{
			GameConsole.Print(Render.SectorVisibility.All ? "seen: all (no camera sector or r_cullmoving 0)"
				: $"seen: {Render.SectorVisibility.SeenCount} of {_world.Map?.SectorCount} sectors from sector {Render.StackedRooms.Sector}");
			if (a.Length > 1 && int.TryParse(a[1], out int s))
				for (int i = 1; i < a.Length; i++)
					if (int.TryParse(a[i], out s))
					{
						Render.SectorVisibility.Window(s, out float lo, out float hi, out float tlo, out float thi);
						GameConsole.Print($"sector {s}: {(Render.SectorVisibility.Seen(s) ? "seen" : "not seen")}{(Render.SectorVisibility.DirectlySeen(s) ? ", directly (its sprites over the sky)" : "")}"
							+ $" window angles {Mathf.RadToDeg(lo):0.#}..{Mathf.RadToDeg(hi):0.#} slopes {tlo:0.###}..{thi:0.###}");
					}
		});
		GameConsole.Register("sprat", "sprat <px> <py>", "the drawn sprites and voxel models whose box covers a screen pixel (debugging)", a =>
		{
			foreach (var line in _sprites.At(_cam, new Vector2(F(a[1]), F(a[2]))))
				GameConsole.Print(line);
		});
		GameConsole.Register("send", "send <channel>|s<sector> [off|on|toggle|<command>]", "send a command on an x-object channel as a switch would, or to one Build sector (s136) (debugging: moving parts; default toggle)", a =>
			Send(_world, _player, a));
		GameConsole.Register("zfight", "zfight [radius]", "drawn triangles near the camera lying in one plane with another mesh's and overlapping it (z-fighting; debugging)", a =>
		{
			var q = Coords.GodotToQuake(_cam.GlobalPosition);
			float r = a.Length > 1 ? F(a[1]) : 128;
			// every visible level, live and mover triangle near the camera, in Quake space, and the portals' copies of the
			// rooms beyond (named after their PortalCopy node)
			var tris = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n, float d, string what)>();
			foreach (var node in FindChildren("*", "MeshInstance3D", true, false))
			{
				var mi = (MeshInstance3D)node;
				if (!mi.IsVisibleInTree() || mi.Mesh is not ArrayMesh mesh)
					continue;
				var xf = mi.GlobalTransform;
				for (int s = 0; s < mesh.GetSurfaceCount(); s++)
				{
					string sn = mesh.SurfaceGetName(s);
					if (sn.StartsWith("tools/"))
						continue;
					using var arrays = mesh.SurfaceGetArrays(s);
					var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
					var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
					int n = idx.Length > 0 ? idx.Length : v.Length;
					for (int i = 0; i + 2 < n; i += 3)
					{
						Vector3 A = Coords.GodotToQuake(xf * v[idx.Length > 0 ? idx[i] : i]), B = Coords.GodotToQuake(xf * v[idx.Length > 0 ? idx[i + 1] : i + 1]),
							C = Coords.GodotToQuake(xf * v[idx.Length > 0 ? idx[i + 2] : i + 2]);
						var mid = (A + B + C) / 3;
						if ((mid - q).Length() > r)
							continue;
						var nn = (B - A).Cross(C - A);
						// a sliver (the live sweep leaves some along edges) fights nothing: its height over its longest side
						float longest = Mathf.Max((B - A).Length(), Mathf.Max((C - B).Length(), (A - C).Length()));
						if (longest < 1e-4f || nn.Length() / longest < 0.05f)
							continue;
						nn = nn.Normalized();
						tris.Add((A, B, C, nn, nn.Dot(A), $"{mi.GetInstanceId()} {(_portals.IsAncestorOf(mi) ? mi.GetParent().Name + "/" : "")}{mi.Name} {sn}"));
					}
				}
			}
			int found = 0;
			for (int i = 0; i < tris.Count && found < 40; i++)
				for (int j = i + 1; j < tris.Count && found < 40; j++)
				{
					var x = tris[i];
					var y = tris[j];
					if (x.what.Split(' ')[0] == y.what.Split(' ')[0] || x.n.Dot(y.n) < 0.999f)
						continue;
					float gap = Mathf.Abs(x.n.Dot(y.a) - x.d);
					if (gap > 0.1f)
						continue;
					// overlap: in the plane, does either triangle's centre (or a corner pulled in) fall in the other?
					var u = x.n.Cross(Mathf.Abs(x.n.Z) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
					var w = x.n.Cross(u);
					Vector2 P(Vector3 p) => new(p.Dot(u), p.Dot(w));
					var tx = new[] { P(x.a), P(x.b), P(x.c) };
					var ty = new[] { P(y.a), P(y.b), P(y.c) };
					var cy = (ty[0] + ty[1] + ty[2]) / 3;
					var cx = (tx[0] + tx[1] + tx[2]) / 3;
					bool over = Geometry2D.IsPointInPolygon(cy, tx) || Geometry2D.IsPointInPolygon(cx, ty);
					for (int k = 0; k < 3 && !over; k++)
						over = Geometry2D.IsPointInPolygon(ty[k].Lerp(cy, 0.1f), tx) || Geometry2D.IsPointInPolygon(tx[k].Lerp(cx, 0.1f), ty);
					if (!over)
						continue;
					found++;
					GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "{0} | {1} gap {2:0.###} at {3:0.#} normal {4:0.##}: {5:0.#} {6:0.#} {7:0.#} | {8:0.#} {9:0.#} {10:0.#}",
						x.what, y.what, gap, (x.a + x.b + x.c) / 3, x.n, x.a, x.b, x.c, y.a, y.b, y.c));
				}
			GameConsole.Print($"zfight: {found} pair(s) among {tris.Count} triangles within {r}");
		});
		GameConsole.Register("faces", "faces <x> <y> <z> [radius]", "drawn triangles with a corner near a point (Quake units; level, live and portal-copy meshes; debugging)", a =>
		{
			var q = new Vector3(F(a[1]), F(a[2]), F(a[3]));
			float r = a.Length > 4 ? F(a[4]) : 8;
			int n = 0;
			foreach (var node in FindChildren("*", "MeshInstance3D", true, false))
			{
				var mi = (MeshInstance3D)node;
				if (!mi.IsVisibleInTree() || mi.Mesh is not ArrayMesh mesh)
					continue;
				var xf = mi.GlobalTransform;
				for (int s = 0; s < mesh.GetSurfaceCount(); s++)
				{
					string sn = mesh.SurfaceGetName(s);
					using var arrays = mesh.SurfaceGetArrays(s);
					var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
					var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
					int count = idx.Length > 0 ? idx.Length : v.Length;
					for (int i = 0; i + 2 < count && n < 60; i += 3)
					{
						var c = new Vector3[3];
						bool near = false;
						for (int k = 0; k < 3; k++)
						{
							c[k] = Coords.GodotToQuake(xf * v[idx.Length > 0 ? idx[i + k] : i + k]);
							near |= (c[k] - q).Length() <= r;
						}
						if (!near)
							continue;
						n++;
						GameConsole.Print(string.Format(CultureInfo.InvariantCulture, "{0} {1}: ({2:0.###}) ({3:0.###}) ({4:0.###})",
							mi.Name, sn, c[0], c[1], c[2]));
					}
				}
			}
			GameConsole.Print($"faces: {n} triangle(s) within {r}");
		});
		GameConsole.Register("pickshade", "pickshade <px> <py>", "the drawn level triangle under a screen pixel: its mesh, surface and Build shade and rate (debugging)", a =>
		{
			var from = _cam.ProjectRayOrigin(new Vector2(F(a[1]), F(a[2])));
			var dir = _cam.ProjectRayNormal(new Vector2(F(a[1]), F(a[2])));
			float best = float.MaxValue;
			string found = "nothing";
			var all = new List<(float d, string what)>();      // every triangle hit: those a hair behind the nearest fight it
			// the level's meshes and the live sectors' (Render.LiveRenderer)
			foreach (var node in FindChildren("*", "MeshInstance3D", true, false))
			{
				var mi = (MeshInstance3D)node;
				if (!mi.IsVisibleInTree() || mi.Mesh is not ArrayMesh mesh)
					continue;
				var xf = mi.GlobalTransform;
				for (int s = 0; s < mesh.GetSurfaceCount(); s++)
				{
					using var arrays = mesh.SurfaceGetArrays(s);
					var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
					var c = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
					var uv2 = arrays[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil ? System.Array.Empty<Vector2>()
						: arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
					for (int t = 0; t + 2 < v.Length; t += 3)
					{
						var r = Geometry3D.RayIntersectsTriangle(from, dir, xf * v[t], xf * v[t + 1], xf * v[t + 2]);
						if (r.VariantType != Variant.Type.Vector3)
							continue;
						var hit = r.AsVector3();
						float d = from.DistanceTo(hit);
						if (Render.StackedRooms.FaceHidden(mesh.SurfaceGetName(s), Coords.GodotToQuake(hit)))
						{
							var hm = mesh.SurfaceGetMaterial(s) as ShaderMaterial;
							GameConsole.Print($"  hidden: {mesh.SurfaceGetName(s)} at {Coords.GodotToQuake(hit)} stack_own {hm?.GetShaderParameter("stack_own")} shader {hm?.Shader?.ResourcePath}{(hm?.Shader?.Code.Contains("blend_mix") == true ? " (translucent)" : "")}");
							continue;
						}
						string why = "";
						if (uv2.Length > t && uv2[t].X > 0 && Render.StackedRooms.SectorFaceHidden((int)uv2[t].X - 1, Coords.GodotToQuake(hit), out why))
						{
							GameConsole.Print($"  hidden: {mesh.SurfaceGetName(s)} at {Coords.GodotToQuake(hit)} sector {uv2[t].X - 1:0} ({why})");
							continue;
						}
						all.Add((d, $"{mi.GetPath()} surface {mesh.SurfaceGetName(s)} at {Coords.GodotToQuake(hit)}"));
						if (d >= best)
							continue;
						best = d;
						var col = c.Length > t ? c[t] : new Color(0, 0, 0);
						var tn = Coords.GodotToQuake((xf * v[t + 1] - xf * v[t]).Cross(xf * v[t + 2] - xf * v[t])).Normalized();
						var mat = mesh.SurfaceGetMaterial(s) as ShaderMaterial;
						string tex = mat?.GetShaderParameter(Render.BuildShading.AlbedoUniform).AsGodotObject() is Texture2D tx ? tx.ResourcePath : "";
						found = string.Format(CultureInfo.InvariantCulture, "{0} surface {1} {8} triangle {2}: shade {3:0} rate {4:0} mark {5:0} alpha {7:0.0} at {6} winding normal {9}{10}",
							mi.GetPath(), mesh.SurfaceGetName(s), t / 3, 127 - col.R * 255, col.G * 255, col.B * 255, Coords.GodotToQuake(hit), col.A, tex, tn,
							uv2.Length > t && uv2[t].X > 0 ? $" sector {uv2[t].X - 1:0} ({why})" : "");
					}
				}
			}
			GameConsole.Print($"pickshade {a[1]} {a[2]}: {found}");
			foreach (var (d, what) in all)
				if (d > best && (d - best) * 32 < 0.5f)       // Godot units: 1/32 of a Quake unit
					GameConsole.Print($"  also {(d - best) * 32:0.###} behind: {what}");
		});
		GameConsole.Register("light", "light [x y z | clear]", "place a white test light at the camera or a Quake position (r_dynamiclights; debugging)", a =>
		{
			if (a.Length > 1 && a[1] == "clear")
				_lights.ClearTestLights();
			else
				_lights.AddTestLight(a.Length > 3 ? new Vector3(F(a[1]), F(a[2]), F(a[3])) : Coords.GodotToQuake(_cam.GlobalPosition));
		});
		GameConsole.Register("movingmesh", "movingmesh", "the moving sectors' meshes: rigid (built once a game frame), each draw, stopped; last build's times (debugging)", _ =>
		{
			if (_native == null)
				return;
			foreach (var line in _native.DescribeMoving())
				GameConsole.Print(line);
		});
		GameConsole.Register("lights", "lights", "list the dynamic lights in use (debugging)", _ =>
		{
			foreach (var line in _lights.Describe())
				GameConsole.Print(line);
		});
		GameConsole.Register("links", "links", "list the room-over-room link sides: plane, direction, offset, footprint (debugging)", _ =>
		{
			foreach (var l in _world.Collision.Links)
				GameConsole.Print($"link {l.Id} type {l.Type} portal {l.Portal} (drawn as one: {l.ConvertedPortal}) sector {l.Sector} far {l.FarSector} dir {l.Dir} plane {l.Plane} offset {l.Offset} bounds {l.Bounds.Position} .. {l.Bounds.End}" + (l.Mover != null ? $" moves with {l.Mover}" : ""));
		});
		GameConsole.Register("fov_apply", "fov_apply", "apply the fov variable", _ => ApplyFov());
	}

	static void AddHudSize(int d)
	{
		if (Cvars.Find("b3d_hudsize") is Cvar c)
			c.Set(Mathf.Clamp(c.Int + d, 0, 3).ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// --views=&lt;file&gt; [--viewshots=&lt;dir&gt;]: a screenshot from each camera of a list (a line per view: Quake x y z,
	/// yaw, pitch as the game's bug reports and NBlood's -b3dviews take them, negative up, and optionally the camera's
	/// Build sector, where rooms stack or share space), saved as dir/0000.png,
	/// 0001.png, ... in list order, then the game quits (pipeline/compare.py). --exec runs first (monsters off, no HUD).
	/// </summary>
	async void ViewBatch(string file, string dir)
	{
		var lines = System.IO.File.ReadAllLines(file);
		DirAccess.MakeDirRecursiveAbsolute(dir);
		World.FreezeLightFx = true;      // flickering rooms at their base shades, as NBlood draws them with -b3dviews
		Render.DynamicLights.Suspended = true;      // and no dynamic lights (NBlood has none)
		for (int i = 0; i < 5; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (_args.TryGetValue("wait", out var wait) && wait != "")
			await ToSignal(GetTree().CreateTimer(F(wait)), SceneTreeTimer.SignalName.Timeout);
		// --viewtime=<seconds>: the level runs to that game time (NBlood's -b3dshot) and is held there, so every view
		// shows the same moment in both engines (NBlood holds its logic from its first view on): a train, a lift, a door
		// on its timer
		if (_args.TryGetValue("viewtime", out var viewTime) && viewTime != "")
		{
			int target = Mathf.RoundToInt(F(viewTime) * 30);
			while (_world.Frame < target)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			_paused = true;
		}
		int n = 0;
		foreach (var line in lines)
		{
			var p = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
			if (p.Length < 4)
				continue;
			// Blood's look as the turn NBlood's polymost gives it (r_yshearing 0): it turns the view by atan(horiz / 160);
			// Blood's horizon line at 90 of 200 (a level look tips 3.6 degrees down in NBlood) is the camera's frustum
			// shift, as in play (ApplyHorizon)
			double look = Mathf.Clamp(-(p.Length > 4 ? F(p[4]) : 0) * 2048 / 360.0, -347, 289);
			double horiz = 100 * System.Math.Tan(look * System.Math.PI / 1024);
			float pitch = -(float)Mathf.RadToDeg(System.Math.Atan2(horiz, 160));
			_ghost = null;
			_fly = true;
			PlaceCamera(new[] { p[0], p[1], p[2], p[3], pitch.ToString(CultureInfo.InvariantCulture) });
			if (p.Length > 5 && int.TryParse(p[5], out int camSector) && camSector >= 0)
				Render.StackedRooms.ForceSector(camSector);
			for (int i = 0; i < 4; i++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			string path = $"{dir}/{n:0000}.png";
			Render.HdrOutput.Picture(GetViewport()).SavePng(path);
			if (_args.TryGetValue("viewcmd", out var viewCmd))
				GameConsole.Execute(viewCmd);      // debugging: after each view (--viewcmd=stacks)
			n++;
		}
		GameConsole.Print($"saved {n} views in {ProjectSettings.GlobalizePath(dir)}");
		GetTree().Quit();
	}

	async void SaveShotSoon()
	{
		for (int i = 0; i < 5; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (_args.TryGetValue("wait", out var wait) && wait != "")
			await ToSignal(GetTree().CreateTimer(F(wait)), SceneTreeTimer.SignalName.Timeout);
		if (_args.TryGetValue("atshot", out var atShot))
		{
			GameConsole.Execute(atShot);
			for (int i = 0; i < 3; i++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		Render.HdrOutput.Picture(GetViewport()).SavePng(_shotPath);
		GameConsole.Print("saved " + ProjectSettings.GlobalizePath(_shotPath) + " (camera " + Coords.GodotToQuake(_cam.GlobalPosition) + ")");
		if (_args.ContainsKey("perf"))
			GameConsole.Execute("perf");
		if (_quitAfterShot)
			GetTree().Quit();
	}

	public override void _Process(double delta)
	{
		if (_cam == null)
			return;	  // a headless check (--check) quit before the game was set up
		long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
		_tickTicks = 0;
		Perf.BeginFrame();
		Render.ProceduralFire.Process();
		Cvars.SaveConfigIfChanged();
		ApplyFov();
		if (_level.MapPath == "")
			return;     // the title screen before a level is picked
		// game frames at Blood's 30 per second (the world, and a replay's recorded frames); the console pauses the game
		bool paused = (GameConsole.IsOpen || _paused || _menu.IsOpen || _movies.Playing) && _player != null;
		_sound.SetPaused(paused);
		int n = paused ? 0 : _clock.Advance(delta);
		int frames = n;
		bool playing = _ghost == null && !_fly && !GameConsole.IsOpen && Client.Mouse.Captured;
		for (int i = 0; i < n; i++)
		{
			if (_play != null && !_play.Finished && !(_stopFrame >= 0 && _play.FrameNumber >= _stopFrame))
			{
				_play.Prepare(_world, _player);
				_recorder?.Record(_player);
				_controls.Yaw = _player.Cmd.Yaw;
				_controls.Pitch = _player.Cmd.Pitch > 180 ? _player.Cmd.Pitch - 360 : _player.Cmd.Pitch;
				TimedTick();
				_play.Finish(_player);
				if (_play.Finished || (_stopFrame >= 0 && _play.FrameNumber >= _stopFrame))
				{
					GameConsole.Print(_play.Report());
					_play = null;
					_playRec = null;
					_clock.Speed = 1;
					if (_args.TryGetValue("atend", out var atEnd))
						GameConsole.Execute(atEnd);
					if (_args.TryGetValue("report", out var report))
						ReportAndQuit(report);
					else if (_quitAfterShot)
						SaveShotSoon();
					_clock.Step();
					break;      // the frames still due this time run from the next draw (after --atend's commands)
				}
				_clock.Step();
				continue;
			}
			if (_player != null)
			{
				_player.Cmd = _controls.Build(playing);
				_recorder?.Record(_player);
			}
			TimedTick();
			if (_player != null)
				_controls.Sync(_player);
			if (_world.RestartRequested)
			{
				_world.RestartRequested = false;
				StartLevel();
				break;
			}
			_clock.Step();
			if (_ghost != null && !_ghost.Finished && !(_stopFrame >= 0 && _ghost.FrameNumber >= _stopFrame))
				_ghost.Step();
		}
		if (_ghost != null)
		{
			bool done = _ghost.Finished || (_stopFrame >= 0 && _ghost.FrameNumber >= _stopFrame);
			_ghost.Apply(_cam, done ? 1f : _clock.Lerp);
			if (done)
			{
				GameConsole.Print($"replay stopped at frame {_ghost.FrameNumber}");
				_ghost = null;
				_clock.Speed = 1;
				if (_quitAfterShot)
					SaveShotSoon();
			}
		}
		else if (_fly || _player == null)
		{
			if (!GameConsole.IsOpen)
				FlyCamera(delta);
		}
		else
		{
			if (!paused)
				_controls.Turn(delta);
			Client.PlayerView.Apply(_cam, _world, _player, _controls, _clock.Lerp, _world.Clock + _clock.Lerp * GameClock.TicsPerFrame);
		}
		// the eye in water or not: the shaders' fog, the view moving (Render.WaterFx)
		{
			bool own = _ghost == null && !_fly && _player != null;
			bool under = own ? _player.IsUnderwater && _player.Medium != Player.MediumNormal : _world.PointIsWater(Coords.GodotToQuake(_cam.GlobalPosition));
			float fov = _fovBase * (1f + Render.WaterFx.Update(under, own ? _player.Medium : Player.MediumWater));
			if (fov != _cam.Fov)
				_cam.Fov = fov;
		}
		ApplyHorizon();
		Perf.Mark("game");
		// the camera's Build sector: the player's (Blood's view), else the flying camera's followed as it moves; and
		// the sectors it may see (the moving groups not seen aren't rebuilt)
		var camQ = Coords.GodotToQuake(_cam.GlobalPosition);
		RenderingServer.GlobalShaderParameterSet(CameraWorldName, _cam.GlobalPosition);
		RenderingServer.GlobalShaderParameterSet(EyeName, _cam.GlobalPosition);
		_mist.Portals = _portals.Active;
		_mist.Update(_cam);
		_weather.Update(camQ, Time.GetTicksMsec() / 1000.0);
		bool ownEye = _ghost == null && !_fly && _player != null;
		Render.StackedRooms.Update(camQ, -1,
			ownEye ? Client.PlayerView.EyeSector(_world, _player) : -1);
		// the distance fog of the camera's area's atmosphere (faded into)
		{
			var atmo = Render.AreaAtmosphere.Of(Render.StackedRooms.Sector);
			Render.GroundMist.CameraAtmosphere = Render.AreaAtmosphere.IndexOf(Render.StackedRooms.Sector);
			Render.GroundMist.CameraRooms = Render.DynamicLights.RoomsByNearness(Render.StackedRooms.Sector);
			var (fogEnd, fogColour) = Render.AreaAtmosphere.FogOf(atmo);
			Render.EnhancedFog.SetArea(fogEnd, fogColour, atmo.Fade);
			Render.EnhancedFog.Update(delta);
		}
		// flying outside every sector there is nothing to walk from: all of the level is shown
		int walkFrom = Render.StackedRooms.Sector;
		if (_fly && _world.SectorOfPointB(World.Q2BX(camQ), World.Q2BY(camQ), World.Q2BZ(camQ), walkFrom) < 0)
			walkFrom = -1;
		Render.SectorVisibility.Update(_world, walkFrom, camQ,
			Coords.GodotToQuake(_cam.GlobalPosition - _cam.GlobalBasis.Z) - camQ, HalfFovAcross());
		_native?.SendSeen();
		_atmosphere.Update(_cam, camQ, Render.StackedRooms.Sector, delta);
		Perf.Mark("visibility");
		// the moving groups built once a game frame are drawn this far between it and the one before
		RenderingServer.GlobalShaderParameterSet(TickLerpName, _clock.Lerp);
		_native?.Draw(_clock.Lerp, _world.Clock);
		Perf.Mark("movers");
		// portals first: the sprites near the ones drawn get copies
		_portals.Draw(_cam);
		Perf.Mark("portals");
		_lights.Draw(_ghost == null && !_fly ? _player : null, Coords.GodotToQuake(_cam.GlobalPosition), _clock.Lerp);
		_sprites.Portals = _portals.Active;
		_sprites.Draw(_cam, _clock.Lerp);
		Perf.Mark("sprites");
		if (_world.LevelEnded && _play == null && _ghost == null && _player != null && !_menu.IsOpen && !_movies.Playing)
			EndOfLevelInput();
		// the player's HUD and weapon while the view is theirs
		bool ownView = _ghost == null && !_fly && _player != null;
		_hud.Update(ownView ? _player : null, _clock.Lerp, Coords.GodotToQuake(_cam.GlobalPosition));
		Perf.Mark("hud");
		// heard from the player (Blood: gMe's sprite), the camera only when the view isn't theirs
		_sound.Update(ownView ? _world.Collision.Interpolate(_player.PrevOrigin, _player.Origin, _clock.Lerp)
			: Coords.GodotToQuake(_cam.GlobalPosition), _cam.GlobalBasis);
		Perf.Mark("sound");
		Perf.EndFrame(delta, frames);
		BurstShot();
		MotionShots();
		double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		_perfOverlay.Record(delta * 1000, _tickTicks * toMs, (System.Diagnostics.Stopwatch.GetTimestamp() - frameStart) * toMs);
	}

	/// <summary>A modern map's Screen triggers: the sprites with one that the player has on the screen now (in a sector
	/// the view reaches, within the view).</summary>
	void MarkScreenSprites()
	{
		foreach (var e in _world.ScreenTriggerSprites())
		{
			int sec = _world.SpriteBSector(e);
			if (sec >= 0 && !Render.SectorVisibility.Seen(sec))
				continue;
			if (_cam.IsPositionInFrustum(Coords.QuakeToGodot(e.Origin)))
				_world.SeenOnScreen.Add(e);
		}
	}

	void TimedTick()
	{
		long t = System.Diagnostics.Stopwatch.GetTimestamp();
		_world.Tick();
		if (_world.IsModern)
			MarkScreenSprites();
		if (_world.Board.Reshaped.Count > 0)
			Render.StackedRooms.RefreshWalls(_world.Map);
		_native?.GameFrame(_world.Frame);
		_tickTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t;
	}

	// ---------------------------------------------------------------- end of level (levels.qc levelEndGameInput)
	/// <summary>On the stats screen: after a second, a fresh press of fire, jump or use goes on (the key that ended
	/// the level must be let go first).</summary>
	void EndOfLevelInput()
	{
		double now = Time.GetTicksMsec() / 1000.0;
		if (_endTime < 0)
			_endTime = now;
		var c = _controls.Build(_ghost == null && !_fly && !GameConsole.IsOpen && Client.Mouse.Captured);
		bool pressed = c.Fire || c.Jump || c.Use;
		if (now < _endTime + 1)
			_endKeysReleased = false;
		else if (!pressed)
			_endKeysReleased = true;
		else if (_endKeysReleased)
			GoNextLevel();
	}

	/// <summary>levelGoNext / levelEndLevel: the next map, with the player's inventory: the map's own nextmap /
	/// secretmap keys (worldspawn), else blood.ini's EndingA (normal exit) or EndingB (secret exit). None: the
	/// episode ends (NBlood: cutscene B, then back to the menu).</summary>
	void GoNextLevel()
	{
		string next = _world.NextLevel();
		if (next == "")
			next = Data.LevelInfo.NextMap(MapName, _world.ExitKind == 1) ?? "";
		if (next == "" || Data.Addons.BuildMapPath(next) == null)
		{
			EndEpisode();
			return;
		}
		_carry = World.CaptureCarry(_player);
		LoadMap(MapResPath(next));
	}

	/// <summary>levelPlayEndScene: the episode's cutscene B, then the credits over the title screen (the level stays
	/// paused behind them until a new game).</summary>
	void EndEpisode()
	{
		GameConsole.Print("The End.");
		_carry = null;
		var (e, _) = Data.LevelInfo.Find(MapName);
		var ep = e >= 0 ? Data.LevelInfo.AllEpisodes[e] : null;
		void Credits()
		{
			ShowTitle(false);
			_menu.OpenPage("credits", title: true);
		}
		if (ep != null && ep.CutB != "")
			PlayMovies(new[] { Cutscene(ep.CutB, ep.CutWavB) }, Credits);
		else
			Credits();
	}

	// ---------------------------------------------------------------- fly camera
	public override void _UnhandledInput(InputEvent e)
	{
		if (GameConsole.IsOpen)
			return;
		// a screenshot run (--shot, --views, --scripted) takes no mouse: a click or move over its window would turn its camera
		if (e is InputEventMouse && (_args.ContainsKey("shot") || _args.ContainsKey("views") || _args.ContainsKey("bugs") || _args.ContainsKey("scripted")))
			return;
		if (e is InputEventMouseMotion mm && Client.Mouse.Captured && _ghost == null)
		{
			if (_fly || _player == null)
			{
				_cam.RotateY(-mm.Relative.X * 0.003f);
				_cam.RotateObjectLocal(Vector3.Right, -mm.Relative.Y * 0.003f);
			}
			else
				_controls.MouseMotion(mm.Relative);
		}
		else if (e is InputEventMouseButton mb && mb.Pressed)
		{
			if (!Client.Mouse.Captured && !_menu.IsOpen)
				Client.Mouse.Capture();
			else if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
				_controls.Wheel(mb.ButtonIndex == MouseButton.WheelUp);
		}
		else if (e is InputEventKey k && k.Pressed)
		{
			if (k.Keycode == Key.Escape && !_menu.IsOpen)
				_menu.Open();
			else if (k.Keycode == Key.F12)
				GameConsole.Execute("screenshot");
			else if (k.Keycode == Key.F11)
				Client.PerfOverlay.Cycle();      // performance overlay: off, numbers, graph
			else if (k.Keycode is Key.F7 or Key.F8)
			{
				// testing toggles: F7 dynamic lights, F8 their shadows
				var cv = k.Keycode == Key.F7 ? Render.DynamicLights.Enabled : Render.DynamicLights.Shadows;
				cv.Set(cv.Bool ? "0" : "1");
				string msg = (k.Keycode == Key.F7 ? "Dynamic lights " : "Dynamic shadows ") + (cv.Bool ? "on" : "off");
				if (_player != null)
					_world.ViewSetMessage(_player, msg);
				else
					GameConsole.Print(msg);
			}
			else if (k.Keycode == Key.F10)
				GameConsole.Execute("r_compare");
			else if (k.Keycode == Key.F9)
			{
				// ambient occlusion: off, SSAO, GTAO
				var ao = Render.AmbientOcclusion.Mode;
				int next = ((int)ao.Float + 1) % 3;
				ao.Set(next.ToString());
				string msg = "Ambient occlusion " + (next == 1 ? "SSAO" : next == 2 ? "GTAO" : "off");
				if (_player != null)
					_world.ViewSetMessage(_player, msg);
				else
					GameConsole.Print(msg);
			}
			else if (k.Keycode is Key.Equal or Key.Plus or Key.KpAdd)     // = on US keyboards, + on Nordic ones
				GameConsole.Execute("sizeup");
			else if (k.Keycode is Key.Minus or Key.KpSubtract)
				GameConsole.Execute("sizedown");
			else if (!_fly)
				_controls.KeyPressed(k);
		}
	}

	void FlyCamera(double delta)
	{
		var dir = Vector3.Zero;
		var b = _cam.GlobalBasis;
		if (Input.IsKeyPressed(Key.W)) dir -= b.Z;
		if (Input.IsKeyPressed(Key.S)) dir += b.Z;
		if (Input.IsKeyPressed(Key.A)) dir -= b.X;
		if (Input.IsKeyPressed(Key.D)) dir += b.X;
		if (Input.IsKeyPressed(Key.Space)) dir += Vector3.Up;
		if (Input.IsKeyPressed(Key.C)) dir -= Vector3.Up;
		float speed = FlySpeed.Float / Coords.QuakeScale;
		if (Input.IsKeyPressed(Key.Shift))
			speed *= 3f;
		var from = _cam.GlobalPosition;
		_cam.GlobalPosition += dir.Normalized() * speed * (float)delta;
		// through a room-over-room link (a portal, a water surface) into the room beyond, as a sprite crossing it goes
		if (_world == null || dir == Vector3.Zero)
			return;
		var l = _world.Collision.LinkPathCross(Coords.GodotToQuake(from), Coords.GodotToQuake(_cam.GlobalPosition), out _);
		if (l == null)
			return;
		_cam.GlobalPosition += Coords.QuakeToGodot(l.Offset);
		// the camera's Build sector: the room beyond's (followed in plan, it stayed in the room over it: E1M8's link 20
		// into 249 under 243, 32 units across)
		if (_world.Map != null && _world.Map.Valid(l.FarSector))
		{
			var q = Coords.GodotToQuake(_cam.GlobalPosition);
			int far = _world.SectorFrom(l.FarSector, q, q);
			Render.StackedRooms.ForceSector(far >= 0 ? far : l.FarSector);
		}
	}
}
