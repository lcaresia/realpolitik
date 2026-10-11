# Resource trade (luxury and strategic): vanilla rules and what the mod changes (2026-10-11)

Read-only research of the decompiled game. [CODE] = confirmed in code; [DATA] = formula lives in the compressed
MercuryDatabases asset bundle (not dumped), so the exact numbers are unknown.

## What is transferred
- [CODE] Buying gives the buyer **copies** of access; nothing is removed from the seller.
  `DepartmentOfResources.cs:171-174`: access = Produced + Bought + Leeched + UnlockedBonus + Difficulty + God + Spy.
- Produced = accesses of the seller's own connected deposits (one per extractor, usually 1). Bought accesses cannot be
  resold.
- Cap per buyer = the seller's Produced count; no limit on the number of buyers (`TradeController.cs:1542-1559`).
- UI "(0/3)": 0 = what you buy from that empire, 3 = the seller's Produced accesses (`TradeTableItem.cs:109-111`).

## Money
- One-time setup cost, paid by the buyer per added access: `SetupTradeBuyerCostRPN` [DATA] × diplomatic multiplier
  (`TradeController.cs:1100-1149, 1219-1223`). The seller receives `SetupTradeSellerGainRPN(cost)` [DATA] once.
  Selling to a minor gives patronage instead of money. No refund when access is dropped. The simulation never checks
  the buyer's money (`Pay` can go negative); only the UI clamps.
- Per-turn upkeep is paid by the buyer and received by **nobody** (money destroyed). It grows with path nodes and,
  super-linearly, with the number of distinct resource types on the route (UI estimate `×(1+0.2(N^1.5−4))`).
- Vanilla bug: Buy-All pays the seller only the gain of the last resource type (`TradeController.cs:1181`).

## Price dynamics
- Setup input `setupCostPerResourceType[r]` = `SetupTradeResourceLicenseCostRPN(TradeBaseLicenceCost[r],
  numberOfTradePerResourceType[r])` [DATA]; the counter is global and only increases (each new buyer of r, ever).
- Not in the price: distance, the seller's deposit count, current number of buyers. No market dynamic otherwise.
- Quantity: new extractors add, lost territory/extractor or a frozen link to the capital removes; existing buyers keep
  their bought count but become short-supplied. A destroyed route (war, ransack, failed path) wipes all purchases.

## Luxury effects
- Binary per type: the empire gets the luxury descriptors when access goes 0 → >0 and loses them at 0
  (`DepartmentOfResources.cs:780-829`); category set bonuses when all resources of a category are held.
- Exporting never reduces the seller's bonus.

## Native AI buying
- Luxury need is a constant 0.4 (+0.3 PreferToTrade, +0.3 Luxurious bias), regardless of what it already holds.
- Buys +1 access at a time (`BuyOneResourceAccess`). `UpdateCosts` marks the task DebtAllowed and adds upkeep ×15;
  `MoneyAllocator` budgets MoneyStock + MoneyNet × 15 for debt-allowed tasks (`MoneyAllocator.cs:38-213`): the AI may
  go into debt for ~15 turns of income and never checks its current debt level.
- `ReduceTradeUpkeep` cuts imports only when net income < 0.

## Mod changes
- `TransferConversion.cs:218-240`: price × Rate(seller, buyer) (buyer pays in its currency); the seller is settled back
  in its own currency. Upkeep is not converted.
- Tolls/blockades (`Trade/TradeBlockade.cs`), bloc upkeep refund 25% (`Diplomacia/Blocs/BlocTrade.cs`).
- The mod adds no resource-buying AI; `Trade/TradeAi.cs` only sets toll/blockade stances.
