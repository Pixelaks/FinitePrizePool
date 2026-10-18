using System;
using NUnit.Framework;
using DealEngine;

public class MoneyTests
{
    [Test]
    public void FromDollars_ConvertsToCents()
    {
        Assert.AreEqual(125, Money.FromDollars(1.25m).Cents);
    }

    [Test]
    public void FromDollars_RejectsFractionOfACent()
    {
        Assert.Throws<ArgumentException>(() => Money.FromDollars(0.255m));
    }

    [Test]
    public void Addition_AddsCents()
    {
        Money total = Money.FromDollars(0.25m) + Money.FromDollars(0.75m);
        Assert.AreEqual(Money.FromDollars(1.00m), total);
    }

    [Test]
    public void Multiply_ByWholeNumber()
    {
        Assert.AreEqual(500, (Money.FromDollars(1.25m) * 4).Cents);
    }
}