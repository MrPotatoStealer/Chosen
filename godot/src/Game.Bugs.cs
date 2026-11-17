using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chosen.Core;
using Chosen.Data;
using Chosen.Sim;
using Godot;

namespace Chosen;

/// <summary>
/// Bug reports, as the DarkPlaces version made them: "reportbug &lt;what is wrong&gt;" writes &lt;id&gt;.txt (where you are,
/// what you aim at), &lt;id&gt;.png (the view without the console) and &lt;id&gt;.rec (the level's input recording) to
/// game/blood3d/data/bugs, where pipeline/bugs.py import picks them up into bugs/&lt;id&gt;/. "gotobug &lt;id&gt;" returns
/// to a report's spot. Cheats run through RunCheat so the recording has them.
/// </summary>
public partial class Game
{
	static string BugInbox => BloodData.Folder.PathJoin("data/bugs");

	/// <summary>A cheat from the console (a: the command and its arguments), recorded before the next frame.</summary>
	void RunCheat(string[] a)
	{
		string rest = string.Join(' ', a[1..]);
		_recorder?.Command(rest == "" ? a[0] : a[0] + " " + rest);
		_world.CheatCommand(_player, a[0], rest);
	}

	static string N(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
	static string V(Vector3 v) => $"{N(v.X)} {N(v.Y)} {N(v.Z)}";

	/// <summary>The Quake forward vector of the player's view.</summary>
	Vector3 ViewForward()
	{
		float p = Mathf.DegToRad(_controls.Pitch), y = Mathf.DegToRad(_controls.Yaw);
		return new Vector3(Mathf.Cos(p) * Mathf.Cos(y), Mathf.Cos(p) * Mathf.Sin(y), -Mathf.Sin(p));
	}

	string BuildReport(string id, string description)
	{
		var cam = Coords.GodotToQuake(_cam.GlobalPosition);
		var fwd = ViewForward();
		var col = _world.Collision;
		var down = col.Trace(cam, Vector3.Zero, Vector3.Zero, cam - new Vector3(0, 0, 4096), Sim.Collision.Contents.MaskMove);
		var aim = col.Trace(cam, Vector3.Zero, Vector3.Zero, cam + fwd * 8192, Sim.Collision.Contents.MaskShot);
		string aimObject = aim.Entity switch
		{
			null => "world",
			Sprite s => $"{s} type {s.Type} pic {s.Picnum} stat {s.StatNum}",
			var other => other.ToString(),
		};
		var o = _player.Origin;
		float floor = down.Fraction < 1 ? down.EndPos.Z : o.Z;
		var size = DisplayServer.WindowGetSize();
		bool full = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
		var lines = new List<string>
		{
			"id: " + id,
			"description: " + description,
			"map: " + _level.MapPath.Replace("res://", ""),
			"engine: godot",
			"version: " + Shared.Engine.ChosenVersion.Full,
			"date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
			"frame: " + _recorder.Frame,
			"camera: " + V(cam),
			$"angles: {N(_controls.Pitch)} {N(_controls.Yaw)} 0.00",
			"forward: " + V(fwd),
			"floor_below: " + N(floor),
			"aim_hit: " + V(aim.EndPos),
			"aim_normal: " + V(aim.Normal),
			"aim_distance: " + (aim.Fraction * 8192).ToString("0.0", CultureInfo.InvariantCulture),
			"aim_object: " + aimObject,
			"aim_texture: " + (aim.Texture ?? ""),
			"player_origin: " + V(o),
			"player_health: " + Math.Floor(_player.XHealth / 16),
			$"video: {size.X}x{size.Y} fullscreen {(full ? 1 : 0)} hudsize {Cvars.Find("b3d_hudsize")?.Value}",
			// (with the player's Build sector: where rooms share space a spot alone doesn't say which)
			string.Format(CultureInfo.InvariantCulture, "goto: warp {0:0.0} {1:0.0} {2:0.0} {3:0.0} {4:0.0}{5}", o.X, o.Y, floor, _controls.Yaw, _controls.Pitch,
				_world.SpriteBSector(_player) is int bs && bs >= 0 ? " " + bs : ""),
			$"screenshot: data/bugs/{id}.png",
			$"recording: data/bugs/{id}.rec (play {id})",
		};
		return string.Join("\n", lines) + "\n";
	}

	async System.Threading.Tasks.Task ReportBug(string description)
	{
		if (_player == null || _recorder == null || _cam == null)
		{
			GameConsole.Print("reportbug: no level is being played");
			return;
		}
		string id = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		string dir = BugInbox;
		Directory.CreateDirectory(dir);
		// what the report says is the moment it was asked for; the screenshot follows once the console is gone
		File.WriteAllText(Path.Combine(dir, id + ".txt"), BuildReport(id, description), new UTF8Encoding(false));
		_recorder.Save(Path.Combine(dir, id + ".rec"), id);
		GameConsole.Show(false);
		for (int i = 0; i < 3; i++)
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Render.HdrOutput.Picture(GetViewport()).SavePng(Path.Combine(dir, id + ".png"));
		GameConsole.Show(true);
		GameConsole.Print($"bug {id} saved to {dir} ({_recorder.Frame} frames recorded)");
	}

	/// <summary>--report: file a report where the replay stops, then quit.</summary>
	async void ReportAndQuit(string description)
	{
		// the camera catches up with the frames run since the last draw first
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ReportBug(description);
		GetTree().Quit();
	}

	/// <summary>A report's file: bugs/&lt;id&gt;/report.txt in the repository, else the inbox's &lt;id&gt;.txt.</summary>
	static string BugReportPath(string id)
	{
		var p = ProjectSettings.GlobalizePath("res://").PathJoin("../bugs").PathJoin(id).PathJoin("report.txt");
		return File.Exists(p) ? p : Path.Combine(BugInbox, id + ".txt");
	}

	/// <summary>
	/// --bugs=&lt;file&gt; [--bugshots=&lt;dir&gt;]: a screenshot at each bug report of a list, for pipeline/bugs.py regress. A
	/// line per bug: "id|setup|seconds[|commands before the shot]", the console commands that bring the level into the bug's state (a door
	/// opened: send s136 on) and how long to wait after them in game time (default 3 s: the doors Blood stores open
	/// have closed). Each bug starts from a restarted level, at the report's spot (gotobug; with a setup, again after its
	/// wait: what moved may have let the player fall), and is saved as
	/// dir/&lt;id&gt;.png; then the game quits. --exec runs first (monsters off, no HUD). So the pictures repeat from run
	/// to run: a fixed random seed, the wait counted in game frames, light effects at their base shades, no
	/// messages. Dynamic lights and shadows are as the setup sets them (regress: on for the dynamic-lights category,
	/// off for the rest).
	/// </summary>
	async void BugBatch(string file, string dir)
	{
		var lines = File.ReadAllLines(file);
		DirAccess.MakeDirRecursiveAbsolute(dir);
		World.FreezeLightFx = true;
		FixedSeed = 1;
		Render.Hud.HideMessages = true;
		for (int i = 0; i < 5; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		int n = 0;
		foreach (var line in lines)
		{
			var p = line.Split('|');
			if (p[0].Trim() == "")
				continue;
			string id = p[0].Trim();
			GameConsole.Execute("restart");
			GotoBug(id);
			if (p.Length > 1 && p[1].Trim() != "")
				GameConsole.Execute(p[1]);
			int frames = (int)((p.Length > 2 && p[2].Trim() != "" ? F(p[2]) : 3f) * GameClock.FramesPerSecond);
			while (_clock.Frame < frames)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			// with a setup, the spot again once the level is in the bug's state: a sector that has moved since (E1M1's
			// crypt, closed at the start, opens) can have had the player fall from where it was before
			if (p.Length > 1 && p[1].Contains("send "))     // (regress puts the light cvars in every setup)
				GotoBug(id);
			if (p.Length > 3 && p[3].Trim() != "")
				GameConsole.Execute(p[3]);      // debugging: commands just before the shot (sprat, stacks)
			// the game held while the view catches up: frames drawn slowly (the window in the background, a busy
			// machine) ran more game time, and a player put back over an opened coffin fell further into it
			// (drawn at the last game frame: between two, a player on a moving lid stood wherever the fraction had it)
			_paused = true;
			_clock.AtFrame();
			for (int i = 0; i < 3; i++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			Render.HdrOutput.Picture(GetViewport()).SavePng($"{dir}/{id}.png");
			_paused = false;
			n++;
		}
		GameConsole.Print($"saved {n} bug views in {ProjectSettings.GlobalizePath(dir)}");
		GetTree().Quit();
	}

	void GotoBug(string id)
	{
		var path = BugReportPath(id);
		if (!File.Exists(path))
		{
			GameConsole.Print($"gotobug: no report {id}");
			return;
		}
		var r = new Dictionary<string, string>();
		foreach (var line in File.ReadLines(path))
		{
			int c = line.IndexOf(": ", StringComparison.Ordinal);
			if (c > 0)
				r[line[..c]] = line[(c + 2)..];
		}
		if (!r.TryGetValue("goto", out var go))
		{
			GameConsole.Print($"gotobug: report {id} has no goto line");
			return;
		}
		// DarkPlaces reports say "cmd warp ..." (a server command) and name the map's .bsp
		if (go.StartsWith("cmd "))
			go = go[4..];
		var map = Path.GetFileNameWithoutExtension(r.GetValueOrDefault("map", ""));
		if (map != "" && !map.Equals(Path.GetFileNameWithoutExtension(_level.MapPath), StringComparison.OrdinalIgnoreCase))
			LoadMap(MapResPath(map));
		_fly = false;
		_ghost = null;
		if (_sprites != null)
			_sprites.ViewSprite = _player;
		GameConsole.Execute(go);
		GameConsole.Print($"{id}: {r.GetValueOrDefault("description", "")}");
	}
}
