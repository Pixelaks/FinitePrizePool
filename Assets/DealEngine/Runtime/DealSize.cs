namespace DealEngine
{
    /// <summary>Result of the deal-size calculation for one mode and bet.</summary>
    public class DealSize
    {
        public string ModeName;
        public long BetCents;
        public long PairedCycles;
        public long PayoutPerCycleCents;
        public long RegularTickets;
        public long BigTickets;
        public long PrizeTickets;
        public long PaidSpins;
        public long LossTickets;
        public long TotalWagersCents;
        public long TotalPayoutsCents;
        public bool IsValid;
        public string RejectReason;
    }
}