using System.Collections.Generic;

namespace Sts2DebtLoan;

/// <summary>
/// Localized strings for the Ledger relic + the Debt curse cards (빚 독촉 / 연체 / 차압) in every language the
/// game ships (13 + English). Keys are the game's 3-letter language codes (LocManager.Language); LocInjection
/// Patch picks the row for the current language and merges it into the "relics"/"cards" LocTables.
///
/// The relic description is a per-relic template ({borrowed}/{paid}/{cards}). The Dunning card uses
/// {draw}/{play} (its DynamicVars, which switch to the '+' values when upgraded). [gold]…[/gold] BBCode and
/// the placeholders are kept verbatim — only the surrounding words are translated.
/// </summary>
internal static class DebtLoanLoc
{
    internal readonly struct Row
    {
        public readonly string RelicTitle, RelicDesc, RelicFlavor;
        public readonly string DunTitle, DunDesc;   // 빚 독촉 (Dunning)
        public readonly string DelTitle, DelDesc;   // 연체 (Delinquency)
        public readonly string SeiTitle, SeiDesc;   // 차압 (Seizure)
        public Row(string relicTitle, string relicDesc, string relicFlavor,
                   string dunTitle, string dunDesc, string delTitle, string delDesc, string seiTitle, string seiDesc)
        { RelicTitle = relicTitle; RelicDesc = relicDesc; RelicFlavor = relicFlavor;
          DunTitle = dunTitle; DunDesc = dunDesc; DelTitle = delTitle; DelDesc = delDesc; SeiTitle = seiTitle; SeiDesc = seiDesc; }
    }

    internal static Row For(string? lang)
        => lang != null && ByLang.TryGetValue(lang, out var r) ? r : ByLang["eng"];

    private const string RELIC_DESC_EN =
        "Borrowed [gold]{borrowed} Gold[/gold]. Paid [gold]{paid} Gold[/gold] so far.\nEach combat injects [b]{cards}[/b] Debt card(s) into your draw pile — up to 3 as time passes.\nRepay the debt at a shop to remove this relic.";

    private static readonly Dictionary<string, Row> ByLang = new()
    {
        ["eng"] = new("Merchant's Ledger", RELIC_DESC_EN, "Every signature is a small surrender.",
            "Dunning", "When drawn, lose [gold]{draw} Gold[/gold]. Ethereal. You may play it to lose [gold]{play} Gold[/gold] and pay off the loan faster.",
            "Delinquency", "While this is in your hand, enemy attacks deal 50% more damage.",
            "Seizure", "While this is in your hand, you can only play cards of the first type you play each turn."),

        ["kor"] = new("상인의 장부",
            "빌린 금액 [gold]{borrowed} 골드[/gold]. 지금까지 지불 [gold]{paid} 골드[/gold].\n전투마다 빚 카드 [b]{cards}[/b]장이 뽑을 더미에 주입됩니다 — 시간이 지나면 최대 3장.\n상점에서 빚을 갚으면 이 유물이 제거됩니다.",
            "모든 서명은 작은 항복이다.",
            "빚 독촉", "뽑을 때 [gold]{draw} 골드[/gold]를 잃습니다. 휘발. 플레이하면 [gold]{play} 골드[/gold]를 내고 빚을 더 빨리 갚습니다.",
            "연체", "이 카드가 손에 있는 동안, 적의 공격이 50% 더 큰 피해를 줍니다.",
            "차압", "이 카드가 손에 있는 동안, 이번 턴 처음 낸 카드 종류만 사용할 수 있습니다."),

        ["jpn"] = new("商人の元帳",
            "借入 [gold]{borrowed} ゴールド[/gold]。これまでの支払い [gold]{paid} ゴールド[/gold]。\n戦闘ごとに借金カードが[b]{cards}[/b]枚、ドロー山札に加わる — 時間経過で最大3枚。\nショップで借金を返済すると、このレリックは取り除かれる。",
            "署名はすべて、小さな降伏だ。",
            "督促", "ドロー時、[gold]{draw} ゴールド[/gold]を失う。エセリアル。プレイすると[gold]{play} ゴールド[/gold]を失い、借金をより早く返済する。",
            "延滞", "この手札にある間、敵の攻撃ダメージが50%増加する。",
            "差し押さえ", "この手札にある間、そのターン最初にプレイしたタイプのカードしかプレイできない。"),

        ["zhs"] = new("商人的账簿",
            "已借入 [gold]{borrowed} 金币[/gold]。已偿还 [gold]{paid} 金币[/gold]。\n每场战斗会将 [b]{cards}[/b] 张债务牌注入你的抽牌堆——随时间最多 3 张。\n在商店还清债务即可移除此遗物。",
            "每一个签名都是一次小小的屈服。",
            "催债", "抽到时，失去 [gold]{draw} 金币[/gold]。虚无。你可以打出它，失去 [gold]{play} 金币[/gold] 以更快偿还贷款。",
            "拖欠", "当此牌在你手牌中时，敌人的攻击造成的伤害提高 50%。",
            "扣押", "当此牌在你手牌中时，你每回合只能打出你本回合第一张打出的类型的牌。"),

        ["deu"] = new("Händlerbuch",
            "Geliehen: [gold]{borrowed} Gold[/gold]. Zurückgezahlt: [gold]{paid} Gold[/gold].\nJeder Kampf schleust [b]{cards}[/b] Schulden-Karte(n) in deinen Nachziehstapel — mit der Zeit bis zu 3.\nZahle die Schuld in einem Laden zurück, um dieses Relikt zu entfernen.",
            "Jede Unterschrift ist eine kleine Kapitulation.",
            "Mahnung", "Beim Ziehen verlierst du [gold]{draw} Gold[/gold]. Ätherisch. Du kannst sie spielen, um [gold]{play} Gold[/gold] zu verlieren und die Schuld schneller zu tilgen.",
            "Verzug", "Solange sie auf deiner Hand ist, richten gegnerische Angriffe 50% mehr Schaden an.",
            "Pfändung", "Solange sie auf deiner Hand ist, kannst du nur Karten des ersten Typs spielen, den du in dieser Runde spielst."),

        ["fra"] = new("Grand livre du marchand",
            "Emprunté : [gold]{borrowed} or[/gold]. Remboursé : [gold]{paid} or[/gold].\nChaque combat injecte [b]{cards}[/b] carte(s) Dette dans ta pioche — jusqu'à 3 avec le temps.\nRembourse la dette dans une boutique pour retirer cette relique.",
            "Chaque signature est une petite reddition.",
            "Relance", "À la pioche, perds [gold]{draw} or[/gold]. Éthérée. Tu peux la jouer pour perdre [gold]{play} or[/gold] et rembourser la dette plus vite.",
            "Défaut", "Tant qu'elle est dans ta main, les attaques ennemies infligent 50% de dégâts en plus.",
            "Saisie", "Tant qu'elle est dans ta main, tu ne peux jouer que des cartes du premier type que tu joues ce tour."),

        ["spa"] = new("Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nCada combate inyecta [b]{cards}[/b] carta(s) de Deuda en tu mazo de robo — hasta 3 con el tiempo.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Reclamación", "Al robarla, pierdes [gold]{draw} de oro[/gold]. Etérea. Puedes jugarla para perder [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno."),

        ["esp"] = new("Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nCada combate inyecta [b]{cards}[/b] carta(s) de Deuda en tu mazo de robo — hasta 3 con el tiempo.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Reclamación", "Al robarla, pierdes [gold]{draw} de oro[/gold]. Etérea. Puedes jugarla para perder [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno."),

        ["ita"] = new("Registro del mercante",
            "Preso in prestito: [gold]{borrowed} Oro[/gold]. Pagato: [gold]{paid} Oro[/gold].\nOgni combattimento inserisce [b]{cards}[/b] carta/e Debito nel tuo mazzo di pesca — fino a 3 col tempo.\nSalda il debito in un negozio per rimuovere questo cimelio.",
            "Ogni firma è una piccola resa.",
            "Sollecito", "Quando la peschi, perdi [gold]{draw} Oro[/gold]. Eterea. Puoi giocarla per perdere [gold]{play} Oro[/gold] e saldare il debito più in fretta.",
            "Morosità", "Finché è nella tua mano, gli attacchi nemici infliggono il 50% di danni in più.",
            "Pignoramento", "Finché è nella tua mano, puoi giocare solo carte del primo tipo che giochi ogni turno."),

        ["pol"] = new("Księga kupca",
            "Pożyczono: [gold]{borrowed} złota[/gold]. Spłacono: [gold]{paid} złota[/gold].\nKażda walka dodaje [b]{cards}[/b] kart(y) Długu do talii dobierania — z czasem do 3.\nSpłać dług w sklepie, aby usunąć ten relikt.",
            "Każdy podpis to mała kapitulacja.",
            "Ponaglenie", "Przy dobraniu tracisz [gold]{draw} złota[/gold]. Ulotna. Możesz ją zagrać, aby stracić [gold]{play} złota[/gold] i szybciej spłacić dług.",
            "Zaległość", "Gdy jest w twojej ręce, ataki wrogów zadają 50% więcej obrażeń.",
            "Zajęcie", "Gdy jest w twojej ręce, możesz grać tylko karty pierwszego typu zagranego w danej turze."),

        ["ptb"] = new("Livro-razão do mercador",
            "Emprestado: [gold]{borrowed} de Ouro[/gold]. Pago: [gold]{paid} de Ouro[/gold].\nCada combate injeta [b]{cards}[/b] carta(s) de Dívida no seu monte de compra — até 3 com o tempo.\nQuite a dívida em uma loja para remover esta relíquia.",
            "Cada assinatura é uma pequena rendição.",
            "Cobrança", "Ao comprar, perca [gold]{draw} de Ouro[/gold]. Etérea. Você pode jogá-la para perder [gold]{play} de Ouro[/gold] e quitar a dívida mais rápido.",
            "Inadimplência", "Enquanto estiver na sua mão, ataques inimigos causam 50% mais dano.",
            "Penhora", "Enquanto estiver na sua mão, você só pode jogar cartas do primeiro tipo que jogar no turno."),

        ["rus"] = new("Гроссбух торговца",
            "Взято в долг: [gold]{borrowed} золота[/gold]. Выплачено: [gold]{paid} золота[/gold].\nКаждый бой добавляет [b]{cards}[/b] карт(ы) Долга в колоду добора — со временем до 3.\nПогасите долг в магазине, чтобы убрать эту реликвию.",
            "Каждая подпись — маленькая капитуляция.",
            "Взыскание", "При взятии теряете [gold]{draw} золота[/gold]. Эфемерная. Можно разыграть, чтобы потерять [gold]{play} золота[/gold] и быстрее погасить долг.",
            "Просрочка", "Пока она в руке, атаки врагов наносят на 50% больше урона.",
            "Арест", "Пока она в руке, за ход вы можете разыгрывать только карты того типа, что разыграли первым."),

        ["tha"] = new("บัญชีของพ่อค้า",
            "ยืมมา [gold]{borrowed} ทอง[/gold] จ่ายไปแล้ว [gold]{paid} ทอง[/gold]\nการต่อสู้แต่ละครั้งจะใส่การ์ดหนี้ [b]{cards}[/b] ใบลงในกองจั่ว — สูงสุด 3 ใบเมื่อเวลาผ่านไป\nชำระหนี้ที่ร้านค้าเพื่อนำวัตถุโบราณนี้ออก",
            "ทุกลายเซ็นคือการยอมจำนนเล็กๆ",
            "ทวงหนี้", "เมื่อจั่ว เสีย [gold]{draw} ทอง[/gold] อีเทอเรียล เล่นได้เพื่อเสีย [gold]{play} ทอง[/gold] และชำระหนี้เร็วขึ้น",
            "ค้างชำระ", "ขณะอยู่ในมือ การโจมตีของศัตรูสร้างความเสียหายเพิ่ม 50%",
            "ยึดทรัพย์", "ขณะอยู่ในมือ คุณเล่นได้เฉพาะการ์ดประเภทแรกที่คุณเล่นในเทิร์นนั้น"),

        ["tur"] = new("Tüccarın Defteri",
            "Borç alınan: [gold]{borrowed} Altın[/gold]. Ödenen: [gold]{paid} Altın[/gold].\nHer savaş çekme destene [b]{cards}[/b] Borç kartı ekler — zamanla 3'e kadar.\nBu kalıntıyı kaldırmak için borcu bir dükkânda öde.",
            "Her imza küçük bir teslimiyettir.",
            "İhtar", "Çekildiğinde [gold]{draw} Altın[/gold] kaybedersin. Uçucu. Oynayarak [gold]{play} Altın[/gold] kaybedip borcu daha hızlı ödeyebilirsin.",
            "Temerrüt", "Elindeyken düşman saldırıları %50 daha fazla hasar verir.",
            "Haciz", "Elindeyken, o tur yalnızca oynadığın ilk türden kart oynayabilirsin."),
    };
}
