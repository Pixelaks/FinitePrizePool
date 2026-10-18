using System;
using System.Collections.Generic;

namespace DealEngine
{
    public enum DealState
    {
        Ready = 0,
        Active = 1,
        Completed = 2,
        Retired = 3
    }

    [Serializable]
    public class Deal
    {
        public string dealId;
        public string configVersion;
        public string modeName;
        public int modeIndex;
        public long betCents;
        public long bonusUnitCents;
        public int targetRtpBasisPoints;
        public long pairedCycles;
        public DealState state;
        public int nextTicketIndex;
        public List<Ticket> tickets = new List<Ticket>();
    }
}