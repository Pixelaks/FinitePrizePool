using System;

namespace DealEngine
{
    public static class DealSizeCalculator
    {
        private const long BasisPointsScale = 10000;

        /// <summary>Finds the smallest number of paired cycles that gives a whole number of paid spins.</summary>
        public static DealSize Calculate(DealConfig config, int modeIndex, long betCents)
        {
            long payoutPerCycle = PayoutPerCycle(config, modeIndex, betCents);
            long numerator = checked(payoutPerCycle * BasisPointsScale);
            long denominator = checked(betCents * config.targetRtpBasisPoints);
            long cycles = denominator / Gcd(numerator, denominator);
            return Build(config, modeIndex, betCents, cycles);
        }

        /// <summary>Checks a fixed number of cycles. Rejects it if the spins would be fractional.</summary>
        public static DealSize CalculateForCycles(DealConfig config, int modeIndex, long betCents, long cycles)
        {
            return Build(config, modeIndex, betCents, cycles);
        }

        public static long PayoutPerCycle(DealConfig config, int modeIndex, long betCents)
        {
            ModeConfig mode = config.modes[modeIndex];
            long multiplierTotal = mode.regularBag.MultiplierTotal() + mode.bigBag.MultiplierTotal();
            long bonusUnits = mode.regularBag.BonusUnitsTotal() + mode.bigBag.BonusUnitsTotal();
            return checked(multiplierTotal * betCents + bonusUnits * config.bonusUnitCents);
        }

        private static DealSize Build(DealConfig config, int modeIndex, long betCents, long cycles)
        {
            ModeConfig mode = config.modes[modeIndex];
            DealSize size = new DealSize
            {
                ModeName = mode.name,
                BetCents = betCents,
                PairedCycles = cycles,
                PayoutPerCycleCents = PayoutPerCycle(config, modeIndex, betCents),
                RegularTickets = checked(cycles * mode.regularBag.TicketCount()),
                BigTickets = checked(cycles * mode.bigBag.TicketCount())
            };
            size.PrizeTickets = size.RegularTickets + size.BigTickets;
            size.TotalPayoutsCents = checked(cycles * size.PayoutPerCycleCents);

            long numerator = checked(size.TotalPayoutsCents * BasisPointsScale);
            long denominator = checked(betCents * config.targetRtpBasisPoints);

            if (cycles <= 0)
            {
                size.RejectReason = "Number of cycles must be positive.";
                return size;
            }
            if (numerator % denominator != 0)
            {
                size.RejectReason = "Paid spins would not be a whole number at the target RTP.";
                return size;
            }

            size.PaidSpins = numerator / denominator;
            size.TotalWagersCents = checked(size.PaidSpins * betCents);
            size.LossTickets = size.PaidSpins - size.PrizeTickets;

            if (size.PaidSpins < size.PrizeTickets)
            {
                size.RejectReason = "Paid spins (" + size.PaidSpins + ") are fewer than prize tickets (" + size.PrizeTickets + ").";
                return size;
            }

            size.IsValid = true;
            return size;
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                long t = a % b;
                a = b;
                b = t;
            }
            return a;
        }
    }
}