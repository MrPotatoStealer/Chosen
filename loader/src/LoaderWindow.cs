// Chosen Blood module (GPL v2): ChosenLoader's window: Chosen's data folder made from a Blood install
// (shared/Chosen.Blood/Files/Extractor.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chosen.Shared.Blood.Files;
using Godot;

namespace Chosen.Loader;

/// <summary>
/// ChosenLoader: finds the Blood install (Steam: Blood: Refreshed Supply, One Unit Whole Blood; GOG) or lets the player
/// pick it, offers its add-ons, and extracts what Chosen needs into data/ beside Chosen.exe (in development: the
/// repository's game/blood3d), with a progress bar. Run again after an update: unchanged files are kept.
/// Mods made for NBlood (Eviction): Add a Mod Folder takes the mod's folder or the one it was unpacked into; the data
/// folder remembers where each came from (addon.txt, "source"), so they are listed again the next time.
/// Command line: --blood=&lt;folder&gt; --out=&lt;folder&gt; --addons=all|none|id,id --mod=&lt;folder&gt; (a mod made for NBlood,
/// anywhere on disk; may be given more than once) --auto (extract at once, then quit).
/// </summary>
public partial class LoaderWindow : Control
{
	LineEdit _blood, _out;
	Label _state;
	VBoxContainer _addonBox;
	readonly List<(CheckBox box, Addon addon)> _addons = new();
	// mods made for NBlood (a folder with an .ini, maps, perhaps .RFS scripts and custom dudes) the player pointed at
	readonly List<string> _mods = new();
	Button _extract, _play;
	ProgressBar _bar;
	RichTextLabel _log;
	bool _busy, _cancel;

	static string DefaultOut() => OS.HasFeature("editor")
		? ProjectSettings.GlobalizePath("res://").PathJoin("../game/blood3d").SimplifyPath()
		: OS.GetExecutablePath().GetBaseDir().PathJoin("data");

	static string ChosenExe() => OS.GetExecutablePath().GetBaseDir().PathJoin("Chosen.exe");

	public override void _Ready()
	{
		var args = OS.GetCmdlineUserArgs();
		string Arg(string k) => args.FirstOrDefault(a => a.StartsWith("--" + k + "="))?[(k.Length + 3)..];
		BuildUi();
		_out.Text = Arg("out") ?? DefaultOut();
		// the mods this data folder was made with before, then those named now
		AddMods(Addon.InstalledMods(_out.Text));
		foreach (var a in args)
			if (a.StartsWith("--mod="))
				AddMods(Addon.ModsIn(a[6..]));
		string blood = Arg("blood") ?? BloodInstall.Find() ?? "";
		UseBlood(blood);
		if (Arg("addons") is { } want)
			foreach (var (box, a) in _addons)
				box.ButtonPressed = want == "all" || (want != "none" && want.Split(',').Any(x => x.Equals(a.Id, StringComparison.OrdinalIgnoreCase)));
		if (args.Contains("--auto"))
			Extract(true);
	}

	void BuildUi()
	{
		var bg = new Panel();
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);
		var m = new MarginContainer();
		m.SetAnchorsPreset(LayoutPreset.FullRect);
		foreach (var side in new[] { "left", "right", "top", "bottom" })
			m.AddThemeConstantOverride("margin_" + side, 16);
		AddChild(m);
		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 10);
		m.AddChild(v);

		var title = new Label { Text = "ChosenLoader " + Chosen.Shared.Engine.ChosenVersion.Number, TooltipText = Chosen.Shared.Engine.ChosenVersion.Full };
		title.AddThemeFontSizeOverride("font_size", 26);
		v.AddChild(title);
		v.AddChild(new Label
		{
			Text = "Chosen plays Blood from your own copy of the game. Point this at your Blood install (Blood: Fresh Supply / Refreshed Supply, "
				+ "or One Unit Whole Blood) and press Extract: what Chosen needs is copied and converted into its data folder.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		});

		v.AddChild(new Label { Text = "Blood folder" });
		var row = new HBoxContainer();
		v.AddChild(row);
		_blood = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "the folder with BLOOD.RFF and TILES000.ART" };
		_blood.TextSubmitted += UseBlood;
		_blood.FocusExited += () => UseBlood(_blood.Text);
		row.AddChild(_blood);
		var browse = new Button { Text = "Browse..." };
		browse.Pressed += () => Pick(UseBlood);
		row.AddChild(browse);
		var find = new Button { Text = "Find" };
		find.Pressed += () => UseBlood(BloodInstall.Find() ?? "");
		row.AddChild(find);
		_state = new Label();
		v.AddChild(_state);

		var arow = new HBoxContainer();
		v.AddChild(arow);
		arow.AddChild(new Label { Text = "Add-ons and mods to install", SizeFlagsHorizontal = SizeFlags.ExpandFill });
		var addMod = new Button { Text = "Add a Mod Folder...", TooltipText = "A mod made for NBlood (Eviction): its folder with the .ini and maps, or the folder it was unpacked into, anywhere on disk" };
		addMod.Pressed += () => Pick(p =>
		{
			var found = Addon.ModsIn(p);
			if (found.Count == 0)
			{
				Say("[color=#ff8866]No mod in " + p + ": its folder holds an .ini naming its maps, and the maps.[/color]");
				return;
			}
			AddMods(found);
			UseBlood(_blood.Text);
			foreach (var f in found)
				Say($"Mod {Path.GetFileName(f)} added ({f}): press Extract to install it.");
		});
		arow.AddChild(addMod);
		_addonBox = new VBoxContainer();
		v.AddChild(_addonBox);

		v.AddChild(new Label { Text = "Chosen's data folder" });
		var orow = new HBoxContainer();
		v.AddChild(orow);
		_out = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		orow.AddChild(_out);
		var obrowse = new Button { Text = "Browse..." };
		obrowse.Pressed += () => Pick(p =>
		{
			_out.Text = p;
			AddMods(Addon.InstalledMods(p));
			UseBlood(_blood.Text);
		});
		orow.AddChild(obrowse);

		var brow = new HBoxContainer();
		v.AddChild(brow);
		_extract = new Button { Text = "Extract", CustomMinimumSize = new Vector2(140, 36) };
		_extract.Pressed += () =>
		{
			if (_busy)
				_cancel = true;
			else
				Extract(false);
		};
		brow.AddChild(_extract);
		_play = new Button { Text = "Play Chosen", CustomMinimumSize = new Vector2(140, 36), Visible = File.Exists(ChosenExe()) };
		_play.Pressed += () =>
		{
			OS.CreateProcess(ChosenExe(), Array.Empty<string>());
			GetTree().Quit();
		};
		brow.AddChild(_play);
		_bar = new ProgressBar { SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxValue = 1, Step = 0.001, CustomMinimumSize = new Vector2(0, 36) };
		brow.AddChild(_bar);

		_log = new RichTextLabel { SizeFlagsVertical = SizeFlags.ExpandFill, ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = true };
		v.AddChild(_log);
	}

	void Pick(Action<string> then)
	{
		var d = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true };
		d.DirSelected += p => then(p);
		AddChild(d);
		d.PopupCentered(new Vector2I(800, 500));
	}

	void AddMods(IEnumerable<string> folders)
	{
		foreach (var f in folders)
			if (!_mods.Contains(f, StringComparer.OrdinalIgnoreCase))
				_mods.Add(f);
	}

	void UseBlood(string folder)
	{
		_blood.Text = folder ?? "";
		foreach (var c in _addonBox.GetChildren())
			c.QueueFree();
		_addons.Clear();
		bool ok = BloodInstall.IsValid(folder);
		_state.Text = ok ? "Blood found." : string.IsNullOrEmpty(folder) ? "Blood wasn't found: choose its folder." : "That folder holds no BLOOD.RFF and TILES000.ART.";
		_state.Modulate = ok ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f);
		if (_extract != null)
			_extract.Disabled = !ok;
		if (!ok)
			return;
		foreach (var a in Addon.All(folder, _mods))
		{
			var box = new CheckBox { Text = $"{a.Name} ({a.Maps.Count} levels{(a.Mod ? ", mod" : "")})", ButtonPressed = true, TooltipText = a.Folder };
			_addonBox.AddChild(box);
			_addons.Add((box, a));
		}
		if (_addons.Count == 0)
			_addonBox.AddChild(new Label { Text = "(none in this install)" });
	}

	void Say(string s) => _log.AppendText(s + "\n");

	void Extract(bool quitAfter)
	{
		if (!BloodInstall.IsValid(_blood.Text))
			return;
		_busy = true;
		_cancel = false;
		_extract.Text = "Stop";
		_log.Clear();
		var x = new Extractor
		{
			BloodDir = _blood.Text, OutDir = _out.Text,
			Addons = _addons.Where(t => t.box.ButtonPressed).Select(t => t.addon.Id).ToList(),
			// (a mod folder left unticked is left out)
			Mods = _mods.Where(m => _addons.Any(t => t.box.ButtonPressed && string.Equals(Path.GetFullPath(t.addon.Folder).TrimEnd('/', '\\'),
				Path.GetFullPath(m).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))).ToList(),
			Progress = (f, what) => Callable.From(() =>
			{
				_bar.Value = f;
				Say($"{f * 100:0}%  {what}");
			}).CallDeferred(),
			Log = s => Callable.From(() => Say("      " + s)).CallDeferred(),
			Cancelled = () => _cancel,
		};
		Say($"from {x.BloodDir}\ninto {x.OutDir}");
		Task.Run(() =>
		{
			string error = null;
			try
			{
				x.Run();
			}
			catch (OperationCanceledException)
			{
				error = "stopped";
			}
			catch (Exception e)
			{
				error = e.Message;
			}
			Callable.From(() => Done(error, quitAfter)).CallDeferred();
		});
	}

	void Done(string error, bool quitAfter)
	{
		_busy = false;
		_extract.Text = "Extract";
		if (error == null)
		{
			Say(File.Exists(ChosenExe()) ? "Ready: press Play Chosen." : "Ready.");
			_play.Visible = File.Exists(ChosenExe());
		}
		else
			Say("[color=#ff8866]" + (error == "stopped" ? "Stopped." : "Failed: " + error) + "[/color]");
		if (quitAfter)
			GetTree().Quit(error == null ? 0 : 1);
	}
}
