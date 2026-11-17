// Chosen Blood module (GPL v2): finding a Blood install on this computer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Chosen.Shared.Blood.Files;

/// <summary>Where Blood is installed: Steam's library folders (Blood: Refreshed Supply, then One Unit Whole Blood), GOG's
/// usual folders; a folder counts when it holds BLOOD.RFF and TILES000.ART.</summary>
public static class BloodInstall
{
    static readonly string[] SteamNames = { "Blood - Refreshed Supply", "One Unit Whole Blood", "Blood" };

    public static bool IsValid(string folder) =>
        !string.IsNullOrEmpty(folder) && Directory.Exists(folder) && FindFile(folder, "BLOOD.RFF") != null && FindFile(folder, "TILES000.ART") != null;

    /// <summary>A file in folder by name, case-insensitively (the install's names vary: sounds.rff, SOUNDS.RFF).</summary>
    public static string FindFile(string folder, string name) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary>The first valid install found, or null.</summary>
    public static string Find() => Candidates().FirstOrDefault(IsValid);

    public static IEnumerable<string> Candidates()
    {
        foreach (var lib in SteamLibraries())
            foreach (var n in SteamNames)
                yield return Path.Combine(lib, "steamapps", "common", n);
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"C:\GOG Games", @"D:\GOG Games" })
            if (!string.IsNullOrEmpty(root))
            {
                yield return Path.Combine(root, "GOG Galaxy", "Games", "Blood Fresh Supply");
                yield return Path.Combine(root, "Blood Fresh Supply");
                yield return Path.Combine(root, "One Unit Whole Blood");
            }
    }

    /// <summary>Steam's library folders: its own folder and those libraryfolders.vdf lists.</summary>
    static IEnumerable<string> SteamLibraries()
    {
        var steam = new List<string>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            if (!string.IsNullOrEmpty(root))
                steam.Add(Path.Combine(root, "Steam"));
        var libs = new List<string>(steam);
        foreach (var s in steam)
        {
            string vdf = Path.Combine(s, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return libs.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
