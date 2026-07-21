using System.Collections.Generic;

namespace Sts2DebtLoan;

/// <summary>
/// Localized strings for the Ledger relic + Debt card in every language the game ships (13 + English).
/// Keys are the game's 3-letter language codes (LocManager.Language). LocInjectionPatch picks the row for
/// the current language and merges it into the live "relics"/"cards" LocTables.
///
/// The relic description is a TEMPLATE: <c>{borrowed}</c> / <c>{paid}</c> are filled per-relic from that
/// relic's own DynamicVars (see DebtLoanRelic.CanonicalVars), so two co-op players each see their own
/// numbers. The card description keeps <c>{Gold:diff()}</c> (the card's GoldVar) and the <c>[gold]…[/gold]</c>
/// BBCode markup verbatim — only the surrounding words are translated.
/// </summary>
internal static class DebtLoanLoc
{
    internal readonly struct Row
    {
        public readonly string RelicTitle, RelicDesc, RelicFlavor, CardTitle, CardDesc;
        public Row(string relicTitle, string relicDesc, string relicFlavor, string cardTitle, string cardDesc)
        { RelicTitle = relicTitle; RelicDesc = relicDesc; RelicFlavor = relicFlavor; CardTitle = cardTitle; CardDesc = cardDesc; }
    }

    internal static Row For(string? lang)
        => lang != null && ByLang.TryGetValue(lang, out var r) ? r : ByLang["eng"];

    private static readonly Dictionary<string, Row> ByLang = new()
    {
        ["eng"] = new(
            "Merchant's Ledger",
            "Borrowed [gold]{borrowed} Gold[/gold]. Paid [gold]{paid} Gold[/gold] so far.\nRepay the debt at a shop to remove this relic.",
            "Every signature is a small surrender.",
            "Debt",
            "If this card is in your hand at the end of your turn, lose {Gold:diff()} [gold]Gold[/gold]."),

        ["deu"] = new(
            "Händlerbuch",
            "Geliehen: [gold]{borrowed} Gold[/gold]. Bisher zurückgezahlt: [gold]{paid} Gold[/gold].\nZahle die Schuld in einem Laden zurück, um dieses Relikt zu entfernen.",
            "Jede Unterschrift ist eine kleine Kapitulation.",
            "Schulden",
            "Wenn diese Karte am Ende deines Zuges auf deiner Hand ist, verliere {Gold:diff()} [gold]Gold[/gold]."),

        ["spa"] = new(
            "Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado hasta ahora: [gold]{paid} de oro[/gold].\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Deuda",
            "Si esta carta está en tu mano al final de tu turno, pierdes {Gold:diff()} [gold]de oro[/gold]."),

        ["esp"] = new(
            "Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado hasta ahora: [gold]{paid} de oro[/gold].\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Deuda",
            "Si esta carta está en tu mano al final de tu turno, pierdes {Gold:diff()} [gold]de oro[/gold]."),

        ["fra"] = new(
            "Grand livre du marchand",
            "Emprunté : [gold]{borrowed} or[/gold]. Remboursé jusqu'ici : [gold]{paid} or[/gold].\nRembourse la dette dans une boutique pour retirer cette relique.",
            "Chaque signature est une petite reddition.",
            "Dette",
            "Si cette carte est dans ta main à la fin de ton tour, perds {Gold:diff()} [gold]or[/gold]."),

        ["ita"] = new(
            "Registro del mercante",
            "Preso in prestito: [gold]{borrowed} Oro[/gold]. Pagato finora: [gold]{paid} Oro[/gold].\nSalda il debito in un negozio per rimuovere questo cimelio.",
            "Ogni firma è una piccola resa.",
            "Debito",
            "Se questa carta è nella tua mano alla fine del turno, perdi {Gold:diff()} [gold]Oro[/gold]."),

        ["jpn"] = new(
            "商人の元帳",
            "借入 [gold]{borrowed} ゴールド[/gold]。これまでの支払い [gold]{paid} ゴールド[/gold]。\nショップで借金を返済すると、このレリックは取り除かれる。",
            "署名はすべて、小さな降伏だ。",
            "借金",
            "ターン終了時にこのカードが手札にある場合、{Gold:diff()} [gold]ゴールド[/gold]を失う。"),

        ["kor"] = new(
            "상인의 장부",
            "빌린 금액 [gold]{borrowed} 골드[/gold]. 지금까지 지불 [gold]{paid} 골드[/gold].\n상점에서 빚을 갚으면 이 유물이 제거됩니다.",
            "모든 서명은 작은 항복이다.",
            "빚",
            "턴 종료 시 이 카드가 손에 있으면 {Gold:diff()} [gold]골드[/gold]를 잃습니다."),

        ["pol"] = new(
            "Księga kupca",
            "Pożyczono: [gold]{borrowed} złota[/gold]. Spłacono dotąd: [gold]{paid} złota[/gold].\nSpłać dług w sklepie, aby usunąć ten relikt.",
            "Każdy podpis to mała kapitulacja.",
            "Dług",
            "Jeśli ta karta jest w twojej ręce na koniec tury, tracisz {Gold:diff()} [gold]złota[/gold]."),

        ["ptb"] = new(
            "Livro-razão do mercador",
            "Emprestado: [gold]{borrowed} de Ouro[/gold]. Pago até agora: [gold]{paid} de Ouro[/gold].\nQuite a dívida em uma loja para remover esta relíquia.",
            "Cada assinatura é uma pequena rendição.",
            "Dívida",
            "Se esta carta estiver na sua mão no fim do seu turno, perca {Gold:diff()} [gold]de Ouro[/gold]."),

        ["rus"] = new(
            "Гроссбух торговца",
            "Взято в долг: [gold]{borrowed} золота[/gold]. Выплачено: [gold]{paid} золота[/gold].\nПогасите долг в магазине, чтобы убрать эту реликвию.",
            "Каждая подпись — маленькая капитуляция.",
            "Долг",
            "Если эта карта у вас в руке в конце хода, вы теряете {Gold:diff()} [gold]золота[/gold]."),

        ["tha"] = new(
            "บัญชีของพ่อค้า",
            "ยืมมา [gold]{borrowed} ทอง[/gold] จ่ายไปแล้ว [gold]{paid} ทอง[/gold]\nชำระหนี้ที่ร้านค้าเพื่อนำวัตถุโบราณนี้ออก",
            "ทุกลายเซ็นคือการยอมจำนนเล็กๆ",
            "หนี้",
            "หากการ์ดนี้อยู่ในมือของคุณเมื่อจบเทิร์น จะสูญเสีย {Gold:diff()} [gold]ทอง[/gold]"),

        ["tur"] = new(
            "Tüccarın Defteri",
            "Borç alınan: [gold]{borrowed} Altın[/gold]. Şu ana kadar ödenen: [gold]{paid} Altın[/gold].\nBu kalıntıyı kaldırmak için borcu bir dükkânda öde.",
            "Her imza küçük bir teslimiyettir.",
            "Borç",
            "Bu kart turunun sonunda elindeyse, {Gold:diff()} [gold]Altın[/gold] kaybedersin."),

        ["zhs"] = new(
            "商人的账簿",
            "已借入 [gold]{borrowed} 金币[/gold]。已偿还 [gold]{paid} 金币[/gold]。\n在商店还清债务即可移除此遗物。",
            "每一个签名都是一次小小的屈服。",
            "债务",
            "若你的回合结束时此牌在手牌中，失去 {Gold:diff()} [gold]金币[/gold]。"),
    };
}
