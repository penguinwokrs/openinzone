// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class UpdateSupportTests
{
    [Fact]
    public void Drops_the_revision_component()
    {
        var reduced = UpdateSupport.ThreeComponent(new Version(1, 4, 0, 2));

        Assert.Equal(new Version(1, 4, 0), reduced);
    }

    [Fact]
    public void A_same_version_release_no_longer_compares_as_newer_once_reduced()
    {
        // Regression for the gotcha the update-core report recorded: System.Version comparison is
        // component-count sensitive, so the raw four-component assembly version would make an
        // identical three-component release tag compare as newer.
        var running = new Version(0, 1, 0, 0);

        Assert.False(new Version(0, 1, 0) > UpdateSupport.ThreeComponent(running));
        Assert.Equal(new Version(0, 1, 0), UpdateSupport.ThreeComponent(running));
    }

    [Fact]
    public void A_missing_build_component_is_treated_as_zero_not_negative_one()
    {
        var reduced = UpdateSupport.ThreeComponent(new Version(1, 0));

        Assert.Equal(new Version(1, 0, 0), reduced);
    }
}
