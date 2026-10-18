using System;
using System.IO;
using NUnit.Framework;
using DealEngine;

public class DealServiceTests
{
    private string folder;
    private DealConfig config;
    private readonly System.Collections.Generic.List<DealService> services = new System.Collections.Generic.List<DealService>();

    // Unity side of the JSON bridge (the engine itself cannot use UnityEngine).
    private class UnityJson : IJsonSerializer
    {
        public string ToJson<T>(T obj) { return UnityEngine.JsonUtility.ToJson(obj); }
        public T FromJson<T>(string json) { return UnityEngine.JsonUtility.FromJson<T>(json); }
    }

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "fpp_test_" + Guid.NewGuid().ToString("N"));
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

    [Test]
    public void SpinWithTooLittleBalance_IsRejected_AndUsesNoTicket()
    {
        DealService s = NewService();
        s.EnsurePlayer("rich", "Rich", 100);
        s.EnsurePlayer("poor", "Poor", 10);

        SpinResult rejected = s.Spin("poor", 0, 100);
        Assert.AreEqual(SpinStatus.InsufficientBalance, rejected.status);
        Assert.AreEqual(10, s.GetBalance("poor"));
        Assert.IsNull(s.GetDealInfo(0, 100)); // no deal was even started

        SpinResult ok = s.Spin("rich", 0, 100);
        Assert.IsTrue(ok.IsSuccess);
        Assert.AreEqual(0, ok.record.ticketPosition); // first ticket still unused

        s.Spin("poor", 0, 100); // rejected again, with a deal now running
        Assert.AreEqual(1, s.GetDealInfo(0, 100).spinsPlayed);
    }

    [Test]
    public void Spin_ChargesWagerAndCreditsAward()
    {
        DealService s = NewService();
        s.EnsurePlayer("p", "P", 10000);
        SpinResult r = s.Spin("p", 0, 100);

        Assert.IsTrue(r.IsSuccess);
        Assert.AreEqual(10000 - 100 + r.record.awardCents, s.GetBalance("p"));
    }

    [Test]
    public void SaveAndReload_ContinuesFromExactNextTicket()
    {
        DealService first = NewService();
        first.EnsurePlayer("p", "P", 100000);

        string dealId = null;
        for (int i = 0; i < 5; i++)
        {
            SpinResult r = first.Spin("p", 0, 100);
            Assert.AreEqual(i, r.record.ticketPosition);
            dealId = r.record.dealId;
            first.MarkResultShown(r.record.spinId);
        }
        first.WaitForSuccessors();
        long balance = first.GetBalance("p");

        DealService reopened = NewService(); // simulates closing and reopening the game
        Assert.AreEqual(balance, reopened.GetBalance("p"));

        SpinResult next = reopened.Spin("p", 0, 100);
        Assert.AreEqual(5, next.record.ticketPosition);
        Assert.AreEqual(dealId, next.record.dealId);
    }

    [Test]
    public void UnshownResult_SurvivesReload_AndUsesNoExtraTicket()
    {
        DealService first = NewService();
        first.EnsurePlayer("p", "P", 100000);
        SpinResult taken = first.Spin("p", 0, 100); // game "closed" before the result was shown
        first.WaitForSuccessors();

        DealService reopened = NewService();
        SpinRecord pending = reopened.GetPendingResult("p");
        Assert.IsNotNull(pending);
        Assert.AreEqual(taken.record.spinId, pending.spinId);
        Assert.AreEqual(taken.record.awardCents, pending.awardCents);
        Assert.AreEqual(taken.record.ticketPosition, pending.ticketPosition);

        SpinResult blocked = reopened.Spin("p", 0, 100);
        Assert.AreEqual(SpinStatus.ResultPending, blocked.status);
        Assert.AreEqual(1, reopened.GetDealInfo(0, 100).spinsPlayed); // still only one ticket used

        reopened.MarkResultShown(pending.spinId);
        SpinResult next = reopened.Spin("p", 0, 100);
        Assert.AreEqual(1, next.record.ticketPosition);
    }

    [Test]
    public void FinalTicket_CompletesDeal_AndNextSpinUsesNewDeal()
    {
        DealService s = NewService();
        s.EnsurePlayer("p", "P", 1000000);

        string firstDeal = null;
        long totalAwards = 0;
        for (int i = 0; i < 60; i++) // placeholder Inner $0.25 deal has exactly 60 spins
        {
            SpinResult r = s.Spin("p", 0, 25);
            Assert.IsTrue(r.IsSuccess);
            firstDeal = r.record.dealId;
            totalAwards += r.record.awardCents;
            s.MarkResultShown(r.record.spinId);
        }

        Assert.AreEqual(1350, totalAwards);      // 90% of 60 x 25 cents, exactly
        Assert.AreNotEqual(firstDeal, s.GetDealInfo(0, 25)?.dealId);   // the finished deal is no longer active

        SpinResult next = s.Spin("p", 0, 25);
        Assert.AreNotEqual(firstDeal, next.record.dealId);
        Assert.AreEqual(0, next.record.ticketPosition);
    }

    [Test]
    public void SafeFile_WriteAtomic_ReplacesContent_AndIgnoresStrayTempFile()
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "x.json");
        File.WriteAllText(path + ".tmp", "leftover from a crash");

        SafeFile.WriteAtomic(path, "one");
        SafeFile.WriteAtomic(path, "two");

        Assert.AreEqual("two", File.ReadAllText(path));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }
}