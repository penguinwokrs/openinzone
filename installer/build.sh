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

INSTALLER="$ROOT/dist/OpenInzone-$VERSION-setup.exe"

# ISCC.exe reads the payload through the \\wsl.localhost share, and that share has been observed
# on this machine to hand Windows an incomplete view of a directory the Linux side already sees
# in full — a run can compile "successfully" from a partial dist/tray or dist/cli and produce a
# short installer instead of failing. dist/tray alone is ~160 MB uncompressed and a correct
# installer compresses to ~70 MB; a partial-view build in testing came out single-digit MB. 40 MB
# sits far below the former and far above the latter, so it separates a real build from a
# truncated one without being so tight that ordinary size variation could trip it.
#
# The size must be read from the Windows side: it just wrote the file, and the Linux view of the
# same file can lag behind for a few seconds afterward (observed directly on this machine).
# A Linux-side existence check right here would hit the same stale view the size check below is
# built to distrust, so there is no separate "-f" test: an empty result from installer_size_bytes
# (neither side could report a length) is what stands in for "not found".
installer_size_bytes() {
  local win_path bytes
  win_path="$(wslpath -w "$INSTALLER")"
  bytes="$(powershell.exe -NoProfile -Command "(Get-Item -LiteralPath '$win_path').Length" 2>/dev/null | tr -d '\r')" || true
  if [ -n "$bytes" ] && [ "$bytes" -eq "$bytes" ] 2>/dev/null; then
    printf '%s' "$bytes"
    return 0
  fi
  # powershell.exe unavailable or errored; fall back to the (possibly stale) Linux view rather
  # than skip the check outright. Captured rather than returned directly, so a failed/missing
  # stat leaves bytes empty for the caller to judge instead of tripping set -e in here.
  bytes="$(stat -c%s "$INSTALLER" 2>/dev/null)" || true
  printf '%s' "$bytes"
}

MIN_INSTALLER_BYTES=$((40 * 1024 * 1024))
size_bytes="$(installer_size_bytes)"
if [ -z "$size_bytes" ]; then
  echo "Error: could not read a size for $INSTALLER from either side of the wsl.localhost boundary." >&2
  exit 1
fi
if [ "$size_bytes" -lt "$MIN_INSTALLER_BYTES" ]; then
  echo "Error: $INSTALLER is only $size_bytes bytes, which is smaller than the $MIN_INSTALLER_BYTES-byte floor for a complete build." >&2
  echo "This is the wsl.localhost share serving ISCC.exe a partial view of dist/; re-run the build." >&2
  exit 1
fi

echo "installer written to $INSTALLER ($size_bytes bytes)"
