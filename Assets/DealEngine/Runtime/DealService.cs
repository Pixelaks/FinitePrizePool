using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace DealEngine
{
    public class DealService : IDealService
    {
        private DealConfig config;
        private readonly IJsonSerializer json;
        private readonly string statePath;
        private readonly string dealsFolder;
        private readonly Action<string> log;
        private readonly object sync = new object();
        private readonly Dictionary<string, Deal> dealCache = new Dictionary<string, Deal>();
        private EngineState state;
        private readonly HashSet<string> building = new HashSet<string>();
        private readonly List<Task> pendingBuilds = new List<Task>();

        public DealService(DealConfig config, IJsonSerializer json, string folder, Action<string> log = null)
        {
            config.Validate();
            this.config = config;
            this.json = json;
            this.log = log;
            statePath = Path.Combine(folder, "state.json");
            dealsFolder = Path.Combine(folder, "deals");
            Directory.CreateDirectory(dealsFolder);
            state = LoadState();

            lock (sync)
            {
                DiscardStaleReady("config version changed");
                foreach (DealProgress p in state.deals.ToArray())
                    if (p.state == DealState.Active) StartSuccessorBuild(p.modeIndex, p.betCents);
            }
        }

        public void EnsurePlayer(string playerId, string displayName, long startingBalanceCents)
        {
            lock (sync)
            {
                if (state.players.Exists(p => p.playerId == playerId)) return;
                state.players.Add(new PlayerAccount
                {
                    playerId = playerId,
                    displayName = displayName,
                    balanceCents = startingBalanceCents
                });
                Save();
            }
        }

        public IList<PlayerAccount> GetPlayers()
        {
            lock (sync)
            {
                List<PlayerAccount> copy = new List<PlayerAccount>();
                foreach (PlayerAccount p in state.players)
                    copy.Add(new PlayerAccount { playerId = p.playerId, displayName = p.displayName, balanceCents = p.balanceCents });
                return copy;
            }
        }

        public long GetBalance(string playerId)
        {
            lock (sync)
            {
                PlayerAccount p = state.players.Find(x => x.playerId == playerId);
                return p == null ? 0 : p.balanceCents;
            }
        }

        public SpinResult Spin(string playerId, int modeIndex, long betCents)
        {
            lock (sync)
            {
                PlayerAccount player = state.players.Find(p => p.playerId == playerId);
                if (player == null)
                    return Fail(SpinStatus.UnknownPlayer, "Unknown player.", 0);

                if (modeIndex < 0 || modeIndex >= config.modes.Count || !config.betLevelsCents.Contains(betCents))
                    return Fail(SpinStatus.InvalidRequest, "Invalid mode or bet.", player.balanceCents);

                if (state.spins.FindLast(s => s.playerId == playerId && !s.shown) != null)
                    return Fail(SpinStatus.ResultPending, "Previous result has not been shown yet.", player.balanceCents);

                // Balance check comes before any ticket is touched (and before any deal is built).
                if (player.balanceCents < betCents)
                    return Fail(SpinStatus.InsufficientBalance, "Balance too low.", player.balanceCents);

                DealProgress progress = null;
                Deal deal = null;
                bool createdProgress = false;
                DealProgress promoted = null;
                DealProgress successor = null;

                // If no deal is active for this mode/bet, switch to the prepared Ready deal (if there is one).
                if (!state.deals.Exists(d => d.modeIndex == modeIndex && d.betCents == betCents && d.state == DealState.Active))
                    promoted = ActivateReady(modeIndex, betCents);

                try
                {
                    progress = state.deals.Find(d =>
                        d.modeIndex == modeIndex && d.betCents == betCents && d.state == DealState.Active);

                    if (progress == null)
                    {
                        deal = DealBuilder.Build(config, modeIndex, betCents, log);
                        WriteDealFile(deal); // the complete ordered deal is saved before it is used
                        dealCache[deal.dealId] = deal;
                        progress = new DealProgress
                        {
                            dealId = deal.dealId,
                            configVersion = deal.configVersion,
                            modeIndex = modeIndex,
                            betCents = betCents,
                            state = DealState.Active,
                            nextTicketIndex = 0,
                            ticketCount = deal.tickets.Count,
                            modeName = deal.modeName,
                            targetRtpBasisPoints = deal.targetRtpBasisPoints
                        };
                        state.deals.Add(progress);
                        createdProgress = true;
                    }
                    else
                    {
                        deal = LoadDeal(progress.dealId);
                    }
                }
                catch (Exception e)
                {
                    if (log != null) log("Could not prepare a deal: " + e.Message);
                    return Fail(SpinStatus.Error, e.Message, player.balanceCents);
                }

                int position = progress.nextTicketIndex;
                Ticket ticket = deal.tickets[position];
                long award = ticket.AwardCents(betCents, deal.bonusUnitCents);
                long oldBalance = player.balanceCents;

                // Charge the wager, take the ticket, credit the award, record the spin.
                player.balanceCents = checked(oldBalance - betCents + award);
                progress.nextTicketIndex = position + 1;
                progress.totalWagersCents = checked(progress.totalWagersCents + betCents);
                progress.totalPayoutsCents = checked(progress.totalPayoutsCents + award);
                if (progress.nextTicketIndex >= progress.ticketCount)
                    successor = CompleteDeal(progress, deal);

                SpinRecord record = new SpinRecord
                {
                    spinId = Guid.NewGuid().ToString("N"),
                    playerId = playerId,
                    dealId = deal.dealId,
                    ticketPosition = position,
                    modeIndex = modeIndex,
                    modeName = deal.modeName,
                    betCents = betCents,
                    awardCents = award,
                    kind = ticket.kind,
                    value = ticket.value,
                    shown = false,
                    timeUtc = DateTime.UtcNow.ToString("o")
                };
                state.spins.Add(record);

                try
                {
                    Save(); // one atomic write: balance + position + spin record together
                }
                catch (Exception e)
                {
                    // Nothing reached the disk, so this spin never happened. Undo it in memory.
                    state.spins.RemoveAt(state.spins.Count - 1);
                    progress.nextTicketIndex = position;
                    progress.state = DealState.Active;
                    progress.reachedTarget = false;
                    progress.completedUtc = null;
                    progress.actualRtpPercent = null;
                    progress.totalWagersCents -= betCents;
                    progress.totalPayoutsCents -= award;
                    if (successor != null) successor.state = DealState.Ready;
                    if (promoted != null) promoted.state = DealState.Ready;
                    if (createdProgress) state.deals.Remove(progress);
                    player.balanceCents = oldBalance;
                    if (log != null) log("Save failed, spin rolled back: " + e.Message);
                    return Fail(SpinStatus.Error, "Could not save the spin.", player.balanceCents);
                }

                deal.nextTicketIndex = progress.nextTicketIndex;
                deal.state = progress.state;

                StartSuccessorBuild(modeIndex, betCents); // no-op if one is ready or already being built

                return new SpinResult
                {
                    status = SpinStatus.Success,
                    record = record.Clone(),
                    balanceCents = player.balanceCents
                };
            }
        }

        public SpinRecord GetPendingResult(string playerId)
        {
            lock (sync)
            {
                SpinRecord pending = state.spins.FindLast(s => s.playerId == playerId && !s.shown);
                return pending == null ? null : pending.Clone();
            }
        }

        public bool MarkResultShown(string spinId)
        {
            lock (sync)
            {
                SpinRecord record = state.spins.Find(s => s.spinId == spinId);
                if (record == null) return false;
                if (record.shown) return true;

                record.shown = true;
                try
                {
                    Save();
                    return true;
                }
                catch (Exception e)
                {
                    record.shown = false;
                    if (log != null) log("Could not save 'shown' flag: " + e.Message);
                    return false;
                }
            }
        }

        public DealInfo GetDealInfo(int modeIndex, long betCents)
        {
            lock (sync)
            {
                DealProgress p = state.deals.Find(d =>
                    d.modeIndex == modeIndex && d.betCents == betCents && d.state == DealState.Active);
                if (p == null) return null;
                return new DealInfo
                {
                    dealId = p.dealId,
                    state = p.state,
                    configVersion = p.configVersion,
                    spinsPlayed = p.nextTicketIndex
                };
            }
        }

        // ---------- lifecycle ----------

        public void Prepare(int modeIndex, long betCents)
        {
            lock (sync) { StartSuccessorBuild(modeIndex, betCents); }
        }

        /// <summary>
        /// Replaces the config. Active deals keep their own config version. Ready deals built on the old
        /// version are discarded and rebuilt. The caller must not modify newConfig afterwards.
        /// </summary>
        public void ApplyConfig(DealConfig newConfig)
        {
            newConfig.Validate();
            lock (sync)
            {
                config = newConfig;
                DiscardStaleReady("config changed to " + newConfig.configVersion);
                foreach (DealProgress p in state.deals.ToArray())
                    if (p.state == DealState.Active) StartSuccessorBuild(p.modeIndex, p.betCents);
            }
        }

        /// <summary>Blocks until background deal builds finish. Used by tests and on shutdown.</summary>
        public void WaitForSuccessors()
        {
            while (true)
            {
                Task[] tasks;
                lock (sync)
                {
                    tasks = pendingBuilds.ToArray();
                    pendingBuilds.Clear();
                }
                if (tasks.Length == 0) return;
                Task.WaitAll(tasks);
            }
        }

        public string GetReadyDealId(int modeIndex, long betCents)
        {
            lock (sync)
            {
                DealProgress p = state.deals.Find(d => d.state == DealState.Ready
                    && d.modeIndex == modeIndex && d.betCents == betCents
                    && d.configVersion == config.configVersion);
                return p == null ? null : p.dealId;
            }
        }

        public IList<DealProgress> GetDealRecords()
        {
            lock (sync)
            {
                List<DealProgress> copy = new List<DealProgress>();
                foreach (DealProgress p in state.deals)
                    if (p.state == DealState.Completed || p.state == DealState.Retired) copy.Add(p.Clone());
                return copy;
            }
        }

        public IList<RetireRecord> GetRetirements()
        {
            lock (sync)
            {
                List<RetireRecord> copy = new List<RetireRecord>();
                foreach (RetireRecord r in state.retirements) copy.Add(r.Clone());
                return copy;
            }
        }

        public RetireResult Retire(int modeIndex, long betCents, string reason)
        {
            lock (sync)
            {
                DealProgress old = state.deals.Find(d =>
                    d.state == DealState.Active && d.modeIndex == modeIndex && d.betCents == betCents);
                if (old == null) return RetireFail("There is no active deal for that mode and bet.");
                if (string.IsNullOrWhiteSpace(reason)) return RetireFail("A reason is required.");

                // Replacement: the verified Ready successor, or a freshly built and verified deal.
                DealProgress replacement = ActivateReady(modeIndex, betCents);
                bool builtNow = false;
                if (replacement == null)
                {
                    try
                    {
                        Deal fresh = DealBuilder.Build(config, modeIndex, betCents, log);
                        WriteDealFile(fresh);
                        dealCache[fresh.dealId] = fresh;
                        replacement = NewProgress(fresh, DealState.Active);
                        state.deals.Add(replacement);
                        builtNow = true;
                    }
                    catch (Exception e)
                    {
                        return RetireFail("Could not build a replacement deal: " + e.Message);
                    }
                }

                old.state = DealState.Retired;
                old.reachedTarget = false; // a retired deal never reached its target
                old.actualRtpPercent = RtpText(old.totalPayoutsCents, old.totalWagersCents);

                RetireRecord record = new RetireRecord
                {
                    dealId = old.dealId,
                    modeName = old.modeName,
                    betCents = old.betCents,
                    reason = reason,
                    timeUtc = DateTime.UtcNow.ToString("o"),
                    oldConfigVersion = old.configVersion,
                    newConfigVersion = replacement.configVersion,
                    replacementDealId = replacement.dealId,
                    ticketsUsed = old.nextTicketIndex,
                    ticketsRemaining = old.ticketCount - old.nextTicketIndex,
                    wagersCents = old.totalWagersCents,
                    payoutsCents = old.totalPayoutsCents,
                    actualRtpPercent = old.actualRtpPercent,
                    reachedTarget = false
                };
                state.retirements.Add(record);

                try
                {
                    Save();
                }
                catch (Exception e)
                {
                    state.retirements.Remove(record);
                    old.state = DealState.Active;
                    old.actualRtpPercent = null;
                    if (builtNow) state.deals.Remove(replacement);
                    else replacement.state = DealState.Ready;
                    if (log != null) log("Retire could not be saved and was undone: " + e.Message);
                    return RetireFail("Could not save the retirement.");
                }

                StartSuccessorBuild(modeIndex, betCents);
                return new RetireResult { success = true, record = record.Clone() };
            }
        }

        /// <summary>Turns the verified Ready deal for this mode/bet (current config only) into the Active one.</summary>
        private DealProgress ActivateReady(int modeIndex, long betCents)
        {
            DealProgress ready = state.deals.Find(d => d.state == DealState.Ready
                && d.modeIndex == modeIndex && d.betCents == betCents
                && d.configVersion == config.configVersion);
            if (ready == null) return null;

            try
            {
                LoadDeal(ready.dealId); // re-verifies the saved deal before it is used
                ready.state = DealState.Active;
                return ready;
            }
            catch (Exception e)
            {
                ready.state = DealState.Retired;
                if (log != null) log("Ready deal " + ready.dealId + " could not be used: " + e.Message);
                return null;
            }
        }

        private DealProgress CompleteDeal(DealProgress progress, Deal deal)
        {
            progress.state = DealState.Completed;
            progress.completedUtc = DateTime.UtcNow.ToString("o");
            progress.reachedTarget =
                checked(progress.totalPayoutsCents * 10000) ==
                checked(progress.totalWagersCents * deal.targetRtpBasisPoints);
            progress.actualRtpPercent = RtpText(progress.totalPayoutsCents, progress.totalWagersCents);
            return ActivateReady(progress.modeIndex, progress.betCents);
        }

        private void DiscardStaleReady(string reason)
        {
            bool changed = false;
            foreach (DealProgress p in state.deals)
            {
                if (p.state != DealState.Ready || p.configVersion == config.configVersion) continue;
                p.state = DealState.Retired;
                changed = true;
                if (log != null) log("Ready deal " + p.dealId + " discarded (" + reason + ").");
            }
            if (!changed) return;
            try { Save(); }
            catch (Exception e) { if (log != null) log("Could not save after discarding Ready deals: " + e.Message); }
        }

        /// <summary>Builds a verified Ready deal on a background thread. Call while holding the lock.</summary>
        private void StartSuccessorBuild(int modeIndex, long betCents)
        {
            if (modeIndex < 0 || modeIndex >= config.modes.Count || !config.betLevelsCents.Contains(betCents)) return;

            string version = config.configVersion;
            string key = modeIndex + "|" + betCents + "|" + version;
            if (building.Contains(key) || HasReady(modeIndex, betCents, version)) return;

            building.Add(key);
            DealConfig snapshot = config;

            Task task = Task.Run(() =>
            {
                Deal built = null;
                string error = null;
                try { built = DealBuilder.Build(snapshot, modeIndex, betCents, log); }
                catch (Exception e) { error = e.Message; }

                lock (sync)
                {
                    building.Remove(key);
                    if (built == null)
                    {
                        if (log != null) log("Successor build failed: " + error);
                        return;
                    }

                    // The config may have changed while building, or a Ready deal may already exist.
                    if (version != config.configVersion || HasReady(modeIndex, betCents, version)) return;

                    DealProgress added = null;
                    try
                    {
                        WriteDealFile(built);
                        dealCache[built.dealId] = built;
                        added = NewProgress(built, DealState.Ready);
                        state.deals.Add(added);
                        Save();
                    }
                    catch (Exception e)
                    {
                        if (added != null) state.deals.Remove(added);
                        if (log != null) log("Could not store the successor deal: " + e.Message);
                    }
                }
            });
            pendingBuilds.Add(task);
        }

        private bool HasReady(int modeIndex, long betCents, string version)
        {
            return state.deals.Exists(d => d.state == DealState.Ready
                && d.modeIndex == modeIndex && d.betCents == betCents && d.configVersion == version);
        }

        private static DealProgress NewProgress(Deal deal, DealState dealState)
        {
            return new DealProgress
            {
                dealId = deal.dealId,
                configVersion = deal.configVersion,
                modeName = deal.modeName,
                modeIndex = deal.modeIndex,
                betCents = deal.betCents,
                state = dealState,
                nextTicketIndex = 0,
                ticketCount = deal.tickets.Count,
                targetRtpBasisPoints = deal.targetRtpBasisPoints
            };
        }

        private static string RtpText(long payoutsCents, long wagersCents)
        {
            if (wagersCents == 0) return "n/a";
            decimal percent = (decimal)payoutsCents * 100m / wagersCents;
            return percent.ToString("0.0000", CultureInfo.InvariantCulture) + "%";
        }

        private static RetireResult RetireFail(string message)
        {
            return new RetireResult { success = false, message = message };
        }

        // ---------- helpers ----------

        private EngineState LoadState()
        {
            if (!File.Exists(statePath)) return new EngineState();
            string text = File.ReadAllText(statePath, Encoding.UTF8);
            EngineState loaded = json.FromJson<EngineState>(text);
            if (loaded == null) throw new InvalidOperationException("State file could not be read: " + statePath);
            return loaded;
        }

        private void Save()
        {
            SafeFile.WriteAtomic(statePath, json.ToJson(state));
        }

        private void WriteDealFile(Deal deal)
        {
            SafeFile.WriteAtomic(Path.Combine(dealsFolder, deal.dealId + ".json"), json.ToJson(deal));
        }

        private Deal LoadDeal(string dealId)
        {
            Deal cached;
            if (dealCache.TryGetValue(dealId, out cached)) return cached;

            string path = Path.Combine(dealsFolder, dealId + ".json");
            if (!File.Exists(path))
                throw new InvalidOperationException("Deal file is missing: " + dealId);

            Deal deal = json.FromJson<Deal>(File.ReadAllText(path, Encoding.UTF8));
            string problem = DealVerifier.Verify(deal);
            if (problem != null)
                throw new InvalidOperationException("Saved deal failed verification: " + problem);

            dealCache[dealId] = deal;
            return deal;
        }

        private static SpinResult Fail(SpinStatus status, string message, long balanceCents)
        {
            return new SpinResult { status = status, message = message, balanceCents = balanceCents };
        }
    }
}