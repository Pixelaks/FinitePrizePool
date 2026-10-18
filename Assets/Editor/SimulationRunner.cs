using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DealEngine;
using UnityEditor;
using UnityEngine;

public static class SimulationRunner
{
    private class Streak
    {
        public int run;
        public int max;
        public int wins;
    }

    [MenuItem("Tools/Deal Engine/Run Simulation Report (10 Inner + 1 Outer at $1.00)")]
    public static void Run()
    {
        DealConfig config = ConfigLoader.Load(out string source);
        int inner = IndexOfMode(config, "Inner");
        int outer = IndexOfMode(config, "Outer");
        const long bet = 100;

        if (inner < 0 || outer < 0 || !config.betLevelsCents.Contains(bet))
        {
            Debug.LogError("Config needs modes named Inner and Outer, and a $1.00 bet level.");
            return;
        }

        string folder = Path.Combine(Path.GetTempPath(), "fpp_sim_" + Guid.NewGuid().ToString("N"));
        DealService service = new DealService(config, new UnityJsonSerializer(), folder, null);

        try
        {
            service.EnsurePlayer("sim", "Simulator", 1000000000L);
            Dictionary<string, Streak> streaks = new Dictionary<string, Streak>();

            PlayDeals(service, inner, bet, 10, "Inner", streaks);
            PlayDeals(service, outer, bet, 1, "Outer", streaks);
            service.WaitForSuccessors();

            WriteReport(service, config, source, new[] { inner, outer }, bet, streaks);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            service.WaitForSuccessors();
            try { Directory.Delete(folder, true); } catch { /* temp folder, safe to leave */ }
        }
    }

    private static int IndexOfMode(DealConfig config, string name)
    {
        for (int i = 0; i < config.modes.Count; i++)
            if (config.modes[i].name == name) return i;
        return -1;
    }

    private static void PlayDeals(DealService service, int modeIndex, long bet, int target,
        string label, Dictionary<string, Streak> streaks)
    {
        int completed = 0;
        int spins = 0;
        while (completed < target)
        {
            SpinResult r = service.Spin("sim", modeIndex, bet);
            if (!r.IsSuccess) throw new Exception("Spin failed: " + r.message);
            service.MarkResultShown(r.record.spinId);
            Track(streaks, r.record);

            spins++;
            if (spins > 10000000) throw new Exception("Too many spins, stopping.");

            completed = service.GetDealRecords().Count(d =>
                d.state == DealState.Completed && d.modeIndex == modeIndex && d.betCents == bet);

            if (spins % 50 == 0)
                EditorUtility.DisplayProgressBar("Simulating " + label, completed + " of " + target + " deals complete", (float)completed / target);
        }
    }

    private static void Track(Dictionary<string, Streak> streaks, SpinRecord r)
    {
        Streak s;
        if (!streaks.TryGetValue(r.dealId, out s))
        {
            s = new Streak();
            streaks[r.dealId] = s;
        }
        if (r.awardCents > 0) { s.wins++; s.run = 0; }
        else { s.run++; if (s.run > s.max) s.max = s.run; }
    }

    private static string Dollars(long cents)
    {
        return (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static void WriteReport(DealService service, DealConfig config, string source,
        int[] modes, long bet, Dictionary<string, Streak> streaks)
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("Mode,BetDollars,DealNumber,DealId,ConfigVersion,Tickets,PrizeTickets,Wagers,Payouts,TargetRTP,ActualRTP,ReachedTarget,LongestLossStreak,LossStreakLimit");

        int total = 0;
        int exact = 0;

        foreach (int modeIndex in modes)
        {
            DealSize size = DealSizeCalculator.Calculate(config, modeIndex, bet);
            PlacementLimits limits = PlacementLimits.Calculate(
                (int)size.LossTickets, (int)size.RegularTickets, (int)size.BigTickets,
                config.maxConsecutiveLossesOverride, config.maxRegularGapOverride, config.maxBigGapOverride);

            List<DealProgress> deals = service.GetDealRecords()
                .Where(d => d.state == DealState.Completed && d.modeIndex == modeIndex && d.betCents == bet)
                .OrderBy(d => d.completedUtc, StringComparer.Ordinal)
                .ToList();

            int number = 0;
            foreach (DealProgress d in deals)
            {
                number++;
                total++;
                if (d.reachedTarget) exact++;
                Streak s = streaks[d.dealId];

                csv.Append(d.modeName).Append(',')
                   .Append(Dollars(d.betCents)).Append(',')
                   .Append(number).Append(',')
                   .Append(d.dealId).Append(',')
                   .Append(d.configVersion).Append(',')
                   .Append(d.ticketCount).Append(',')
                   .Append(s.wins).Append(',')
                   .Append(Dollars(d.totalWagersCents)).Append(',')
                   .Append(Dollars(d.totalPayoutsCents)).Append(',')
                   .Append((d.targetRtpBasisPoints / 100m).ToString("0.00", CultureInfo.InvariantCulture)).Append("%,")
                   .Append(d.actualRtpPercent).Append(',')
                   .Append(d.reachedTarget).Append(',')
                   .Append(s.max).Append(',')
                   .AppendLine(limits.MaxConsecutiveLosses.ToString());
            }
        }

        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "SimulationReport.csv");
        File.WriteAllText(path, csv.ToString());

        Debug.Log("Simulation finished: " + exact + " of " + total + " completed deals landed exactly on the target RTP.\n" +
                  "Config: " + config.configVersion + " (" + source + ")\nReport written to: " + path);
        EditorUtility.RevealInFinder(path);
    }
}