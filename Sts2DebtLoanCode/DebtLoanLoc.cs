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
            "Borrowed [gold]{borrowed} Gold[/gold]. Paid [gold]{paid} Gold[/gold] so far.\nEach combat injects [b]{cards}[/b] Debt card(s) into your draw pile — up to 3 as time passes.\nRepay the debt at a shop to remove this relic.",
            "Every signature is a small surrender.",
            "Debt",
            "If this card is in your hand at the end of your turn, lose {Gold:diff()} [gold]Gold[/gold]."),

        ["deu"] = new(
            "Händlerbuch",
            "Geliehen: [gold]{borrowed} Gold[/gold]. Bisher zurückgezahlt: [gold]{paid} Gold[/gold].\nJeder Kampf schleust [b]{cards}[/b] Schulden-Karte(n) in deinen Nachziehstapel — mit der Zeit bis zu 3.\nZahle die Schuld in einem Laden zurück, um dieses Relikt zu entfernen.",
            "Jede Unterschrift ist eine kleine Kapitulation.",
            "Schulden",
            "Wenn diese Karte am Ende deines Zuges auf deiner Hand ist, verliere {Gold:diff()} [gold]Gold[/gold]."),

        ["spa"] = new(
            "Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado hasta ahora: [gold]{paid} de oro[/gold].\nCada combate inyecta [b]{cards}[/b] carta(s) de Deuda en tu mazo de robo — hasta 3 con el tiempo.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Deuda",
            "Si esta carta está en tu mano al final de tu turno, pierdes {Gold:diff()} [gold]de oro[/gold]."),

        ["esp"] = new(
            "Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado hasta ahora: [gold]{paid} de oro[/gold].\nCada combate inyecta [b]{cards}[/b] carta(s) de Deuda en tu mazo de robo — hasta 3 con el tiempo.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Deuda",
            "Si esta carta está en tu mano al final de tu turno, pierdes {Gold:diff()} [gold]de oro[/gold]."),

        ["fra"] = new(
            "Grand livre du marchand",
            "Emprunté : [gold]{borrowed} or[/gold]. Remboursé jusqu'ici : [gold]{paid} or[/gold].\nChaque combat injecte [b]{cards}[/b] carte(s) Dette dans ta pioche — jusqu'à 3 avec le temps.\nRembourse la dette dans une boutique pour retirer cette relique.",
            "Chaque signature est une petite reddition.",
            "Dette",
            "Si cette carte est dans ta main à la fin de ton tour, perds {Gold:diff()} [gold]or[/gold]."),

        ["ita"] = new(
            "Registro del mercante",
            "Preso in prestito: [gold]{borrowed} Oro[/gold]. Pagato finora: [gold]{paid} Oro[/gold].\nOgni combattimento inserisce [b]{cards}[/b] carta/e Debito nel tuo mazzo di pesca — fino a 3 col tempo.\nSalda il debito in un negozio per rimuovere questo cimelio.",
            "Ogni firma è una piccola resa.",
            "Debito",
            "Se questa carta è nella tua mano alla fine del turno, perdi {Gold:diff()} [gold]Oro[/gold]."),

        ["jpn"] = new(
            "商人の元帳",
            "借入 [gold]{borrowed} ゴールド[/gold]。これまでの支払い [gold]{paid} ゴールド[/gold]。\n戦闘ごとに借金カードが[b]{cards}[/b]枚、ドロー山札に加わる — 時間経過で最大3枚。\nショップで借金を返済すると、このレリックは取り除かれる。",
            "署名はすべて、小さな降伏だ。",
            "借金",
            "ターン終了時にこのカードが手札にある場合、{Gold:diff()} [gold]ゴールド[/gold]を失う。"),

        ["kor"] = new(
            "상인의 장부",
            "빌린 금액 [gold]{borrowed} 골드[/gold]. 지금까지 지불 [gold]{paid} 골드[/gold].\n전투마다 빚 카드 [b]{cards}[/b]장이 뽑을 더미에 주입됩니다 — 시간이 지나면 최대 3장.\n상점에서 빚을 갚으면 이 유물이 제거됩니다.",
            "모든 서명은 작은 항복이다.",
            "빚",
            "턴 종료 시 이 카드가 손에 있으면 {Gold:diff()} [gold]골드[/gold]를 잃습니다."),

        ["pol"] = new(
            "Księga kupca",
            "Pożyczono: [gold]{borrowed} złota[/gold]. Spłacono dotąd: [gold]{paid} złota[/gold].\nKażda walka dodaje [b]{cards}[/b] kart(y) Długu do twojej talii dobierania — z czasem do 3.\nSpłać dług w sklepie, aby usunąć ten relikt.",
            "Każdy podpis to mała kapitulacja.",
            "Dług",
            "Jeśli ta karta jest w twojej ręce na koniec tury, tracisz {Gold:diff()} [gold]złota[/gold]."),

        ["ptb"] = new(
            "Livro-razão do mercador",
            "Emprestado: [gold]{borrowed} de Ouro[/gold]. Pago até agora: [gold]{paid} de Ouro[/gold].\nCada combate injeta [b]{cards}[/b] carta(s) de Dívida no seu monte de compra — até 3 com o tempo.\nQuite a dívida em uma loja para remover esta relíquia.",
            "Cada assinatura é uma pequena rendição.",
            "Dívida",
            "Se esta carta estiver na sua mão no fim do seu turno, perca {Gold:diff()} [gold]de Ouro[/gold]."),

        ["rus"] = new(
            "Гроссбух торговца",
            "Взято в долг: [gold]{borrowed} золота[/gold]. Выплачено: [gold]{paid} золота[/gold].\nКаждый бой добавляет [b]{cards}[/b] карт(ы) Долга в вашу колоду добора — со временем до 3.\nПогасите долг в магазине, чтобы убрать эту реликвию.",
            "Каждая подпись — маленькая капитуляция.",
            "Долг",
            "Если эта карта у вас в руке в конце хода, вы теряете {Gold:diff()} [gold]золота[/gold]."),

        ["tha"] = new(
            "บัญชีของพ่อค้า",
            "ยืมมา [gold]{borrowed} ทอง[/gold] จ่ายไปแล้ว [gold]{paid} ทอง[/gold]\nการต่อสู้แต่ละครั้งจะใส่การ์ดหนี้ [b]{cards}[/b] ใบลงในกองจั่ว — สูงสุด 3 ใบเมื่อเวลาผ่านไป\nชำระหนี้ที่ร้านค้าเพื่อนำวัตถุโบราณนี้ออก",
            "ทุกลายเซ็นคือการยอมจำนนเล็กๆ",
            "หนี้",
            "หากการ์ดนี้อยู่ในมือของคุณเมื่อจบเทิร์น จะสูญเสีย {Gold:diff()} [gold]ทอง[/gold]"),

        ["tur"] = new(
            "Tüccarın Defteri",
            "Borç alınan: [gold]{borrowed} Altın[/gold]. Şu ana kadar ödenen: [gold]{paid} Altın[/gold].\nHer savaş çekme destene [b]{cards}[/b] Borç kartı ekler — zamanla 3'e kadar.\nBu kalıntıyı kaldırmak için borcu bir dükkânda öde.",
            "Her imza küçük bir teslimiyettir.",
            "Borç",
            "Bu kart turunun sonunda elindeyse, {Gold:diff()} [gold]Altın[/gold] kaybedersin."),

        ["zhs"] = new(
            "商人的账簿",
            "已借入 [gold]{borrowed} 金币[/gold]。已偿还 [gold]{paid} 金币[/gold]。\n每场战斗会将 [b]{cards}[/b] 张债务牌注入你的抽牌堆——随时间最多 3 张。\n在商店还清债务即可移除此遗物。",
            "每一个签名都是一次小小的屈服。",
            "债务",
            "若你的回合结束时此牌在手牌中，失去 {Gold:diff()} [gold]金币[/gold]。"),
    };
}
