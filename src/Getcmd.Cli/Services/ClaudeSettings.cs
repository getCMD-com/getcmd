using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Getcmd.Cli.Services;

/// <summary>
/// Adds and removes getcmd's PreToolUse entry in a Claude Code settings.json,
/// leaving everything else in the file as it was.
/// </summary>
internal static class ClaudeSettings
{
    public const string Matcher = "Bash";
    public const string HookCommand = "getcmd hook claude";
    public const int TimeoutSeconds = 5;

    public static string UserSettingsPath => Path.Combine(AppPaths.UserHome, ".claude", "settings.json");

    public static string ProjectSettingsPath(string cwd) => Path.Combine(cwd, ".claude", "settings.json");

    public static bool IsInstalled(string path)
    {
        if (!File.Exists(path) || Load(path)["hooks"] is not JsonObject hooks || hooks["PreToolUse"] is not JsonArray entries)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (IsBashEntry(entry, out var entryHooks) && IndexOfOurHook(entryHooks) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns false when the entry was already there.</summary>
    public static bool Install(string path)
    {
        if (IsInstalled(path))
        {
            return false;
        }

        var root = File.Exists(path) ? Load(path) : new JsonObject();
        var hooks = GetOrAdd(root, "hooks", path, () => new JsonObject());
        var entries = GetOrAdd(hooks, "PreToolUse", path, () => new JsonArray());
        // Typed as JsonNode so this binds to the non-generic, AOT-safe Add.
        JsonNode entry = new JsonObject
        {
            ["matcher"] = Matcher,
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = HookCommand,
                ["timeout"] = TimeoutSeconds,
            }),
        };
        entries.Add(entry);

        Save(path, root);
        return true;
    }

    /// <summary>Returns false when there was nothing to remove.</summary>
    public static bool Uninstall(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var root = Load(path);
        if (root["hooks"] is not JsonObject hooks || hooks["PreToolUse"] is not JsonArray entries)
        {
            return false;
        }

        var removed = false;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (!IsBashEntry(entries[i], out var entryHooks))
            {
                continue;
            }

            var removedHere = false;
            for (var index = IndexOfOurHook(entryHooks); index >= 0; index = IndexOfOurHook(entryHooks))
            {
                entryHooks.RemoveAt(index);
                removedHere = true;
            }

            // Only drop an entry that we emptied ourselves.
            if (removedHere && entryHooks.Count == 0)
            {
                entries.RemoveAt(i);
            }

            removed |= removedHere;
        }

        if (!removed)
        {
            return false;
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
        return true;
    }

    private static bool IsBashEntry(JsonNode? entry, out JsonArray entryHooks)
    {
        entryHooks = null!;
        if (entry is JsonObject entryObject
            && StringValue(entryObject["matcher"]) == Matcher
            && entryObject["hooks"] is JsonArray array)
        {
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
