# Design Note: Finite Prize Pool

**Status:** the deal engine, Unity client, Edit Mode tests and simulation are done. The "Finite Pool Mathematics" PDF was not included with my copy of the assignment, so every bag value in this project is a **placeholder I chose myself**. The code reads all values from `deal-config.json`, so the real bags can be dropped in without code changes. Numbers below that come from my placeholder bags are marked as such.

## 1. Why does Inner fit in one paired cycle at $1.00, while Outer needs nine?

With `k` paired cycles and a payout of `P` cents per cycle, the number of paid spins at 90% is `k × P ÷ (0.9 × 100)`, which is `k × P ÷ 90` at a $1.00 bet. It must be a whole number, and I never round.

Because 0.9 = 9/10, the factor 9 has to be cancelled by something. Either the cycle payout already contains it, or the number of cycles supplies it:

- If `P` is a multiple of 90 cents, one cycle is enough. This is what Inner must look like.
- If `P` is a multiple of 10 but contains no factor of 3, all of the 9 must come from the cycles, so `k = 9`. This is what Outer must look like.

I derived this from the figures in the brief, since I could not check the real bags. With my placeholder bags the same rule gives 3 cycles for Inner $1.00 (payout $24.00 = 2,400 cents, which has exactly one factor of 3, so `90 ÷ gcd(2400, 90) = 3`).

## 2. Why does Outer at $4.75 need such a large deal, and what are the practical consequences?

$4.75 is 475 cents = 5 × 5 × 19. Multiplier prizes scale with the bet, but a bonus is a fixed $1 per unit, so the cycle payout usually does not contain the factor 19. The number of cycles has to supply it, on top of any factor needed for the 9, which multiplies the deal size.

Consequences, because deals have no automatic timeout and all profiles on the device share one deal per mode and bet:

- A rarely used bet can stay Active for a very long time. The 90% is only guaranteed once the whole deal has been used, so a half-played deal says little about RTP.
- A config change does not reach that deal until it finishes. Only an operator retiring it changes that.
- A retired deal never reached its target, and is recorded that way. Its tickets stay on disk for audit.
- Bigger deal files and longer build times. Successors are built on a background thread so play never waits.

## 3. Why must a ticket never be returned when a player closes the game mid-spin?

Once a ticket is taken, the outcome is fixed. If closing the game returned it, the order of tickets would depend on player behaviour. A player (or a glitch) could cancel spins that turn out badly and keep the good ones, which pushes real RTP above 90% and breaks the exact-total guarantee of the pool. So the spin is saved before any animation, the ticket is consumed permanently, and on reopening the saved result is shown again.

## 4. Does a player who plays 50 spins get 90% RTP?

No. The deal is built so that **the complete deal** returns exactly 90%. A player's 50 spins are only a slice of it, and the deal is shared by every profile on the device. A slice can contain the big prizes or none of them. In my own testing, one deal gave 15 losses in a row before the first win on spin 16. The placement limits only cap how bad a losing streak can get. They do not make a short run average 90%. A deal that is retired before it finishes never reaches 90% at all.

## 5. Why is shuffling while building acceptable when spins must not use random numbers?

The shuffle only decides **order**. The set of tickets is fixed by the bags and the deal-size calculation, so the totals (and the RTP) are the same however the shuffle turns out, and the verifier recalculates them exactly before use. The shuffle happens once, before play, using a cryptographic generator. The finished order is saved and never reshuffled. A spin just reads the next ticket, so a result cannot be re-rolled, influenced or lost, and every outcome can be audited from the saved deal.

## 6. What would I change to move the engine to a server shared by many devices?

The game already talks only to `IDealService`, so the client stays the same and the service moves behind an API.

- Store deals, balances, spin records and the retire log in a database. Deal ticket lists are never sent to clients.
- Claim a ticket, change the balance and write the spin record in one **database transaction**, instead of one atomic file write. Many players share each deal, so claiming the next ticket must be atomic (a row lock or atomic counter).
- Add an **idempotency key** per spin request, so a retried request cannot take a second ticket.
- Add authentication, and make the pending-result lookup work across devices.
- Build successor deals in a background job, keep the config version in the database, and make retire an authenticated admin action with an audit trail.
- Run the shuffle on the server with the same secure generator.

## Assumptions

- **Missing PDF.** All bag contents are placeholders (see above).
- **Block-wise slot ordering.** The brief says to shuffle the whole category list and check it against the placement limits. I tested that: for deals of a few hundred spins or more it almost never passes within 1,000 attempts. The builder instead shuffles one block per paired cycle and accepts a block only if it keeps every limit satisfied given what came before. The limits are never loosened, the 1,000-attempt maximum per deal still applies, and the complete order is checked again against the limits before use.
- **Gap definition:** the number of spins between two slots of one prize type, including the stretch from the start to the first one and from the last one to the end.
- **Fixed interval:** a deal is rejected if three or more prize slots are all exactly the same distance apart.
- A new spin is refused while the player's previous result has not been marked as shown.
- A Ready deal discarded after a config change is marked Retired without a retire-log entry, since it was never played.
- The Canvas uses Unity's legacy `Text` component. Reel symbols are derived only from the saved spin record.

## Skipped

- Tolerance mode (stretch goal).
- Tests written against the real bags: *Inner $1.00 with max consecutive losses 5 is rejected* and *Outer $1.75 with one paired cycle is rejected*. The same behaviours are tested with the placeholder bags.
- The deal-size table and simulation report were generated with the placeholder bags and should be regenerated when the real bags are available.

## Evidence

- 39 Edit Mode tests pass.
- Simulation (placeholder bags): 10 complete Inner $1.00 deals (80 tickets each) and 1 complete Outer $1.00 deal (140 tickets). Every deal ended at exactly 90.0000% RTP, and no loss streak exceeded its limit. See `SimulationReport.csv`.
