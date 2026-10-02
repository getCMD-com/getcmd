namespace Getcmd.Core.Tests;

public class UnwrapperTests
{
    private static SimpleCommand Parse(string line) => Assert.Single(Tokenizer.Split(line));

    private static UnwrapResult Unwrap(string line) => Unwrapper.Unwrap(Parse(line));

    private static void AssertWords(SimpleCommand command, params string[] words)
    {
        string[] actual = [command.Program, .. command.Args];
        Assert.Equal(words, actual);
    }

    [Fact]
    public void SudoIsStripped()
    {
        var result = Unwrap("sudo rm -rf /var/log");

        AssertWords(Assert.Single(result.Commands), "rm", "-rf", "/var/log");
        Assert.True(result.Sudo);
        Assert.Null(result.RemoteHost);
        Assert.Null(result.Wrapper);
    }

    [Fact]
    public void BashCIsSplit()
    {
        var result = Unwrap("bash -c 'git push -f'");

        var command = Assert.Single(result.Commands);
        AssertWords(command, "git", "push", "-f");
        Assert.Equal("git push -f", command.Raw);
        Assert.Equal("bash -c", result.Wrapper);
        Assert.False(result.Sudo);
    }

    [Fact]
    public void BashCWithConnectorsYieldsEachCommand()
    {
        var result = Unwrap("bash -c \"rm -rf x && git push -f\"");

        Assert.Equal(2, result.Commands.Count);
        AssertWords(result.Commands[0], "rm", "-rf", "x");
        Assert.Null(result.Commands[0].Before);
        AssertWords(result.Commands[1], "git", "push", "-f");
        Assert.Equal(Connector.And, result.Commands[1].Before);
        Assert.Equal("bash -c", result.Wrapper);
    }

    [Fact]
    public void SudoBashCUnwrapsTwoLayers()
    {
        var result = Unwrap("sudo bash -c 'rm -rf /'");

        AssertWords(Assert.Single(result.Commands), "rm", "-rf", "/");
        Assert.True(result.Sudo);
        Assert.Equal("bash -c", result.Wrapper);
    }

    [Fact]
    public void XargsGetsSyntheticInputArg()
    {
        var result = Unwrap("xargs rm -f");

        var command = Assert.Single(result.Commands);
        AssertWords(command, "rm", "-f", "{xargs-input}");
        Assert.Equal("rm -f {xargs-input}", command.Raw);
        Assert.Equal("xargs", result.Wrapper);
    }

    [Fact]
    public void XargsOptionsAreSkipped()
    {
        var result = Unwrap("xargs -0 -n 1 -P4 --max-chars 100 rm -f");

        AssertWords(Assert.Single(result.Commands), "rm", "-f", "{xargs-input}");
    }

    [Fact]
    public void FindExecYieldsFindAndExecCommand()
    {
        var find = Parse("find . -name \"*.log\" -exec rm {} \\;");

        var result = Unwrapper.Unwrap(find);

        Assert.Equal(2, result.Commands.Count);
        Assert.Same(find, result.Commands[0]);
        AssertWords(result.Commands[1], "rm", "{}");
        Assert.Equal("find -exec", result.Wrapper);
    }

    [Fact]
    public void FindExecPlusAndNestedShell()
    {
        var result = Unwrap("find . -exec sh -c 'rm \"$1\"; echo done' _ {} + -execdir chmod 600 {} \\;");

        Assert.Equal(4, result.Commands.Count);
        Assert.Equal("find", result.Commands[0].Program);
        AssertWords(result.Commands[1], "rm", "$1");
        AssertWords(result.Commands[2], "echo", "done");
        AssertWords(result.Commands[3], "chmod", "600", "{}");
        Assert.Equal("find -exec", result.Wrapper);
    }

    [Fact]
    public void FindWithoutExecIsUnchanged()
    {
        var find = Parse("find . -name '*.log' -delete");

        var result = Unwrapper.Unwrap(find);

        Assert.Same(find, Assert.Single(result.Commands));
        Assert.Null(result.Wrapper);
    }

    [Fact]
    public void SshRemoteCommandIsSplit()
    {
        var result = Unwrap("ssh vps \"docker ps\"");

        AssertWords(Assert.Single(result.Commands), "docker", "ps");
        Assert.Equal("vps", result.RemoteHost);
        Assert.Equal("ssh", result.Wrapper);
    }

    [Fact]
    public void SshOptionsAndUserAreSkipped()
    {
        var result = Unwrap("ssh -p 2222 deploy@10.0.0.5 \"rm -rf /data\"");

        AssertWords(Assert.Single(result.Commands), "rm", "-rf", "/data");
        Assert.Equal("10.0.0.5", result.RemoteHost);
        Assert.Equal("ssh", result.Wrapper);
    }

    [Fact]
    public void SshUnquotedRemoteCommandIsJoined()
    {
        var result = Unwrap("ssh -i ~/.ssh/key -o StrictHostKeyChecking=no vps sudo systemctl restart nginx");

        AssertWords(Assert.Single(result.Commands), "systemctl", "restart", "nginx");
        Assert.Equal("vps", result.RemoteHost);
        Assert.True(result.Sudo);
    }

    [Fact]
    public void SshLoginIsKeptAsIs()
    {
        var ssh = Parse("ssh vps");

        var result = Unwrapper.Unwrap(ssh);

        Assert.Same(ssh, Assert.Single(result.Commands));
        Assert.Equal("vps", result.RemoteHost);
    }

    [Fact]
    public void EnvAndNohupAreStripped()
    {
        var result = Unwrap("env FOO=1 nohup node app.js");

        AssertWords(Assert.Single(result.Commands), "node", "app.js");
        Assert.False(result.Sudo);
        Assert.Null(result.Wrapper);
    }

    [Fact]
    public void EvalIsSplit()
    {
        var result = Unwrap("eval \"rm -rf /tmp/x\"");

        AssertWords(Assert.Single(result.Commands), "rm", "-rf", "/tmp/x");
        Assert.Equal("eval", result.Wrapper);
    }

    [Fact]
    public void PlainCommandIsUnchanged()
    {
        var ls = Parse("ls");

        var result = Unwrapper.Unwrap(ls);

        Assert.Same(ls, Assert.Single(result.Commands));
        Assert.False(result.Sudo);
        Assert.Null(result.RemoteHost);
        Assert.Null(result.Wrapper);
    }

    [Theory]
    [InlineData("sudo -u postgres -E -- psql -c 'DROP TABLE users'", "psql")]
    [InlineData("sudo -Eu root FOO=1 rm x", "rm")]
    [InlineData("/usr/bin/sudo rm x", "rm")]
    [InlineData("doas -u root rm x", "rm")]
    public void SudoAndDoasForms(string line, string program)
    {
        var result = Unwrap(line);

        Assert.Equal(program, Assert.Single(result.Commands).Program);
        Assert.True(result.Sudo);
    }

    [Theory]
    [InlineData("nice -n 10 make -j8", "make", "-j8")]
    [InlineData("nice make -j8", "make", "-j8")]
    [InlineData("timeout 30 curl x.io", "curl", "x.io")]
    [InlineData("timeout -s KILL --kill-after=5 30s curl x.io", "curl", "x.io")]
    [InlineData("time -p make", "make")]
    [InlineData("command git status", "git", "status")]
    [InlineData("exec node app.js", "node", "app.js")]
    [InlineData("env -i -u HOME A=1 B=2 node app.js", "node", "app.js")]
    [InlineData("FOO=1 BAR=2 node app.js", "node", "app.js")]
    public void PrefixWrappersAreStripped(string line, params string[] words)
    {
        var result = Unwrap(line);

        AssertWords(Assert.Single(result.Commands), words);
        Assert.False(result.Sudo);
    }

    [Theory]
    [InlineData("bash -lc 'make test'")]
    [InlineData("sh -ec 'make test'")]
    [InlineData("zsh -c 'make test'")]
    [InlineData("dash -c 'make test'")]
    [InlineData("/bin/bash -eo pipefail -c 'make test'")]
    public void ShellCommandStringForms(string line)
    {
        var result = Unwrap(line);

        AssertWords(Assert.Single(result.Commands), "make", "test");
        Assert.Equal("bash -c", result.Wrapper);
    }

    [Theory]
    [InlineData("bash script.sh")]
    [InlineData("bash")]
    [InlineData("sudo -i")]
    [InlineData("sudo -e /etc/hosts")]
    [InlineData("command -v git")]
    [InlineData("env")]
    [InlineData("xargs")]
    [InlineData("FOO=1")]
    public void NonMatchingFormsAreUnchanged(string line)
    {
        var command = Parse(line);

        var result = Unwrapper.Unwrap(command);

        Assert.Same(command, Assert.Single(result.Commands));
        Assert.False(result.Sudo);
        Assert.Null(result.Wrapper);
    }

    [Fact]
    public void StrippedCommandKeepsRedirectionsAndConnector()
    {
        var commands = Tokenizer.Split("cd /tmp && sudo rm x > log 2>&1");

        var result = Unwrapper.Unwrap(commands[1]);

        var command = Assert.Single(result.Commands);
        AssertWords(command, "rm", "x");
        Assert.Equal(Connector.And, command.Before);
        Assert.Equal([new Redirection(">", "log"), new Redirection("2>&", "1")], command.Redirections);
    }

    [Fact]
    public void InnerCommandsKeepTheirOwnRedirections()
    {
        var result = Unwrap("bash -c 'echo a > one; echo b' 2> err");

        Assert.Equal(2, result.Commands.Count);
        Assert.Equal([new Redirection(">", "one")], result.Commands[0].Redirections);
        Assert.Equal([new Redirection("2>", "err")], result.Commands[1].Redirections);
    }

    [Fact]
    public void FirstInnerCommandInheritsOuterConnector()
    {
        var commands = Tokenizer.Split("a | bash -c 'b; c'");

        var result = Unwrapper.Unwrap(commands[1]);

        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(Connector.Pipe, result.Commands[0].Before);
        Assert.Equal(Connector.Semicolon, result.Commands[1].Before);
    }

    [Fact]
    public void RebuiltRawQuotesWordsThatNeedIt()
    {
        var result = Unwrap("sudo psql -c \"DROP TABLE users\"");

        var command = Assert.Single(result.Commands);
        Assert.Equal("psql -c 'DROP TABLE users'", command.Raw);
        AssertWords(Assert.Single(Tokenizer.Split(command.Raw)), "psql", "-c", "DROP TABLE users");
    }

    [Fact]
    public void UnwrappingStopsAtMaxDepth()
    {
        var result = Unwrap("sudo sudo sudo sudo sudo rm x");

        AssertWords(Assert.Single(result.Commands), "sudo", "rm", "x");
        Assert.True(result.Sudo);
    }

    [Fact]
    public void NestedWrappersKeepOutermostWrapperAndHost()
    {
        var result = Unwrap("ssh vps \"sudo bash -c 'rm -rf /srv && reboot'\"");

        Assert.Equal(2, result.Commands.Count);
        AssertWords(result.Commands[0], "rm", "-rf", "/srv");
        AssertWords(result.Commands[1], "reboot");
        Assert.True(result.Sudo);
        Assert.Equal("vps", result.RemoteHost);
        Assert.Equal("ssh", result.Wrapper);
    }
}
