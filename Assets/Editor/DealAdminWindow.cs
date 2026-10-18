using System.Collections.Generic;
using System.IO;
using DealEngine;
using UnityEditor;
using UnityEngine;

public class DealAdminWindow : EditorWindow
{
    [MenuItem("Tools/Deal Engine/Admin Window")]
    public static void Open()
    {
        GetWindow<DealAdminWindow>("Deal Admin");
    }

    private DealConfig config;
    private string[] modeNames;
    private string[] betLabels;
    private int modeIndex;
    private int betIndex;
    private string reason = "";
    private string message = "";
    private Vector2 scroll;

    private void OnEnable() { LoadConfig(); }
    private void OnInspectorUpdate() { Repaint(); }

    private void LoadConfig()
    {
        string source;
        config = ConfigLoader.Load(out source);
        modeNames = new string[config.modes.Count];
        for (int i = 0; i < modeNames.Length; i++) modeNames[i] = config.modes[i].name;
        betLabels = new string[config.betLevelsCents.Count];
        for (int i = 0; i < betLabels.Length; i++) betLabels[i] = Money.FromCents(config.betLevelsCents[i]).ToString();
        modeIndex = 0;
        betIndex = 0;
    }

    private void OnGUI()
    {
        IDealService live = Application.isPlaying ? GameController.Current : null;

        EditorGUILayout.LabelField(
            live != null ? "Live: connected to the running game" : "Read-only: press Play to retire deals",
            EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(live == null))
        {
            EditorGUILayout.LabelField("Retire the active deal", EditorStyles.boldLabel);
            modeIndex = EditorGUILayout.Popup("Mode", modeIndex, modeNames);
            betIndex = EditorGUILayout.Popup("Bet", betIndex, betLabels);
            reason = EditorGUILayout.TextField("Reason", reason);

            if (GUILayout.Button("Retire active deal"))
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    message = "Enter a reason first.";
                }
                else if (EditorUtility.DisplayDialog("Retire deal",
                    "Retire the active " + modeNames[modeIndex] + " " + betLabels[betIndex] + " deal?\n\n" +
                    "Its unused tickets are kept for audit, and it will never count as reaching its target.",
                    "Retire", "Cancel"))
                {
                    RetireResult r = live.Retire(modeIndex, config.betLevelsCents[betIndex], reason);
                    message = r.success ? "Retired. Replacement deal: " + r.record.replacementDealId : r.message;
                    if (r.success) reason = "";
                }
            }
        }

        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reload config")) LoadConfig();
        if (GUILayout.Button("Show save folder"))
        {
            Directory.CreateDirectory(GameController.DataFolder);
            EditorUtility.RevealInFinder(GameController.DataFolder);
        }
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Delete saved data") &&
                EditorUtility.DisplayDialog("Delete saved data", "Delete all deals, balances and spin records?", "Delete", "Cancel"))
            {
                if (Directory.Exists(GameController.DataFolder)) Directory.Delete(GameController.DataFolder, true);
                message = "Saved data deleted.";
            }
        }
        EditorGUILayout.EndHorizontal();

        List<DealProgress> deals = new List<DealProgress>();
        List<RetireRecord> retirements = new List<RetireRecord>();
        if (live != null)
        {
            deals.AddRange(live.GetDealRecords());
            retirements.AddRange(live.GetRetirements());
        }
        else
        {
            string path = Path.Combine(GameController.DataFolder, "state.json");
            if (File.Exists(path))
            {
                EngineState state = JsonUtility.FromJson<EngineState>(File.ReadAllText(path));
                if (state != null)
                {
                    foreach (DealProgress p in state.deals)
                        if (p.state == DealState.Completed || p.state == DealState.Retired) deals.Add(p);
                    retirements.AddRange(state.retirements);
                }
            }
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Completed and retired deals (" + deals.Count + ")", EditorStyles.boldLabel);
        foreach (DealProgress p in deals)
        {
            EditorGUILayout.LabelField(
                p.modeName + " " + Money.FromCents(p.betCents) + " | " + p.state + " | config " + p.configVersion +
                " | tickets " + p.nextTicketIndex + "/" + p.ticketCount +
                " | wagers " + Money.FromCents(p.totalWagersCents) + " | payouts " + Money.FromCents(p.totalPayoutsCents) +
                " | target " + (p.targetRtpBasisPoints / 100m).ToString("0.00") + "% | actual " + p.actualRtpPercent +
                " | reached target: " + p.reachedTarget);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Retire log (" + retirements.Count + ")", EditorStyles.boldLabel);
        foreach (RetireRecord r in retirements)
        {
            EditorGUILayout.LabelField(
                r.timeUtc + " | " + r.modeName + " " + Money.FromCents(r.betCents) + " | deal " + r.dealId +
                " | reason: " + r.reason + " | config " + r.oldConfigVersion + " -> " + r.newConfigVersion +
                " | replacement " + r.replacementDealId +
                " | used " + r.ticketsUsed + ", remaining " + r.ticketsRemaining +
                " | wagers " + Money.FromCents(r.wagersCents) + " | payouts " + Money.FromCents(r.payoutsCents) +
                " | actual RTP " + r.actualRtpPercent + " | reached target: " + r.reachedTarget);
        }

        EditorGUILayout.EndScrollView();
    }
}