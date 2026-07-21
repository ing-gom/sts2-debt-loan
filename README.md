# Sts2DebtLoan — Merchant Loans (상점 대출)

A [Slay the Spire 2](https://store.steampowered.com/app/2868840/) mod that lets you **borrow gold at the merchant** to afford an item you can't quite pay for — at a price.

## How it works

- At an **Act 1** shop, an item you can't fully afford can still be bought: the shortfall is lent to you (up to a **300 gold** total cap).
- Taking a loan grants the **Merchant's Ledger** relic, which carries the debt.
- Leave it unpaid and **Debt** curse cards seep into your deck as you travel:
  - **1** card after 14 rooms · **3** after 17 · **5** after 20 (capped).
  - Each Debt card drains gold at the end of your turn while it's in hand — that's the interest.
- **Repay the principal** at any shop to clear the debt, **or** once total interest reaches **200% of the loan** the ledger settles itself. Either way, every Debt card is then removed.

Config (in-game ModConfig): max loan, interest per Debt card, interest ceiling %, allow loans outside Act 1.

## Status

v0.1.0 — prototype. Verified headlessly (single-player) end to end:

- Loan grant → relic, room-count → Debt-card escalation (14/17/20 → 1/3/5), repay-retire, interest-cap-retire.
- **Save/load persistence** (state lives on the relic as `[SavedProperty]` fields; rebuilt on `NGame.LoadRun`).
- **Shop purchase interception** (`MerchantEntry.OnTryPurchaseWrapper`) and the **repay button** (`NMerchantRepayButton`) attach + function in a real shop.

See [`DESIGN.md`](DESIGN.md) for the full design + remaining TODOs (yellow price tags, grant-only relic, co-op).

## Build

This mod is part of the author's monorepo and depends on the shared **Sts2.ModKit** SDK
(`..\Sts2.ModKit\build\Sts2.ModKit.props` in the csproj). To build standalone, point that import
at a copy of Sts2.ModKit.

- DLL: `dotnet build Sts2DebtLoan.csproj -c Release` → deploy to `Slay the Spire 2/mods/Sts2DebtLoan/`.
- Resource pack (relic icon): built from `pck_src/` with Godot 4.5.1 `--export-pack` → `Sts2DebtLoan.pck`.

Author: **inggom**
