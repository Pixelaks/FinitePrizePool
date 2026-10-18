using System;

namespace DealEngine
{
    public enum TicketKind
    {
        Loss = 0,
        Multiplier = 1, // value = multiple of the bet, the full credited payout
        Bonus = 2       // value = number of bonus units
    }

    [Serializable]
    public class Ticket
    {
        public TicketKind kind;
        public int value;
        public SlotType slot;  // Regular or Big bag it came from (Loss for losing tickets)
        public int cycle;      // cycle number for prize tickets, -1 for losses

        public long AwardCents(long betCents, long bonusUnitCents)
        {
            switch (kind)
            {
                case TicketKind.Multiplier: return checked(value * betCents);
                case TicketKind.Bonus: return checked(value * bonusUnitCents);
                default: return 0;
            }
        }
    }
}