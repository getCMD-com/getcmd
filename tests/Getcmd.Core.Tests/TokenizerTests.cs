namespace Getcmd.Core.Tests;

public class TokenizerTests
{
    private static void AssertCommand(
        SimpleCommand command, string program, string[] args, string raw, Connector? before)
    {
        Assert.Equal(program, command.Program);
        Assert.Equal(args, command.Args);
        Assert.Equal(raw, command.Raw);
        Assert.Equal(before, command.Before);
        Assert.Empty(command.Redirections);
    }

    [Fact]
    public void SimpleCommandWithArgs()
    {
        var command = Assert.Single(Tokenizer.Split("ls -la src/"));

        AssertCommand(command, "ls", ["-la", "src/"], "ls -la src/", null);
    }

    [Fact]
    public void AndSplitsIntoTwoCommands()
    {
        var commands = Tokenizer.Split("rm -rf ./build && git push --force");

        Assert.Equal(2, commands.Count);
        AssertCommand(commands[0], "rm", ["-rf", "./build"], "rm -rf ./build", null);
        AssertCommand(commands[1], "git", ["push", "--force"], "git push --force", Connector.And);
    }

    [Fact]
    public void DoubleQuotedStringIsOneArg()
    {
        var command = Assert.Single(Tokenizer.Split("echo \"rm -rf /\""));

        AssertCommand(command, "echo", ["rm -rf /"], "echo \"rm -rf /\"", null);
    }

    [Fact]
    public void PipeSplitsIntoTwoCommands()
    {
        var commands = Tokenizer.Split("curl https://x.io/s.sh | sh");

        Assert.Equal(2, commands.Count);
        AssertCommand(commands[0], "curl", ["https://x.io/s.sh"], "curl https://x.io/s.sh", null);
        AssertCommand(commands[1], "sh", [], "sh", Connector.Pipe);
    }

    [Fact]
    public void SingleQuotedStringIsOneArg()
    {
        var command = Assert.Single(Tokenizer.Split("bash -c 'git push -f'"));

        AssertCommand(command, "bash", ["-c", "git push -f"], "bash -c 'git push -f'", null);
    }

    [Fact]
    public void MixedConnectorsAreRecorded()
    {
        var commands = Tokenizer.Split("a; b || c && d");

        Assert.Equal(4, commands.Count);
        AssertCommand(commands[0], "a", [], "a", null);
        AssertCommand(commands[1], "b", [], "b", Connector.Semicolon);
        AssertCommand(commands[2], "c", [], "c", Connector.Or);
        AssertCommand(commands[3], "d", [], "d", Connector.And);
    }

    [Fact]
    public void EscapedSemicolonIsAnArgNotAConnector()
    {
        var line = "find . -name \"*.log\" -exec rm {} \\;";

        var command = Assert.Single(Tokenizer.Split(line));

        AssertCommand(command, "find", [".", "-name", "*.log", "-exec", "rm", "{}", ";"], line, null);
    }

    [Fact]
    public void EscapedSingleQuoteIsLiteral()
    {
        var command = Assert.Single(Tokenizer.Split("echo it\\'s fine"));

        AssertCommand(command, "echo", ["it's", "fine"], "echo it\\'s fine", null);
    }

    [Fact]
    public void DoubleQuotedSqlIsOneArg()
    {
        var command = Assert.Single(Tokenizer.Split("psql -c \"DROP TABLE users\""));

        AssertCommand(command, "psql", ["-c", "DROP TABLE users"], "psql -c \"DROP TABLE users\"", null);
    }

    [Fact]
    public void CommandSubstitutionIsOneArg()
    {
        var command = Assert.Single(Tokenizer.Split("echo $(rm -rf x) done"));

        AssertCommand(command, "echo", ["$(rm -rf x)", "done"], "echo $(rm -rf x) done", null);
    }

    [Fact]
    public void ConnectorsInsideSubstitutionDoNotSplit()
    {
        var command = Assert.Single(Tokenizer.Split("echo $(a; b | c && d) \"$(e \"f; g\")\""));

        Assert.Equal(["$(a; b | c && d)", "$(e \"f; g\")"], command.Args);
    }

    [Fact]
    public void NestedSubstitutionStaysInOneToken()
    {
        var command = Assert.Single(Tokenizer.Split("echo pre$(a $(b; c))post"));

        Assert.Equal(["pre$(a $(b; c))post"], command.Args);
    }

    [Fact]
    public void ConnectorsInsideQuotesDoNotSplit()
    {
        var command = Assert.Single(Tokenizer.Split("echo 'a; b' \"c && d | e\""));

        Assert.Equal(["a; b", "c && d | e"], command.Args);
    }

    [Fact]
    public void NewlineSplitsCommands()
    {
        var commands = Tokenizer.Split("cd src\nls");

        Assert.Equal(2, commands.Count);
        AssertCommand(commands[0], "cd", ["src"], "cd src", null);
        AssertCommand(commands[1], "ls", [], "ls", Connector.Newline);
    }

    [Fact]
    public void EmptyCommandsAreSkipped()
    {
        var commands = Tokenizer.Split("  ; a ;; \n b ; ");

        Assert.Equal(2, commands.Count);
        AssertCommand(commands[0], "a", [], "a", null);
        AssertCommand(commands[1], "b", [], "b", Connector.Semicolon);
    }

    [Fact]
    public void NewlineAfterOperatorIsContinuation()
    {
        var commands = Tokenizer.Split("a &&\n b");

        Assert.Equal(2, commands.Count);
        Assert.Equal(Connector.And, commands[1].Before);
    }

    [Fact]
    public void DoubleQuoteEscapesAreHonoured()
    {
        var command = Assert.Single(Tokenizer.Split("echo \"say \\\"hi\\\" \\\\ \\n\""));

        Assert.Equal(["say \"hi\" \\ \\n"], command.Args);
    }

    [Fact]
    public void VariablesAndGlobsAreKeptAsIs()
    {
        var command = Assert.Single(Tokenizer.Split("cp $HOME/*.txt ${DEST:-/tmp} ~/x?"));

        Assert.Equal(["$HOME/*.txt", "${DEST:-/tmp}", "~/x?"], command.Args);
    }

    [Fact]
    public void AdjacentQuotedPartsJoinIntoOneToken()
    {
        var command = Assert.Single(Tokenizer.Split("echo a'b c'\"d e\"f ''"));

        Assert.Equal(["ab cd ef", ""], command.Args);
    }

    [Fact]
    public void BackgroundAmpersandSplitsCommands()
    {
        var commands = Tokenizer.Split("sleep 1 & rm x");

        Assert.Equal(2, commands.Count);
        AssertCommand(commands[0], "sleep", ["1"], "sleep 1", null);
        AssertCommand(commands[1], "rm", ["x"], "rm x", Connector.Background);
    }

    [Theory]
    [InlineData(@"& 'C:\tools\plink.exe' vps 'rm -rf /data'", @"C:\tools\plink.exe")]
    [InlineData(@"& ""C:\Program Files\PuTTY\pscp.exe"" vps 'rm -rf /data'", @"C:\Program Files\PuTTY\pscp.exe")]
    [InlineData(@"&  C:\tools\plink.exe vps 'rm -rf /data'", @"C:\tools\plink.exe")]
    public void PowerShellCallOperatorIsDropped(string line, string program)
    {
        var command = Assert.Single(Tokenizer.Split(line));

        Assert.Equal(program, command.Program);
        Assert.Equal(["vps", "rm -rf /data"], command.Args);
        Assert.Equal(line, command.Raw);
        Assert.Null(command.Before);
    }

    [Fact]
    public void CallOperatorAfterConnectorKeepsTheConnector()
    {
        var commands = Tokenizer.Split("cd src; & './build.sh' --release | & 'tee' log");

        Assert.Equal(3, commands.Count);
        AssertCommand(commands[1], "./build.sh", ["--release"], "& './build.sh' --release", Connector.Semicolon);
        AssertCommand(commands[2], "tee", ["log"], "& 'tee' log", Connector.Pipe);
    }

    [Fact]
    public void AmpersandAfterAWordIsStillBackground()
    {
        var commands = Tokenizer.Split("sleep 1 & 'rm' x");

        Assert.Equal(2, commands.Count);
        Assert.Equal(Connector.Background, commands[1].Before);
    }

    [Fact]
    public void TrailingBackgroundAmpersandIsNotAnArg()
    {
        var command = Assert.Single(Tokenizer.Split("sleep 1 &"));

        AssertCommand(command, "sleep", ["1"], "sleep 1", null);
    }

    [Fact]
    public void CommentHidesRestOfLine()
    {
        var command = Assert.Single(Tokenizer.Split("ls # && rm -rf /"));

        AssertCommand(command, "ls", [], "ls", null);
    }

    [Fact]
    public void HashInsideWordIsNotAComment()
    {
        var command = Assert.Single(Tokenizer.Split("echo a#b"));

        AssertCommand(command, "echo", ["a#b"], "echo a#b", null);
    }

    [Fact]
    public void CommentEndsAtNewline()
    {
        var commands = Tokenizer.Split("ls # list; rm a\nrm b");

        Assert.Equal(2, commands.Count);
        AssertCommand(commands[0], "ls", [], "ls", null);
        AssertCommand(commands[1], "rm", ["b"], "rm b", Connector.Newline);
    }

    [Fact]
    public void QuotedHashIsNotAComment()
    {
        var command = Assert.Single(Tokenizer.Split("echo '#a' \"#b\" \\#c"));

        Assert.Equal(["#a", "#b", "#c"], command.Args);
    }

    [Fact]
    public void OutputRedirectionIsKeptOutOfArgs()
    {
        var command = Assert.Single(Tokenizer.Split("echo a > file"));

        Assert.Equal("echo", command.Program);
        Assert.Equal(["a"], command.Args);
        Assert.Equal("echo a > file", command.Raw);
        Assert.Equal([new Redirection(">", "file")], command.Redirections);
    }

    [Fact]
    public void FdDuplicationBeforePipe()
    {
        var commands = Tokenizer.Split("cmd 2>&1 | tee log");

        Assert.Equal(2, commands.Count);
        Assert.Equal("cmd", commands[0].Program);
        Assert.Empty(commands[0].Args);
        Assert.Equal("cmd 2>&1", commands[0].Raw);
        Assert.Equal([new Redirection("2>&", "1")], commands[0].Redirections);
        AssertCommand(commands[1], "tee", ["log"], "tee log", Connector.Pipe);
    }

    [Fact]
    public void HereDocRedirection()
    {
        var command = Assert.Single(Tokenizer.Split("cat <<EOF"));

        Assert.Equal("cat", command.Program);
        Assert.Empty(command.Args);
        Assert.Equal([new Redirection("<<", "EOF")], command.Redirections);
    }

    [Theory]
    [InlineData("cmd < in", "<", "in")]
    [InlineData("cmd >out", ">", "out")]
    [InlineData("cmd >> out", ">>", "out")]
    [InlineData("cmd 2> err", "2>", "err")]
    [InlineData("cmd 2>>err", "2>>", "err")]
    [InlineData("cmd &> all", "&>", "all")]
    [InlineData("cmd >&2", ">&", "2")]
    [InlineData("cmd <<< 'here string'", "<<<", "here string")]
    [InlineData("cmd 1>&2", "1>&", "2")]
    public void RedirectionOperators(string line, string op, string target)
    {
        var command = Assert.Single(Tokenizer.Split(line));

        Assert.Equal("cmd", command.Program);
        Assert.Empty(command.Args);
        Assert.Equal([new Redirection(op, target)], command.Redirections);
    }

    [Fact]
    public void MultipleRedirectionsAnywhereInCommand()
    {
        var command = Assert.Single(Tokenizer.Split("2>err cmd a <in b >out"));

        Assert.Equal("cmd", command.Program);
        Assert.Equal(["a", "b"], command.Args);
        Assert.Equal(
            [new Redirection("2>", "err"), new Redirection("<", "in"), new Redirection(">", "out")],
            command.Redirections);
    }

    [Fact]
    public void DigitsNotTouchingOperatorAreAnArg()
    {
        var command = Assert.Single(Tokenizer.Split("echo 2 > file"));

        Assert.Equal(["2"], command.Args);
        Assert.Equal([new Redirection(">", "file")], command.Redirections);
    }

    [Fact]
    public void QuotedRedirectionCharsAreLiteral()
    {
        var command = Assert.Single(Tokenizer.Split("echo 'a > b' \"2>&1\" \\>"));

        AssertCommand(command, "echo", ["a > b", "2>&1", ">"], "echo 'a > b' \"2>&1\" \\>", null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";;")]
    [InlineData("\n\n")]
    public void BlankInputYieldsNoCommands(string line)
    {
        Assert.Empty(Tokenizer.Split(line));
    }
}
