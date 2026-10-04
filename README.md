# getcmd

Command safety for AI coding agents. getcmd sits between Claude Code and your shell: every `Bash` call is classified, logged, and either allowed, sent to you for approval, or blocked with a reason the agent can act on.

![demo](docs/demo.gif)

## Install

Linux and macOS:

```sh
curl -fsSL https://raw.githubusercontent.com/getCMD-com/getcmd/main/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/getCMD-com/getcmd/main/install.ps1 | iex
```

Homebrew:

```sh
brew install getcmd-com/tap/getcmd
```

Manual: download the archive for your platform from the [releases page](https://github.com/getCMD-com/getcmd/releases), check it against `SHA256SUMS`, and put the single `getcmd` binary on your PATH. Builds: linux-x64, linux-arm64, linux-musl-x64 (Alpine), osx-arm64, osx-x64, win-x64, win-arm64.

## Set up

```sh
getcmd hook claude --install
getcmd doctor
```

`--install` adds a `PreToolUse` entry to `~/.claude/settings.json` (or `.claude/settings.json` in the current directory with `--project`), with matcher `Bash` and command `getcmd hook claude`, timeout 5 seconds. On Windows it adds a second entry for the `PowerShell` tool. Everything else in the file is left as it was, and `--uninstall` removes exactly those entries. Restart Claude Code afterwards so it picks up the hook.

## What it does

Each command is placed on one of five levels. The level decides the default action; `config.json` can change the action per level.

| Level | Meaning | Default | Examples |
|---|---|---|---|
| read | Reads or builds, changes nothing that matters | allow | `ls`, `git status`, `dotnet test`, `npm run build` |
| mutate | Changes project files, dependencies or the repo | allow | `npm install`, `git commit`, `rm -rf node_modules` |
| egress | Sends data or runs code from outside | ask | `curl ... \| sh`, `npm publish`, `scp`, commands on a prod host |
| secrets | Exposes credentials | ask | `cat .env`, `echo $API_KEY`, `mysql -pPASSWORD` |
| destructive | Hard to undo | block | `git push --force`, `rm -rf ~`, `DROP TABLE`, `kubectl delete` |

Modifiers: `sudo` raises a command one level; a host tagged `prod` in `hostTags` turns mutate into egress and makes otherwise-unknown remote commands egress; wrappers nested too deep to inspect are at least egress.

## The rules

Generated with `getcmd rules list --markdown`. First matching rule wins; several rules can share an id.

| Rule | Level | What it catches |
|---|---|---|
| `rm-root-or-home` | destructive | Recursive delete of the filesystem root or the home directory |
| `rm-recursive` | destructive | Recursive delete of source or system files; only build directories are safe to remove recursively |
| `rm-build-dir` | mutate | Recursive delete of build output |
| `rm-file` | mutate | Deletes files |
| `git-force-push` | destructive | Force push can overwrite remote history; use --force-with-lease |
| `git-reset-hard` | destructive | git reset --hard discards uncommitted changes |
| `git-clean` | destructive | git clean deletes untracked files |
| `git-checkout-discard` | destructive | Discards all uncommitted changes in the working tree |
| `git-branch-delete` | destructive | Force-deletes a branch, even if unmerged |
| `git-write` | mutate | Changes the git repository or working tree |
| `sql-drop` | destructive | SQL DROP/TRUNCATE destroys data |
| `sql-delete-all` | destructive | SQL DELETE without WHERE removes every row |
| `db-drop` | destructive | Drops the database |
| `docker-destroy` | destructive | Removes Docker data that cannot be recovered |
| `kubectl-delete` | destructive | Deletes Kubernetes resources |
| `disk-write` | destructive | Writes directly to disks or filesystems |
| `perm-bomb` | destructive | Recursively makes everything world-writable |
| `kill-all` | destructive | Shuts down or reboots the machine |
| `curl-pipe-sh` | egress | Runs a downloaded script without review; download it and inspect it first |
| `curl-upload` | egress | Sends data to a remote server |
| `remote-copy` | egress | Copies files to or from a remote host |
| `ssh-remote-cmd` | egress | Runs a command on a remote host |
| `publish` | egress | Pushes an image to a registry |
| `read-credentials` | secrets | Reads a credentials file into the conversation |
| `print-secret` | secrets | Prints a secret environment variable |
| `creds-in-command` | secrets | Credentials on the command line end up in logs and shell history |
| `write-sensitive` | destructive | Overwrites a login, shell-startup or system configuration file |
| `build-tool` | read | Build or test run |
| `project-write` | mutate | Changes project files or dependencies |

`getcmd rules disable <id>` and `getcmd rules enable <id>` switch a rule off and on; `getcmd rules test` runs the case file the rules are tested against.

## Try it

```
$ getcmd check "rm -rf ./build && git push --force"
DESTRUCTIVE  block   git-force-push
  rm -rf ./build    mutate       rm-build-dir
  git push --force  destructive  Force push can overwrite remote history; use --force-with-lease

$ getcmd check "rm -rf node_modules"
MUTATE       allow   rm-build-dir
  rm -rf node_modules  mutate       Recursive delete of build output

$ getcmd check "rm -rf ~/Documents"
DESTRUCTIVE  block   rm-root-or-home
  rm -rf ~/Documents  destructive  Recursive delete of the filesystem root or the home directory

$ getcmd check 'ssh vps "docker ps"'
READ         allow   -
  docker ps  read         No rule matched; treated as read-only

$ getcmd check 'ssh vps "npm install"'        # with "hostTags": { "vps": "prod" }
EGRESS       ask     project-write
  npm install  egress       Changes project files or dependencies; on prod host vps
```

`check` exits 0 for allow, 1 for ask, 2 for block; `--json` prints the decision, `--cwd` sets the directory paths are resolved against. `getcmd log` shows what the hook decided recently. In Windows PowerShell 5.1, write inner quotes as `\"`.

## Configuration

`~/.getcmd/config.json` is created on first use. `GETCMD_HOME` moves the whole directory.

| Key | Default | Meaning |
|---|---|---|
| `actions` | `{"read":"allow","mutate":"allow","egress":"ask","secrets":"ask","destructive":"block"}` | Action per level: `allow`, `ask` or `block` |
| `buildDirs` | `["build","dist","bin","obj","node_modules",".next","target","out"]` | Directories under the project that are safe to delete recursively |
| `hostTags` | `{}` | Tags for ssh hosts; `"prod"` escalates, for example `{"vps": "prod"}` |
| `disabledRules` | `[]` | Rule ids to skip |
| `logRetentionDays` | `30` | How long rows stay in `log.db` |

If the file cannot be parsed, getcmd logs the error to `~/.getcmd/hook-errors.log`, uses the defaults and keeps enforcing; `getcmd doctor` reports it. `GETCMD_DISABLE=1` makes the hook exit without evaluating.

## How it works

1. Claude Code calls `getcmd hook claude` before each `Bash` (and `PowerShell`) tool call, with the command on stdin.
2. The command line is tokenised like a POSIX shell: quotes, escapes, `;`, `&&`, `||`, `|`, redirections, `$(...)`.
3. Wrappers are peeled off to reach what actually runs: `sudo`, `env`, `bash -c`, `eval`, `xargs`, `find -exec`, `ssh`, `plink`.
4. Each inner command is matched against the rules in order; the highest level among them wins.
5. The level maps to an action and the hook answers Claude Code.

Hook exit codes: allow exits 0 silently; ask exits 0 with a `permissionDecision: "ask"` JSON object on stdout; block exits 2 with the reason and a safer alternative on stderr. Any internal failure is written to `hook-errors.log` and exits 0, so a broken getcmd never breaks the agent. Every decision is one row in `~/.getcmd/log.db`.

## Limitations

- Shell syntax only: PowerShell-native verbs (`Remove-Item -Recurse`, `Format-Volume`, `Stop-Computer`, `Set-ExecutionPolicy`) are not classified yet.
- Heredoc bodies are tokenised as commands (over-reports, never hides), and process substitution `<(...)` is not understood.
- Rules are patterns, not a model. There is no LLM involved, so novel phrasings of a destructive action can slip through; the log is how you find them.
- Local only: decisions and logs stay on your machine. There is no sync, no team view and no remote approval yet.
- Classification runs on the command text. What a script file or a remote `plink -m` script does is invisible.

## Roadmap

- Cloud audit log across machines
- Approvals from your phone (Telegram first)
- Shared rule sets for teams

Updates at [getcmd.com](https://getcmd.com).

## Licence

Apache License 2.0. See [LICENSE](LICENSE).
