using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using DealEngine;

public class DealBuilderTests
{
    [Test]
    public void Inner_OneDollar_HasCorrectCountsAndExactRtp()
    {
        Deal d = DealBuilder.Build(PlaceholderConfig.Create(), 0, 100);

        Assert.AreEqual(80, d.tickets.Count);
        Assert.AreEqual(62, d.tickets.Count(t => t.kind == TicketKind.Loss));
        Assert.AreEqual(18, d.tickets.Count(t => t.kind != TicketKind.Loss));
        Assert.AreEqual(8000, DealVerifier.TotalWagersCents(d));
        Assert.AreEqual(7200, DealVerifier.TotalPayoutsCents(d));
        Assert.IsNull(DealVerifier.Verify(d));
        Assert.AreEqual(DealState.Ready, d.state);
    }

    [Test]
    public void Cycles_AreNotMixed()
    {
        DealConfig config = PlaceholderConfig.Create();
        Deal d = DealBuilder.Build(config, 0, 100);
        AssertCycleOrder(d, SlotType.Regular, config.modes[0].regularBag.TicketCount());
        AssertCycleOrder(d, SlotType.Big, config.modes[0].bigBag.TicketCount());
    }

    private static void AssertCycleOrder(Deal d, SlotType slot, int perCycle)
    {
        int expectedCycle = 0;
        int usedInCycle = 0;
        foreach (Ticket t in d.tickets)
        {
            if (t.slot != slot) continue;
            Assert.AreEqual(expectedCycle, t.cycle);
            usedInCycle++;
            if (usedInCycle == perCycle) { expectedCycle++; usedInCycle = 0; }
        }
        Assert.AreEqual(d.pairedCycles, expectedCycle);
        Assert.AreEqual(0, usedInCycle);
    }

    [Test]
    public void BuiltDeal_SatisfiesPlacementLimits()
    {
        Deal d = DealBuilder.Build(PlaceholderConfig.Create(), 0, 100);
        SlotType[] slots = d.tickets.Select(t => t.slot).ToArray();
        int losses = slots.Count(s => s == SlotType.Loss);
        int regular = slots.Count(s => s == SlotType.Regular);
        int big = slots.Count(s => s == SlotType.Big);

        PlacementLimits limits = PlacementLimits.Calculate(losses, regular, big, 0, 0, 0);
        Assert.IsTrue(PlacementChecker.Check(slots, limits, out string reason), reason);
    }

    [Test]
    public void ImpossibleLimits_AreRejected_AndLogged()
    {
        DealConfig config = PlaceholderConfig.Create();
        config.maxConsecutiveLossesOverride = 2;
        List<string> logs = new List<string>();

        Assert.Throws<DealBuildException>(() => DealBuilder.Build(config, 0, 100, logs.Add));
        Assert.IsNotEmpty(logs);
    }

    [Test]
    public void PaidSpinsFewerThanPrizeTickets_IsRejected()
    {
        DealConfig config = new DealConfig
        {
            configVersion = "t",
            targetRtpBasisPoints = 9000,
            bonusUnitCents = 1
        };
        config.betLevelsCents.Add(100);
        ModeConfig mode = new ModeConfig { name = "Tiny" };
        mode.regularBag.prizes.Add(new PrizeEntry { kind = PrizeKind.Bonus, value = 1, count = 10 });
        mode.bigBag.prizes.Add(new PrizeEntry { kind = PrizeKind.Bonus, value = 1, count = 10 });
        config.modes.Add(mode);

        Assert.Throws<DealBuildException>(() => DealBuilder.Build(config, 0, 100));
    }

    [Test]
    public void AllFortyCombinations_BuildAndVerify()
    {
        DealConfig config = PlaceholderConfig.Create();
        for (int m = 0; m < config.modes.Count; m++)
        {
            foreach (long bet in config.betLevelsCents)
            {
                Deal d = DealBuilder.Build(config, m, bet);
                Assert.IsNull(DealVerifier.Verify(d), config.modes[m].name + " " + bet);
            }
        }
    }

    [Test]
    public void TamperedDeal_FailsVerification()
    {
        Deal d = DealBuilder.Build(PlaceholderConfig.Create(), 0, 100);
        Ticket prize = d.tickets.First(t => t.kind != TicketKind.Loss);
        prize.value += 1;
        Assert.IsNotNull(DealVerifier.Verify(d));
    }

    [Test]
    public void TwoDeals_HaveDifferentOrderAndIds()
    {
        DealConfig config = PlaceholderConfig.Create();
        Deal a = DealBuilder.Build(config, 0, 100);
        Deal b = DealBuilder.Build(config, 0, 100);

        Assert.AreNotEqual(a.dealId, b.dealId);
        string orderA = string.Join(",", a.tickets.Select(t => (int)t.kind + ":" + t.value));
        string orderB = string.Join(",", b.tickets.Select(t => (int)t.kind + ":" + t.value));
        Assert.AreNotEqual(orderA, orderB);
    }
}