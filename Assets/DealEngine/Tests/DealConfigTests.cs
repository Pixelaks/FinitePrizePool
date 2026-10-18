using System;
using NUnit.Framework;
using DealEngine;

public class DealConfigTests
{
    [Test]
    public void Placeholder_HasTwentyBetLevelsAndTwoModes()
    {
        DealConfig config = PlaceholderConfig.Create();
        Assert.AreEqual(20, config.betLevelsCents.Count);
        Assert.AreEqual(25, config.betLevelsCents[0]);
        Assert.AreEqual(500, config.betLevelsCents[19]);
        Assert.AreEqual(2, config.modes.Count);
    }

    [Test]
    public void Placeholder_PassesValidation()
    {
        Assert.DoesNotThrow(() => PlaceholderConfig.Create().Validate());
    }

    [Test]
    public void Validate_RejectsMissingVersion()
    {
        DealConfig config = PlaceholderConfig.Create();
        config.configVersion = "";
        Assert.Throws<ArgumentException>(() => config.Validate());
    }

    [Test]
    public void Bag_TotalsAreCorrect()
    {
        BagConfig bag = PlaceholderConfig.Create().modes[0].regularBag; // Inner regular
        Assert.AreEqual(4, bag.TicketCount());
        Assert.AreEqual(9, bag.MultiplierTotal()); // 3 x 2 + 1 x 3
        Assert.AreEqual(0, bag.BonusUnitsTotal());
    }
}