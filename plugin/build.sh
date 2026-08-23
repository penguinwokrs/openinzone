#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
#
# Builds the Stream Deck plugin. Assembles a .sdPlugin directory under dist/ - the committed one
# is the source and is left alone - and, if Elgato's tool is on the machine, packs it for
# distribution. With --install it is copied into Stream Deck's own plugins directory instead,
# which is how the plugin is tried on real hardware: Stream Deck loads an unpacked .sdPlugin
# folder from there, so packing is only needed to hand the plugin to someone else.
set -euo pipefail

VERSION="${1:?VERSION argument required (e.g., 0.1.0)}"
INSTALL="${2:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_ID="com.penguinwokrs.openinzone"
# Pinned so a release is not at the mercy of whatever the CLI does next.
ELGATO_CLI_VERSION="1.9.0"
SOURCE="$ROOT/plugin/$PLUGIN_ID.sdPlugin"
STAGE="$ROOT/dist/streamdeck/$PLUGIN_ID.sdPlugin"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

# Stream Deck's manifest wants four parts; releases are tagged with three.
case "$VERSION" in
  *.*.*.*) MANIFEST_VERSION="$VERSION" ;;
  *.*.*)   MANIFEST_VERSION="$VERSION.0" ;;
  *) echo "Error: VERSION should look like 0.1.0" >&2; exit 1 ;;
esac

rm -rf "$ROOT/dist/streamdeck"
mkdir -p "$(dirname "$STAGE")"
cp -r "$SOURCE" "$STAGE"

# The version in the committed manifest is a placeholder; the released one carries the tag. Done
# with sed rather than a JSON library so this runs the same on a CI runner, where python3 may not
# be on PATH: "Version" appears once in the manifest, at the top level.
sed -i "s|\"Version\": \"[^\"]*\"|\"Version\": \"$MANIFEST_VERSION\"|" "$STAGE/manifest.json"
grep -q "\"Version\": \"$MANIFEST_VERSION\"" "$STAGE/manifest.json" || {
  echo "Error: could not set the manifest version." >&2
  exit 1
}

# Trimmed because CodePath must be one file and 14 MB beats 64. The plugin opens no COM of its
# own - it asks the tray - so nothing here depends on built-in COM surviving the trimmer.
dotnet publish "$ROOT/src/OpenInzone.StreamDeck" -c Release -r win-x64 \
  --self-contained true -p:PublishTrimmed=true -p:PublishSingleFile=true \
  -p:Version="$VERSION" -o "$STAGE"

EXE="$STAGE/openinzone-streamdeck.exe"
if [ ! -f "$EXE" ]; then
  echo "Error: $EXE was not published." >&2
  exit 1
fi

# A trimmed self-contained build of this plugin measures about 14 MB. Anything far below that is
# a framework-dependent build that will not start on a machine without .NET, which Stream Deck
# reports only as a plugin that refuses to load.
MIN_BYTES=$((8 * 1024 * 1024))
SIZE="$(stat -c%s "$EXE")"
if [ "$SIZE" -lt "$MIN_BYTES" ]; then
  echo "Error: $EXE is only $SIZE bytes; a self-contained build is around 14 MB." >&2
  exit 1
fi

# Publishing leaves debugging symbols beside the executable; they are not wanted in a plugin
# that gets copied into someone else's Stream Deck.
rm -f "$STAGE"/*.pdb

echo "staged $STAGE ($SIZE bytes)"

if [ "$INSTALL" = "--install" ]; then
  APPDATA="$(powershell.exe -NoProfile -Command "[Environment]::GetFolderPath('ApplicationData')" 2>/dev/null | tr -d '\r')" || true
  if [ -z "$APPDATA" ]; then
    echo "Error: could not ask Windows where %APPDATA% is." >&2
    exit 1
  fi

  TARGET="$(wslpath -u "$APPDATA")/Elgato/StreamDeck/Plugins/$PLUGIN_ID.sdPlugin"
  if [ ! -d "$(dirname "$TARGET")" ]; then
    echo "Error: Stream Deck does not appear to be installed ($(dirname "$TARGET") is missing)." >&2
    exit 1
  fi

  # Stream Deck holds the running plugin's executable open, so it has to be stopped first.
  echo "Stop Stream Deck before installing, then press Enter." >&2
  read -r _
  rm -rf "$TARGET"
  cp -r "$STAGE" "$TARGET"
  echo "installed to $TARGET - start Stream Deck again"
  exit 0
fi

# Elgato's own tool is the only thing that can read a manifest the way Stream Deck will, and the
# only thing that can write the .streamDeckPlugin container. It needs Node; npx runs it without
# installing anything, and a copy already on PATH is used in preference.
streamdeck_cli() {
  if command -v streamdeck >/dev/null 2>&1; then
    streamdeck "$@"
  elif command -v npx >/dev/null 2>&1; then
    npx --yes "@elgato/cli@$ELGATO_CLI_VERSION" "$@"
  else
    return 127
  fi
}

if ! streamdeck_cli -v >/dev/null 2>&1; then
  echo
  echo "Node is not available, so the plugin was not validated or packaged."
  echo "Install Node 20.1 or later and re-run to produce a .streamDeckPlugin."
  exit 0
fi

# Validation is the part worth having even when not packaging: a manifest Stream Deck will not
# accept is otherwise only discovered by installing the plugin and finding it does nothing.
streamdeck_cli validate "$STAGE"

streamdeck_cli pack "$STAGE" --output "$ROOT/dist" --force
PACKAGE="$ROOT/dist/$PLUGIN_ID.streamDeckPlugin"
if [ ! -f "$PACKAGE" ]; then
  echo "Error: $PACKAGE was not produced." >&2
  exit 1
fi
echo "packaged $PACKAGE ($(stat -c%s "$PACKAGE") bytes)"
