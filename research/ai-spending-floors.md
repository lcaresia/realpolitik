# Native AI money spending: where to enforce the LLM-set cash floors (2026-10-11)

Located with the graphify game map (`mapa/jogo`). Goal (agreed with Lucas): the LLM nation sets three cash floors each
decision — army, cities, resources — and the native AI never spends money in a category if the stock would end below
that category's floor. Only the army floor may be negative (planned debt for war).

## The budget
`Amplitude.Mercury.AI.Brain.Allocators.MoneyAllocator` (StockResourceAllocator<MajorEmpireBrain, MajorEmpire>):
- `AllocateTasks(MajorEmpire, List<Task> allocableTasks, List<Task> cancelableTasks, FixedPoint stockToAllocate,
  FixedPoint netToAllocate)` (MoneyAllocator.cs:38) walks tasks in priority order and fills each money
  `ResourceAllocation`.
- Debt trap (lines 80-86, 100): a task flagged `DebtAllowed`/`LongTermDebtAllowed` with `Motivation >= 1` gets its full
  `NeededQuantity` regardless of the stock; otherwise the budget is `stock + net × AcceptableShortTermDebtDuration (15)`
  (or the long-term duration). Nothing checks the current debt.
- `ComputeAllocationMotivation` (line 136) scales motivation by how many turns of income the purchase costs.

## Money-spending tasks (Amplitude.Mercury.AI.Brain.Tasks) by floor category
- Army: `UpgradeArmy`, `RentArmy` (mercenaries), `BuyoutQueuedConstruction` whose constructible is a unit
  (`BaseBuildConstruction.ConstructibleType == ConstructibleType.Unit`; see Generators.Construction/BuyoutQueuedConstructions.cs:36-44).
- Cities: `BuyoutQueuedConstruction` for buildings/districts, `BaseBuildConstruction` money costs, `DoSettlementAction`,
  `DoAffinityActionOnSettlement`, `DoOsmosisAction`, `DoCulturalAction`.
- Resources: `BuyOneResourceAccess` (adds upkeep × 15 and DebtAllowed in `UpdateCosts`).
- Diplomacy (not a floor category; LLM nations already hold native treaty initiatives in NativeAiLocks):
  `DiplomaticAction`, `DiplomaticConsulateAgreementAction`, `HandleDiplomaticProposition`, `PatronizeMinor`.

## Enforcement plan
Harmony prefix on `MoneyAllocator.AllocateTasks`: for an LLM-controlled empire with floors, walk `allocableTasks` in
order, classify each money allocation, track the running stock, and remove from the list (for this pass) every task
whose allocation would take the stock below its category floor. Removing a task only defers it; the native brain asks
again next pass. Empires without LLM floors get a default floor computed from their spending.

## Decisions (Lucas, 2026-10-11)
- The LLM recomputes the three floors on every decision (war, crisis, big sale change them).
- All three floors may be negative: the AI chooses how much debt it accepts per category.
- Nations without LLM floors use 0 in all categories (no native debt spiral).
- Route upkeep idea (pending implementation): the buyer's per-turn upkeep is paid out to the owners of the
  territories the route crosses, by share of path territories; the share crossing the buyer's own land and neutral
  land vanishes as today (default; Lucas did not object).

## Implemented and tested (2026-10-11, Teste16 T85-87)
- `Diplomacy/SpendingFloors.cs`: prefix on `MoneyAllocator.AllocateTasks`; floors in `AiNation.CashFloors`; LLM action
  `set_cash_floors` {army, cities, resources, reason} (English keys); dossier section ECONOMIA
  (`Diplomacy/DossierEconomy.cs`): GDP ranking of known nations, treasury, income, exchange rate, own floors and the most
  expensive purchase each floor held back this turn; dev command `ai floors [set E# army cities resources]`.
- Test: 13 of 14 LLM nations set their floors on their own on the first turn; the floors held back purchases in every
  category. The prompt example first used numbers and nations copied them; it now uses placeholders (N).
- `Trade/RouteTransit.cs`: the buyer's route upkeep is paid to the non-buyer owners of the crossed territories, in
  proportion to their territories on the path (config `[BloqueioComercial] RepasseDaManutencao`; Lucas set 0.1 = 10% paid out, 90% vanishes as route maintenance). Test:
  about 2,330 per turn credited world-wide against about 1,800 of summed income (Carthaginians +446 for +116 income),
  so this injects a lot of money; the share is tunable.
