using System;
using System.Globalization;

namespace DealEngine
{
    /// <summary>
    /// Money stored as whole cents in a long. Never uses float or double.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        public long Cents { get; }

        private Money(long cents)
        {
            Cents = cents;
        }

        public static readonly Money Zero = new Money(0);

        public static Money FromCents(long cents)
        {
            return new Money(cents);
        }

        /// <summary>
        /// Converts a decimal dollar amount to cents.
        /// Throws if the amount has fractions of a cent. Never rounds.
        /// </summary>
        public static Money FromDollars(decimal dollars)
        {
            decimal cents = dollars * 100m;
            if (cents != decimal.Truncate(cents))
            {
                throw new ArgumentException(
                    "Amount has fractions of a cent and will not be rounded: " + dollars);
            }
            return new Money((long)cents);
        }

        public static Money operator +(Money a, Money b) => new Money(checked(a.Cents + b.Cents));
        public static Money operator -(Money a, Money b) => new Money(checked(a.Cents - b.Cents));
        public static Money operator *(Money a, long factor) => new Money(checked(a.Cents * factor));

        public static bool operator ==(Money a, Money b) => a.Cents == b.Cents;
        public static bool operator !=(Money a, Money b) => a.Cents != b.Cents;
        public static bool operator <(Money a, Money b) => a.Cents < b.Cents;
        public static bool operator >(Money a, Money b) => a.Cents > b.Cents;
        public static bool operator <=(Money a, Money b) => a.Cents <= b.Cents;
        public static bool operator >=(Money a, Money b) => a.Cents >= b.Cents;

        public bool Equals(Money other) => Cents == other.Cents;
        public override bool Equals(object obj) => obj is Money m && Equals(m);
        public override int GetHashCode() => Cents.GetHashCode();
        public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

        public override string ToString()
        {
            return "$" + (Cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}