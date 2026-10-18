using System;
using System.IO;
using DealEngine;
using UnityEngine;

public class GameController : MonoBehaviour
{
    /// <summary>The running service, so the Editor admin window can use it in Play Mode.</summary>
    public static IDealService Current { get; set; }

    public static string DataFolder
    {
        get { return Path.Combine(Application.persistentDataPath, "FinitePrizePool"); }
    }

    private const long StartingBalanceCents = 10000; // $100.00 per test player
    private const float SpinSeconds = 3f;            // long enough to close the game mid-spin

    private enum Phase { Idle, Spinning, Showing }

    private readonly string[] playerIds = { "player1", "player2" };
    private readonly string[] playerNames = { "Alice", "Bob" };

    private DealConfig config;
    private IDealService service;
    private string configSource;
    private string startupError;
    private string[] modeNames;
    private string[] betLabels;

    private int playerIndex;
    private int modeIndex;
    private int betIndex;
    private Phase phase = Phase.Idle;
    private SpinRecord current;
    private float spinEndsAt;
    private string status = "";
    private bool restored;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool showDebug = true;
#endif

    private long CurrentBet { get { return config.betLevelsCents[betIndex]; } }

    private void Awake()
    {
        try
        {
            config = ConfigLoader.Load(out configSource);
            service = new DealService(config, new UnityJsonSerializer(), DataFolder, msg => Debug.Log(msg));
            Current = service;

            for (int i = 0; i < playerIds.Length; i++)
                service.EnsurePlayer(playerIds[i], playerNames[i], StartingBalanceCents);

            modeNames = new string[config.modes.Count];
            for (int i = 0; i < modeNames.Length; i++) modeNames[i] = config.modes[i].name;

            betLabels = new string[config.betLevelsCents.Count];
            for (int i = 0; i < betLabels.Length; i++)
                betLabels[i] = Money.FromCents(config.betLevelsCents[i]).ToString();

            service.Prepare(modeIndex, CurrentBet);
            ShowPendingResult();
        }
        catch (Exception e)
        {
            startupError = e.Message;
            Debug.LogException(e);
        }
    }

    private void OnDestroy()
    {
        if (Current == service) Current = null;
    }

    private void Update()
    {
        if (phase == Phase.Spinning && Time.unscaledTime >= spinEndsAt)
        {
            phase = Phase.Showing;
            service.MarkResultShown(current.spinId);
        }
    }

    // If the game closed before a result was shown, show that same saved result now.
    private void ShowPendingResult()
    {
        SpinRecord pending = service.GetPendingResult(playerIds[playerIndex]);
        if (pending == null) return;

        current = pending;
        phase = Phase.Showing;
        restored = true;
        service.MarkResultShown(pending.spinId);
    }

    private void DoSpin()
    {
        restored = false;
        status = "";

        SpinResult result = service.Spin(playerIds[playerIndex], modeIndex, CurrentBet);
        if (!result.IsSuccess)
        {
            status = result.message; // for example "Balance too low." (no ticket was used)
            return;
        }

        // The spin is already decided and saved. The animation only replays it.
        current = result.record;
        phase = Phase.Spinning;
        spinEndsAt = Time.unscaledTime + SpinSeconds;
    }

    private long DisplayedBalance()
    {
        long balance = service.GetBalance(playerIds[playerIndex]);
        // Hide the result until the animation ends: show the balance as it was before the award.
        if (phase == Phase.Spinning && current != null)
            balance = balance - current.awardCents + current.betCents;
        return balance;
    }

    private static string Describe(SpinRecord r)
    {
        switch (r.kind)
        {
            case TicketKind.Loss: return "No win this time";
            case TicketKind.Multiplier: return "WIN " + Money.FromCents(r.awardCents) + "  (multiplier x" + r.value + ")";
            default: return "WIN " + Money.FromCents(r.awardCents) + "  (bonus, " + r.value + " units)";
        }
    }

    private void OnGUI()
    {
        float scale = Mathf.Max(1f, Screen.height / 600f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUILayout.BeginArea(new Rect(12, 12, Screen.width / scale - 24, Screen.height / scale - 24));

        if (startupError != null)
        {
            GUILayout.Label("Could not start: " + startupError);
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label("Finite Prize Pool");
        bool busy = phase == Phase.Spinning;
        GUI.enabled = !busy;

        int newPlayer = GUILayout.Toolbar(playerIndex, playerNames);
        if (newPlayer != playerIndex)
        {
            playerIndex = newPlayer;   // switching player never touches the deal
            current = null;
            phase = Phase.Idle;
            restored = false;
            status = "";
            ShowPendingResult();
        }

        GUILayout.Label("Balance: " + Money.FromCents(DisplayedBalance()));

        GUILayout.Label("Mode");
        int newMode = GUILayout.Toolbar(modeIndex, modeNames);
        GUILayout.Label("Bet");
        int newBet = GUILayout.SelectionGrid(betIndex, betLabels, 5);
        if (newMode != modeIndex || newBet != betIndex)
        {
            modeIndex = newMode;
            betIndex = newBet;
            status = "";
            service.Prepare(modeIndex, CurrentBet); // build the next deal in the background
        }

        if (GUILayout.Button(busy ? "Spinning..." : "SPIN", GUILayout.Height(48)))
            DoSpin();

        GUI.enabled = true;
        GUILayout.Space(8);

        if (phase == Phase.Spinning)
        {
            GUILayout.Label("Spinning" + new string('.', 1 + (int)(Time.unscaledTime * 3f) % 3));
        }
        else if (phase == Phase.Showing && current != null)
        {
            GUILayout.Label(Describe(current));
            if (restored) GUILayout.Label("(Restored from your previous session.)");
        }
        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GUILayout.Space(12);
        showDebug = GUILayout.Toggle(showDebug, "Debug panel (development builds only)");
        if (showDebug)
        {
            DealInfo info = service.GetDealInfo(modeIndex, CurrentBet);
            GUILayout.Label("Config source: " + configSource);
            if (info == null)
            {
                GUILayout.Label("Deal: none active yet for this mode and bet (starts on the first spin)");
            }
            else
            {
                GUILayout.Label("Deal ID: " + info.dealId);
                GUILayout.Label("State: " + info.state);
                GUILayout.Label("Config version: " + info.configVersion);
                GUILayout.Label("Spins played: " + info.spinsPlayed);
            }
            GUI.enabled = !busy;
            if (GUILayout.Button("Dev: 10 quick spins (no animation)")) QuickSpins(10);
            GUI.enabled = true;
        }
#endif

        GUILayout.EndArea();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void QuickSpins(int count)
    {
        restored = false;
        for (int i = 0; i < count; i++)
        {
            SpinResult r = service.Spin(playerIds[playerIndex], modeIndex, CurrentBet);
            if (!r.IsSuccess) { status = r.message; break; }
            current = r.record;
            service.MarkResultShown(r.record.spinId);
            phase = Phase.Showing;
        }
    }
#endif
}