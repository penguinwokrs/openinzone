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

# The version in the committed manifest is a placeholder; the released one carries the tag.
python3 - "$STAGE/manifest.json" "$MANIFEST_VERSION" <<'PY'
import json, sys, pathlib
path, version = pathlib.Path(sys.argv[1]), sys.argv[2]
manifest = json.loads(path.read_text(encoding="utf-8"))
manifest["Version"] = version
path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
PY

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

# .streamDeckPlugin is Elgato's own container format, so only their tool can write one.
if command -v streamdeck.cmd >/dev/null 2>&1 || command -v streamdeck >/dev/null 2>&1; then
  STREAMDECK="$(command -v streamdeck.cmd || command -v streamdeck)"
  "$STREAMDECK" pack "$(wslpath -w "$STAGE" 2>/dev/null || echo "$STAGE")" \
    --output "$(wslpath -w "$ROOT/dist/streamdeck" 2>/dev/null || echo "$ROOT/dist/streamdeck")" --force
  echo "packed into $ROOT/dist/streamdeck"
else
  echo
  echo "To package it for distribution, install Elgato's CLI and run:"
  echo "  npm install -g @elgato/cli"
  echo "  streamdeck pack $(wslpath -w "$STAGE" 2>/dev/null || echo "$STAGE")"
  echo "To try it on this machine instead, re-run with --install."
fi
