using System.Linq;
using NUnit.Framework;
using DealEngine;

public class PlacementTests
{
    [Test]
    public void Limits_MatchBriefFormulas()
    {
        // Placeholder Inner $1.00: 62 losses, 12 Regular, 6 Big, 80 spins
        PlacementLimits l = PlacementLimits.Calculate(62, 12, 6, 0, 0, 0);
        Assert.AreEqual(8, l.MaxConsecutiveLosses);   // 2 * ceil(62 / 19)
        Assert.AreEqual(14, l.MaxRegularGap);         // 2 * ceil(80 / 13)
        Assert.AreEqual(24, l.MaxBigGap);             // 2 * ceil(80 / 7)
    }

    [Test]
    public void Formula_Limits_AreFeasible()
    {
        PlacementLimits l = PlacementLimits.Calculate(62, 12, 6, 0, 0, 0);
        Assert.IsNull(l.FeasibilityError(62, 12, 6));
    }

    [Test]
    public void TooTightLossLimit_IsRejectedAsImpossible()
    {
        PlacementLimits l = PlacementLimits.Calculate(62, 12, 6, 2, 0, 0); // (18 + 1) * 2 = 38 < 62
        Assert.IsNotNull(l.FeasibilityError(62, 12, 6));
    }

    [Test]
    public void TooManyConsecutiveLosses_Fails()
    {
        SlotType[] slots =
        {
            SlotType.Loss, SlotType.Loss, SlotType.Loss, SlotType.Loss,
            SlotType.Regular, SlotType.Big, SlotType.Loss, SlotType.Regular, SlotType.Big
        };
        PlacementLimits l = new PlacementLimits { MaxConsecutiveLosses = 3, MaxRegularGap = 100, MaxBigGap = 100 };
        Assert.IsFalse(PlacementChecker.Check(slots, l, out string reason));
        StringAssert.Contains("consecutive", reason);
    }

    [Test]
    public void FixedPrizeInterval_Fails()
    {
        // Prizes at spins 2, 5, 8, 11: always 3 apart
        SlotType[] slots =
        {
            SlotType.Loss, SlotType.Loss, SlotType.Regular, SlotType.Loss, SlotType.Loss, SlotType.Big,
            SlotType.Loss, SlotType.Loss, SlotType.Regular, SlotType.Loss, SlotType.Loss, SlotType.Big
        };
        PlacementLimits l = new PlacementLimits { MaxConsecutiveLosses = 10, MaxRegularGap = 100, MaxBigGap = 100 };
        Assert.IsFalse(PlacementChecker.Check(slots, l, out string reason));
        StringAssert.Contains("interval", reason);
    }

    [Test]
    public void IrregularOrder_WithinLimits_Passes()
    {
        SlotType[] slots =
        {
            SlotType.Loss, SlotType.Regular, SlotType.Loss, SlotType.Loss, SlotType.Big,
            SlotType.Regular, SlotType.Loss, SlotType.Big, SlotType.Loss, SlotType.Regular
        };
        PlacementLimits l = new PlacementLimits { MaxConsecutiveLosses = 2, MaxRegularGap = 4, MaxBigGap = 4 };
        Assert.IsTrue(PlacementChecker.Check(slots, l, out string reason), reason);
    }

    [Test]
    public void Shuffle_KeepsSameItems_AndChangesOrder()
    {
        var items = Enumerable.Range(0, 100).ToList();
        var copy = items.ToList();
        SecureShuffle.Shuffle(copy);

        CollectionAssert.AreEquivalent(items, copy);
        CollectionAssert.AreNotEqual(items, copy); // chance of an identical order is about 1 in 10^157
    }
}