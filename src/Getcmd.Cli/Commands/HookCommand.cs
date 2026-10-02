using System.Diagnostics;
using System.Text.Json;
using Getcmd.Cli.Services;
using Getcmd.Core;
using Action = Getcmd.Core.Action;

namespace Getcmd.Cli.Commands;

/// <summary>getcmd hook claude: the Claude Code PreToolUse hook, and its installer.</summary>
internal static class HookCommand
{
    private const string Agent = "claude";

    /// <summary>
    /// Reads the hook payload from stdin and answers per the hook protocol.
    /// Never throws: a failure here must not break the agent.
    /// </summary>
    public static int Run(Stream stdin, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("GETCMD_DISABLE") == "1")
            {
                return 0;
            }

            var started = Stopwatch.GetTimestamp();
            var input = JsonSerializer.Deserialize(stdin, HookJsonContext.Default.HookInput);
            var command = input?.ToolInput?.Command;
            if (input?.ToolName != "Bash" || string.IsNullOrWhiteSpace(command))
            {
                return 0;
            }

            var paths = AppPaths.Resolve();
            var config = ConfigService.Load(paths);
            var cwd = string.IsNullOrEmpty(input.Cwd) ? Environment.CurrentDirectory : input.Cwd;
            var decision = RuleEngine.Evaluate(command, ConfigService.ToContext(config, cwd), ConfigService.ActiveRules(config));
            var level = Names.Of(decision.Level);

            try
            {
                using var log = DecisionLog.Open(paths);
                log.Append(
                    new LogEntry(
                        0,
                        DecisionLog.Timestamp(DateTime.UtcNow),
                        Agent,
                        input.SessionId,
                        cwd,
                        command,
                        level,
                        Names.Of(decision.Action),
                        decision.RuleId,
                        decision.Reason,
                        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds),
                    config.LogRetentionDays);
            }
            catch (Exception ex)
            {
                // A logging failure must not turn a block into an allow.
                LogError(paths, ex);
            }

            switch (decision.Action)
            {
                case Action.Allow:
                    return 0;

                case Action.Ask:
                    var output = new HookOutput(new HookSpecificOutput(
                        "PreToolUse",
                        "ask",
                        $"getcmd: {level} — {decision.Reason}"));
                    stdout.WriteLine(JsonSerializer.Serialize(output, HookJsonContext.Default.HookOutput));
                    return 0;

                default:
                    var alternative = SaferAlternative(decision.RuleId);
                    stderr.WriteLine(
                        $"getcmd blocked ({level}, {decision.RuleId ?? "no rule"}): {decision.Reason}."
                        + (alternative is null ? "" : " " + alternative));
                    return 2;
            }
        }
        catch (Exception ex)
        {
            LogError(AppPaths.Resolve(), ex);
            return 0;
        }
    }

    public static int Install(string settingsPath, TextWriter stdout)
    {
        stdout.WriteLine(ClaudeSettings.Install(settingsPath)
            ? $"Added PreToolUse hook to {settingsPath}: matcher \"{ClaudeSettings.Matcher}\", command \"{ClaudeSettings.HookCommand}\", timeout {ClaudeSettings.TimeoutSeconds}"
            : $"Hook already present in {settingsPath}; nothing changed");
        return 0;
    }

    public static int Uninstall(string settingsPath, TextWriter stdout)
    {
        stdout.WriteLine(ClaudeSettings.Uninstall(settingsPath)
            ? $"Removed PreToolUse hook \"{ClaudeSettings.HookCommand}\" from {settingsPath}"
            : $"Hook not found in {settingsPath}; nothing changed");
        return 0;
    }

    // One line the agent can act on instead of retrying the blocked command.
    private static string? SaferAlternative(string? ruleId) => ruleId switch
    {
        "rm-root-or-home" => "Delete a specific subdirectory instead.",
        "rm-recursive" => "Delete the specific files, or ask the user to run it.",
        "git-force-push" => "Use git push --force-with-lease.",
        "git-reset-hard" => "Use git stash to keep the changes, or git reset --soft.",
        "git-clean" => "Run git clean -n first and ask the user to confirm.",
        "git-checkout-discard" => "Use git stash to set the changes aside instead.",
        "git-branch-delete" => "Use git branch -d, which refuses to delete unmerged work.",
        "sql-drop" => "Ask the user to run destructive SQL themselves.",
        "sql-delete-all" => "Add a WHERE clause.",
        "db-drop" => "Ask the user to run it themselves.",
        "docker-destroy" => "Stop containers without removing volumes (docker compose down).",
        "curl-pipe-sh" => "Download the script to a file and read it before running it.",
        "read-credentials" => "Ask the user for the specific value you need.",
        "print-secret" => "Check that the variable is set without printing it: [ -n \"$VAR\" ].",
        "creds-in-command" => "Pass the credential through an environment variable or a config file.",
        _ => null,
    };

    private static void LogError(AppPaths paths, Exception exception)
    {
        try
        {
            paths.EnsureHome();
            File.AppendAllText(paths.HookErrorLog, $"{DecisionLog.Timestamp(DateTime.UtcNow)} {exception}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere left to report it.
        }
    }
}
