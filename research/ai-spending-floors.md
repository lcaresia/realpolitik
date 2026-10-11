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
