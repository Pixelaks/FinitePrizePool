using System;
using System.Collections.Generic;

namespace DealEngine
{
    public enum PrizeKind
    {
        Multiplier = 0, // value = multiple of the bet, paid as the full credited payout
        Bonus = 1       // value = number of bonus units (each worth BonusUnitCents)
    }

    [Serializable]
    public class PrizeEntry
    {
        public PrizeKind kind;
        public int value;
        public int count;
    }

    [Serializable]
    public class BagConfig
    {
        public List<PrizeEntry> prizes = new List<PrizeEntry>();

        public int TicketCount()
        {
            int total = 0;
            foreach (PrizeEntry p in prizes) total += p.count;
            return total;
        }

        /// <summary>Sum of all multipliers in one cycle of this bag.</summary>
        public long MultiplierTotal()
        {
            long total = 0;
            foreach (PrizeEntry p in prizes)
                if (p.kind == PrizeKind.Multiplier) total += (long)p.value * p.count;
            return total;
        }

        /// <summary>Sum of all bonus units in one cycle of this bag.</summary>
        public long BonusUnitsTotal()
        {
            long total = 0;
            foreach (PrizeEntry p in prizes)
                if (p.kind == PrizeKind.Bonus) total += (long)p.value * p.count;
            return total;
        }
    }

    [Serializable]
    public class ModeConfig
    {
        public string name;
        public BagConfig regularBag = new BagConfig();
        public BagConfig bigBag = new BagConfig();
    }

    [Serializable]
    public class DealConfig
    {
        public string configVersion;
        public int targetRtpBasisPoints;      // 9000 = 90.00%
        public long bonusUnitCents;           // 100 = $1.00
        public List<long> betLevelsCents = new List<long>();
        public List<ModeConfig> modes = new List<ModeConfig>();

        public int maxGenerationAttempts = 1000;

        // 0 means "use the formula from the brief". A value above 0 overrides it.
        public int maxConsecutiveLossesOverride;
        public int maxRegularGapOverride;
        public int maxBigGapOverride;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(configVersion))
                throw new ArgumentException("configVersion is required.");
            if (targetRtpBasisPoints <= 0 || targetRtpBasisPoints >= 10000)
                throw new ArgumentException("targetRtpBasisPoints must be between 1 and 9999.");
            if (bonusUnitCents <= 0)
                throw new ArgumentException("bonusUnitCents must be positive.");
            if (betLevelsCents.Count == 0)
                throw new ArgumentException("At least one bet level is required.");
            foreach (long bet in betLevelsCents)
                if (bet <= 0) throw new ArgumentException("Bet levels must be positive.");
            if (modes.Count == 0)
                throw new ArgumentException("At least one mode is required.");
            if (maxGenerationAttempts <= 0)
                throw new ArgumentException("maxGenerationAttempts must be positive.");

            foreach (ModeConfig mode in modes)
            {
                if (string.IsNullOrWhiteSpace(mode.name))
                    throw new ArgumentException("Every mode needs a name.");
                ValidateBag(mode.name + " regular", mode.regularBag);
                ValidateBag(mode.name + " big", mode.bigBag);
            }
        }

        private static void ValidateBag(string label, BagConfig bag)
        {
            if (bag == null || bag.prizes.Count == 0)
                throw new ArgumentException("Bag is empty: " + label);
            foreach (PrizeEntry p in bag.prizes)
                if (p.value <= 0 || p.count <= 0)
                    throw new ArgumentException("Prize value and count must be positive in: " + label);
        }
    }
}