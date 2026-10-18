using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using DealEngine;

public class LifecycleTests
{
    private string folder;
    private DealConfig config;
    private readonly System.Collections.Generic.List<DealService> services = new System.Collections.Generic.List<DealService>();

    private class UnityJson : IJsonSerializer
    {
        public string ToJson<T>(T obj) { return UnityEngine.JsonUtility.ToJson(obj); }
        public T FromJson<T>(string json) { return UnityEngine.JsonUtility.FromJson<T>(json); }
    }

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "fpp_life_" + Guid.NewGuid().ToString("N"));
        config = PlaceholderConfig.Create();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (DealService service in services) service.WaitForSuccessors();
        services.Clear();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private DealService NewService()
    {
        DealService service = new DealService(config, new UnityJson(), folder);
        services.Add(service);
        return service;
    }

    // Plays spins for player "p" on Inner, returns the total awarded in cents.
    private static long PlaySpins(DealService s, int spins, long bet, string expectedDealId = null)
    {
        long awards = 0;
        for (int i = 0; i < spins; i++)
        {
            SpinResult r = s.Spin("p", 0, bet);
            Assert.IsTrue(r.IsSuccess, r.message);
            if (expectedDealId != null) Assert.AreEqual(expectedDealId, r.record.dealId);
            awards += r.record.awardCents;
            s.MarkResultShown(r.record.spinId);
        }
        return awards;
    }

    [Test]
    public void FinalTicket_CompletesDeal_AndSwitchesToPreparedSuccessor()
    {
        DealService s = NewService();
        s.EnsurePlayer("p", "P", 1000000);

        s.Prepare(0, 25);
        s.WaitForSuccessors();
        string firstDeal = s.GetReadyDealId(0, 25);
        Assert.IsNotNull(firstDeal);

        PlaySpins(s, 1, 25, firstDeal);   // the prepared deal is the one that gets used
        s.WaitForSuccessors();            // its successor is built in the background
        string successor = s.GetReadyDealId(0, 25);
        Assert.IsNotNull(successor);
        Assert.AreNotEqual(firstDeal, successor);

        PlaySpins(s, 59, 25, firstDeal);  // rest of the 60-spin deal

        DealProgress completed = s.GetDealRecords().Single(d => d.dealId == firstDeal);
        Assert.AreEqual(DealState.Completed, completed.state);
        Assert.AreEqual(60, completed.ticketCount);
        Assert.AreEqual(1500, completed.totalWagersCents);
        Assert.AreEqual(1350, completed.totalPayoutsCents);
        Assert.AreEqual(9000, completed.targetRtpBasisPoints);
        Assert.AreEqual("90.0000%", completed.actualRtpPercent);
        Assert.IsTrue(completed.reachedTarget);

        DealInfo now = s.GetDealInfo(0, 25);
        Assert.AreEqual(successor, now.dealId);   // play continues on the Ready successor
        Assert.AreEqual(0, now.spinsPlayed);
        PlaySpins(s, 1, 25, successor);
    }

    [Test]
    public void ConfigChange_LeavesActiveDealAlone_AndNextDealUsesNewVersion()
    {
        DealService s = NewService();
        s.EnsurePlayer("p", "P", 1000000);

        PlaySpins(s, 1, 25);
        string firstDeal = s.GetDealInfo(0, 25).dealId;
        s.WaitForSuccessors();
        string oldReady = s.GetReadyDealId(0, 25);
        Assert.IsNotNull(oldReady);

        DealConfig v2 = PlaceholderConfig.Create();
        v2.configVersion = "placeholder-2";
        s.ApplyConfig(v2);

        DealInfo info = s.GetDealInfo(0, 25);
        Assert.AreEqual(firstDeal, info.dealId);
        Assert.AreEqual("placeholder-1", info.configVersion);
        Assert.AreEqual(1, info.spinsPlayed);

        s.WaitForSuccessors();
        string newReady = s.GetReadyDealId(0, 25);
        Assert.IsNotNull(newReady);
        Assert.AreNotEqual(oldReady, newReady);
        Assert.IsTrue(s.GetDealRecords().Any(d => d.dealId == oldReady && d.state == DealState.Retired));

        PlaySpins(s, 59, 25, firstDeal);  // the active deal finishes on its own config version

        DealInfo next = s.GetDealInfo(0, 25);
        Assert.AreEqual(newReady, next.dealId);
        Assert.AreEqual("placeholder-2", next.configVersion);
    }

    [Test]
    public void Retire_LogsEveryField_AndReplacementIsFreshAndVerified()
    {
        DealService s = NewService();
        s.EnsurePlayer("p", "P", 1000000);

        long awards = PlaySpins(s, 10, 25);
        s.WaitForSuccessors();
        DealInfo before = s.GetDealInfo(0, 25);
        string readyBefore = s.GetReadyDealId(0, 25);

        RetireResult result = s.Retire(0, 25, "operator test");
        Assert.IsTrue(result.success, result.message);

        RetireRecord r = result.record;
        Assert.AreEqual(before.dealId, r.dealId);
        Assert.AreEqual("operator test", r.reason);
        Assert.IsNotEmpty(r.timeUtc);
        Assert.AreEqual("placeholder-1", r.oldConfigVersion);
        Assert.AreEqual("placeholder-1", r.newConfigVersion);
        Assert.AreEqual(readyBefore, r.replacementDealId);
        Assert.AreEqual(10, r.ticketsUsed);
        Assert.AreEqual(50, r.ticketsRemaining);
        Assert.AreEqual(250, r.wagersCents);
        Assert.AreEqual(awards, r.payoutsCents);
        Assert.IsNotEmpty(r.actualRtpPercent);
        Assert.IsFalse(r.reachedTarget);

        DealInfo after = s.GetDealInfo(0, 25);
        Assert.AreEqual(r.replacementDealId, after.dealId);
        Assert.AreEqual(0, after.spinsPlayed);

        DealProgress retired = s.GetDealRecords().Single(d => d.dealId == before.dealId);
        Assert.AreEqual(DealState.Retired, retired.state);
        Assert.IsFalse(retired.reachedTarget);

        // The unused tickets are kept for audit.
        string oldFile = Path.Combine(folder, "deals", before.dealId + ".json");
        Deal kept = new UnityJson().FromJson<Deal>(File.ReadAllText(oldFile));
        Assert.AreEqual(60, kept.tickets.Count);

        // The replacement is untouched and exactly on target.
        string newFile = Path.Combine(folder, "deals", r.replacementDealId + ".json");
        Deal fresh = new UnityJson().FromJson<Deal>(File.ReadAllText(newFile));
        Assert.AreEqual(0, fresh.nextTicketIndex);
        Assert.IsNull(DealVerifier.Verify(fresh));
    }

    [Test]
    public void Retirement_SurvivesReload()
    {
        DealService first = NewService();
        first.EnsurePlayer("p", "P", 1000000);
        PlaySpins(first, 3, 25);
        Assert.IsTrue(first.Retire(0, 25, "reload test").success);
        first.WaitForSuccessors();

        DealService reopened = NewService();
        Assert.AreEqual(1, reopened.GetRetirements().Count);
        Assert.AreEqual("reload test", reopened.GetRetirements()[0].reason);
        Assert.AreEqual(0, reopened.GetDealInfo(0, 25).spinsPlayed);
    }

    [Test]
    public void Retire_WithNoActiveDeal_IsRefused()
    {
        DealService s = NewService();
        Assert.IsFalse(s.Retire(0, 25, "nothing to retire").success);
    }
}