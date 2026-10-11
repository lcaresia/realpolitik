# Retroactive review with the code map (2026-10-11)

Scope: the code changed on 2026-10-10/11 (commits 909f476..22c081c), checked with the graphify maps (`codemap/`)
plus deterministic scripts; AI only judged the flagged items.

| Step | Check | Result |
|---|---|---|
| 1 | Game methods patched more than once | 7 targets. Banner shutdown (x5), save (x2) and load (x3) are independent by design. ComputeProductionIncome: both multiply (order irrelevant). GetSettlementPublicOrderGain: the veteran patch runs last (Priority.Low) on top of the discounted target, so they compose. InvokePasses / TurnEndPass_CollectMoney: independent prefixes, each guarded once per turn. No conflict. |
| 2 | Callers of changed functions (new optional parameters; result prefixes) | Fixed: NationAndLeader mid-sentence without `inline` (group member lists, the Alliances sent-invite title). InterestFlow callers all pass GDP. The UI result protocol "erro: " became "error: " in producers and consumers together; persisted status values unchanged. |
| 3 | Orphan methods | Removed `BlocEngine.TariffAgainst` (duplicate of BlocIndex). `PlayerCouncil.HasNews` is now used by the council badge instead of a hand-written copy. `ActionExecutor.FillGift` was a false alarm (passed as a callback). Kept `GameText.EraNameUi` (documented API with OfUi/InlineUi). MoreEmpires not touched. |
| 4 | Main-thread state read from simulation/AI-thread patches | 63 simulation/AI patches checked. Only save/load touch the AI module, through a volatile string and a locked handoff. Blocs, spending floors and route transit use snapshots. Clean. |
| 5 | Formulas duplicated across files | The real rate and the interest push on inflation were recomputed in both bank screens; now EconomySimulation.RealRate / InterestInflationPush, used by the simulation and both screens (same behavior). |

Scripts: `tools/codemap/harmony_edges.py` (patch edges); the one-off checks live in the session scratchpad
(orphans, thread reachability, duplicate formulas). Static call resolution misses qualified calls, method groups and
reflection, so every orphan and thread hit was confirmed by text search before acting.
