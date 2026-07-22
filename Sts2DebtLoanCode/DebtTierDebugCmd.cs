using System.Globalization;
using System.Linq;
using MegaCrit.Sts2.Core.DevConsole;                // CmdResult
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands; // AbstractConsoleCmd
using MegaCrit.Sts2.Core.Entities.Players;          // Player
using MegaCrit.Sts2.Core.Helpers;                   // TaskHelper
using MegaCrit.Sts2.Core.Runs;                      // RunManager

namespace Sts2DebtLoan;

/// <summary>
/// DEBUG console command <c>dl_tier &lt;1-4&gt;</c> — jump the merchant loan to the rooms-since-loan for that
/// escalation tier so you can preview the Ledger relic's icon evolution (gold → cursed) instantly, without
/// walking 22 rooms. Grants a starter loan if you have none. Local + DebugOnly (mods enable the dev console),
/// auto-registered via ReflectionHelper.GetSubtypesInMods&lt;AbstractConsoleCmd&gt;. Not for real play.
/// </summary>
public sealed class DebtTierDebugCmd : AbstractConsoleCmd
{
    public override string CmdName => "dl_tier";
    public override string Args => "<1-4>";
    public override string Description => "DEBUG: preview the Ledger tier icon (1..4) by setting rooms-since-loan.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => true;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var player = issuingPlayer ?? RunManager.Instance?.State?.Players?.FirstOrDefault();
        if (player?.RunState == null) return new CmdResult(success: false, "dl_tier: no active player.");
        if (args.Length < 1 ||
            !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier) ||
            tier < 1 || tier > 4)
            return new CmdResult(success: false, "dl_tier: expected a tier 1..4.");

        // rooms threshold for this tier, read from the live schedule (0/10/17/22)
        int rooms = DebtLoanConfig.Schedule.Where(s => s.Cards == tier).Select(s => s.Room).DefaultIfEmpty(0).First();
        TaskHelper.RunSafely(LoanService.DebugSetTier(player, rooms));
        return new CmdResult(success: true, $"dl_tier {tier} → rooms {rooms}. Ledger updated.");
    }
}
