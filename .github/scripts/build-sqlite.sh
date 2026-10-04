#!/usr/bin/env bash
# Compiles the SQLite amalgamation into one object file so the Native AOT build
# can link SQLite into the getcmd binary (see SqliteStaticObject in
# src/Getcmd.Cli/Getcmd.Cli.csproj). Prints the object file's path.
#
# Usage: build-sqlite.sh [output-dir]
set -euo pipefail

version=3500400 # 3.50.4
year=2025
sha256=1d3049dd0f830a025a53105fc79fd2ab9431aea99e137809d064d8ee8356b032
out="${1:-native}"

mkdir -p "$out"
curl -fsSL -o "$out/sqlite.zip" "https://sqlite.org/$year/sqlite-amalgamation-$version.zip"

# Refuse to build from anything but the pinned amalgamation.
if command -v sha256sum > /dev/null; then
  actual=$(sha256sum "$out/sqlite.zip" | cut -d' ' -f1)
else
  actual=$(shasum -a 256 "$out/sqlite.zip" | cut -d' ' -f1)
fi
if [ "$actual" != "$sha256" ]; then
  echo "sqlite-amalgamation-$version.zip: expected sha256 $sha256, got $actual" >&2
  exit 1
fi

unzip -q -o "$out/sqlite.zip" -d "$out"
src="$out/sqlite-amalgamation-$version/sqlite3.c"

# Same feature set as SQLitePCLRaw's e_sqlite3, so every P/Invoke in the provider resolves.
flags=(
  -O2
  -DSQLITE_THREADSAFE=1
  -DSQLITE_DEFAULT_FOREIGN_KEYS=1
  -DSQLITE_ENABLE_COLUMN_METADATA
  -DSQLITE_ENABLE_DBSTAT_VTAB
  -DSQLITE_ENABLE_FTS3_PARENTHESIS
  -DSQLITE_ENABLE_FTS4
  -DSQLITE_ENABLE_FTS5
  -DSQLITE_ENABLE_MATH_FUNCTIONS
  -DSQLITE_ENABLE_RTREE
  -DSQLITE_ENABLE_SNAPSHOT
)

case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    # clang targets the host (x64 or ARM64) and emits an MSVC-compatible object;
    # cl is the fallback when a developer command prompt is set up instead.
    if command -v clang > /dev/null; then
      clang -c "${flags[@]}" "$src" -o "$out/sqlite3.obj"
    else
      cl -nologo -O2 -c "${flags[@]}" "$src" "-Fo$out/sqlite3.obj" > /dev/null
    fi
    cygpath -w "$(realpath "$out/sqlite3.obj")"
    ;;
  *)
    cc -c -fPIC "${flags[@]}" "$src" -o "$out/sqlite3.o"
    realpath "$out/sqlite3.o"
    ;;
esac
