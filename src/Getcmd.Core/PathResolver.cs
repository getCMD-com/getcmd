namespace Getcmd.Core;

public enum PathScope { Root, Home, Outside, Inside, BuildDir, Unknown }

/// <summary>
/// Classifies a command argument by where it points relative to the working
/// directory and home. Pure string handling: the filesystem is never touched.
/// </summary>
public static class PathResolver
{
    private static readonly string[] KnownNames = ["node_modules", "build", "dist", "target", "bin", "obj"];

    public static PathScope Classify(string arg, string cwd, string home, IReadOnlyList<string> buildDirs)
    {
        ArgumentNullException.ThrowIfNull(arg);
        ArgumentNullException.ThrowIfNull(cwd);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(buildDirs);

        if (IsNeverPath(arg))
        {
            return PathScope.Unknown;
        }

        // Globs are classified by the directory part before the first wildcard.
        var glob = arg.AsSpan().IndexOfAny('*', '?', '[');
        if (glob >= 0)
        {
            arg = arg[..(arg.AsSpan(0, glob).LastIndexOfAny('/', '\\') + 1)];
        }

        var homeRelative = false;
        if (arg == "~" || arg.StartsWith("~/", StringComparison.Ordinal) || arg.StartsWith("~\\", StringComparison.Ordinal))
        {
            arg = arg[1..];
            homeRelative = true;
        }
        else if (HomeVariableLength(arg) is > 0 and var length)
        {
            arg = arg[length..];
            homeRelative = true;
        }
        else if (arg.StartsWith('~'))
        {
            // "~user": someone else's home.
            return PathScope.Outside;
        }

        if (arg.Contains('$') || arg.Contains('`'))
        {
            // Depends on a variable or substitution we cannot resolve.
            return PathScope.Unknown;
        }

        var windows = IsDrivePath(arg) || IsDrivePath(cwd) || IsDrivePath(home);
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var cwdPath = Resolve(cwd, null, windows);
        var homePath = Resolve(home, null, windows);
        var path = Resolve(arg, homeRelative ? homePath : cwdPath, windows, forceRelative: homeRelative);

        if (path.Count == 0 || (path.Count == 1 && IsDriveSegment(path[0])))
        {
            return PathScope.Root;
        }

        if (path.Count == homePath.Count && StartsWith(path, homePath, comparison))
        {
            return PathScope.Home;
        }

        if (StartsWith(path, cwdPath, comparison))
        {
            return path.Count > cwdPath.Count && ContainsName(buildDirs, path[cwdPath.Count])
                ? PathScope.BuildDir
                : PathScope.Inside;
        }

        if (path.Count == homePath.Count + 1 && StartsWith(path, homePath, comparison))
        {
            return PathScope.Home;
        }

        return PathScope.Outside;
    }

    public static bool LooksLikePath(string arg)
    {
        ArgumentNullException.ThrowIfNull(arg);

        return !IsNeverPath(arg)
            && (HasPathPrefix(arg) || arg.AsSpan().IndexOfAny('/', '\\') >= 0 || ContainsName(KnownNames, arg));
    }

    // Flags, URLs and remote targets (user@host, host:path, image:tag) are
    // never local paths. Neither is the empty string.
    private static bool IsNeverPath(string arg)
    {
        if (arg.Length == 0 || arg[0] == '-' || arg.Contains("://", StringComparison.Ordinal))
        {
            return true;
        }

        if (HasPathPrefix(arg))
        {
            return false;
        }

        var separator = arg.AsSpan().IndexOfAny('/', '\\');
        var marker = arg.AsSpan().IndexOfAny('@', ':');
        return marker >= 0 && (separator < 0 || marker < separator);
    }

    private static bool HasPathPrefix(string arg) =>
        arg[0] is '/' or '~' or '.' || IsDrivePath(arg) || HomeVariableLength(arg) > 0;

    // Splits a path into normalised segments. A drive, whether written "C:\" or
    // Git Bash style "/c/", becomes a leading "c:" segment. Relative paths are
    // resolved against baseSegments.
    private static List<string> Resolve(string path, List<string>? baseSegments, bool windows, bool forceRelative = false)
    {
        var s = path.Replace('\\', '/');
        var segments = new List<string>();

        if (forceRelative)
        {
            segments.AddRange(baseSegments!);
        }
        else if (IsDrivePath(s))
        {
            segments.Add(DriveSegment(s[0]));
            s = s[2..];
        }
        else if (s.StartsWith('/'))
        {
            if (windows && s.Length >= 2 && char.IsAsciiLetter(s[1]) && (s.Length == 2 || s[2] == '/'))
            {
                segments.Add(DriveSegment(s[1]));
                s = s[2..];
            }
        }
        else if (baseSegments is not null)
        {
            segments.AddRange(baseSegments);
        }

        foreach (var part in s.Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (segments.Count > 0 && !IsDriveSegment(segments[^1]))
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(part);
        }

        return segments;
    }

    private static bool StartsWith(List<string> path, List<string> prefix, StringComparison comparison)
    {
        if (path.Count < prefix.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(path[i], prefix[i], comparison))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsName(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // "C:", "C:\..." or "C:/...".
    private static bool IsDrivePath(string path) =>
        path.Length >= 2
        && char.IsAsciiLetter(path[0])
        && path[1] == ':'
        && (path.Length == 2 || path[2] is '/' or '\\');

    private static string DriveSegment(char letter) => $"{char.ToLowerInvariant(letter)}:";

    private static bool IsDriveSegment(string segment) => segment.Length == 2 && segment[1] == ':';

    // Length of a leading "$HOME" or "${HOME}" that is a whole path segment, else 0.
    private static int HomeVariableLength(string arg)
    {
        var length = arg.StartsWith("${HOME}", StringComparison.Ordinal) ? 7
            : arg.StartsWith("$HOME", StringComparison.Ordinal) ? 5
            : 0;

        return length > 0 && (arg.Length == length || arg[length] is '/' or '\\') ? length : 0;
    }
}
