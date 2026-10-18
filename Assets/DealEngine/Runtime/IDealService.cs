using System.Collections.Generic;

namespace DealEngine
{
    public enum SpinStatus
    {
        Success = 0,
        InsufficientBalance = 1,
        ResultPending = 2,
        UnknownPlayer = 3,
        InvalidRequest = 4,
        Error = 5
    }

    public class SpinResult
    {
        public SpinStatus status;
        public SpinRecord record;
        public long balanceCents;
        public string message;

        public bool IsSuccess { get { return status == SpinStatus.Success; } }
    }

    /// <summary>Safe to show in a debug panel. Never contains upcoming tickets or remaining prizes.</summary>
    public class DealInfo
    {
        public string dealId;
        public DealState state;
        public string configVersion;
        public int spinsPlayed;
    }

    public class RetireResult
    {
        public bool success;
        public string message;
        public RetireRecord record;
    }

    /// <summary>The only thing the game is allowed to talk to.</summary>
    public interface IDealService
    {
        void EnsurePlayer(string playerId, string displayName, long startingBalanceCents);
        IList<PlayerAccount> GetPlayers();
        long GetBalance(string playerId);

        SpinResult Spin(string playerId, int modeIndex, long betCents);

        /// <summary>The player's latest spin whose result has not been shown yet, or null.</summary>
        SpinRecord GetPendingResult(string playerId);
        bool MarkResultShown(string spinId);

        DealInfo GetDealInfo(int modeIndex, long betCents);

        /// <summary>Call when the player picks a mode/bet, so a deal is built in the background ahead of time.</summary>
        void Prepare(int modeIndex, long betCents);

        RetireResult Retire(int modeIndex, long betCents, string reason);
        IList<DealProgress> GetDealRecords();     // Completed and Retired deals
        IList<RetireRecord> GetRetirements();
    }
}