// Chosen Blood module (GPL v2): the resource scripts (.RFS) of an NBlood add-on. Its launcher packs the files they name
// into a copy of BLOOD.RFF and SOUNDS.RFF with BARF; the extraction reads the scripts themselves instead.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Chosen.Shared.Blood.Files;

/// <summary>
/// A resource script: <c>resource "EVICTION\SEQ\HHI1.SEQ" as 6700;</c> puts a file in the archive under its name, type
/// (its extension) and id, <c>data "ZDIE1.SFX" as 12146: 180, 0x10000, 0x0, 1, -1, "ZDIE1";</c> an entry made of the
/// values (numbers as 32-bit, strings as their bytes and a zero). <c>//</c> starts a comment.
/// </summary>
public sealed class RfsScript
{
    /// <summary>A file entry: the path as written, its name and type (upper case), its id (-1: none).</summary>
    public sealed record Resource(string Path, string Name, string Type, int Id);

    /// <summary>A data entry: name, type, id and its values (a long, or a string).</summary>
    public sealed record Data(string Name, string Type, int Id, List<object> Values)
    {
        /// <summary>The entry as BARF writes it: each number 4 bytes, each string its bytes and a zero.</summary>
        public byte[] Bytes()
        {
            using var ms = new MemoryStream();
            foreach (var v in Values)
            {
                if (v is string s)
                {
                    ms.Write(Encoding.Latin1.GetBytes(s));
                    ms.WriteByte(0);
                }
                else
                    ms.Write(BitConverter.GetBytes(unchecked((int)(long)v)));
            }
            return ms.ToArray();
        }
    }

    public readonly List<Resource> Resources = new();
    public readonly List<Data> Datas = new();

    public static RfsScript Parse(string text)
    {
        var script = new RfsScript();
        foreach (var statement in Statements(text))
        {
            var t = Tokens(statement);
            if (t.Count < 2 || t[1].str == null)
                continue;
            string kind = t[0].word?.ToLowerInvariant();
            string file = t[1].str.Replace('/', '\\');
            string name = System.IO.Path.GetFileNameWithoutExtension(file.Replace('\\', '/')).ToUpperInvariant();
            string type = System.IO.Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
            int id = -1, at = 2;
            if (at + 1 < t.Count && string.Equals(t[at].word, "as", StringComparison.OrdinalIgnoreCase) && Number(t[at + 1].word, out long n))
            {
                id = (int)n;
                at += 2;
            }
            if (kind == "resource")
                script.Resources.Add(new Resource(file, name, type, id));
            else if (kind == "data")
            {
                var values = new List<object>();
                for (; at < t.Count; at++)
                {
                    if (t[at].str != null)
                        values.Add(t[at].str);
                    else if (Number(t[at].word, out long v))
                        values.Add(v);
                }
                script.Datas.Add(new Data(name, type, id, values));
            }
        }
        return script;
    }

    static bool Number(string s, out long v)
    {
        v = 0;
        if (string.IsNullOrEmpty(s))
            return false;
        bool neg = s[0] == '-';
        string body = neg || s[0] == '+' ? s[1..] : s;
        bool ok = body.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(body[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
            : long.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out v);
        if (neg)
            v = -v;
        return ok;
    }

    // the script cut at its semicolons, comments left out (neither inside a string)
    static IEnumerable<string> Statements(string text)
    {
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                sb.Append(c);
                if (c == '"' || c == '\n')
                    quoted = false;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
                sb.Append(' ');
                continue;
            }
            if (c == ';')
            {
                yield return sb.ToString();
                sb.Clear();
                continue;
            }
            if (c == '"')
                quoted = true;
            sb.Append(c);
        }
        if (sb.ToString().Trim() != "")
            yield return sb.ToString();
    }

    // a statement's words and strings (commas, colons and blanks between them)
    static List<(string word, string str)> Tokens(string s)
    {
        var o = new List<(string, string)>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == ',' || c == ':' || c == '@')
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                int e = s.IndexOf('"', i + 1);
                if (e < 0)
                    e = s.Length;
                o.Add((null, s[(i + 1)..e]));
                i = e + 1;
                continue;
            }
            int b = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',' && s[i] != ':' && s[i] != '"')
                i++;
            o.Add((s[b..i], null));
        }
        return o;
    }
}

/// <summary>What an NBlood add-on's folder brings beside its art and maps: the resources its scripts name (by type and
/// id, as its launcher packs them into the archives) and its loose files.</summary>
public sealed class ModResources
{
    /// <summary>A resource: name, type, id (-1: none) and its bytes.</summary>
    public sealed record Entry(string Name, string Type, int Id, byte[] Bytes);

    public readonly List<Entry> Entries = new();
    public readonly List<string> Problems = new();

    /// <summary>The entries of a type, in script order (a later one of the same id replaces the earlier in the archive).</summary>
    public IEnumerable<Entry> OfType(string type) => Entries.Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    /// <summary>The last entry of that name and type, or null.</summary>
    public Entry Find(string name, string type) =>
        Entries.LastOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every *.RFS of the folder read, in name order.</summary>
    public static ModResources Load(string folder)
    {
        var m = new ModResources();
        if (!Directory.Exists(folder))
            return m;
        foreach (var f in Directory.GetFiles(folder).Where(f => f.EndsWith(".rfs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f).ToUpperInvariant(), StringComparer.Ordinal))
        {
            var script = RfsScript.Parse(File.ReadAllText(f, Encoding.Latin1));
            foreach (var r in script.Resources)
            {
                if (Resolve(folder, r.Path) is { } path)
                    m.Entries.Add(new Entry(r.Name, r.Type, r.Id, File.ReadAllBytes(path)));
                else
                    m.Problems.Add($"{Path.GetFileName(f)}: no file {r.Path}");
            }
            foreach (var d in script.Datas)
                m.Entries.Add(new Entry(d.Name, d.Type, d.Id, d.Bytes()));
        }
        return m;
    }

    /// <summary>A script's path as a file: from the folder beside the add-on's (the scripts name the add-on's folder
    /// first: EVICTION\SEQ\X.SEQ), else from the add-on's own; names matched without case.</summary>
    public static string Resolve(string folder, string rel)
    {
        var parts = rel.Split('\\', '/').Where(p => p != "" && p != ".").ToArray();
        if (parts.Length == 0)
            return null;
        string parent = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('\\', '/'));
        if (parent != null && Walk(parent, parts) is { } a && a.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            return a;
        if (Walk(folder, parts) is { } b)
            return b;
        return parts.Length > 1 ? Walk(folder, parts[1..]) : null;
    }

    static string Walk(string root, string[] parts)
    {
        string at = root;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == ".." || !Directory.Exists(at))
                return null;
            bool last = i == parts.Length - 1;
            string next = (last ? Directory.GetFiles(at) : Directory.GetDirectories(at))
                .FirstOrDefault(p => Path.GetFileName(p).Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            if (next == null)
                return null;
            at = next;
        }
        return at;
    }
}
