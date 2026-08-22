#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
#
# Builds the installer. WiX cannot author packages off Windows, so this publishes on the Linux
# side and hands the payload to the Windows-side Inno Setup compiler through interop.
set -euo pipefail

VERSION="${1:?VERSION argument required (e.g., 0.1.0)}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

# winget's install location for JRSoftware.InnoSetup varies by machine: some installs land in
# the machine-wide Program Files (x86), others go per-user under %LOCALAPPDATA%\Programs. Rather
# than hardcode either — the latter embeds a username that only exists on one machine — ask
# Windows itself where %LOCALAPPDATA% is and check both spots.
find_iscc() {
  local candidate="/mnt/c/Program Files (x86)/Inno Setup 6/ISCC.exe"
  if [ -x "$candidate" ]; then
    printf '%s' "$candidate"
    return 0
  fi

  local local_appdata
  # A failure here (powershell.exe missing or erroring) must not abort the script under -e; it's
  # distinct from Inno Setup simply not being installed, and either way this function should fall
  # through to `return 1` so the caller prints the friendly "not found" message.
  local_appdata="$(powershell.exe -NoProfile -Command "[Environment]::GetFolderPath('LocalApplicationData')" 2>/dev/null | tr -d '\r')" || true
  if [ -n "$local_appdata" ]; then
    candidate="$(wslpath -u "$local_appdata")/Programs/Inno Setup 6/ISCC.exe"
    if [ -x "$candidate" ]; then
      printf '%s' "$candidate"
      return 0
    fi
  fi

  return 1
}

ISCC="${ISCC:-}"
if [ -z "$ISCC" ]; then
  ISCC="$(find_iscc)" || true
fi

if [ -z "$ISCC" ] || [ ! -x "$ISCC" ]; then
  echo "Inno Setup (ISCC.exe) not found. Install it with:" >&2
  echo "  winget.exe install --id JRSoftware.InnoSetup --silent --accept-package-agreements --accept-source-agreements" >&2
  echo "or set ISCC to its path." >&2
  exit 1
fi

rm -rf "$ROOT/dist"
dotnet publish "$ROOT/src/OpenInzone.Tray" -c Release -r win-x64 --self-contained true \
  -p:Version="$VERSION" -o "$ROOT/dist/tray"
dotnet publish "$ROOT/src/OpenInzone.Cli" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:Version="$VERSION" -o "$ROOT/dist/cli"

# Verify both publish outputs exist; Inno Setup only warns on wildcard mismatches
if [ ! -f "$ROOT/dist/tray/inzonetray.exe" ]; then
  echo "Error: $ROOT/dist/tray/inzonetray.exe was not published." >&2
  exit 1
fi
if [ ! -f "$ROOT/dist/cli/inzone.exe" ]; then
  echo "Error: $ROOT/dist/cli/inzone.exe was not published." >&2
  exit 1
fi

# WSL does not forward the environment to a Windows binary invoked directly unless the variable
# is named in WSLENV; without this, ISCC.exe sees an empty OPENINZONE_VERSION and the installer
# silently falls back to 0.0.0.
WSLENV="OPENINZONE_VERSION${WSLENV:+:$WSLENV}" OPENINZONE_VERSION="$VERSION" \
  "$ISCC" "$(wslpath -w "$ROOT/installer/openinzone.iss")"
echo "installer written to $ROOT/dist"
