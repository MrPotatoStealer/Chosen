// Chosen core: original code. Chosen's version (VERSION at the repository's root, Directory.Build.props).
using System.Reflection;

namespace Chosen.Shared.Engine;

/// <summary>The version every Chosen program shares: Number ("0.1.0") and Full, what the programs show and their
/// reports carry ("0.1.0+1a2b3c4" from a release build, "0.1.0-dev+1a2b3c4" from any other).</summary>
public static class ChosenVersion
{
    static readonly Assembly Self = typeof(ChosenVersion).Assembly;

    public static string Number { get; } = Self.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public static string Full { get; } =
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Number;
}
