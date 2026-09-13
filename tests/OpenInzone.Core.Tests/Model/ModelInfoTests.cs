// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;

namespace OpenInzone.Tests.Model;

public class ModelInfoTests
{
    /// <summary>INZONE Hub offers the microphone mute button on INZONE Buds and nothing else (#19).</summary>
    [Theory]
    [InlineData(4, true)]   // INZONE Buds
    [InlineData(0, false)]  // INZONE H9
    [InlineData(3, false)]  // INZONE H5
    [InlineData(5, false)]  // INZONE H9 II
    [InlineData(99, false)] // a model this build does not know
    public void Only_the_earbuds_take_a_microphone_mute_from_the_computer(byte modelId, bool accepts)
    {
        var model = new ModelInfo(modelId, 0, 0, 0, 0, "", "", "");

        Assert.Equal(accepts, model.AcceptsMicMute);
    }
}
