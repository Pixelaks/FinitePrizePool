using System;
using System.Collections.Generic;

namespace DealEngine
{
    [Serializable]
    public class PlayerAccount
    {
        public string playerId;
        public string displayName;
        public long balanceCents;
    }

    [Serializable]
    public class SpinRecord
    {
        public string spinId;
        public string playerId;
        public string dealId;
        public int ticketPosition;
        public int modeIndex;
        public string modeName;
        public long betCents;
        public long awardCents;
        public TicketKind kind;
        public int value;
        public bool shown;
        public string timeUtc;

        public SpinRecord Clone()
        {
            return (SpinRecord)MemberwiseClone();
        }
    }

    /// <summary>Everything about a deal that changes while it is played. The tickets themselves never change.</summary>
    [Serializable]
    public class DealProgress
    {
        public string dealId;
        public string configVersion;
        public int modeIndex;
        public long betCents;
        public DealState state;
        public int nextTicketIndex;
        public int ticketCount;
        public string modeName;
        public int targetRtpBasisPoints;
        public long totalWagersCents;
        public long totalPayoutsCents;
        public bool reachedTarget;       // true only if fully used and RTP is exactly the target
        public string actualRtpPercent;  // for a Retired deal: RTP of the tickets used so far only
        public string completedUtc;

        public DealProgress Clone()
        {
            return (DealProgress)MemberwiseClone();
        }
    }

    [Serializable]
    public class RetireRecord
    {
        public string dealId;
        public string modeName;
        public long betCents;
        public string reason;
        public string timeUtc;
        public string oldConfigVersion;
        public string newConfigVersion;
        public string replacementDealId;
        public int ticketsUsed;
        public int ticketsRemaining;
        public long wagersCents;
        public long payoutsCents;
        public string actualRtpPercent;
        public bool reachedTarget; // always false for a retired deal

        public RetireRecord Clone()
        {
            return (RetireRecord)MemberwiseClone();
        }
    }

    /// <summary>The single file that is rewritten (atomically) on every spin.</summary>
    [Serializable]
    public class EngineState
    {
        public List<PlayerAccount> players = new List<PlayerAccount>();
        public List<SpinRecord> spins = new List<SpinRecord>();
        public List<DealProgress> deals = new List<DealProgress>();
        public List<RetireRecord> retirements = new List<RetireRecord>();
    }
}