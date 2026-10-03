namespace Getcmd.Core;

public static class BuiltinRules
{
    /// <summary>
    /// Id of the rule the engine applies itself: it covers a remote command on a
    /// prod-tagged host that no other rule classified. It is skipped during
    /// ordinary matching.
    /// </summary>
    public const string SshRemoteCommandId = "ssh-remote-cmd";

    private static readonly string[] Recursive = ["-r*", "-R*", "--recursive"];
    private static readonly string[] SqlClients = ["psql", "mysql", "sqlite3", "mongosh"];
    private static readonly string[] Downloaders = ["curl", "wget"];

    public static IReadOnlyList<Rule> All { get; } =
    [
        // rm
        Create("rm-root-or-home", Level.Destructive, "Recursive delete of the filesystem root or the home directory",
            programs: ["rm"], any: Recursive, scopes: [PathScope.Root, PathScope.Home]),
        Create("rm-recursive", Level.Destructive, "Recursive delete of source or system files; only build directories are safe to remove recursively",
            programs: ["rm"], any: Recursive, scopes: [PathScope.Outside, PathScope.Inside, PathScope.Unknown]),
        Create("rm-build-dir", Level.Mutate, "Recursive delete of build output",
            programs: ["rm"], any: Recursive, scopes: [PathScope.BuildDir]),
        Create("rm-file", Level.Mutate, "Deletes files",
            programs: ["rm"], none: Recursive),

        // git
        Create("git-force-push", Level.Destructive, "Force push can overwrite remote history; use --force-with-lease",
            programs: ["git"], all: ["push"], any: ["--force", "-f*", "+*"], none: ["--force-with-lease"]),
        Create("git-reset-hard", Level.Destructive, "git reset --hard discards uncommitted changes",
            programs: ["git"], all: ["reset", "--hard"]),
        Create("git-clean", Level.Destructive, "git clean deletes untracked files",
            programs: ["git"], all: ["clean"], any: ["-f*", "--force"]),
        Create("git-checkout-discard", Level.Destructive, "Discards all uncommitted changes in the working tree",
            programs: ["git"], all: ["checkout", "."]),
        Create("git-checkout-discard", Level.Destructive, "Discards all uncommitted changes in the working tree",
            programs: ["git"], all: ["restore", "."]),
        Create("git-branch-delete", Level.Destructive, "Force-deletes a branch, even if unmerged",
            programs: ["git"], all: ["branch", "-D"]),
        Create("git-branch-delete", Level.Destructive, "Deletes a tag",
            programs: ["git"], all: ["tag", "-d"]),
        Create("git-branch-delete", Level.Destructive, "Deletes stashed changes",
            programs: ["git"], all: ["stash"], any: ["drop", "clear"]),
        Create("git-write", Level.Mutate, "Changes the git repository or working tree",
            programs: ["git"],
            any: ["commit", "add", "checkout", "switch", "merge", "rebase", "pull", "fetch", "stash", "push"]),

        // databases
        Create("sql-drop", Level.Destructive, "SQL DROP/TRUNCATE destroys data",
            programs: SqlClients, raw: @"\b(DROP|TRUNCATE)\b"),
        Create("sql-delete-all", Level.Destructive, "SQL DELETE without WHERE removes every row",
            programs: SqlClients, raw: @"\bDELETE\b(?![\s\S]*\bWHERE\b)"),
        Create("db-drop", Level.Destructive, "Drops the database",
            programs: ["dotnet"], all: ["ef", "database", "drop"]),
        Create("db-drop", Level.Destructive, "Resets the database, deleting all data",
            programs: ["prisma"], all: ["migrate", "reset"]),
        Create("db-drop", Level.Destructive, "Resets the database, deleting all data",
            programs: ["npx", "pnpm", "yarn", "bunx"], all: ["prisma", "migrate", "reset"]),
        Create("db-drop", Level.Destructive, "Drops the database",
            programs: ["rails", "rake", "bundle"], all: ["db:drop"]),

        // infrastructure
        Create("docker-destroy", Level.Destructive, "Removes Docker data that cannot be recovered",
            programs: ["docker"], all: ["system", "prune"]),
        Create("docker-destroy", Level.Destructive, "Removes Docker volumes and the data in them",
            programs: ["docker"], all: ["volume"], any: ["rm", "prune"]),
        Create("docker-destroy", Level.Destructive, "Force-removes running containers",
            programs: ["docker"], all: ["rm"], any: ["-f*", "--force"]),
        Create("docker-destroy", Level.Destructive, "compose down -v also deletes the volumes",
            programs: ["docker"], all: ["compose", "down"], any: ["-v*", "--volumes"]),
        Create("docker-destroy", Level.Destructive, "compose down -v also deletes the volumes",
            programs: ["docker-compose"], all: ["down"], any: ["-v*", "--volumes"]),
        Create("kubectl-delete", Level.Destructive, "Deletes Kubernetes resources",
            programs: ["kubectl"], all: ["delete"]),
        Create("kubectl-delete", Level.Destructive, "Uninstalls a Helm release",
            programs: ["helm"], all: ["uninstall"]),
        Create("disk-write", Level.Destructive, "Writes directly to disks or filesystems",
            programs: ["dd", "mkfs*", "fdisk", "parted", "shred", "wipefs"]),
        Create("perm-bomb", Level.Destructive, "Recursively makes everything world-writable",
            programs: ["chmod"], all: ["777"], any: ["-R*", "--recursive"]),
        Create("perm-bomb", Level.Destructive, "Recursively changes ownership of the filesystem root or the home directory",
            programs: ["chown"], any: ["-R*", "--recursive"], scopes: [PathScope.Root, PathScope.Home]),
        Create("kill-all", Level.Destructive, "Shuts down or reboots the machine",
            programs: ["shutdown", "reboot", "halt", "poweroff"]),
        Create("kill-all", Level.Destructive, "Kills every process the user can signal",
            programs: ["kill"], all: ["-9", "-1"]),
        Create("kill-all", Level.Destructive, "Kills every process the user can signal",
            programs: ["pkill"], all: ["-f*"], any: [".", ""]),

        // network
        Create("curl-pipe-sh", Level.Egress, "Runs a downloaded script without review; download it and inspect it first",
            programs: Downloaders, pipedTo: "sh|bash|zsh|python|python3|node"),
        Create("curl-upload", Level.Egress, "Sends data to a remote server",
            programs: ["curl"], any: ["-d*", "--data", "--json", "-F*", "--form", "-T*", "--upload-file"]),
        Create("curl-upload", Level.Egress, "Sends data to a remote server",
            programs: ["curl"], raw: @"(^|\s)(-X\s*|--request[= ])(POST|PUT|PATCH|DELETE)\b"),
        Create("curl-upload", Level.Egress, "Sends data to a remote server",
            programs: ["wget"], any: ["--post-data", "--post-file", "--body-data", "--body-file", "--method"]),
        Create("remote-copy", Level.Egress, "Copies files to or from a remote host",
            programs: ["scp", "rsync"], raw: @"@|(^|\s)[\w.-]{2,}:(?!//)"),
        Create("remote-copy", Level.Egress, "Copies files to or from a remote host",
            programs: ["sftp", "pscp", "psftp", "winscp"]),
        Create(SshRemoteCommandId, Level.Egress, "Runs a command on a remote host",
            programs: ["ssh"]),
        Create("publish", Level.Egress, "Pushes an image to a registry",
            programs: ["docker"], all: ["push"]),
        Create("publish", Level.Egress, "Publishes a package",
            programs: ["npm", "pnpm", "yarn", "cargo"], all: ["publish"]),
        Create("publish", Level.Egress, "Publishes a package",
            programs: ["dotnet"], all: ["nuget", "push"]),
        Create("publish", Level.Egress, "Creates a public release",
            programs: ["gh"], all: ["release", "create"]),
        Create("publish", Level.Egress, "Publishes a package",
            programs: ["twine"], all: ["upload"]),

        // secrets
        Create("read-credentials", Level.Secrets, "Reads a credentials file into the conversation",
            programs: ["cat", "less", "more", "head", "tail", "grep", "rg", "base64", "strings"],
            raw: """(^|[\s/"'=])\.env($|[\s."'])|\.pem($|[\s"'])|\.key($|[\s"'])|id_rsa|id_ed25519|\.aws/|\.kube/config|\.netrc|\.npmrc|\.pypirc"""),
        Create("print-secret", Level.Secrets, "Prints a secret environment variable",
            programs: ["echo", "printenv", "env", "export"],
            raw: @"\$\{?\w*(TOKEN|SECRET|KEY|PASSWORD|PASSWD|CREDENTIAL)\w*"),
        Create("print-secret", Level.Secrets, "Prints a secret environment variable",
            programs: ["printenv"], raw: @"\w*(TOKEN|SECRET|KEY|PASSWORD|PASSWD|CREDENTIAL)\w*"),
        Create("creds-in-command", Level.Secrets, "Credentials on the command line end up in logs and shell history",
            raw: @"sshpass|--password[= ]|://[^/\s:]+:[^/\s@]+@"),
        Create("creds-in-command", Level.Secrets, "Password on the command line ends up in logs and shell history",
            programs: ["mysql", "mysqldump", "mysqladmin", "mariadb"], raw: @"(^|\s)(?-i:-p)\S+"),
        Create("creds-in-command", Level.Secrets, "Password on the command line ends up in logs and shell history",
            programs: ["plink", "putty", "pscp", "psftp"], all: ["-pw"]),
        Create("write-sensitive", Level.Destructive, "Overwrites a login, shell-startup or system configuration file",
            redirects: [@"authorized_keys|/etc/(passwd|shadow|sudoers|hosts)|\.bashrc|\.zshrc|\.profile|\.ssh/"]),

        // everyday project work
        Create("build-tool", Level.Read, "Build or test run",
            programs: ["dotnet"], any: ["build", "test", "run"]),
        Create("build-tool", Level.Read, "Build or test run",
            programs: ["npm"], any: ["test", "ci"]),
        Create("build-tool", Level.Read, "Build or test run",
            programs: ["npm"], all: ["run", "build"]),
        Create("build-tool", Level.Read, "Build or test run",
            programs: ["cargo", "go"], any: ["build", "test"]),
        Create("build-tool", Level.Read, "Build or test run",
            programs: ["make"]),
        Create("project-write", Level.Mutate, "Changes project files or dependencies",
            programs:
            [
                "npm", "pnpm", "yarn", "pip", "pipx", "dotnet", "cargo", "go", "make", "mkdir", "touch",
                "cp", "mv", "sed", "tee", "chmod", "chown", "ln",
            ]),
    ];

    private static Rule Create(
        string id,
        Level level,
        string reason,
        string[]? programs = null,
        string[]? all = null,
        string[]? any = null,
        string[]? none = null,
        PathScope[]? scopes = null,
        string? raw = null,
        string[]? redirects = null,
        string? pipedTo = null) =>
        new(id, level, reason, programs ?? [], all ?? [], any ?? [], none ?? [], scopes ?? [], raw, redirects ?? [], pipedTo);
}
