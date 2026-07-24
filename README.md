# Sts2DebtLoan — Merchant Loans

**English** · [한국어](README.ko.md) · [中文](README.zh.md)

A [Slay the Spire 2](https://store.steampowered.com/app/2868840/) mod that lets you **borrow gold at the merchant** to afford what you can't quite pay for — then work the debt back off, or drown in it.

## Core loop

1. **Borrow.** At a shop, an item you can't fully afford can still be bought — the shortfall is lent to you, up to a cap. You receive the **Merchant's Ledger** relic, which tracks what you owe.
2. **Interest grows.** You owe more than you borrowed: an origination fee up front, plus interest that accrues each room you carry the debt. The longer you drag it out, the more it costs.
3. **Fall behind → curses.** Carry the debt too long and escalating **Debt** curse cards seep into your combats — **Delinquency**, then **Seizure**, then **Bad Credit** and its relentless **Forced Collection**.
4. **Pay it down.** The **Standing Order** power feeds you **Payment** cards each turn; play them (and the payment-engine cards) to whittle down the principal, earning **Plating** as you go. **Repay the principal at any shop** to clear the Ledger and restore your credit — then you can borrow again.

## The debt shop

Once you owe, a dedicated **debt shop** lets you buy payment-engine cards **on credit** — settlement, invoice, garnishment and more — adding their price onto what you owe. The stock rotates each visit, with one card on sale every time.

## Co-op (multiplayer)

Debt is a shared burden:

- **Contagion** — a partner's loan seeps into *your* combats too.
- **Harsher together** — interest accrues faster and climbs higher the more players are in debt.
- **Bailout (대납)** — a multiplayer-only card that pays down a teammate's debt for them. When someone misses a payment, the wealthiest ally is handed a Bailout card so they can cover it.

## Config (in-game ModConfig)

Maximum loan amount, and which acts the merchant will lend in.

## Status

Actively developed. Verified headlessly, end to end, in **single-player** (`solo-verify`) and in **2-instance co-op** (`coop-verify`: shop-purchase replication, bailout grant, and bailout use all converge across peers with no desync). Not yet published to the Steam Workshop.

See [`DESIGN.md`](DESIGN.md) for the full design notes.

## Build

Part of the author's monorepo; depends on the shared **Sts2.ModKit** SDK (`..\Sts2.ModKit\build\Sts2.ModKit.props` in the csproj). To build standalone, point that import at a copy of Sts2.ModKit.

- **DLL:** `dotnet build Sts2DebtLoan.csproj -c Release` → deploy to `Slay the Spire 2/mods/Sts2DebtLoan/`.
- **Resource pack** (relic/card art, localization): built from `pck_src/` with Godot 4.5.1 `--export-pack` → `Sts2DebtLoan.pck`.

Author: **inggom**
