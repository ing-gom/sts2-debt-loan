using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;                 // LocalContext
using MegaCrit.Sts2.Core.Entities.Merchant;       // MerchantEntry
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;               // IHoverTip, HoverTip, HoverTipAlignment
using MegaCrit.Sts2.Core.Nodes.HoverTips;         // NHoverTipSet
using MegaCrit.Sts2.Core.Localization;            // LocManager
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;     // NMerchantSlot
using MegaCrit.Sts2.Core.Runs;                    // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// Appends a "대출 기회 2 / 3" line to the merchant item's NATIVE hover tip, and only for items a loan could
/// actually cover. It answers the question the draw limit creates — <i>is this the thing worth one of my three?</i>
/// — at the moment it is asked, without adding another floating panel to an already dense shop screen.
///
/// WHY THIS HOOK. Every hover tip in the game funnels through
/// <c>NHoverTipSet.CreateAndShow(owner, IEnumerable&lt;IHoverTip&gt;, alignment)</c>, which passes the list straight
/// to <c>Init</c>. A prefix that CONCATENATES one tip onto that argument therefore lets the game do all the work it
/// is already good at: stacking the line under the item's own tips, flipping sides at the viewport edge, following
/// the cursor, and tearing down on un-hover.
///
/// Two earlier attempts and why they lost:
///   • a permanent chip on the rug — sat beside the debt shop's "대출 가능 {잔액}/{한도}" header, same word and same
///     X/Y shape but one counts GOLD and the other counts TIMES;
///   • a follower panel beside the hovered slot — measured on screen, it covered the neighbouring cards' PRICE tags,
///     which is information the player needs to make exactly this decision.
/// Per-subtype patching of <c>CreateHoverTip</c> was also rejected: each NMerchantSlot flavour builds its own tips,
/// and re-registering an owner in NHoverTipSet's active map throws on the duplicate key.
///
/// The gate is <see cref="LoanService.CanLoanCover"/> — the same predicate that paints the loanable price tag
/// purple — so the tag and this line can never disagree, and an item you can already afford shows nothing (buying
/// it spends no draw). Read-only: nothing here mutates run state, so it is display-layer for co-op purposes.
/// </summary>
[HarmonyPatch]
internal static class LoanChanceHoverTipPatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method(typeof(NHoverTipSet), nameof(NHoverTipSet.CreateAndShow),
                           new[] { typeof(Godot.Control), typeof(IEnumerable<IHoverTip>), typeof(HoverTipAlignment) });

    /// <summary>Runs for EVERY hover tip in the game, so it early-outs on the cheapest checks first: the owner must
    /// be a merchant slot and the draw limit must be on before anything else is touched.</summary>
    private static void Prefix(Godot.Control owner, ref IEnumerable<IHoverTip> hoverTips)
    {
        try
        {
            if (DebtLoanConfig.MaxLoanDraws <= 0 || owner is not NMerchantSlot slot) return;
            var player = LocalContext.GetMe(RunManager.Instance?.State?.Players ?? Enumerable.Empty<Player>());
            if (player == null) return;
            var entry = EntryOf(slot);
            if (entry == null || !LoanService.CanLoanCover(entry, player)) return;   // affordable / uncoverable → no line

            int max = DebtLoanConfig.MaxLoanDraws;
            int left = Math.Min(LoanService.DrawsLeftFor(player), max);
            var ui = DebtLoanLoc.DebtShopUiFor(LocManager.Instance?.Language ?? "eng");
            var tip = new HoverTip
            {
                Title = string.Format(ui.Draws, left, max),
                Description = ui.DrawsTip,
                Id = "sts2debtloan_loanchance",
            };
            hoverTips = (hoverTips ?? Enumerable.Empty<IHoverTip>()).Concat(new IHoverTip[] { tip });
        }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] loan-chance hover tip failed: {e.Message}"); }
    }

    /// <summary>The MerchantEntry a slot is showing — found by field type, because each slot subtype names its own
    /// entry field differently (same helper MerchantPriceColorPatch uses for the purple price tag).</summary>
    private static MerchantEntry? EntryOf(NMerchantSlot slot)
    {
        foreach (var f in slot.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            if (typeof(MerchantEntry).IsAssignableFrom(f.FieldType))
                return f.GetValue(slot) as MerchantEntry;
        return null;
    }
}
