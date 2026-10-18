namespace DealEngine
{
    public static class DealVerifier
    {
        public static long TotalWagersCents(Deal deal)
        {
            return checked(deal.tickets.Count * deal.betCents);
        }

        public static long TotalPayoutsCents(Deal deal)
        {
            long total = 0;
            foreach (Ticket t in deal.tickets)
                total = checked(total + t.AwardCents(deal.betCents, deal.bonusUnitCents));
            return total;
        }

        /// <summary>Recalculates wagers and payouts from the saved tickets. Returns null if RTP is exactly the target.</summary>
        public static string Verify(Deal deal)
        {
            if (deal.tickets.Count == 0)
                return "Deal has no tickets.";

            long wagers = TotalWagersCents(deal);
            long payouts = TotalPayoutsCents(deal);

            if (checked(payouts * 10000) != checked(wagers * deal.targetRtpBasisPoints))
                return "RTP is not exact: payouts " + payouts + " cents, wagers " + wagers + " cents.";

            return null;
        }
    }
}