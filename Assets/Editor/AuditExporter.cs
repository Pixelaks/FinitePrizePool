using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DealEngine;
using UnityEditor;
using UnityEngine;

public static class AuditExporter
{
    [MenuItem("Tools/Deal Engine/Export Audit CSV (Completed and Retired Deals)")]
    public static void Export()
    {
        List<DealProgress> deals = new List<DealProgress>();
        List<RetireRecord> retirements = new List<RetireRecord>();

        IDealService live = Application.isPlaying ? GameController.Current : null;
        if (live != null)
        {
            deals.AddRange(live.GetDealRecords());
            retirements.AddRange(live.GetRetirements());
        }
        else
        {
            string statePath = Path.Combine(GameController.DataFolder, "state.json");
            if (!File.Exists(statePath))
            {
                Debug.LogWarning("No saved data found at " + statePath + ". Play the game first.");
                return;
            }

            EngineState state = JsonUtility.FromJson<EngineState>(File.ReadAllText(statePath));
            if (state == null)
            {
                Debug.LogError("Could not read " + statePath);
                return;
            }

            foreach (DealProgress p in state.deals)
                if (p.state == DealState.Completed || p.state == DealState.Retired) deals.Add(p);
            retirements.AddRange(state.retirements);
        }

        Dictionary<string, RetireRecord> retiredById = new Dictionary<string, RetireRecord>();
        foreach (RetireRecord r in retirements) retiredById[r.dealId] = r;

        StringBuilder csv = new StringBuilder();
        csv.AppendLine("DealId,Mode,BetDollars,State,ConfigVersion,TicketsUsed,TicketCount,TicketsRemaining," +
                       "Wagers,Payouts,TargetRTP,ActualRTP,ReachedTarget,CompletedUtc," +
                       "RetiredUtc,RetireReason,ReplacementDealId,NewConfigVersion");

        foreach (DealProgress d in deals)
        {
            RetireRecord retired;
            bool hasRetireRecord = retiredById.TryGetValue(d.dealId, out retired);

            string reason = "";
            if (d.state == DealState.Retired)
                reason = hasRetireRecord ? retired.reason : "Discarded before play (no retire record)";

            csv.Append(Cell(d.dealId)).Append(',')
               .Append(Cell(d.modeName)).Append(',')
               .Append(Dollars(d.betCents)).Append(',')
               .Append(d.state).Append(',')
               .Append(Cell(d.configVersion)).Append(',')
               .Append(d.nextTicketIndex).Append(',')
               .Append(d.ticketCount).Append(',')
               .Append(d.ticketCount - d.nextTicketIndex).Append(',')
               .Append(Dollars(d.totalWagersCents)).Append(',')
               .Append(Dollars(d.totalPayoutsCents)).Append(',')
               .Append((d.targetRtpBasisPoints / 100m).ToString("0.00", CultureInfo.InvariantCulture)).Append("%,")
               .Append(Cell(d.actualRtpPercent)).Append(',')
               .Append(d.reachedTarget).Append(',')
               .Append(Cell(d.completedUtc)).Append(',')
               .Append(hasRetireRecord ? Cell(retired.timeUtc) : "").Append(',')
               .Append(Cell(reason)).Append(',')
               .Append(hasRetireRecord ? Cell(retired.replacementDealId) : "").Append(',')
               .AppendLine(hasRetireRecord ? Cell(retired.newConfigVersion) : "");
        }

        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AuditReport.csv");
        File.WriteAllText(path, csv.ToString());
        Debug.Log("Audit report with " + deals.Count + " deals written to: " + path);
        EditorUtility.RevealInFinder(path);
    }

    private static string Dollars(long cents)
    {
        return (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    // Wraps text in quotes if it contains a comma, quote or line break, so the CSV stays valid.
    private static string Cell(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}