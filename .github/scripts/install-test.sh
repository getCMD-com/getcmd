#!/usr/bin/env bash
# Runs install.sh inside a fresh Docker image and checks that getcmd works
# there. Every doctor check except "hook installed" (there is no Claude Code
# in the container) must pass.
#
# Usage: install-test.sh <image>            # e.g. ubuntu:24.04, alpine:3.20
# Env:   GETCMD_VERSION                     # optional; default is the latest release
set -euo pipefail

image="$1"

case "$image" in
  alpine*) prepare='apk add --no-cache curl > /dev/null' ;;
  *) prepare='apt-get update -qq && apt-get install -y -qq curl ca-certificates > /dev/null' ;;
esac

docker run --rm \
  -v "$PWD/install.sh:/install.sh:ro" \
  -e "GETCMD_VERSION=${GETCMD_VERSION:-}" \
  "$image" sh -c "
    set -eu
    $prepare
    sh /install.sh
    echo
    getcmd doctor > doctor.txt || true
    cat doctor.txt
    if grep -v 'hook installed' doctor.txt | grep -q FAIL; then
      echo 'doctor reported a failure other than the hook check' >&2
      exit 1
    fi
    grep -q 'OK.*hook installed\|FAIL.*hook installed' doctor.txt
    echo 'install test passed on $image'
  "
