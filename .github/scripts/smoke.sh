#!/usr/bin/env bash
# Runs a freshly built getcmd binary on its own, in an empty directory with its
# own GETCMD_HOME, to prove it needs no files next to it: --version, an allow,
# a block (exit 2) and a log read, which exercises the linked-in SQLite.
#
# Usage: smoke.sh <path-to-getcmd-binary>
set -euo pipefail

binary="$(realpath "$1")"
work="$(mktemp -d)"
cd "$work"
export GETCMD_HOME="$work/home"

"$binary" --version

"$binary" check "ls"

set +e
"$binary" check "git push --force"
code=$?
set -e
if [ "$code" -ne 2 ]; then
  echo "expected exit code 2 for a blocked command, got $code" >&2
  exit 1
fi

"$binary" log --last 2
test -f "$GETCMD_HOME/log.db"
echo "smoke test passed"
