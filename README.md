# The Red Ledger

**English** · [한국어](README.ko.md) · [中文](README.zh.md)

A [Slay the Spire 2](https://store.steampowered.com/app/2868840/) mod that lets you **borrow gold at the merchant** to afford what you can't quite pay for — then work the debt back off, or drown in it.

## Core loop

1. **Borrow.** At a shop, an item you can't fully afford can still be bought — the shortfall is lent to you, up to a cap. You receive the **Debt Ledger** relic, which tracks what you owe.
2. **Interest grows.** You owe more than you borrowed: an origination fee up front, plus interest that accrues each room you carry the debt. The longer you drag it out, the more it costs.
3. **Fall behind → curses.** Carry the debt too long and escalating **Debt** curse cards seep into your combats — **Delinquency**, then **Seizure**, then **Bad Credit** and its relentless **Forced Collection**.
4. **Pay it down.** The **Standing Order** power feeds you **Payment** cards each turn — playing one spends gold to knock down the principal. **Repay the principal at a later shop** — never the one you borrowed from — to settle. The Ledger and your payment cards stay; only your **debt tier** resets, so the curses stop and you can borrow again.
5. **Build credit.** Everything you ever repay adds up into a **Credit** score that *never* resets, not even when you settle. Each rung unlocks a reward you claim yourself — and past the top rung it keeps paying out, letting you upgrade or remove a card again and again.

## The debt shop

Once you owe, a dedicated **debt shop** lets you buy payoff cards **on credit** — adding their price onto what you owe. The stock rotates each visit; one offer is free, one is on sale, and the rest run 45–95 gold. Each shop grants a limited **credit line** (default 120 gold), which turns every visit into the same question: *one premium card, or two cheaper ones whose prices add up to 120 or less?* The shop also sells card removal, on credit.

## The payment engine

Every payment you make banks a **Receipt**, a combat resource with its own counter. The payoff cards you collect then cash Receipts in:

- **Payoff powers** — *Payment Benefit*, *Refund*, *Interest Support* and more react to each payment, handing back block, cards, or gold.
- **Receipt-spenders** scale with the Receipts you've banked: *Settlement* converts them to block, *Invoice* to a multi-hit attack.
- **Collections** turns the loop offensive — each turn it slips you a *Shakedown* token that spends a Receipt for **Vigor**, boosting your next attack.

## Debt as leverage

Paying it off is not the only way to play. The same Ledger that deals out curses also deals out collateral — cards that pay you *for the debt you are still carrying*:

- **Collateral** — 1 Block per 45 gold of debt (34 upgraded).
- **Default Risk** — 1 Strength per 250 gold of debt (180 upgraded), granted once when you play it.
- **Bad Debt** — 5 damage per Curse in your hand. It counts them; it never spends them.
- **Seized Goods** — 0-cost, Exhaust, 8 Block, shuffled into your draw pile at the start of combat. *How many* you get steps up with the debt you carry (250 / 500 / 750 gold → 1 / 2 / 3), so the reward tracks the size of the debt, not how long you stalled.

So "when do I cash out?" is an actual decision, not a countdown.

## Declare Bankruptcy

When debt clogs your deck, **Declare Bankruptcy** exhausts every Debt card you hold and turns the wreckage into **Strength** — but you earn no gold for the rest of the fight. An all-in pivot for a deck buried in debt.

## Co-op (multiplayer)

Debt is a shared burden:

- **Contagion** — a partner's loan seeps into *your* combats too.
- **Harsher together** — interest accrues faster and climbs higher the more players are in debt.
- **Bailout (대납)** — a multiplayer-only card that pays down a teammate's debt for them. When someone misses a payment, the wealthiest ally is handed a Bailout card so they can cover it.

## Config (in-game ModConfig)

Maximum loan gold (0 = uncapped), how many separate draws one loan allows, the per-shop credit line for buying cards on debt, the share of gold income garnished once interest maxes out, and the ceiling on total interest.

## Status

Published as a **public playtest** on the Steam Workshop — card balance and content may change at any time. Verified headlessly, end to end, in **single-player** (`solo-verify`) and in **2-instance co-op** (`coop-verify`: shop-purchase replication, bailout grant, and bailout use all converge across peers with no desync). See [`DESIGN.md`](DESIGN.md) for the full design notes.

## Build

Part of the author's monorepo; depends on the shared **Sts2.ModKit** SDK (`..\Sts2.ModKit\build\Sts2.ModKit.props` in the csproj). To build standalone, point that import at a copy of Sts2.ModKit.

- **DLL:** `dotnet build Sts2DebtLoan.csproj -c Release` → deploy to `Slay the Spire 2/mods/Sts2DebtLoan/`.
- **Resource pack** (relic/card art, localization): built from `pck_src/` with Godot 4.5.1 `--export-pack` → `Sts2DebtLoan.pck`.

## Assets

All in-game art — card portraits, the relic icon, and power icons — was created with AI image generation.

Author: **inggom**
