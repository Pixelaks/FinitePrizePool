# Finite Prize Pool (Unity)

A Unity implementation of a finite prize pool game. Outcomes are not rolled on each spin. They are prepared in advance as a **deal**: a fixed, shuffled list of tickets (Regular prizes, Big prizes and losses). Every spin uses the next ticket in order. When a deal has been fully used, its return to player (RTP) is exactly the target (90%).

Everything runs inside Unity in C#. There is no server, database or network part.

## Unity version

**Unity 6.2 (6000.2.13f1)**

## How to open and run

1. Clone the repository.
2. In Unity Hub, choose **Add > Add project from disk** and select the repository folder (the one containing `Assets`, `Packages` and `ProjectSettings`).
3. Open it with Unity 6.2 (6000.2.13f1).
4. Open `Assets/Scenes/SampleScene`. The scene contains a `GameController` object with the `CanvasGame` component and an Event System. The game builds its own Canvas at runtime.
5. Press **Play**.

### Using the game

- Pick a player (Alice or Bob). Each starts with $100.00 and has a separate balance.
- Pick a mode (Inner or Outer) and a bet (20 levels, $0.25 to $5.00).
- Press **SPIN**. The reels run for 3 seconds and then show the result.
- To test the "closed mid-spin" case: press SPIN, then stop Play within 3 seconds and press Play again. The same result is shown again, marked as restored, and no extra ticket is used.
- The debug panel (Editor and development builds only) shows deal ID, deal state, config version and spins played. It never shows upcoming tickets or remaining prizes.

### Tools menu (Editor only): `Tools > Deal Engine`

| Menu item | What it does |
|---|---|
| Write Placeholder Config JSON | Writes `Assets/Game/Resources/deal-config.json` |
| Export Deal Size Table (CSV) | Writes `DealSizeTable.csv` (all 40 mode/bet combinations) to the project root |
| Run Simulation Report | Plays 10 complete Inner $1.00 deals and 1 complete Outer $1.00 deal, writes `SimulationReport.csv` to the project root |
| Admin Window | Retire an active deal (while in Play mode) and view completed and retired deal records | | Export Audit CSV | Writes `AuditReport.csv` to the project root, listing completed and retired deals with wagers, payouts, target and actual RTP, and retire details |

### Running the tests

Open **Window > General > Test Runner**, choose the **EditMode** tab and click **Run All**.

### Where data is saved

Under `Application.persistentDataPath/FinitePrizePool/`:

- `deals/<deal id>.json`: the full ordered ticket list of one deal. Written once, before the deal is used, and never modified.
- `state.json`: player balances, spin records, each deal's progress (next ticket index, totals, state) and the retire log. Rewritten on every spin.

The Admin Window has **Show save folder** and **Delete saved data** buttons.

## Configuration

All game settings are in `Assets/Game/Resources/deal-config.json`: bags, bet levels, target RTP (in basis points, 9000 = 90.00%), bonus unit value, generation attempts, placement limit overrides and a config version ID. Nothing is hard-coded. If the file is missing, a built-in placeholder config is used.

> **Important:** the Regular and Big bag contents in the current config are **placeholder values chosen by me**. The "Finite Pool Mathematics" PDF referenced in the brief was not included with my assignment. See *Assumptions* below. To use the real bags, edit the JSON, then re-run the tests, the table export and the simulation.

## Project structure

```
Assets/
  DealEngine/
    Runtime/    Plain C# engine. Assembly "DealEngine", "No Engine References" is ticked,
                so it cannot use UnityEngine.
    Tests/      Unity Test Framework Edit Mode tests
  Game/         Unity client: Canvas UI, config loader, JSON bridge. Assembly "Game".
  Editor/       Editor-only tools (CSV export, simulation, admin window)
```

The Game assembly references the engine. The engine references nothing. The UI talks to the engine only through `IDealService` and never reads the ticket list.

### Engine overview

| Script | Purpose |
|---|---|
| `Money` | Money as whole cents in a `long` |
| `DealConfig` | Settings classes (modes, bags, prizes, bet levels, limits) and validation |
| `DealSizeCalculator`, `DealSize`, `DealSizeTable` | Smallest number of paired cycles that gives a whole number of paid spins at exactly the target RTP, using exact integer arithmetic. Rejects anything that does not fit. |
| `SecureShuffle` | Fisher-Yates using `RandomNumberGenerator.GetInt32` |
| `Placement` | Placement limits (max consecutive losses, max gaps) and the checker |
| `Ticket`, `Deal` | Ticket and deal data |
| `DealBuilder` | Builds a deal: cycle shuffling, slot order, merge, verification |
| `DealVerifier` | Recalculates wagers and payouts from the saved tickets and checks the RTP is exact |
| `Persistence` | `SafeFile.WriteAtomic` (temp file, flush, replace) and `IJsonSerializer` |
| `EngineState` | Saved state: players, spin records, deal progress, retire log |
| `IDealService`, `DealService` | The only API the game uses: spins, balances, pending results, lifecycle, retire |

## Key behaviour

- **Spin order:** check balance (reject without using a ticket), charge the wager, take the next ticket, credit the award, save the spin record. Only then does the UI animate.
- **Atomic saving:** balance, ticket position and spin record are written together in one atomic file replacement. If the save fails, the spin is rolled back in memory.
- **Unshown results:** a spin is stored as "not shown" until the UI has finished the animation. After a restart, the saved result is shown again. A new spin is refused until the pending result has been shown.
- **Deal lifecycle:** Ready, Active, Completed, Retired. A verified Ready successor is built on a background thread so play never waits. Completed deals record ticket count, wagers, payouts, target RTP and actual RTP.
- **Config changes** apply only to the next deal. An active deal keeps its config version, and a Ready deal built on an old version is discarded and rebuilt.
- **Retire:** marks the deal Retired, switches to a verified replacement, and logs reason, time, old and new config version, replacement deal ID, tickets used and remaining, wagers, payouts and actual RTP. A retired deal is never reported as having reached its target. The unused tickets stay in the deal file for audit.
- **Randomness:** only `SecureShuffle` uses random numbers, and only while a deal is being built. `UnityEngine.Random` and `System.Random` are not used in the engine, and a spin never generates an outcome.

## Assumptions and deviations

- **Missing PDF.** The brief refers to a separate "Finite Pool Mathematics" PDF, which I did not receive. The bag contents, and therefore the deal sizes in `DealSizeTable.csv` and the simulation report, are based on placeholder bags. The code does not depend on the specific numbers.
- **Block-wise slot ordering.** The brief describes shuffling the whole category list and checking it against the placement limits. I tested this with the limits as specified: for deals of a few hundred spins or more, a full shuffle almost never passes within 1,000 attempts. The builder therefore shuffles one block per paired cycle and accepts a block only if it keeps all limits satisfied given what came before. The limits are never loosened, the maximum of 1,000 attempts per deal still applies, and the full order is checked again against the limits before use.
- **Gap definition.** The gap between prize slots of one type is the number of spins between two such slots, including the stretch from the start to the first one and from the last one to the end.
- **Fixed-interval rule.** A deal is rejected if its prize slots (three or more) are all exactly the same distance apart.
- **Pending result.** A new spin is refused while the player's previous result has not been marked as shown.
- **Discarded Ready deals** (after a config change) are marked Retired without a retire-log entry, since they were never played.
- **UI.** The Canvas is built from code using Unity's legacy `Text` component to avoid depending on TextMeshPro resources. Reel symbols are letters and `$`, and are derived from the saved spin record only.

## Not done

- Tolerance mode (stretch goal)
- Tests that depend on the real bags (see below)

## Test coverage

Edit Mode tests cover: money handling, config validation, deal-size calculation (exact RTP for every valid combination, fractional spins rejected), shuffle, placement limits, deal building (counts, cycle isolation, limits, verification, tampering), spin handling (low balance uses no ticket, save and reload mid-deal, unshown result after reload, final ticket and successor handoff), config change during a deal, and retire logging.

Two scenarios from the brief need the real bags to be tested exactly as written: *Inner $1.00 with max consecutive losses set to 5 is rejected as impossible*, and *Outer at $1.75 with only one paired cycle is rejected*. The current tests cover the same behaviour with the placeholder bags.

## Deliverables

- `DealSizeTable.csv`: deal sizes for all 40 mode/bet combinations, generated by the code
- `SimulationReport.csv`: 10 complete Inner $1.00 deals and 1 complete Outer $1.00 deal with actual RTP - `AuditReport.csv`: completed and retired deals from a test session (Tools > Deal Engine > Export Audit CSV)
- Design note: answers to the six questions and a list of assumptions
- **Screen Recording:** [Watch the project demonstration video](https://drive.google.com/file/d/1mvT_F07yFMPC4m88mimNoVgf__9nlYO1/view?usp=sharing) (5 minutes or less)
