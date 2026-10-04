#!/bin/sh
# getcmd installer for Linux and macOS.
#
#   curl -fsSL https://getcmd.com/install | sh
#
# Environment:
#   GETCMD_VERSION      install this version (e.g. 0.1.0) instead of the latest release
#   GETCMD_INSTALL_DIR  install here instead of /usr/local/bin or ~/.local/bin
#
# Downloads the release archive and SHA256SUMS from GitHub, verifies the
# checksum, and copies the single binary into place. No sudo, no prompts.
set -eu

REPO="getCMD-com/getcmd"
BASE="https://github.com/$REPO/releases"

say() { printf '%s\n' "$*"; }
fail() { printf 'getcmd install: %s\n' "$*" >&2; exit 1; }

# --- platform --------------------------------------------------------------

os=$(uname -s)
case "$os" in
  Linux) os=linux ;;
  Darwin) os=osx ;;
  *) fail "unsupported operating system: $os (Linux and macOS are supported; on Windows use install.ps1)" ;;
esac

arch=$(uname -m)
case "$arch" in
  x86_64 | amd64) arch=x64 ;;
  aarch64 | arm64) arch=arm64 ;;
  *) fail "unsupported architecture: $arch (x86_64 and arm64 are supported)" ;;
esac

# Alpine and other musl systems need the musl build.
libc=""
if [ "$os" = linux ] && { ls /lib/ld-musl-*.so* > /dev/null 2>&1 || ldd --version 2>&1 | grep -qi musl; }; then
  libc="-musl"
fi

rid="$os$libc-$arch"

# --- download tools --------------------------------------------------------

if command -v curl > /dev/null 2>&1; then
  fetch() { curl -fsSL "$1" -o "$2"; }
  fetch_stdout() { curl -fsSL "$1"; }
elif command -v wget > /dev/null 2>&1; then
  fetch() { wget -qO "$2" "$1"; }
  fetch_stdout() { wget -qO- "$1"; }
else
  fail "curl or wget is required"
fi

if command -v sha256sum > /dev/null 2>&1; then
  checksum() { sha256sum "$1" | cut -d' ' -f1; }
elif command -v shasum > /dev/null 2>&1; then
  checksum() { shasum -a 256 "$1" | cut -d' ' -f1; }
else
  fail "sha256sum or shasum is required to verify the download"
fi

# --- version ---------------------------------------------------------------

version="${GETCMD_VERSION:-}"
if [ -z "$version" ]; then
  # /releases/latest ignores pre-releases; fall back to the newest release of any kind.
  version=$(fetch_stdout "https://api.github.com/repos/$REPO/releases/latest" 2> /dev/null \
    | sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p' | head -n 1) || true
  if [ -z "$version" ]; then
    version=$(fetch_stdout "https://api.github.com/repos/$REPO/releases?per_page=1" \
      | sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p' | head -n 1) || true
  fi
  [ -n "$version" ] || fail "could not determine the latest release; set GETCMD_VERSION"
fi
version="${version#v}"

archive="getcmd-$version-$rid.tar.gz"
say "Installing getcmd $version ($rid)"

# --- download and verify ---------------------------------------------------

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT INT TERM

fetch "$BASE/download/v$version/$archive" "$tmp/$archive" \
  || fail "no release asset $archive for v$version (is there a build for $rid yet?)"
fetch "$BASE/download/v$version/SHA256SUMS" "$tmp/SHA256SUMS" \
  || fail "could not download SHA256SUMS for v$version"

expected=$(grep " $archive\$" "$tmp/SHA256SUMS" | cut -d' ' -f1)
[ -n "$expected" ] || fail "$archive is not listed in SHA256SUMS"
actual=$(checksum "$tmp/$archive")
[ "$actual" = "$expected" ] || fail "checksum mismatch for $archive: expected $expected, got $actual"
say "Checksum verified"

tar -xzf "$tmp/$archive" -C "$tmp"
[ -f "$tmp/getcmd" ] || fail "archive did not contain the getcmd binary"

# --- install ---------------------------------------------------------------

if [ -n "${GETCMD_INSTALL_DIR:-}" ]; then
  dir="$GETCMD_INSTALL_DIR"
  mkdir -p "$dir"
elif [ -d /usr/local/bin ] && [ -w /usr/local/bin ]; then
  dir=/usr/local/bin
else
  dir="$HOME/.local/bin"
  mkdir -p "$dir"
fi

chmod +x "$tmp/getcmd"
# mv replaces the file in one step, so a running getcmd is not overwritten in place.
mv -f "$tmp/getcmd" "$dir/getcmd"
say "Installed $dir/getcmd"

# --- PATH ------------------------------------------------------------------

case ":$PATH:" in
  *":$dir:"*) ;;
  *)
    say ""
    say "$dir is not on your PATH. Add it with:"
    case "${SHELL:-}" in
      */zsh) say "  echo 'export PATH=\"$dir:\$PATH\"' >> ~/.zshrc && source ~/.zshrc" ;;
      */fish) say "  fish_add_path $dir" ;;
      */bash) say "  echo 'export PATH=\"$dir:\$PATH\"' >> ~/.bashrc && source ~/.bashrc" ;;
      *) say "  export PATH=\"$dir:\$PATH\"   # add this line to your shell's startup file" ;;
    esac
    ;;
esac

say ""
say "Next steps:"
say "  getcmd hook claude --install   # add the PreToolUse hook to ~/.claude/settings.json"
say "  getcmd doctor                  # check the installation"
