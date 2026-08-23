// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Control;

/// <summary>
/// The pieces of installing an update that need no network and so can be decided - and tested -
/// without any of the download or process-launch machinery the tray wraps around them.
/// </summary>
public static class UpdateSupport
{
    /// <summary>
    /// Reduces an assembly version to the three components a release tag can express.
    /// <see cref="System.Version"/> comparison is sensitive to how many components are present, not
    /// just their values - a four-component "0.1.0.0" does not equal the three-component "0.1.0"
    /// that <see cref="UpdateInfo.CheckRelease"/> parses from a tag, so comparing the raw assembly
    /// version directly would make a same-version release look newer than the build already running
    /// it. A missing build component reads as -1, which is clamped to 0 rather than carried through.
    /// </summary>
    public static Version ThreeComponent(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
