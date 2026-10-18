using System;
using System.Collections.Generic;
using DealEngine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Canvas version of the game screen. It builds its own UI and only ever reads SpinRecord
/// objects returned by IDealService. It never looks at the ticket list.
/// </summary>
public class CanvasGame : MonoBehaviour
{
    private const long StartingBalanceCents = 10000; // $100.00 per test player
    private const float SpinSeconds = 3f;            // reels stop at 1s, 2s and 3s

    // Index 5 ("$") is reserved for bonus wins. Losses and multiplier wins use 0..4.
    private static readonly string[] Symbols = { "A", "K", "Q", "J", "7", "$" };

    private static readonly Color ButtonNormal = new Color(0.25f, 0.27f, 0.33f);
    private static readonly Color ButtonSelected = new Color(0.20f, 0.55f, 0.90f);
    private static readonly Color SpinGreen = new Color(0.15f, 0.60f, 0.30f);
    private static readonly Color ReelNormal = new Color(0.16f, 0.17f, 0.22f);
    private static readonly Color ReelWin = new Color(0.80f, 0.60f, 0.10f);
    private static readonly Color WinText = new Color(1f, 0.85f, 0.30f);
    private static readonly Color ErrorText = new Color(1f, 0.45f, 0.45f);

    private enum Phase { Idle, Spinning, Showing }

    private readonly string[] playerIds = { "player1", "player2" };
    private readonly string[] playerNames = { "Alice", "Bob" };

    private DealConfig config;
    private IDealService service;
    private string configSource;
    private string startupError;

    private int playerIndex;
    private int modeIndex;
    private int betIndex;
    private Phase phase = Phase.Idle;
    private SpinRecord current;
    private string[] finalSymbols = { "?", "?", "?" };
    private float spinStart;
    private bool restored;
    private string status = "";

    private Font font;
    private Text balanceText;
    private Text resultText;
    private Text noteText;
    private readonly Text[] reelTexts = new Text[3];
    private readonly Image[] reelPanels = new Image[3];
    private readonly List<Button> allButtons = new List<Button>();
    private readonly List<Image> playerImages = new List<Image>();
    private readonly List<Image> modeImages = new List<Image>();
    private readonly List<Image> betImages = new List<Image>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private Text debugText;
    private float nextDebugUpdate;
#endif

    private long CurrentBet { get { return config.betLevelsCents[betIndex]; } }

    // ---------- startup ----------

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        try
        {
            config = ConfigLoader.Load(out configSource);
            service = new DealService(config, new UnityJsonSerializer(), GameController.DataFolder, msg => Debug.Log(msg));
            GameController.Current = service; // lets the Editor admin window find the running service

            for (int i = 0; i < playerIds.Length; i++)
                service.EnsurePlayer(playerIds[i], playerNames[i], StartingBalanceCents);

            service.Prepare(modeIndex, CurrentBet);
        }
        catch (Exception e)
        {
            startupError = e.Message;
            Debug.LogException(e);
        }

        if (FindFirstObjectByType<EventSystem>() == null)
            Debug.LogError("No EventSystem in the scene, so buttons will not respond. " +
                           "Use GameObject > UI > Event System.");

        BuildUi();

        if (service != null)
        {
            ShowPendingResult();
            Refresh();
        }
    }

    private void OnDestroy()
    {
        if (GameController.Current == service) GameController.Current = null;
    }

    // ---------- game logic ----------

    private void Update()
    {
        if (service == null) return;

        if (phase == Phase.Spinning)
        {
            float elapsed = Time.unscaledTime - spinStart;
            for (int i = 0; i < reelTexts.Length; i++)
            {
                float stopAt = SpinSeconds * (i + 1) / 3f;
                if (elapsed >= stopAt)
                    reelTexts[i].text = finalSymbols[i];
                else
                    reelTexts[i].text = Symbols[(int)(Time.unscaledTime * 18f + i * 2) % Symbols.Length];
            }

            if (elapsed >= SpinSeconds)
            {
                phase = Phase.Showing;
                service.MarkResultShown(current.spinId);
                Refresh();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        UpdateDebug();
#endif
    }

    // If the game was closed before a result was shown, show that same saved result.
    private void ShowPendingResult()
    {
        SpinRecord pending = service.GetPendingResult(playerIds[playerIndex]);
        if (pending == null) return;

        current = pending;
        finalSymbols = SymbolsFor(pending);
        phase = Phase.Showing;
        restored = true;
        service.MarkResultShown(pending.spinId);
    }

    private void DoSpin()
    {
        if (phase == Phase.Spinning) return;
        restored = false;
        status = "";

        SpinResult result = service.Spin(playerIds[playerIndex], modeIndex, CurrentBet);
        if (!result.IsSuccess)
        {
            status = result.message; // for example "Balance too low." (no ticket was used)
            Refresh();
            return;
        }

        // The result is already decided and saved. The reels only replay it.
        current = result.record;
        finalSymbols = SymbolsFor(current);
        phase = Phase.Spinning;
        spinStart = Time.unscaledTime;
        Refresh();
    }

    private void SelectPlayer(int index)
    {
        if (phase == Phase.Spinning) return;
        playerIndex = index;          // switching player never touches the deal
        current = null;
        finalSymbols = new[] { "?", "?", "?" };
        phase = Phase.Idle;
        restored = false;
        status = "";
        ShowPendingResult();
        Refresh();
    }

    private void SelectMode(int index)
    {
        if (phase == Phase.Spinning) return;
        modeIndex = index;
        status = "";
        service.Prepare(modeIndex, CurrentBet);
        Refresh();
    }

    private void SelectBet(int index)
    {
        if (phase == Phase.Spinning) return;
        betIndex = index;
        status = "";
        service.Prepare(modeIndex, CurrentBet);
        Refresh();
    }

    // What to show on the reels, worked out only from the saved spin record.
    private static string[] SymbolsFor(SpinRecord r)
    {
        if (r.kind == TicketKind.Bonus) return new[] { "$", "$", "$" };

        if (r.kind == TicketKind.Multiplier)
        {
            string s = Symbols[r.value % 5];
            return new[] { s, s, s };
        }

        // Loss: three symbols from the spin ID, never all equal, so a reload shows the same reels.
        uint h = Hash(r.spinId);
        int a = (int)(h % 5); h /= 5;
        int b = (int)(h % 5); h /= 5;
        int c = (int)(h % 5);
        if (a == b && b == c) c = (c + 1) % 5;
        return new[] { Symbols[a], Symbols[b], Symbols[c] };
    }

    private static uint Hash(string text)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in text) { h ^= ch; h *= 16777619; }
            return h;
        }
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

    // ---------- screen updates ----------

    private void Refresh()
    {
        if (service == null) return;

        long balance = service.GetBalance(playerIds[playerIndex]);
        // Hide the award until the reels stop: show the balance as it was before this spin paid out.
        if (phase == Phase.Spinning && current != null)
            balance = balance - current.awardCents + current.betCents;
        balanceText.text = "Balance: " + Money.FromCents(balance);

        bool busy = phase == Phase.Spinning;
        foreach (Button b in allButtons) b.interactable = !busy;

        Tint(playerImages, playerIndex);
        Tint(modeImages, modeIndex);
        Tint(betImages, betIndex);

        bool won = phase == Phase.Showing && current != null && current.awardCents > 0;
        for (int i = 0; i < reelPanels.Length; i++)
        {
            reelPanels[i].color = won ? ReelWin : ReelNormal;
            if (phase != Phase.Spinning)
                reelTexts[i].text = phase == Phase.Showing ? finalSymbols[i] : "?";
        }

        if (phase == Phase.Spinning)
        {
            resultText.text = "Spinning...";
            resultText.color = Color.white;
        }
        else if (phase == Phase.Showing && current != null)
        {
            resultText.text = Describe(current);
            resultText.color = won ? WinText : Color.white;
        }
        else
        {
            resultText.text = "Pick a bet and press SPIN";
            resultText.color = Color.white;
        }

        if (restored)
        {
            noteText.text = "Restored from your previous session";
            noteText.color = Color.white;
        }
        else
        {
            noteText.text = status;
            noteText.color = ErrorText;
        }
    }

    private static void Tint(List<Image> images, int selected)
    {
        for (int i = 0; i < images.Count; i++)
            images[i].color = i == selected ? ButtonSelected : ButtonNormal;
    }

    // ---------- building the Canvas ----------

    private void BuildUi()
    {
        GameObject canvasObject = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Transform root = canvasObject.transform;

        GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root, false);
        SetAnchors(background.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
        Image backgroundImage = background.GetComponent<Image>();
        backgroundImage.color = new Color(0.07f, 0.08f, 0.11f);
        backgroundImage.raycastTarget = false;

        if (startupError != null)
        {
            Text error = MakeText(root, "Error", "Could not start:\n" + startupError, 28, TextAnchor.MiddleCenter, 0.05f, 0.2f, 0.95f, 0.8f);
            error.color = ErrorText;
            return;
        }

        MakeText(root, "Title", "Finite Prize Pool", 32, TextAnchor.MiddleLeft, 0.03f, 0.92f, 0.47f, 0.99f);

        // Players
        for (int i = 0; i < playerNames.Length; i++)
        {
            int index = i;
            float x0 = 0.03f + i * 0.23f;
            Image image;
            MakeButton(root, "Player" + i, playerNames[i], 26, x0, 0.83f, x0 + 0.21f, 0.91f, () => SelectPlayer(index), out image);
            playerImages.Add(image);
        }

        balanceText = MakeText(root, "Balance", "Balance:", 32, TextAnchor.MiddleLeft, 0.03f, 0.74f, 0.47f, 0.82f);

        // Modes
        for (int i = 0; i < config.modes.Count; i++)
        {
            int index = i;
            float x0 = 0.03f + i * 0.23f;
            Image image;
            MakeButton(root, "Mode" + i, config.modes[i].name, 26, x0, 0.64f, x0 + 0.21f, 0.72f, () => SelectMode(index), out image);
            modeImages.Add(image);
        }

        // Bet levels, 5 per row
        for (int i = 0; i < config.betLevelsCents.Count; i++)
        {
            int index = i;
            int column = i % 5;
            int row = i / 5;
            float x0 = 0.03f + column * 0.09f;
            float y1 = 0.62f - row * 0.08f;
            Image image;
            MakeButton(root, "Bet" + i, Money.FromCents(config.betLevelsCents[i]).ToString(), 20,
                x0, y1 - 0.07f, x0 + 0.08f, y1, () => SelectBet(index), out image);
            betImages.Add(image);
        }

        Image spinImage;
        MakeButton(root, "SpinButton", "SPIN", 48, 0.03f, 0.12f, 0.47f, 0.27f, DoSpin, out spinImage);
        spinImage.color = SpinGreen;
        // Selection tinting does not apply to the spin button, so keep its green.
        ColorBlock colors = spinImage.GetComponent<Button>().colors;
        colors.normalColor = Color.white;
        spinImage.GetComponent<Button>().colors = colors;

        // Reels
        for (int i = 0; i < 3; i++)
        {
            float x0 = 0.52f + i * 0.155f;
            GameObject panel = new GameObject("Reel" + i, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root, false);
            SetAnchors(panel.GetComponent<RectTransform>(), x0, 0.58f, x0 + 0.14f, 0.92f);
            reelPanels[i] = panel.GetComponent<Image>();
            reelPanels[i].color = ReelNormal;
            reelPanels[i].raycastTarget = false;

            reelTexts[i] = MakeText(panel.transform, "Symbol", "?", 110, TextAnchor.MiddleCenter, 0f, 0f, 1f, 1f, true);
        }

        resultText = MakeText(root, "Result", "", 34, TextAnchor.MiddleCenter, 0.52f, 0.46f, 0.97f, 0.56f);
        noteText = MakeText(root, "Note", "", 22, TextAnchor.MiddleCenter, 0.52f, 0.39f, 0.97f, 0.45f);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        debugText = MakeText(root, "Debug", "", 20, TextAnchor.UpperLeft, 0.52f, 0.10f, 0.97f, 0.36f);
        Image quickImage;
        MakeButton(root, "QuickSpins", "Dev: 10 quick spins (no animation)", 20, 0.52f, 0.02f, 0.97f, 0.08f, QuickSpins, out quickImage);
#endif
    }

    private static void SetAnchors(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0);
        rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private Text MakeText(Transform parent, string name, string content, int size, TextAnchor align,
        float x0, float y0, float x1, float y1, bool bestFit = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), x0, y0, x1, y1);

        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.color = Color.white;
        text.text = content;
        text.raycastTarget = false;
        if (bestFit)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 20;
            text.resizeTextMaxSize = size;
        }
        return text;
    }

    private Button MakeButton(Transform parent, string name, string label, int fontSize,
        float x0, float y0, float x1, float y1, UnityAction onClick, out Image image)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), x0, y0, x1, y1);

        image = go.GetComponent<Image>();
        image.color = ButtonNormal;
        image.sprite = Resources.Load<Sprite>("UI/RoundedButton");
        image.type = Image.Type.Sliced;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        MakeText(go.transform, "Label", label, fontSize, TextAnchor.MiddleCenter, 0f, 0f, 1f, 1f);
        allButtons.Add(button);
        return button;
    }

    // ---------- debug panel (not compiled into release builds) ----------

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void UpdateDebug()
    {
        if (debugText == null || Time.unscaledTime < nextDebugUpdate) return;
        nextDebugUpdate = Time.unscaledTime + 0.25f;

        // Only what the brief allows: no upcoming tickets, no remaining prizes.
        DealInfo info = service.GetDealInfo(modeIndex, CurrentBet);
        string text = "DEBUG (development builds only)\nConfig source: " + configSource + "\n";
        if (info == null)
            text += "Deal: none active yet for this mode and bet";
        else
            text += "Deal ID: " + info.dealId + "\nDeal state: " + info.state +
                    "\nConfig version: " + info.configVersion + "\nSpins played: " + info.spinsPlayed;
        debugText.text = text;
    }

    private void QuickSpins()
    {
        if (phase == Phase.Spinning) return;
        restored = false;
        status = "";
        for (int i = 0; i < 10; i++)
        {
            SpinResult r = service.Spin(playerIds[playerIndex], modeIndex, CurrentBet);
            if (!r.IsSuccess) { status = r.message; break; }
            current = r.record;
            finalSymbols = SymbolsFor(current);
            service.MarkResultShown(r.record.spinId);
            phase = Phase.Showing;
        }
        Refresh();
    }
#endif
}