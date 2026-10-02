using System.CommandLine;
using Getcmd.Cli.Commands;
using Getcmd.Cli.Services;

namespace Getcmd.Cli;

internal static class Program
{
    private static int Main(string[] args) => Run(args, Console.OpenStandardInput, Console.Out, Console.Error);

    internal static int Run(string[] args, Func<Stream> stdin, TextWriter stdout, TextWriter stderr)
    {
        // The hook runs before every Bash call: skip building the command tree.
        if (args is ["hook", "claude"])
        {
            return HookCommand.Run(stdin(), stdout, stderr);
        }

        if (args is ["--version"] or ["-v"])
        {
            stdout.WriteLine($"getcmd {AppInfo.Version}");
            return 0;
        }

        // Turns an expected failure (bad config, unreadable settings.json) into a one-line error.
        int Guard(Func<int> action)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"getcmd: {ex.Message}");
                return 1;
            }
        }

        var root = new RootCommand("Command safety for AI coding agents.");
        foreach (var option in root.Options)
        {
            if (option is VersionOption)
            {
                option.Aliases.Add("-v");
            }
        }

        // check
        var commandArgument = new Argument<string>("command") { Description = "The command line to classify (quote it)" };
        var cwdOption = new Option<string?>("--cwd") { Description = "Directory to resolve paths against (default: current directory)" };
        var checkJsonOption = new Option<bool>("--json") { Description = "Print the decision as JSON" };
        var check = new Command("check", "Classify a command line; exit code 0 allow, 1 ask, 2 block")
        {
            commandArgument, cwdOption, checkJsonOption,
        };
        check.SetAction(result => Guard(() => CheckCommand.Run(
            result.GetValue(commandArgument)!, result.GetValue(cwdOption), result.GetValue(checkJsonOption), stdout)));
        root.Subcommands.Add(check);

        // hook claude
        var installOption = new Option<bool>("--install") { Description = "Add the hook to Claude Code's settings.json" };
        var uninstallOption = new Option<bool>("--uninstall") { Description = "Remove the hook from Claude Code's settings.json" };
        var projectOption = new Option<bool>("--project") { Description = "Use .claude/settings.json in the current directory instead of ~/.claude" };
        var claude = new Command("claude", "Claude Code PreToolUse hook: reads the hook JSON on stdin")
        {
            installOption, uninstallOption, projectOption,
        };
        claude.SetAction(result =>
        {
            var install = result.GetValue(installOption);
            var uninstall = result.GetValue(uninstallOption);
            if (!install && !uninstall)
            {
                return HookCommand.Run(stdin(), stdout, stderr);
            }

            var settings = result.GetValue(projectOption)
                ? ClaudeSettings.ProjectSettingsPath(Environment.CurrentDirectory)
                : ClaudeSettings.UserSettingsPath;
            return Guard(() => install ? HookCommand.Install(settings, stdout) : HookCommand.Uninstall(settings, stdout));
        });
        root.Subcommands.Add(new Command("hook", "Agent hook integrations") { claude });

        // log
        var lastOption = new Option<int>("--last") { Description = "Number of decisions to show", DefaultValueFactory = _ => 20 };
        var levelOption = new Option<string?>("--level") { Description = "Only this level" };
        levelOption.AcceptOnlyFromAmong(Names.Levels);
        var actionOption = new Option<string?>("--action") { Description = "Only this action" };
        actionOption.AcceptOnlyFromAmong(Names.Actions);
        var logJsonOption = new Option<bool>("--json") { Description = "Print the rows as JSON" };
        var log = new Command("log", "Show recent decisions") { lastOption, levelOption, actionOption, logJsonOption };
        log.SetAction(result => Guard(() => LogCommand.Run(
            result.GetValue(lastOption), result.GetValue(levelOption), result.GetValue(actionOption),
            result.GetValue(logJsonOption), stdout)));
        root.Subcommands.Add(log);

        // rules
        var rules = new Command("rules", "List, test, disable or enable the builtin rules");
        rules.SetAction(_ => Guard(() => RulesCommand.List(stdout)));

        var rulesList = new Command("list", "Show every builtin rule");
        rulesList.SetAction(_ => Guard(() => RulesCommand.List(stdout)));
        rules.Subcommands.Add(rulesList);

        var rulesTest = new Command("test", "Run rules/cases.json from the working directory, or the embedded copy");
        rulesTest.SetAction(_ => Guard(() => RulesCommand.Test(stdout)));
        rules.Subcommands.Add(rulesTest);

        var disableId = new Argument<string>("id") { Description = "Rule id" };
        var rulesDisable = new Command("disable", "Turn a rule off in config.json") { disableId };
        rulesDisable.SetAction(result => Guard(() => RulesCommand.SetDisabled(result.GetValue(disableId)!, true, stdout, stderr)));
        rules.Subcommands.Add(rulesDisable);

        var enableId = new Argument<string>("id") { Description = "Rule id" };
        var rulesEnable = new Command("enable", "Turn a disabled rule back on") { enableId };
        rulesEnable.SetAction(result => Guard(() => RulesCommand.SetDisabled(result.GetValue(enableId)!, false, stdout, stderr)));
        rules.Subcommands.Add(rulesEnable);
        root.Subcommands.Add(rules);

        // doctor
        var doctor = new Command("doctor", "Check that getcmd is installed and working");
        doctor.SetAction(_ => Guard(() => DoctorCommand.Run(stdout)));
        root.Subcommands.Add(doctor);

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = stdout, Error = stderr });
    }
}
