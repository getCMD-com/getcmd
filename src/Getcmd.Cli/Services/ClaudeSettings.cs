using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Getcmd.Cli.Services;

/// <summary>
/// Adds and removes getcmd's PreToolUse entries in a Claude Code settings.json,
/// leaving everything else in the file as it was.
/// </summary>
internal static class ClaudeSettings
{
    public const string HookCommand = "getcmd hook claude";
    public const int TimeoutSeconds = 5;

    // Every tool matcher getcmd may install an entry for.
    private static readonly string[] AllMatchers = ["Bash", "PowerShell"];

    public static string UserSettingsPath => Path.Combine(AppPaths.UserHome, ".claude", "settings.json");

    public static string ProjectSettingsPath(string cwd) => Path.Combine(cwd, ".claude", "settings.json");

    /// <summary>The matchers to install: Claude Code only has a PowerShell tool on Windows.</summary>
    public static IReadOnlyList<string> MatchersFor(bool windows) => windows ? AllMatchers : ["Bash"];

    public static bool IsInstalled(string path, string matcher) =>
        File.Exists(path) && IsInstalled(Load(path), matcher);

    /// <summary>Adds an entry for each matcher that has none yet; returns the matchers added.</summary>
    public static List<string> Install(string path, IReadOnlyList<string> matchers)
    {
        var root = File.Exists(path) ? Load(path) : new JsonObject();
        var added = new List<string>();

        foreach (var matcher in matchers)
        {
            if (IsInstalled(root, matcher))
            {
                continue;
            }

            var hooks = GetOrAdd(root, "hooks", path, () => new JsonObject());
            var entries = GetOrAdd(hooks, "PreToolUse", path, () => new JsonArray());

            // Typed as JsonNode so this binds to the non-generic, AOT-safe Add.
            JsonNode entry = new JsonObject
            {
                ["matcher"] = matcher,
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = HookCommand,
                    ["timeout"] = TimeoutSeconds,
                }),
            };
            entries.Add(entry);
            added.Add(matcher);
        }

        if (added.Count > 0)
        {
            Save(path, root);
        }

        return added;
    }

    /// <summary>Removes getcmd's hook under every matcher; returns the matchers it was removed from.</summary>
    public static List<string> Uninstall(string path)
    {
        var removed = new List<string>();
        if (!File.Exists(path))
        {
            return removed;
        }

        var root = Load(path);
        if (root["hooks"] is not JsonObject hooks || hooks["PreToolUse"] is not JsonArray entries)
        {
            return removed;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (!IsOurMatcherEntry(entries[i], out var matcher, out var entryHooks))
            {
                continue;
            }

            var removedHere = false;
            for (var index = IndexOfOurHook(entryHooks); index >= 0; index = IndexOfOurHook(entryHooks))
            {
                entryHooks.RemoveAt(index);
                removedHere = true;
            }

            if (removedHere && !removed.Contains(matcher))
            {
                removed.Add(matcher);
            }

            // Only drop an entry that we emptied ourselves.
            if (removedHere && entryHooks.Count == 0)
            {
                entries.RemoveAt(i--);
            }
        }

        if (removed.Count == 0)
        {
            return removed;
        }

        if (entries.Count == 0)
        {
            hooks.Remove("PreToolUse");
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        Save(path, root);
        return removed;
    }

    private static bool IsInstalled(JsonObject root, string matcher)
    {
        if (root["hooks"] is not JsonObject hooks || hooks["PreToolUse"] is not JsonArray entries)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (IsOurMatcherEntry(entry, out var entryMatcher, out var entryHooks)
                && entryMatcher == matcher
                && IndexOfOurHook(entryHooks) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // True for a PreToolUse entry whose matcher is one getcmd installs under.
    private static bool IsOurMatcherEntry(JsonNode? entry, out string matcher, out JsonArray entryHooks)
    {
        matcher = "";
        entryHooks = null!;
        if (entry is JsonObject entryObject
            && StringValue(entryObject["matcher"]) is { } value
            && Array.IndexOf(AllMatchers, value) >= 0
            && entryObject["hooks"] is JsonArray array)
        {
            matcher = value;
            entryHooks = array;
            return true;
        }

        return false;
    }

    private static int IndexOfOurHook(JsonArray entryHooks)
    {
        for (var i = 0; i < entryHooks.Count; i++)
        {
            if (entryHooks[i] is JsonObject hook && StringValue(hook["command"]) == HookCommand)
            {
                return i;
            }
        }

        return -1;
    }

    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static T GetOrAdd<T>(JsonObject parent, string key, string path, Func<T> create)
        where T : JsonNode
    {
        switch (parent[key])
        {
            case T existing:
                return existing;
            case null:
                var created = create();
                parent[key] = created;
                return created;
            default:
                throw new InvalidDataException($"{path}: \"{key}\" has an unexpected shape; not modifying the file");
        }
    }

    private static JsonObject Load(string path)
    {
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonNode.Parse(text, documentOptions: options) as JsonObject
            ?? throw new InvalidDataException($"{path}: expected a JSON object; not modifying the file");
    }

    private static void Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a sibling file first so a failure cannot leave settings.json half-written.
        var temp = path + ".getcmd-tmp";
        using (var stream = File.Create(temp))
        {
            var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                root.WriteTo(writer);
            }

            stream.WriteByte((byte)'\n');
        }

        File.Move(temp, path, overwrite: true);
    }
}
