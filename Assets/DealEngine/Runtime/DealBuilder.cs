using System;
using System.Collections.Generic;

namespace DealEngine
{
    public class DealBuildException : Exception
    {
        public DealBuildException(string message) : base(message) { }
    }

    public static class DealBuilder
    {
        private const int BlockTries = 200;

        public static Deal Build(DealConfig config, int modeIndex, long betCents, Action<string> log = null)
        {
            config.Validate();
            ModeConfig mode = config.modes[modeIndex];

            DealSize size = DealSizeCalculator.Calculate(config, modeIndex, betCents);
            if (!size.IsValid)
                throw Reject(log, mode.name, betCents, size.RejectReason);

            int cycles = checked((int)size.PairedCycles);
            int regularTotal = checked((int)size.RegularTickets);
            int bigTotal = checked((int)size.BigTickets);
            int losses = checked((int)size.LossTickets);

            // Step 1: shuffle each cycle on its own, append in order. Regular and Big are independent.
            List<Ticket> regularTickets = BuildCycles(mode.regularBag, SlotType.Regular, cycles);
            List<Ticket> bigTickets = BuildCycles(mode.bigBag, SlotType.Big, cycles);

            // Step 2: placement limits, rejected up front if mathematically impossible.
            PlacementLimits limits = PlacementLimits.Calculate(
                losses, regularTotal, bigTotal,
                config.maxConsecutiveLossesOverride,
                config.maxRegularGapOverride,
                config.maxBigGapOverride);

            string impossible = limits.FeasibilityError(losses, regularTotal, bigTotal);
            if (impossible != null)
                throw Reject(log, mode.name, betCents, impossible);

            SlotType[] slots = null;
            string lastReason = "no attempt made";
            for (int attempt = 1; attempt <= config.maxGenerationAttempts && slots == null; attempt++)
            {
                SlotType[] candidate = TryGenerateSlots(
                    cycles, mode.regularBag.TicketCount(), mode.bigBag.TicketCount(), losses, limits);

                if (candidate == null)
                {
                    lastReason = "a block could not be placed within the limits";
                    continue;
                }
                if (PlacementChecker.Check(candidate, limits, out string reason))
                    slots = candidate;
                else
                    lastReason = reason;
            }

            if (slots == null)
                throw Reject(log, mode.name, betCents,
                    "No valid order found in " + config.maxGenerationAttempts + " attempts (" + lastReason + ").");

            // Step 3: merge. Regular slot takes the next Regular ticket, Big takes the next Big, Loss takes none.
            Deal deal = new Deal
            {
                dealId = Guid.NewGuid().ToString("N"),
                configVersion = config.configVersion,
                modeName = mode.name,
                modeIndex = modeIndex,
                betCents = betCents,
                bonusUnitCents = config.bonusUnitCents,
                targetRtpBasisPoints = config.targetRtpBasisPoints,
                pairedCycles = size.PairedCycles,
                state = DealState.Ready,
                nextTicketIndex = 0
            };

            int nextRegular = 0;
            int nextBig = 0;
            foreach (SlotType slot in slots)
            {
                if (slot == SlotType.Regular) deal.tickets.Add(regularTickets[nextRegular++]);
                else if (slot == SlotType.Big) deal.tickets.Add(bigTickets[nextBig++]);
                else deal.tickets.Add(new Ticket { kind = TicketKind.Loss, value = 0, slot = SlotType.Loss, cycle = -1 });
            }

            // Step 5: verify before use.
            string problem = DealVerifier.Verify(deal);
            if (problem != null)
                throw Reject(log, mode.name, betCents, "Verification failed: " + problem);

            return deal;
        }

        private static List<Ticket> BuildCycles(BagConfig bag, SlotType slot, int cycles)
        {
            List<Ticket> all = new List<Ticket>();
            for (int c = 0; c < cycles; c++)
            {
                List<Ticket> cycle = new List<Ticket>();
                foreach (PrizeEntry p in bag.prizes)
                {
                    TicketKind kind = p.kind == PrizeKind.Multiplier ? TicketKind.Multiplier : TicketKind.Bonus;
                    for (int i = 0; i < p.count; i++)
                        cycle.Add(new Ticket { kind = kind, value = p.value, slot = slot, cycle = c });
                }
                SecureShuffle.Shuffle(cycle);
                all.AddRange(cycle);
            }
            return all;
        }

        /// <summary>
        /// Builds the slot order block by block (one block per paired cycle). Each block is shuffled
        /// and accepted only if it keeps every limit satisfied. Returns null if a block cannot be placed.
        /// </summary>
        private static SlotType[] TryGenerateSlots(
            int cycles, int regularPerCycle, int bigPerCycle, int losses, PlacementLimits limits)
        {
            int baseLosses = losses / cycles;
            int extraLosses = losses % cycles;

            List<int> blockIds = new List<int>(cycles);
            for (int i = 0; i < cycles; i++) blockIds.Add(i);
            SecureShuffle.Shuffle(blockIds);
            bool[] getsExtra = new bool[cycles];
            for (int i = 0; i < extraLosses; i++) getsExtra[blockIds[i]] = true;

            List<SlotType> result = new List<SlotType>();
            int lossRun = 0;
            int sinceRegular = 0;
            int sinceBig = 0;

            for (int c = 0; c < cycles; c++)
            {
                int blockLosses = baseLosses + (getsExtra[c] ? 1 : 0);
                SlotType[] block = new SlotType[regularPerCycle + bigPerCycle + blockLosses];
                int pos = 0;
                for (int i = 0; i < regularPerCycle; i++) block[pos++] = SlotType.Regular;
                for (int i = 0; i < bigPerCycle; i++) block[pos++] = SlotType.Big;
                for (int i = 0; i < blockLosses; i++) block[pos++] = SlotType.Loss;

                bool placed = false;
                for (int attempt = 0; attempt < BlockTries && !placed; attempt++)
                {
                    SecureShuffle.Shuffle(block);
                    int l = lossRun, r = sinceRegular, b = sinceBig;
                    if (FitsBlock(block, limits, ref l, ref r, ref b))
                    {
                        result.AddRange(block);
                        lossRun = l;
                        sinceRegular = r;
                        sinceBig = b;
                        placed = true;
                    }
                }
                if (!placed) return null;
            }
            return result.ToArray();
        }

        private static bool FitsBlock(
            SlotType[] block, PlacementLimits limits, ref int lossRun, ref int sinceRegular, ref int sinceBig)
        {
            foreach (SlotType s in block)
            {
                if (s == SlotType.Loss)
                {
                    lossRun++;
                    if (lossRun > limits.MaxConsecutiveLosses) return false;
                }
                else lossRun = 0;

                sinceRegular = s == SlotType.Regular ? 0 : sinceRegular + 1;
                sinceBig = s == SlotType.Big ? 0 : sinceBig + 1;

                if (sinceRegular > limits.MaxRegularGap || sinceBig > limits.MaxBigGap) return false;
            }
            return true;
        }

        private static DealBuildException Reject(Action<string> log, string mode, long betCents, string reason)
        {
            string message = "Deal rejected (" + mode + ", " + betCents + " cents): " + reason;
            if (log != null) log(message);
            return new DealBuildException(message);
        }
    }
}