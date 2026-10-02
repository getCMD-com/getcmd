namespace Getcmd.Core.Tests;

public class PathResolverTests
{
    private static readonly string[] BuildDirs = ["build", "dist", "node_modules"];

    [Theory]
    [InlineData("./build", PathScope.BuildDir)]
    [InlineData("node_modules", PathScope.BuildDir)]
    [InlineData("dist/*", PathScope.BuildDir)]
    [InlineData("Build/out.o", PathScope.BuildDir)]
    [InlineData("/home/ally/src/app/dist/js", PathScope.BuildDir)]
    [InlineData("src/", PathScope.Inside)]
    [InlineData("./", PathScope.Inside)]
    [InlineData(".", PathScope.Inside)]
    [InlineData("src/build", PathScope.Inside)]
    [InlineData("./src/../README.md", PathScope.Inside)]
    [InlineData("src/*.cs", PathScope.Inside)]
    [InlineData("*/tmp", PathScope.Inside)]
    [InlineData("../other", PathScope.Outside)]
    [InlineData("/etc/hosts", PathScope.Outside)]
    [InlineData("/var/lib", PathScope.Outside)]
    [InlineData("../lib/x", PathScope.Outside)]
    [InlineData("..", PathScope.Home)]
    [InlineData("~/Documents/taxes", PathScope.Outside)]
    [InlineData("/home/al*", PathScope.Outside)]
    [InlineData("~bob/x", PathScope.Outside)]
    [InlineData("/c/", PathScope.Outside)]
    [InlineData("~", PathScope.Home)]
    [InlineData("~/Documents", PathScope.Home)]
    [InlineData("/home/ally", PathScope.Home)]
    [InlineData("/home/ally/", PathScope.Home)]
    [InlineData("../..", PathScope.Home)]
    [InlineData("~/*", PathScope.Home)]
    [InlineData("$HOME", PathScope.Home)]
    [InlineData("${HOME}/.ssh", PathScope.Home)]
    [InlineData("/", PathScope.Root)]
    [InlineData("/*", PathScope.Root)]
    [InlineData("/ho*", PathScope.Root)]
    [InlineData("../../../../..", PathScope.Root)]
    [InlineData("*", PathScope.Inside)]
    [InlineData("*.log", PathScope.Inside)]
    [InlineData("src", PathScope.Inside)]
    [InlineData("app.db", PathScope.Inside)]
    [InlineData("origin", PathScope.Inside)]
    [InlineData("build/*", PathScope.BuildDir)]
    [InlineData("build", PathScope.BuildDir)]
    [InlineData("-rf", PathScope.Unknown)]
    [InlineData("user@vps", PathScope.Unknown)]
    [InlineData("user@vps:/var/www", PathScope.Unknown)]
    [InlineData("https://x.io/s.sh", PathScope.Unknown)]
    [InlineData("HEAD:src/app.js", PathScope.Unknown)]
    [InlineData("--output=/etc/passwd", PathScope.Unknown)]
    [InlineData("$TARGET", PathScope.Unknown)]
    [InlineData("$TARGET/data", PathScope.Unknown)]
    [InlineData("", PathScope.Unknown)]
    public void ClassifyPosix(string arg, PathScope expected)
    {
        Assert.Equal(expected, PathResolver.Classify(arg, "/home/ally/src/app", "/home/ally", BuildDirs));
    }

    [Theory]
    [InlineData(@"C:\", PathScope.Root)]
    [InlineData("/c/", PathScope.Root)]
    [InlineData("/c", PathScope.Root)]
    [InlineData("C:", PathScope.Root)]
    [InlineData(@"D:\", PathScope.Root)]
    [InlineData("/", PathScope.Root)]
    [InlineData(@"C:\*", PathScope.Root)]
    [InlineData(@"C:\Users\ally", PathScope.Home)]
    [InlineData(@"c:\users\ALLY\", PathScope.Home)]
    [InlineData("/c/Users/ally/Documents", PathScope.Home)]
    [InlineData("~", PathScope.Home)]
    [InlineData(@".\build", PathScope.BuildDir)]
    [InlineData("/c/Users/ally/src/app/dist", PathScope.BuildDir)]
    [InlineData(@"C:\Users\ally\src\app\Node_Modules\x", PathScope.BuildDir)]
    [InlineData(@".\src\Program.cs", PathScope.Inside)]
    [InlineData("C:/Users/ally/src/app", PathScope.Inside)]
    [InlineData("/c/users/ally/src/app/src", PathScope.Inside)]
    [InlineData(@"..\other", PathScope.Outside)]
    [InlineData(@"C:\Windows\System32", PathScope.Outside)]
    [InlineData(@"D:\Users\ally\src\app", PathScope.Outside)]
    [InlineData("/tmp/x", PathScope.Outside)]
    [InlineData("-rf", PathScope.Unknown)]
    public void ClassifyWindows(string arg, PathScope expected)
    {
        Assert.Equal(expected, PathResolver.Classify(arg, @"C:\Users\ally\src\app", @"C:\Users\ally", BuildDirs));
    }

    [Fact]
    public void GitBashCwdMatchesDriveLetterArg()
    {
        var scope = PathResolver.Classify(@"C:\Users\ally\src\app\src", "/c/Users/ally/src/app", "/c/Users/ally", BuildDirs);

        Assert.Equal(PathScope.Inside, scope);
    }

    [Fact]
    public void HomeWinsWhenCwdIsHome()
    {
        Assert.Equal(PathScope.Home, PathResolver.Classify(".", "/home/ally", "/home/ally", BuildDirs));
        Assert.Equal(PathScope.Inside, PathResolver.Classify("./x", "/home/ally/src", "/home/ally", BuildDirs));
    }

    [Theory]
    [InlineData("/etc/hosts", true)]
    [InlineData("~", true)]
    [InlineData("~/x", true)]
    [InlineData(".", true)]
    [InlineData("./x", true)]
    [InlineData(".env", true)]
    [InlineData(@"C:\Users", true)]
    [InlineData("C:/Users", true)]
    [InlineData("src/app.js", true)]
    [InlineData(@"src\app.js", true)]
    [InlineData("node_modules", true)]
    [InlineData("$HOME/x", true)]
    [InlineData("src/logo@2x.png", true)]
    [InlineData("", false)]
    [InlineData("-rf", false)]
    [InlineData("--force", false)]
    [InlineData("origin", false)]
    [InlineData("main", false)]
    [InlineData("user@vps", false)]
    [InlineData("git@github.com:org/repo.git", false)]
    [InlineData("https://x.io/s.sh", false)]
    [InlineData("HEAD:src/app.js", false)]
    [InlineData("*.log", false)]
    public void LooksLikePath(string arg, bool expected)
    {
        Assert.Equal(expected, PathResolver.LooksLikePath(arg));
    }
}
