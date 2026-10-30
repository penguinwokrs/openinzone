// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.Versioning;

// Everything here reaches Windows through P/Invoke and COM; there is no cross-platform path.
[assembly: SupportedOSPlatform("windows")]
