using NUnit.Framework;
using DealEngine;

public class DealSizeCalculatorTests
{
    [Test]
    public void Inner_OneDollar_HasExpectedCounts()
    {
        DealConfig config = PlaceholderConfig.Create();
        DealSize s = DealSizeCalculator.Calculate(config, 0, 100);

        Assert.IsTrue(s.IsValid);
        Assert.AreEqual(3, s.PairedCycles);
        Assert.AreEqual(80, s.PaidSpins);
        Assert.AreEqual(18, s.PrizeTickets);
        Assert.AreEqual(62, s.LossTickets);
        Assert.AreEqual(8000, s.TotalWagersCents);
        Assert.AreEqual(7200, s.TotalPayoutsCents);
    }

    [Test]
    public void EveryValidCombination_IsExactlyTargetRtp()
    {
        DealConfig config = PlaceholderConfig.Create();
        for (int m = 0; m < config.modes.Count; m++)
        {
            foreach (long bet in config.betLevelsCents)
            {
                DealSize s = DealSizeCalculator.Calculate(config, m, bet);
                if (!s.IsValid) continue;
                Assert.AreEqual(
                    s.TotalPayoutsCents * 10000,
                    s.TotalWagersCents * config.targetRtpBasisPoints);
            }
        }
    }

    [Test]
    public void FixedCycleCount_WithFractionalSpins_IsRejectedNotRounded()
    {
        DealConfig config = PlaceholderConfig.Create();
        DealSize s = DealSizeCalculator.CalculateForCycles(config, 0, 100, 1);
        Assert.IsFalse(s.IsValid);
        Assert.AreEqual(0, s.PaidSpins);
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

        DealSize s = DealSizeCalculator.Calculate(config, 0, 100);
        Assert.IsFalse(s.IsValid);
    }

    [Test]
    public void Table_HasHeaderPlusFortyRows()
    {
        string csv = DealSizeTable.BuildCsv(PlaceholderConfig.Create());
        string[] lines = csv.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(41, lines.Length);
    }
}