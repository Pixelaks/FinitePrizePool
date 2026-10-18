namespace DealEngine
{
    public enum SlotType
    {
        Loss = 0,
        Regular = 1,
        Big = 2
    }

    public class PlacementLimits
    {
        public int MaxConsecutiveLosses;
        public int MaxRegularGap;
        public int MaxBigGap;

        /// <summary>
        /// Computes limits from the brief's formulas. A config override above 0 replaces the formula.
        /// </summary>
        public static PlacementLimits Calculate(
            int losses, int regular, int big,
            int overrideLosses, int overrideRegularGap, int overrideBigGap)
        {
            int prizes = regular + big;
            int total = losses + prizes;

            return new PlacementLimits
            {
                MaxConsecutiveLosses = overrideLosses > 0
                    ? overrideLosses : 2 * CeilDiv(losses, prizes + 1),
                MaxRegularGap = overrideRegularGap > 0
                    ? overrideRegularGap : 2 * CeilDiv(total, regular + 1),
                MaxBigGap = overrideBigGap > 0
                    ? overrideBigGap : 2 * CeilDiv(total, big + 1)
            };
        }

        /// <summary>
        /// Returns null if the limits can be satisfied, otherwise the reason they are mathematically impossible.
        /// </summary>
        public string FeasibilityError(int losses, int regular, int big)
        {
            int prizes = regular + big;
            int total = losses + prizes;

            // Losses can only sit in (prizes + 1) runs.
            if ((long)losses > (long)(prizes + 1) * MaxConsecutiveLosses)
                return "Impossible: " + losses + " losses cannot fit in " + (prizes + 1)
                    + " runs of at most " + MaxConsecutiveLosses + ".";

            // Non-Regular spins sit in (regular + 1) runs.
            if ((long)(total - regular) > (long)(regular + 1) * MaxRegularGap)
                return "Impossible: Regular gap limit " + MaxRegularGap + " is too small.";

            // Non-Big spins sit in (big + 1) runs.
            if ((long)(total - big) > (long)(big + 1) * MaxBigGap)
                return "Impossible: Big gap limit " + MaxBigGap + " is too small.";

            return null;
        }

        private static int CeilDiv(int a, int b)
        {
            return (a + b - 1) / b;
        }
    }

    public static class PlacementChecker
    {
        /// <summary>Checks a slot order against the limits. Returns true if it passes.</summary>
        public static bool Check(SlotType[] slots, PlacementLimits limits, out string reason)
        {
            int lossRun = 0;
            int sinceRegular = 0;
            int sinceBig = 0;

            for (int i = 0; i < slots.Length; i++)
            {
                SlotType s = slots[i];

                if (s == SlotType.Loss)
                {
                    lossRun++;
                    if (lossRun > limits.MaxConsecutiveLosses)
                    {
                        reason = "Too many consecutive losses ending at spin " + i + ".";
                        return false;
                    }
                }
                else
                {
                    lossRun = 0;
                }

                if (s == SlotType.Regular) sinceRegular = 0; else sinceRegular++;
                if (s == SlotType.Big) sinceBig = 0; else sinceBig++;

                // A run of non-Regular spins ends when the next Regular slot arrives (or at the end).
                if (sinceRegular > limits.MaxRegularGap)
                {
                    reason = "Regular gap too large at spin " + i + ".";
                    return false;
                }
                if (sinceBig > limits.MaxBigGap)
                {
                    reason = "Big gap too large at spin " + i + ".";
                    return false;
                }
            }

            if (HasFixedPrizeInterval(slots))
            {
                reason = "Prize slots follow a fixed repeating interval.";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>True if there are at least 3 prize slots and all gaps between them are identical.</summary>
        private static bool HasFixedPrizeInterval(SlotType[] slots)
        {
            int previous = -1;
            int firstGap = -1;
            int prizeCount = 0;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == SlotType.Loss) continue;
                prizeCount++;

                if (previous >= 0)
                {
                    int gap = i - previous;
                    if (firstGap < 0) firstGap = gap;
                    else if (gap != firstGap) return false;
                }
                previous = i;
            }

            return prizeCount >= 3;
        }
    }
}