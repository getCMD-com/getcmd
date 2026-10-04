#!/usr/bin/env bash
# Compiles the SQLite amalgamation into one object file so the Native AOT build
# can link SQLite into the getcmd binary (see SqliteStaticObject in
# src/Getcmd.Cli/Getcmd.Cli.csproj). Prints the object file's path.
#
# Usage: build-sqlite.sh [output-dir]
set -euo pipefail

version=3500400 # 3.50.4
year=2025
out="${1:-native}"

mkdir -p "$out"
curl -fsSL -o "$out/sqlite.zip" "https://sqlite.org/$year/sqlite-amalgamation-$version.zip"
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
    clang -c "${flags[@]}" "$src" -o "$out/sqlite3.obj"
    cygpath -w "$(realpath "$out/sqlite3.obj")"
    ;;
  *)
    cc -c -fPIC "${flags[@]}" "$src" -o "$out/sqlite3.o"
    realpath "$out/sqlite3.o"
    ;;
esac
