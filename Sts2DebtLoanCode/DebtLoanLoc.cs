using System.Collections.Generic;

namespace Sts2DebtLoan;

/// <summary>
/// Localized strings for the Ledger relic + the Debt curse cards (빚 독촉 / 연체 / 차압 / 신용 불량 / 강제 징수)
/// in every language the game ships (13 + English). Keys are the game's 3-letter language codes; LocInjection
/// Patch picks the row for the current language and merges it into the "relics"/"cards" LocTables.
///
/// The relic description is a per-relic template ({owed}/{paid}) whose middle line uses SmartFormat's
/// choose() on {cards} (the current tier 1..4) to list the CUMULATIVE set of curses injected at that tier —
/// so the description grows as the debt escalates. Dunning uses {play}; the Forced Collection
/// uses {hp}/{principal}. [gold]…[/gold] / [b]…[/b] BBCode + the placeholders + the choose(...) syntax are
/// kept verbatim — only the surrounding words are translated.
/// </summary>
internal static class DebtLoanLoc
{
    internal readonly struct Row
    {
        public readonly string RelicTitle, RelicDesc, RelicFlavor;
        public readonly string DunTitle, DunDesc;   // 빚 독촉 (Dunning)
        public readonly string DelTitle, DelDesc;   // 연체 (Delinquency)
        public readonly string SeiTitle, SeiDesc;   // 차압 (Seizure)
        public readonly string BcTitle, BcDesc;     // 신용 불량 (Bad Credit)
        public readonly string FcTitle, FcDesc;     // 강제 징수 (Forced Collection)
        public Row(string relicTitle, string relicDesc, string relicFlavor,
                   string dunTitle, string dunDesc, string delTitle, string delDesc, string seiTitle, string seiDesc,
                   string bcTitle, string bcDesc, string fcTitle, string fcDesc)
        { RelicTitle = relicTitle; RelicDesc = relicDesc; RelicFlavor = relicFlavor;
          DunTitle = dunTitle; DunDesc = dunDesc; DelTitle = delTitle; DelDesc = delDesc; SeiTitle = seiTitle; SeiDesc = seiDesc;
          BcTitle = bcTitle; BcDesc = bcDesc; FcTitle = fcTitle; FcDesc = fcDesc; }
    }

    internal static Row For(string? lang)
        => lang != null && ByLang.TryGetValue(lang, out var r) ? r : ByLang["eng"];

    private static readonly Dictionary<string, Row> ByLang = new()
    {
        ["eng"] = new("Merchant's Ledger",
            "Owed [gold]{owed} Gold[/gold]. Paid [gold]{paid} Gold[/gold] so far.\nInjected each combat: [b]{cards:choose(1|2|3|4):Payment|Payment, Delinquency|Payment, Delinquency, Seizure|Payment, Delinquency, Seizure, Bad Credit|Payment}[/b]. More curses pile on the longer you owe.\nRepay the debt at a shop to remove this relic.",
            "Every signature is a small surrender.",
            "Payment", "Play it to make a [gold]{play} Gold[/gold] [gold]Payment[/gold].",
            "Delinquency", "While this is in your hand, enemy attacks deal 50% more damage.",
            "Seizure", "While this is in your hand, you can only play cards of the first type you play each turn.",
            "Bad Credit", "The instant it reaches your hand you gain [gold]Bad Credit[/gold], then vanishes — a [gold]Forced Collection[/gold] card is added to your hand each turn, growing stronger every 3rd turn.",
            "Forced Collection", "At the end of your turn, lose [b]{hp}[/b] HP and repay [gold]{principal} Gold[/gold] of principal, then Exhaust."),

        ["kor"] = new("상인의 장부",
            "남은 상환액 [gold]{owed} 골드[/gold]. 지금까지 지불 [gold]{paid} 골드[/gold].\n전투마다 주입되는 저주: [b]{cards:choose(1|2|3|4):납부|납부, 연체|납부, 연체, 차압|납부, 연체, 차압, 신용 불량|납부}[/b]. 오래 갚지 않을수록 늘어납니다.\n상점에서 빚을 갚으면 이 유물이 제거됩니다.",
            "모든 서명은 작은 항복이다.",
            "납부", "플레이하여 [gold]{play} 골드[/gold]를 [gold]납부[/gold]합니다.",
            "연체", "이 카드가 손에 있는 동안, 적의 공격이 50% 더 큰 피해를 줍니다.",
            "차압", "이 카드가 손에 있는 동안, 이번 턴 처음 낸 카드 종류만 사용할 수 있습니다.",
            "신용 불량", "손에 들어오는 즉시 [gold]신용 불량[/gold] 상태가 되고 사라집니다 — 이후 매 턴 [gold]강제 징수[/gold] 카드가 손에 추가되며, 3턴마다 강해집니다.",
            "강제 징수", "턴 종료 시, 체력을 [b]{hp}[/b] 잃고 원금 [gold]{principal} 골드[/gold]를 상환한 뒤 소멸합니다."),

        ["jpn"] = new("商人の元帳",
            "残債 [gold]{owed} ゴールド[/gold]。これまでの支払い [gold]{paid} ゴールド[/gold]。\n戦闘ごとに加わる呪い: [b]{cards:choose(1|2|3|4):支払い|支払い・延滞|支払い・延滞・差し押さえ|支払い・延滞・差し押さえ・信用不良|支払い}[/b]。返済が遅れるほど増える。\nショップで借金を返済すると、このレリックは取り除かれる。",
            "署名はすべて、小さな降伏だ。",
            "支払い", "プレイして[gold]{play} ゴールド[/gold]を支払い、借金をより早く返済する。",
            "延滞", "この手札にある間、敵の攻撃ダメージが50%増加する。",
            "差し押さえ", "この手札にある間、そのターン最初にプレイしたタイプのカードしかプレイできない。",
            "信用不良", "手札に入った瞬間[gold]信用不良[/gold]になって消滅する — 以降毎ターン[gold]強制徴収[/gold]カードが手札に加わり、3ターンごとに強くなる。",
            "強制徴収", "ターン終了時、体力を[b]{hp}[/b]失い、元金[gold]{principal} ゴールド[/gold]を返済してから廃棄。"),

        ["zhs"] = new("商人的账簿",
            "待还 [gold]{owed} 金币[/gold]。已偿还 [gold]{paid} 金币[/gold]。\n每场战斗注入的诅咒：[b]{cards:choose(1|2|3|4):还款|还款、拖欠|还款、拖欠、扣押|还款、拖欠、扣押、信用不良|还款}[/b]。拖欠越久，诅咒越多。\n在商店还清债务即可移除此遗物。",
            "每一个签名都是一次小小的屈服。",
            "还款", "打出它，支付 [gold]{play} 金币[/gold]，更快偿还贷款。",
            "拖欠", "当此牌在你手牌中时，敌人的攻击造成的伤害提高 50%。",
            "扣押", "当此牌在你手牌中时，你每回合只能打出你本回合第一张打出的类型的牌。",
            "信用不良", "进入手牌的瞬间获得[gold]信用不良[/gold]并消失——此后每回合将一张[gold]强制征收[/gold]牌加入手牌，每3回合变强。",
            "强制征收", "回合结束时，失去 [b]{hp}[/b] 点生命并偿还 [gold]{principal} 金币[/gold] 本金，然后消耗。"),

        ["deu"] = new("Händlerbuch",
            "Offen: [gold]{owed} Gold[/gold]. Zurückgezahlt: [gold]{paid} Gold[/gold].\nJeder Kampf schleust ein: [b]{cards:choose(1|2|3|4):Zahlung|Zahlung, Verzug|Zahlung, Verzug, Pfändung|Zahlung, Verzug, Pfändung, Zahlungsunfähig|Zahlung}[/b]. Je länger du schuldest, desto mehr Flüche.\nZahle die Schuld in einem Laden zurück, um dieses Relikt zu entfernen.",
            "Jede Unterschrift ist eine kleine Kapitulation.",
            "Zahlung", "Spiele sie, um [gold]{play} Gold[/gold] zu zahlen und die Schuld schneller zu tilgen.",
            "Verzug", "Solange sie auf deiner Hand ist, richten gegnerische Angriffe 50% mehr Schaden an.",
            "Pfändung", "Solange sie auf deiner Hand ist, kannst du nur Karten des ersten Typs spielen, den du in dieser Runde spielst.",
            "Zahlungsunfähig", "Sobald sie auf deine Hand kommt, erhältst du [gold]Zahlungsunfähig[/gold] und sie verschwindet — danach kommt jede Runde eine [gold]Zwangseinziehung[/gold]-Karte auf deine Hand, alle 3 Runden stärker.",
            "Zwangseinziehung", "Am Ende deiner Runde verlierst du [b]{hp}[/b] LP und tilgst [gold]{principal} Gold[/gold] der Schuld, dann verbraucht."),

        ["fra"] = new("Grand livre du marchand",
            "Dû : [gold]{owed} or[/gold]. Remboursé : [gold]{paid} or[/gold].\nInjecté à chaque combat : [b]{cards:choose(1|2|3|4):Paiement|Paiement, Défaut|Paiement, Défaut, Saisie|Paiement, Défaut, Saisie, Insolvabilité|Paiement}[/b]. Plus tu tardes, plus il y a de malédictions.\nRembourse la dette dans une boutique pour retirer cette relique.",
            "Chaque signature est une petite reddition.",
            "Paiement", "Joue-la pour payer [gold]{play} or[/gold] et rembourser la dette plus vite.",
            "Défaut", "Tant qu'elle est dans ta main, les attaques ennemies infligent 50% de dégâts en plus.",
            "Saisie", "Tant qu'elle est dans ta main, tu ne peux jouer que des cartes du premier type que tu joues ce tour.",
            "Insolvabilité", "Dès qu'elle arrive en main, tu gagnes [gold]Insolvabilité[/gold] et elle disparaît — ensuite une carte [gold]Saisie forcée[/gold] est ajoutée à ta main chaque tour, plus forte tous les 3 tours.",
            "Saisie forcée", "À la fin de ton tour, perds [b]{hp}[/b] PV et rembourse [gold]{principal} or[/gold] de la dette, puis Épuise."),

        ["spa"] = new("Libro del mercader",
            "Pendiente: [gold]{owed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nInyectado cada combate: [b]{cards:choose(1|2|3|4):Pago|Pago, Morosidad|Pago, Morosidad, Embargo|Pago, Morosidad, Embargo, Insolvencia|Pago}[/b]. Cuanto más debas, más maldiciones.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Pago", "Juégala para pagar [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno.",
            "Insolvencia", "En cuanto llega a tu mano obtienes [gold]Insolvencia[/gold] y desaparece — después cada turno se añade una carta de [gold]Embargo forzoso[/gold] a tu mano, más fuerte cada 3 turnos.",
            "Embargo forzoso", "Al final de tu turno, pierde [b]{hp}[/b] de vida y salda [gold]{principal} de oro[/gold] de la deuda; luego Agota."),

        ["esp"] = new("Libro del mercader",
            "Pendiente: [gold]{owed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nInyectado cada combate: [b]{cards:choose(1|2|3|4):Pago|Pago, Morosidad|Pago, Morosidad, Embargo|Pago, Morosidad, Embargo, Insolvencia|Pago}[/b]. Cuanto más debas, más maldiciones.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Pago", "Juégala para pagar [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno.",
            "Insolvencia", "En cuanto llega a tu mano obtienes [gold]Insolvencia[/gold] y desaparece — después cada turno se añade una carta de [gold]Embargo forzoso[/gold] a tu mano, más fuerte cada 3 turnos.",
            "Embargo forzoso", "Al final de tu turno, pierde [b]{hp}[/b] de vida y salda [gold]{principal} de oro[/gold] de la deuda; luego Agota."),

        ["ita"] = new("Registro del mercante",
            "Dovuto: [gold]{owed} Oro[/gold]. Pagato: [gold]{paid} Oro[/gold].\nInserito ogni combattimento: [b]{cards:choose(1|2|3|4):Pagamento|Pagamento, Morosità|Pagamento, Morosità, Pignoramento|Pagamento, Morosità, Pignoramento, Insolvenza|Pagamento}[/b]. Più tardi, più maledizioni.\nSalda il debito in un negozio per rimuovere questo cimelio.",
            "Ogni firma è una piccola resa.",
            "Pagamento", "Giocala per pagare [gold]{play} Oro[/gold] e saldare il debito più in fretta.",
            "Morosità", "Finché è nella tua mano, gli attacchi nemici infliggono il 50% di danni in più.",
            "Pignoramento", "Finché è nella tua mano, puoi giocare solo carte del primo tipo che giochi ogni turno.",
            "Insolvenza", "Appena arriva in mano ottieni [gold]Insolvenza[/gold] e svanisce — poi ogni turno una carta [gold]Riscossione forzata[/gold] viene aggiunta alla tua mano, più forte ogni 3 turni.",
            "Riscossione forzata", "Alla fine del turno, perdi [b]{hp}[/b] PV e ripaghi [gold]{principal} Oro[/gold] di debito, poi Consuma."),

        ["pol"] = new("Księga kupca",
            "Do spłaty: [gold]{owed} złota[/gold]. Spłacono: [gold]{paid} złota[/gold].\nDodawane w każdej walce: [b]{cards:choose(1|2|3|4):Spłata|Spłata, Zaległość|Spłata, Zaległość, Zajęcie|Spłata, Zaległość, Zajęcie, Niewypłacalność|Spłata}[/b]. Im dłużej zwlekasz, tym więcej klątw.\nSpłać dług w sklepie, aby usunąć ten relikt.",
            "Każdy podpis to mała kapitulacja.",
            "Spłata", "Zagraj ją, aby zapłacić [gold]{play} złota[/gold] i szybciej spłacić dług.",
            "Zaległość", "Gdy jest w twojej ręce, ataki wrogów zadają 50% więcej obrażeń.",
            "Zajęcie", "Gdy jest w twojej ręce, możesz grać tylko karty pierwszego typu zagranego w danej turze.",
            "Niewypłacalność", "Gdy trafi do ręki, zyskujesz [gold]Niewypłacalność[/gold] i znika — potem co turę do ręki trafia karta [gold]Przymusowa egzekucja[/gold], silniejsza co 3 tury.",
            "Przymusowa egzekucja", "Na końcu tury tracisz [b]{hp}[/b] PŻ i spłacasz [gold]{principal} złota[/gold] długu, potem Zużywa się."),

        ["ptb"] = new("Livro-razão do mercador",
            "Devido: [gold]{owed} de Ouro[/gold]. Pago: [gold]{paid} de Ouro[/gold].\nInjetado a cada combate: [b]{cards:choose(1|2|3|4):Pagamento|Pagamento, Inadimplência|Pagamento, Inadimplência, Penhora|Pagamento, Inadimplência, Penhora, Crédito Ruim|Pagamento}[/b]. Quanto mais você deve, mais maldições.\nQuite a dívida em uma loja para remover esta relíquia.",
            "Cada assinatura é uma pequena rendição.",
            "Pagamento", "Jogue-a para pagar [gold]{play} de Ouro[/gold] e quitar a dívida mais rápido.",
            "Inadimplência", "Enquanto estiver na sua mão, ataques inimigos causam 50% mais dano.",
            "Penhora", "Enquanto estiver na sua mão, você só pode jogar cartas do primeiro tipo que jogar no turno.",
            "Crédito Ruim", "Assim que chega à sua mão você ganha [gold]Crédito Ruim[/gold] e ela some — depois uma carta de [gold]Cobrança Forçada[/gold] é adicionada à sua mão a cada turno, mais forte a cada 3 turnos.",
            "Cobrança Forçada", "No fim do seu turno, perca [b]{hp}[/b] de Vida e quite [gold]{principal} de Ouro[/gold] da dívida, então Exaure."),

        ["rus"] = new("Гроссбух торговца",
            "К оплате: [gold]{owed} золота[/gold]. Выплачено: [gold]{paid} золота[/gold].\nДобавляется каждый бой: [b]{cards:choose(1|2|3|4):Платёж|Платёж, Просрочка|Платёж, Просрочка, Арест|Платёж, Просрочка, Арест, Неплатёжеспособность|Платёж}[/b]. Чем дольше долг, тем больше проклятий.\nПогасите долг в магазине, чтобы убрать эту реликвию.",
            "Каждая подпись — маленькая капитуляция.",
            "Платёж", "Разыграйте её, чтобы заплатить [gold]{play} золота[/gold] и быстрее погасить долг.",
            "Просрочка", "Пока она в руке, атаки врагов наносят на 50% больше урона.",
            "Арест", "Пока она в руке, за ход вы можете разыгрывать только карты того типа, что разыграли первым.",
            "Неплатёжеспособность", "Как только попадает в руку, вы получаете [gold]Неплатёжеспособность[/gold] и она исчезает — затем каждый ход в руку добавляется карта [gold]Принудительное взыскание[/gold], усиливаясь каждый 3-й ход.",
            "Принудительное взыскание", "В конце хода теряете [b]{hp}[/b] здоровья и гасите [gold]{principal} золота[/gold] долга, затем Истощается."),

        ["tha"] = new("บัญชีของพ่อค้า",
            "ค้างชำระ [gold]{owed} ทอง[/gold] จ่ายไปแล้ว [gold]{paid} ทอง[/gold]\nใส่ทุกการต่อสู้: [b]{cards:choose(1|2|3|4):การชำระ|การชำระ, ค้างชำระ|การชำระ, ค้างชำระ, ยึดทรัพย์|การชำระ, ค้างชำระ, ยึดทรัพย์, เครดิตเสีย|การชำระ}[/b] ยิ่งค้างนานยิ่งมากขึ้น\nชำระหนี้ที่ร้านค้าเพื่อนำวัตถุโบราณนี้ออก",
            "ทุกลายเซ็นคือการยอมจำนนเล็กๆ",
            "การชำระ", "เล่นเพื่อจ่าย [gold]{play} ทอง[/gold] และชำระหนี้เร็วขึ้น",
            "ค้างชำระ", "ขณะอยู่ในมือ การโจมตีของศัตรูสร้างความเสียหายเพิ่ม 50%",
            "ยึดทรัพย์", "ขณะอยู่ในมือ คุณเล่นได้เฉพาะการ์ดประเภทแรกที่คุณเล่นในเทิร์นนั้น",
            "เครดิตเสีย", "ทันทีที่เข้ามือ คุณจะได้รับ[gold]เครดิตเสีย[/gold]แล้วหายไป — จากนั้นทุกเทิร์นจะเพิ่มการ์ด[gold]บังคับเก็บหนี้[/gold]เข้ามือ แข็งแกร่งขึ้นทุก 3 เทิร์น",
            "บังคับเก็บหนี้", "เมื่อจบเทิร์น เสียพลังชีวิต [b]{hp}[/b] และชำระเงินต้น [gold]{principal} ทอง[/gold] จากนั้นเผาไหม้"),

        ["tur"] = new("Tüccarın Defteri",
            "Kalan: [gold]{owed} Altın[/gold]. Ödenen: [gold]{paid} Altın[/gold].\nHer savaş eklenir: [b]{cards:choose(1|2|3|4):Ödeme|Ödeme, Temerrüt|Ödeme, Temerrüt, Haciz|Ödeme, Temerrüt, Haciz, Kredi İflası|Ödeme}[/b]. Borç uzadıkça daha fazla lanet.\nBu kalıntıyı kaldırmak için borcu bir dükkânda öde.",
            "Her imza küçük bir teslimiyettir.",
            "Ödeme", "Oyna: [gold]{play} Altın[/gold] öde ve borcu daha hızlı kapat.",
            "Temerrüt", "Elindeyken düşman saldırıları %50 daha fazla hasar verir.",
            "Haciz", "Elindeyken, o tur yalnızca oynadığın ilk türden kart oynayabilirsin.",
            "Kredi İflası", "Eline geldiği anda [gold]Kredi İflası[/gold] kazanır ve kaybolur — sonra her tur eline bir [gold]Zorla Tahsilat[/gold] kartı eklenir, her 3 turda güçlenir.",
            "Zorla Tahsilat", "Turunun sonunda [b]{hp}[/b] Can kaybeder ve [gold]{principal} Altın[/gold] anapara ödersin, sonra Tükenir."),
    };

    // ── 독촉장 (Dunning Letter) leverage card + power ─────────────────────────────
    // Kept in a SEPARATE table (not the Row) so languages can be filled without touching the Row constructor;
    // a missing language falls back to English. The "{card}" placeholder is filled from the card's own
    // DynamicVars (DunningLetterCard.CanonicalVars) with the local name of the 빚 독촉 (Dunning) card. (The old
    // "gain {plate} Plating" line was dropped from every language — the Plating payoff lives on the 빚 독촉 play,
    // not on this card's text.)
    internal readonly struct DlRow
    {
        public readonly string Title, CardDesc, PowerDesc;
        public DlRow(string title, string cardDesc, string powerDesc)
        { Title = title; CardDesc = cardDesc; PowerDesc = powerDesc; }
    }

    internal static DlRow DunningLetterFor(string? lang)
        => lang != null && DlByLang.TryGetValue(lang, out var r) ? r : DlByLang["eng"];

    private static readonly Dictionary<string, DlRow> DlByLang = new()
    {
        ["eng"] = new("Standing Order",
            "At the start of each of your turns, add a [gold]{card}[/gold] card to your hand.",
            "At the start of each of your turns, add a Payment card to your hand."),
        ["kor"] = new("정기 납부",
            "매 턴 시작 시 [gold]{card}[/gold]을 손에 넣습니다.",
            "매 턴 시작 시 납부를 손에 넣습니다."),
        ["jpn"] = new("定期支払い",
            "各ターン開始時、[gold]{card}[/gold]カードを手札に加える。",
            "各ターン開始時、支払いカードを手札に加える。"),
        ["zhs"] = new("定期还款",
            "每个回合开始时，将一张[gold]{card}[/gold]牌加入手牌。",
            "每个回合开始时，将一张还款牌加入手牌。"),
        ["deu"] = new("Dauerauftrag",
            "Zu Beginn jeder deiner Runden lege eine [gold]{card}[/gold]-Karte auf deine Hand.",
            "Zu Beginn jeder deiner Runden lege eine Zahlung-Karte auf deine Hand."),
        ["fra"] = new("Prélèvement permanent",
            "Au début de chacun de tes tours, ajoute une carte [gold]{card}[/gold] à ta main.",
            "Au début de chacun de tes tours, ajoute une carte Paiement à ta main."),
        ["spa"] = new("Pago periódico",
            "Al inicio de cada uno de tus turnos, añade una carta de [gold]{card}[/gold] a tu mano.",
            "Al inicio de cada uno de tus turnos, añade una carta de Pago a tu mano."),
        ["esp"] = new("Pago periódico",
            "Al inicio de cada uno de tus turnos, añade una carta de [gold]{card}[/gold] a tu mano.",
            "Al inicio de cada uno de tus turnos, añade una carta de Pago a tu mano."),
        ["ita"] = new("Pagamento ricorrente",
            "All'inizio di ogni tuo turno, aggiungi una carta [gold]{card}[/gold] alla tua mano.",
            "All'inizio di ogni tuo turno, aggiungi una carta Pagamento alla tua mano."),
        ["pol"] = new("Zlecenie stałe",
            "Na początku każdej twojej tury dodaj kartę [gold]{card}[/gold] do ręki.",
            "Na początku każdej twojej tury dodaj kartę Spłata do ręki."),
        ["ptb"] = new("Pagamento recorrente",
            "No início de cada um dos seus turnos, adicione uma carta de [gold]{card}[/gold] à sua mão.",
            "No início de cada um dos seus turnos, adicione uma carta de Pagamento à sua mão."),
        ["rus"] = new("Регулярный платёж",
            "В начале каждого вашего хода добавьте карту [gold]{card}[/gold] в руку.",
            "В начале каждого вашего хода добавьте карту Платёж в руку."),
        ["tha"] = new("การชำระประจำ",
            "เมื่อเริ่มแต่ละเทิร์นของคุณ เพิ่มการ์ด[gold]{card}[/gold]เข้ามือ",
            "เมื่อเริ่มแต่ละเทิร์นของคุณ เพิ่มการ์ดการชำระเข้ามือ"),
        ["tur"] = new("Düzenli Ödeme",
            "Her turunun başında eline bir [gold]{card}[/gold] kartı ekle.",
            "Her turunun başında eline bir Ödeme kartı ekle."),
    };

    // ── 납부 (Payment) tooltip: what happens to the gold a Debt card takes ─────────────────────────────
    // Shown on the 빚 독촉 hover (custom HoverTip). English fallback for unfilled languages. TODO: 12 more.
    internal readonly struct PayRow { public readonly string Title, Desc; public PayRow(string t, string d) { Title = t; Desc = d; } }

    internal static PayRow PaymentFor(string? lang)
        => lang != null && PayByLang.TryGetValue(lang, out var r) ? r : PayByLang["eng"];

    private static readonly Dictionary<string, PayRow> PayByLang = new()
    {
        ["eng"] = new("Payment", "All the gold you pay goes toward the loan's [b]principal[/b] — the [b]interest[/b] is the up-front 50% surcharge. Paying down the principal shrinks the shop repay cost."),
        ["kor"] = new("납부", "납부한 골드 전액이 대출 [b]원금[/b] 상환에 쓰입니다 — [b]이자[/b]는 대출 시 붙는 50% 할증입니다. 원금을 갚을수록 상점 상환액이 줄어듭니다."),
        ["jpn"] = new("支払い", "支払ったゴールドは全額が借金の[b]元金[/b]の返済に充てられる — [b]利息[/b]は借入時の50%割増だ。元金を返すほど、ショップでの返済額が減る。"),
        ["zhs"] = new("还款", "所付金币全部用于偿还贷款[b]本金[/b]——[b]利息[/b]是借款时的50%附加费。偿还本金越多，商店的还款额越低。"),
        ["deu"] = new("Zahlung", "Das gesamte gezahlte Gold geht an den [b]Kapitalbetrag[/b] der Schuld — die [b]Zinsen[/b] sind der 50%-Aufschlag beim Leihen. Je mehr du tilgst, desto geringer die Rückzahlung im Laden."),
        ["fra"] = new("Paiement", "Tout l'or payé rembourse le [b]capital[/b] de la dette — les [b]intérêts[/b] sont la majoration de 50% à l'emprunt. Rembourser le capital réduit le coût de remboursement en boutique."),
        ["spa"] = new("Pago", "Todo el oro pagado se destina al [b]capital[/b] del préstamo — los [b]intereses[/b] son el recargo del 50% inicial. Amortizar el capital reduce el coste de pago en la tienda."),
        ["esp"] = new("Pago", "Todo el oro pagado se destina al [b]capital[/b] del préstamo — los [b]intereses[/b] son el recargo del 50% inicial. Amortizar el capital reduce el coste de pago en la tienda."),
        ["ita"] = new("Pagamento", "Tutto l'oro pagato va al [b]capitale[/b] del debito — gli [b]interessi[/b] sono la maggiorazione del 50% al prestito. Ridurre il capitale abbassa il costo di rimborso nel negozio."),
        ["pol"] = new("Spłata", "Całe zapłacone złoto idzie na [b]kapitał[/b] pożyczki — [b]odsetki[/b] to 50% dopłata przy pożyczaniu. Spłacanie kapitału zmniejsza koszt spłaty w sklepie."),
        ["ptb"] = new("Pagamento", "Todo o ouro pago vai para o [b]principal[/b] do empréstimo — os [b]juros[/b] são a sobretaxa de 50% inicial. Reduzir o principal diminui o custo de quitação na loja."),
        ["rus"] = new("Платёж", "Всё уплаченное золото идёт на [b]основной долг[/b] — [b]проценты[/b] это 50% надбавка при займе. Погашение основного долга снижает стоимость выплаты в магазине."),
        ["tha"] = new("การชำระ", "ทองที่จ่ายทั้งหมดใช้ชำระ[b]เงินต้น[/b]ของหนี้ — [b]ดอกเบี้ย[/b]คือค่าธรรมเนียม 50% ตอนกู้ ยิ่งชำระเงินต้นมาก ค่าชำระที่ร้านค้ายิ่งลดลง"),
        ["tur"] = new("Ödeme", "Ödenen altının tamamı borcun [b]anaparasına[/b] gider — [b]faiz[/b] borç alırken eklenen %50 ek ücrettir. Anaparayı azaltmak dükkândaki ödeme maliyetini düşürür."),
    };

    // ── Shop "Repay Loan" button hover tooltip (14 languages). {0} = outstanding principal (gold owed). The
    //    ledger name inside PayBack matches the relic's own localized name (see ByLang above).
    internal readonly struct RepayUiRow
    {
        public readonly string Title, PayBack, NotEnough, NoLoan;
        public RepayUiRow(string title, string payBack, string notEnough, string noLoan)
        { Title = title; PayBack = payBack; NotEnough = notEnough; NoLoan = noLoan; }
    }

    internal static RepayUiRow RepayUiFor(string? lang)
        => lang != null && RepayUiByLang.TryGetValue(lang, out var r) ? r : RepayUiByLang["eng"];

    private static readonly Dictionary<string, RepayUiRow> RepayUiByLang = new()
    {
        ["eng"] = new("Repay Loan", "Pay back {0} gold to retire the Merchant's Ledger and clear all Debt cards.", "Not enough gold — you owe {0}.", "No loan to repay."),
        ["kor"] = new("빚 갚기", "{0} 골드를 갚아 상인의 장부를 반납하고 모든 빚 카드를 제거합니다.", "골드가 부족합니다 — {0} 골드를 빚지고 있습니다.", "갚을 빚이 없습니다."),
        ["jpn"] = new("借金返済", "{0} ゴールドを返済して「商人の元帳」を手放し、すべての借金カードを取り除く。", "ゴールドが足りない — {0} の借りがある。", "返済する借金がない。"),
        ["zhs"] = new("偿还贷款", "偿还 {0} 金币以归还商人的账簿，并清除所有债务牌。", "金币不足——你欠 {0}。", "没有需要偿还的贷款。"),
        ["deu"] = new("Kredit zurückzahlen", "Zahle {0} Gold zurück, um das Händlerbuch abzugeben und alle Schuldkarten zu entfernen.", "Nicht genug Gold — du schuldest {0}.", "Kein Kredit zum Zurückzahlen."),
        ["fra"] = new("Rembourser le prêt", "Rembourse {0} or pour rendre le Grand livre du marchand et retirer toutes les cartes de Dette.", "Pas assez d'or — tu dois {0}.", "Aucun prêt à rembourser."),
        ["spa"] = new("Saldar préstamo", "Paga {0} de oro para devolver el Libro del mercader y eliminar todas las cartas de Deuda.", "No tienes suficiente oro — debes {0}.", "No hay préstamo que saldar."),
        ["esp"] = new("Saldar préstamo", "Paga {0} de oro para devolver el Libro del mercader y eliminar todas las cartas de Deuda.", "No tienes suficiente oro — debes {0}.", "No hay préstamo que saldar."),
        ["ita"] = new("Ripaga il prestito", "Ripaga {0} oro per restituire il Registro del mercante e rimuovere tutte le carte Debito.", "Oro insufficiente — devi {0}.", "Nessun prestito da ripagare."),
        ["pol"] = new("Spłać pożyczkę", "Spłać {0} złota, aby oddać Księgę kupca i usunąć wszystkie karty Długu.", "Za mało złota — jesteś winien {0}.", "Brak pożyczki do spłaty."),
        ["ptb"] = new("Quitar empréstimo", "Pague {0} de ouro para devolver o Livro-razão do mercador e remover todas as cartas de Dívida.", "Ouro insuficiente — você deve {0}.", "Nenhum empréstimo para quitar."),
        ["rus"] = new("Погасить заём", "Верните {0} золота, чтобы сдать Гроссбух торговца и убрать все карты Долга.", "Недостаточно золота — вы должны {0}.", "Нет займа для погашения."),
        ["tha"] = new("ชำระหนี้", "จ่ายคืน {0} ทองเพื่อคืนบัญชีของพ่อค้าและกำจัดการ์ดหนี้ทั้งหมด", "ทองไม่พอ — คุณติดหนี้ {0}", "ไม่มีหนี้ให้ชำระ"),
        ["tur"] = new("Krediyi Öde", "Tüccarın Defteri'ni iade etmek ve tüm Borç kartlarını kaldırmak için {0} altın öde.", "Yeterli altın yok — {0} borcun var.", "Ödenecek kredi yok."),
    };

    // ── New payment-set cards + powers (loc keys = ClassName → SCREAMING_SNAKE). EN + KO; English fallback for
    //    the other 12 languages (TODO). Cards → "cards" table, powers → "powers" table (LocInjectionPatch).
    internal static Dictionary<string, string> ExtraCardLoc(string? lang) => lang != null && _cardsByLang.TryGetValue(lang, out var d) ? d : _cardsEng;
    internal static Dictionary<string, string> ExtraPowerLoc(string? lang) => lang != null && _powersByLang.TryGetValue(lang, out var d) ? d : _powersEng;

    private static readonly Dictionary<string, string> _cardsEng = new()
    {
        ["DEBTOR_CARD.title"] = "Forced Collection",
        ["DEBTOR_CARD.description"] = "At the end of your turn, make a [gold]{gold} Gold[/gold] [gold]Payment[/gold] — or, if you can't afford it, lose [b]{hp}[/b] HP and pay in blood.",
        ["WAGES_CARD.title"] = "Wages",
        ["WAGES_CARD.description"] = "Gain [gold]{gold} Gold[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Job Placement",
        ["JOB_PLACEMENT_CARD.description"] = "Add [gold]{fee} Gold[/gold] to your debt.\nAdd a [gold]Wages[/gold] card to your hand now and at the start of each turn.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Payment Benefit",
        ["PAYMENT_BENEFIT_CARD.description"] = "Whenever you make a [gold]Payment[/gold], gain [b]{plate}[/b] [gold]Plating[/gold].",
        ["REFUND_CARD.title"] = "Refund",
        ["REFUND_CARD.description"] = "Whenever you make a [gold]Payment[/gold], add a [gold]Diligent Payment[/gold] card to your hand.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Diligent Payment",
        ["DILIGENT_PAYMENT_CARD.description"] = "Gain [b]{block}[/b] [gold]Block[/gold].{gold:choose(0):|\nRefund [gold]5 Gold[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Settlement",
        ["SETTLEMENT_CARD.description"] = "Gain [gold]Block[/gold] equal to [b]{CalculatedBlock}[/b] — [b]{CalculationExtra}[/b] per [gold]Payment[/gold] you've made this combat.",
        ["INVOICE_CARD.title"] = "Invoice",
        ["INVOICE_CARD.description"] = "Deal [b]{Damage}[/b] damage [b]{CalculatedHits}[/b] times — once per [gold]Payment[/gold] you've made this combat.",
        ["BLOOD_PAYMENT_CARD.title"] = "Blood Payment",
        ["BLOOD_PAYMENT_CARD.description"] = "Lose [b]{hp}[/b] HP and make a [gold]{pay} Gold[/gold] [gold]Payment[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Credit Restored",
        ["CREDIT_RESTORED_CARD.description"] = "Gain [b]{plate}[/b] [gold]Plating[/gold].",
    };

    private static readonly Dictionary<string, string> _cardsKor = new()
    {
        ["DEBTOR_CARD.title"] = "강제 징수",
        ["DEBTOR_CARD.description"] = "턴 종료 시 [gold]{gold} 골드[/gold]를 [gold]납부[/gold]합니다 — 골드가 부족하면 체력 [b]{hp}[/b]을 잃고 대신 납부합니다.",
        ["WAGES_CARD.title"] = "품삯",
        ["WAGES_CARD.description"] = "[gold]{gold} 골드[/gold]를 얻습니다.",
        ["JOB_PLACEMENT_CARD.title"] = "취업알선",
        ["JOB_PLACEMENT_CARD.description"] = "빚에 [gold]{fee} 골드[/gold]를 더합니다.\n[gold]품삯[/gold]을 지금, 그리고 매 턴 시작 시 손에 넣습니다.",
        ["PAYMENT_BENEFIT_CARD.title"] = "납부 혜택",
        ["PAYMENT_BENEFIT_CARD.description"] = "[gold]납부[/gold]할 때마다 [gold]판금[/gold] [b]{plate}[/b]을 얻습니다.",
        ["REFUND_CARD.title"] = "환급",
        ["REFUND_CARD.description"] = "[gold]납부[/gold]할 때마다 [gold]성실 납부[/gold] 카드를 손에 넣습니다.",
        ["DILIGENT_PAYMENT_CARD.title"] = "성실 납부",
        ["DILIGENT_PAYMENT_CARD.description"] = "[gold]블록[/gold] [b]{block}[/b]을 얻습니다.{gold:choose(0):|\n[gold]5 골드[/gold]를 환급합니다.}",
        ["SETTLEMENT_CARD.title"] = "정산",
        ["SETTLEMENT_CARD.description"] = "[gold]블록[/gold] [b]{CalculatedBlock}[/b]을 얻습니다 — 이번 전투에서 한 [gold]납부[/gold] 1회당 [b]{CalculationExtra}[/b].",
        ["INVOICE_CARD.title"] = "청구서",
        ["INVOICE_CARD.description"] = "적에게 [b]{Damage}[/b]의 피해를 [b]{CalculatedHits}[/b]번 줍니다 — 이번 전투에서 한 [gold]납부[/gold] 1회당 1타.",
        ["BLOOD_PAYMENT_CARD.title"] = "혈납",
        ["BLOOD_PAYMENT_CARD.description"] = "체력 [b]{hp}[/b]을 잃고 [gold]{pay} 골드[/gold]를 [gold]납부[/gold]합니다.",
        ["CREDIT_RESTORED_CARD.title"] = "신용 회복",
        ["CREDIT_RESTORED_CARD.description"] = "[gold]판금[/gold] [b]{plate}[/b]을 얻습니다.",
    };

    private static readonly Dictionary<string, string> _powersEng = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Bad Credit",
        ["BAD_CREDIT_POWER.description"] = "At the start of each turn, add a Forced Collection card to your hand. Every 3rd turn it grows stronger.",
        ["JOB_PLACEMENT_POWER.title"] = "Job Placement",
        ["JOB_PLACEMENT_POWER.description"] = "At the start of each turn, add a Wages card to your hand.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Payment Benefit",
        ["PAYMENT_BENEFIT_POWER.description"] = "Whenever you make a Payment, gain 3 Plating.",
        ["REFUND_POWER.title"] = "Refund",
        ["REFUND_POWER.description"] = "Whenever you make a Payment, add a Diligent Payment card to your hand.",
    };

    private static readonly Dictionary<string, string> _powersKor = new()
    {
        ["BAD_CREDIT_POWER.title"] = "신용 불량",
        ["BAD_CREDIT_POWER.description"] = "매 턴 시작 시 강제 징수 카드를 손에 넣습니다. 3턴마다 더 강해집니다.",
        ["JOB_PLACEMENT_POWER.title"] = "취업알선",
        ["JOB_PLACEMENT_POWER.description"] = "매 턴 시작 시 품삯을 손에 넣습니다.",
        ["PAYMENT_BENEFIT_POWER.title"] = "납부 혜택",
        ["PAYMENT_BENEFIT_POWER.description"] = "납부할 때마다 판금 3을 얻습니다.",
        ["REFUND_POWER.title"] = "환급",
        ["REFUND_POWER.description"] = "납부할 때마다 성실 납부 카드를 손에 넣습니다.",
    };

    // ── 12 more languages for the payment-set cards + powers (item 2). Vanilla keyword terms
    //    (Block/Plating/Gold) follow Sts2ModTranslator/glossary.json; debt-domain names (Payment/Wages/
    //    …) follow the relic ByLang table above. spa == esp (esp mapped to the same dict). ──
    private static readonly Dictionary<string, string> _cardsJpn = new()
    {
        ["DEBTOR_CARD.title"] = "強制徴収",
        ["DEBTOR_CARD.description"] = "ターン終了時、[gold]{gold} ゴールド[/gold]の[gold]支払い[/gold]を行う — 支払えなければ、体力を [b]{hp}[/b] 失って血で支払う。",
        ["WAGES_CARD.title"] = "賃金",
        ["WAGES_CARD.description"] = "[gold]{gold} ゴールド[/gold]を得る。",
        ["JOB_PLACEMENT_CARD.title"] = "職業紹介",
        ["JOB_PLACEMENT_CARD.description"] = "[gold]{fee} ゴールド[/gold]を借金に加える。\n[gold]賃金[/gold]カードを今すぐ、そして各ターン開始時に手札に加える。",
        ["PAYMENT_BENEFIT_CARD.title"] = "支払い特典",
        ["PAYMENT_BENEFIT_CARD.description"] = "[gold]支払い[/gold]を行うたびに、[gold]プレート[/gold] [b]{plate}[/b] を得る。",
        ["REFUND_CARD.title"] = "払い戻し",
        ["REFUND_CARD.description"] = "[gold]支払い[/gold]を行うたびに、[gold]誠実な支払い[/gold]カードを手札に加える。",
        ["DILIGENT_PAYMENT_CARD.title"] = "誠実な支払い",
        ["DILIGENT_PAYMENT_CARD.description"] = "[gold]ブロック[/gold] [b]{block}[/b] を得る。{gold:choose(0):|\n[gold]5 ゴールド[/gold]を払い戻す。}",
        ["SETTLEMENT_CARD.title"] = "精算",
        ["SETTLEMENT_CARD.description"] = "[gold]ブロック[/gold]を[b]{CalculatedBlock}[/b]得る — この戦闘で行った[gold]支払い[/gold]1回につき[b]{CalculationExtra}[/b]。",
        ["INVOICE_CARD.title"] = "請求書",
        ["INVOICE_CARD.description"] = "[b]{Damage}[/b]のダメージを[b]{CalculatedHits}[/b]回与える — この戦闘で行った[gold]支払い[/gold]1回につき1ヒット。",
        ["BLOOD_PAYMENT_CARD.title"] = "血の支払い",
        ["BLOOD_PAYMENT_CARD.description"] = "体力を [b]{hp}[/b] 失い、[gold]{pay} ゴールド[/gold]の[gold]支払い[/gold]を行う。",
        ["CREDIT_RESTORED_CARD.title"] = "信用回復",
        ["CREDIT_RESTORED_CARD.description"] = "[gold]プレート[/gold] [b]{plate}[/b] を得る。",
    };
    private static readonly Dictionary<string, string> _cardsZhs = new()
    {
        ["DEBTOR_CARD.title"] = "强制征收",
        ["DEBTOR_CARD.description"] = "回合结束时，进行一次 [gold]{gold} 金币[/gold] [gold]还款[/gold]——若无力支付，失去 [b]{hp}[/b] 点生命，以血偿还。",
        ["WAGES_CARD.title"] = "工钱",
        ["WAGES_CARD.description"] = "获得 [gold]{gold} 金币[/gold]。",
        ["JOB_PLACEMENT_CARD.title"] = "就业安置",
        ["JOB_PLACEMENT_CARD.description"] = "将 [gold]{fee} 金币[/gold]计入你的债务。\n立即将一张[gold]工钱[/gold]牌加入手牌，之后每回合开始时也是如此。",
        ["PAYMENT_BENEFIT_CARD.title"] = "还款福利",
        ["PAYMENT_BENEFIT_CARD.description"] = "每次[gold]还款[/gold]时，获得 [b]{plate}[/b] [gold]覆甲[/gold]。",
        ["REFUND_CARD.title"] = "退款",
        ["REFUND_CARD.description"] = "每次[gold]还款[/gold]时，将一张[gold]按时还款[/gold]牌加入手牌。",
        ["DILIGENT_PAYMENT_CARD.title"] = "按时还款",
        ["DILIGENT_PAYMENT_CARD.description"] = "获得 [b]{block}[/b] [gold]格挡[/gold]。{gold:choose(0):|\n返还 [gold]5 金币[/gold]。}",
        ["SETTLEMENT_CARD.title"] = "结算",
        ["SETTLEMENT_CARD.description"] = "获得 [b]{CalculatedBlock}[/b] 点[gold]格挡[/gold]——本场战斗每[gold]还款[/gold]一次提供 [b]{CalculationExtra}[/b]。",
        ["INVOICE_CARD.title"] = "账单",
        ["INVOICE_CARD.description"] = "造成 [b]{Damage}[/b] 点伤害，共 [b]{CalculatedHits}[/b] 次——本场战斗每[gold]还款[/gold]一次便打一下。",
        ["BLOOD_PAYMENT_CARD.title"] = "血债",
        ["BLOOD_PAYMENT_CARD.description"] = "失去 [b]{hp}[/b] 点生命，并进行一次 [gold]{pay} 金币[/gold] [gold]还款[/gold]。",
        ["CREDIT_RESTORED_CARD.title"] = "信用恢复",
        ["CREDIT_RESTORED_CARD.description"] = "获得 [b]{plate}[/b] [gold]覆甲[/gold]。",
    };
    private static readonly Dictionary<string, string> _cardsDeu = new()
    {
        ["DEBTOR_CARD.title"] = "Zwangseinziehung",
        ["DEBTOR_CARD.description"] = "Am Ende deiner Runde leiste eine [gold]Zahlung[/gold] von [gold]{gold} Gold[/gold] — oder, wenn du sie dir nicht leisten kannst, verliere [b]{hp}[/b] LP und zahle mit Blut.",
        ["WAGES_CARD.title"] = "Lohn",
        ["WAGES_CARD.description"] = "Erhalte [gold]{gold} Gold[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Arbeitsvermittlung",
        ["JOB_PLACEMENT_CARD.description"] = "Füge deiner Schuld [gold]{fee} Gold[/gold] hinzu.\nLege sofort und zu Beginn jeder Runde eine [gold]Lohn[/gold]-Karte auf deine Hand.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Zahlungsvorteil",
        ["PAYMENT_BENEFIT_CARD.description"] = "Immer wenn du eine [gold]Zahlung[/gold] leistest, erhalte [b]{plate}[/b] [gold]Panzerung[/gold].",
        ["REFUND_CARD.title"] = "Rückerstattung",
        ["REFUND_CARD.description"] = "Immer wenn du eine [gold]Zahlung[/gold] leistest, lege eine [gold]Pünktliche Zahlung[/gold]-Karte auf deine Hand.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pünktliche Zahlung",
        ["DILIGENT_PAYMENT_CARD.description"] = "Erhalte [b]{block}[/b] [gold]Block[/gold].{gold:choose(0):|\nErstatte [gold]5 Gold[/gold] zurück.}",
        ["SETTLEMENT_CARD.title"] = "Abrechnung",
        ["SETTLEMENT_CARD.description"] = "Erhalte [b]{CalculatedBlock}[/b] [gold]Block[/gold] — [b]{CalculationExtra}[/b] pro [gold]Zahlung[/gold] in diesem Kampf.",
        ["INVOICE_CARD.title"] = "Rechnung",
        ["INVOICE_CARD.description"] = "Füge [b]{Damage}[/b] Schaden [b]{CalculatedHits}[/b]-mal zu — einmal pro [gold]Zahlung[/gold] in diesem Kampf.",
        ["BLOOD_PAYMENT_CARD.title"] = "Blutzahlung",
        ["BLOOD_PAYMENT_CARD.description"] = "Verliere [b]{hp}[/b] LP und leiste eine [gold]Zahlung[/gold] von [gold]{pay} Gold[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Bonität wiederhergestellt",
        ["CREDIT_RESTORED_CARD.description"] = "Erhalte [b]{plate}[/b] [gold]Panzerung[/gold].",
    };
    private static readonly Dictionary<string, string> _cardsFra = new()
    {
        ["DEBTOR_CARD.title"] = "Saisie forcée",
        ["DEBTOR_CARD.description"] = "À la fin de ton tour, effectue un [gold]Paiement[/gold] de [gold]{gold} or[/gold] — ou, si tu ne peux pas payer, perds [b]{hp}[/b] PV et paie en sang.",
        ["WAGES_CARD.title"] = "Salaire",
        ["WAGES_CARD.description"] = "Gagne [gold]{gold} or[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Placement",
        ["JOB_PLACEMENT_CARD.description"] = "Ajoute [gold]{fee} or[/gold] à ta dette.\nAjoute une carte [gold]Salaire[/gold] à ta main maintenant et au début de chaque tour.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Prime de paiement",
        ["PAYMENT_BENEFIT_CARD.description"] = "Chaque fois que tu effectues un [gold]Paiement[/gold], gagne [b]{plate}[/b] [gold]Blindage[/gold].",
        ["REFUND_CARD.title"] = "Remboursement",
        ["REFUND_CARD.description"] = "Chaque fois que tu effectues un [gold]Paiement[/gold], ajoute une carte [gold]Paiement assidu[/gold] à ta main.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Paiement assidu",
        ["DILIGENT_PAYMENT_CARD.description"] = "Gagne [b]{block}[/b] [gold]Armure[/gold].{gold:choose(0):|\nRembourse [gold]5 or[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Règlement",
        ["SETTLEMENT_CARD.description"] = "Gagne [b]{CalculatedBlock}[/b] [gold]Armure[/gold] — [b]{CalculationExtra}[/b] par [gold]Paiement[/gold] effectué ce combat.",
        ["INVOICE_CARD.title"] = "Facture",
        ["INVOICE_CARD.description"] = "Inflige [b]{Damage}[/b] dégâts [b]{CalculatedHits}[/b] fois — une fois par [gold]Paiement[/gold] effectué ce combat.",
        ["BLOOD_PAYMENT_CARD.title"] = "Paiement de sang",
        ["BLOOD_PAYMENT_CARD.description"] = "Perds [b]{hp}[/b] PV et effectue un [gold]Paiement[/gold] de [gold]{pay} or[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Crédit rétabli",
        ["CREDIT_RESTORED_CARD.description"] = "Gagne [b]{plate}[/b] [gold]Blindage[/gold].",
    };
    private static readonly Dictionary<string, string> _cardsSpa = new()
    {
        ["DEBTOR_CARD.title"] = "Embargo forzoso",
        ["DEBTOR_CARD.description"] = "Al final de tu turno, haz un [gold]Pago[/gold] de [gold]{gold} de oro[/gold] — o, si no puedes pagarlo, pierde [b]{hp}[/b] de vida y paga con sangre.",
        ["WAGES_CARD.title"] = "Salario",
        ["WAGES_CARD.description"] = "Gana [gold]{gold} de oro[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Colocación laboral",
        ["JOB_PLACEMENT_CARD.description"] = "Añade [gold]{fee} de oro[/gold] a tu deuda.\nAñade una carta de [gold]Salario[/gold] a tu mano ahora y al inicio de cada turno.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Beneficio de pago",
        ["PAYMENT_BENEFIT_CARD.description"] = "Cada vez que haces un [gold]Pago[/gold], ganas [b]{plate}[/b] de [gold]Blindaje[/gold].",
        ["REFUND_CARD.title"] = "Reembolso",
        ["REFUND_CARD.description"] = "Cada vez que haces un [gold]Pago[/gold], añade una carta de [gold]Pago diligente[/gold] a tu mano.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pago diligente",
        ["DILIGENT_PAYMENT_CARD.description"] = "Gana [b]{block}[/b] de [gold]Bloqueo[/gold].{gold:choose(0):|\nReembolsa [gold]5 de oro[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Liquidación",
        ["SETTLEMENT_CARD.description"] = "Gana [b]{CalculatedBlock}[/b] de [gold]Bloqueo[/gold] — [b]{CalculationExtra}[/b] por cada [gold]Pago[/gold] que hayas hecho este combate.",
        ["INVOICE_CARD.title"] = "Factura",
        ["INVOICE_CARD.description"] = "Inflige [b]{Damage}[/b] de daño [b]{CalculatedHits}[/b] veces — una por cada [gold]Pago[/gold] que hayas hecho este combate.",
        ["BLOOD_PAYMENT_CARD.title"] = "Pago de sangre",
        ["BLOOD_PAYMENT_CARD.description"] = "Pierde [b]{hp}[/b] de vida y haz un [gold]Pago[/gold] de [gold]{pay} de oro[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Crédito restaurado",
        ["CREDIT_RESTORED_CARD.description"] = "Gana [b]{plate}[/b] de [gold]Blindaje[/gold].",
    };
    private static readonly Dictionary<string, string> _cardsIta = new()
    {
        ["DEBTOR_CARD.title"] = "Riscossione forzata",
        ["DEBTOR_CARD.description"] = "Alla fine del turno, effettua un [gold]Pagamento[/gold] di [gold]{gold} Oro[/gold] — oppure, se non puoi permettertelo, perdi [b]{hp}[/b] PV e paghi col sangue.",
        ["WAGES_CARD.title"] = "Salario",
        ["WAGES_CARD.description"] = "Ottieni [gold]{gold} Oro[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Collocamento",
        ["JOB_PLACEMENT_CARD.description"] = "Aggiungi [gold]{fee} Oro[/gold] al tuo debito.\nAggiungi una carta [gold]Salario[/gold] alla tua mano ora e all'inizio di ogni turno.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Beneficio pagamento",
        ["PAYMENT_BENEFIT_CARD.description"] = "Ogni volta che effettui un [gold]Pagamento[/gold], ottieni [b]{plate}[/b] [gold]Placcatura[/gold].",
        ["REFUND_CARD.title"] = "Rimborso",
        ["REFUND_CARD.description"] = "Ogni volta che effettui un [gold]Pagamento[/gold], aggiungi una carta [gold]Pagamento diligente[/gold] alla tua mano.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pagamento diligente",
        ["DILIGENT_PAYMENT_CARD.description"] = "Ottieni [b]{block}[/b] [gold]Blocco[/gold].{gold:choose(0):|\nRimborsa [gold]5 Oro[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Saldo",
        ["SETTLEMENT_CARD.description"] = "Ottieni [b]{CalculatedBlock}[/b] [gold]Blocco[/gold] — [b]{CalculationExtra}[/b] per ogni [gold]Pagamento[/gold] effettuato in questo combattimento.",
        ["INVOICE_CARD.title"] = "Fattura",
        ["INVOICE_CARD.description"] = "Infliggi [b]{Damage}[/b] danni [b]{CalculatedHits}[/b] volte — una per ogni [gold]Pagamento[/gold] effettuato in questo combattimento.",
        ["BLOOD_PAYMENT_CARD.title"] = "Pagamento di sangue",
        ["BLOOD_PAYMENT_CARD.description"] = "Perdi [b]{hp}[/b] PV ed effettua un [gold]Pagamento[/gold] di [gold]{pay} Oro[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Credito ripristinato",
        ["CREDIT_RESTORED_CARD.description"] = "Ottieni [b]{plate}[/b] [gold]Placcatura[/gold].",
    };
    private static readonly Dictionary<string, string> _cardsPol = new()
    {
        ["DEBTOR_CARD.title"] = "Przymusowa egzekucja",
        ["DEBTOR_CARD.description"] = "Na końcu tury dokonaj [gold]Spłaty[/gold] [gold]{gold} złota[/gold] — lub, jeśli cię na nią nie stać, strać [b]{hp}[/b] PŻ i zapłać krwią.",
        ["WAGES_CARD.title"] = "Wypłata",
        ["WAGES_CARD.description"] = "Zyskaj [gold]{gold} złota[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Pośrednictwo pracy",
        ["JOB_PLACEMENT_CARD.description"] = "Dodaj [gold]{fee} złota[/gold] do swojego długu.\nDodaj kartę [gold]Wypłata[/gold] do ręki teraz i na początku każdej tury.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Premia za spłatę",
        ["PAYMENT_BENEFIT_CARD.description"] = "Za każdym razem, gdy dokonasz [gold]Spłaty[/gold], zyskaj [b]{plate}[/b] [gold]Opancerzenia[/gold].",
        ["REFUND_CARD.title"] = "Zwrot",
        ["REFUND_CARD.description"] = "Za każdym razem, gdy dokonasz [gold]Spłaty[/gold], dodaj kartę [gold]Sumienna spłata[/gold] do ręki.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Sumienna spłata",
        ["DILIGENT_PAYMENT_CARD.description"] = "Zyskaj [b]{block}[/b] [gold]Bloku[/gold].{gold:choose(0):|\nZwróć [gold]5 złota[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Rozliczenie",
        ["SETTLEMENT_CARD.description"] = "Zyskaj [b]{CalculatedBlock}[/b] [gold]Bloku[/gold] — [b]{CalculationExtra}[/b] za każdą [gold]Spłatę[/gold] w tej walce.",
        ["INVOICE_CARD.title"] = "Faktura",
        ["INVOICE_CARD.description"] = "Zadaj [b]{Damage}[/b] obrażeń [b]{CalculatedHits}[/b] razy — raz za każdą [gold]Spłatę[/gold] w tej walce.",
        ["BLOOD_PAYMENT_CARD.title"] = "Krwawa spłata",
        ["BLOOD_PAYMENT_CARD.description"] = "Strać [b]{hp}[/b] PŻ i dokonaj [gold]Spłaty[/gold] [gold]{pay} złota[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Przywrócony kredyt",
        ["CREDIT_RESTORED_CARD.description"] = "Zyskaj [b]{plate}[/b] [gold]Opancerzenia[/gold].",
    };
    private static readonly Dictionary<string, string> _cardsPtb = new()
    {
        ["DEBTOR_CARD.title"] = "Cobrança Forçada",
        ["DEBTOR_CARD.description"] = "No fim do seu turno, faça um [gold]Pagamento[/gold] de [gold]{gold} de Ouro[/gold] — ou, se não puder pagar, perca [b]{hp}[/b] de Vida e pague com sangue.",
        ["WAGES_CARD.title"] = "Salário",
        ["WAGES_CARD.description"] = "Ganhe [gold]{gold} de Ouro[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Colocação",
        ["JOB_PLACEMENT_CARD.description"] = "Adicione [gold]{fee} de Ouro[/gold] à sua dívida.\nAdicione uma carta de [gold]Salário[/gold] à sua mão agora e no início de cada turno.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Benefício de pagamento",
        ["PAYMENT_BENEFIT_CARD.description"] = "Sempre que fizer um [gold]Pagamento[/gold], ganhe [b]{plate}[/b] de [gold]Blindagem[/gold].",
        ["REFUND_CARD.title"] = "Reembolso",
        ["REFUND_CARD.description"] = "Sempre que fizer um [gold]Pagamento[/gold], adicione uma carta de [gold]Pagamento pontual[/gold] à sua mão.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pagamento pontual",
        ["DILIGENT_PAYMENT_CARD.description"] = "Ganhe [b]{block}[/b] de [gold]Proteção[/gold].{gold:choose(0):|\nReembolsa [gold]5 de Ouro[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Acerto",
        ["SETTLEMENT_CARD.description"] = "Ganhe [b]{CalculatedBlock}[/b] de [gold]Proteção[/gold] — [b]{CalculationExtra}[/b] por [gold]Pagamento[/gold] feito neste combate.",
        ["INVOICE_CARD.title"] = "Fatura",
        ["INVOICE_CARD.description"] = "Cause [b]{Damage}[/b] de dano [b]{CalculatedHits}[/b] vezes — uma por [gold]Pagamento[/gold] feito neste combate.",
        ["BLOOD_PAYMENT_CARD.title"] = "Pagamento de sangue",
        ["BLOOD_PAYMENT_CARD.description"] = "Perca [b]{hp}[/b] de Vida e faça um [gold]Pagamento[/gold] de [gold]{pay} de Ouro[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Crédito restaurado",
        ["CREDIT_RESTORED_CARD.description"] = "Ganhe [b]{plate}[/b] de [gold]Blindagem[/gold].",
    };
    private static readonly Dictionary<string, string> _cardsRus = new()
    {
        ["DEBTOR_CARD.title"] = "Принудительное взыскание",
        ["DEBTOR_CARD.description"] = "В конце хода совершите [gold]Платёж[/gold] в [gold]{gold} золота[/gold] — или, если не можете себе этого позволить, потеряйте [b]{hp}[/b] здоровья и заплатите кровью.",
        ["WAGES_CARD.title"] = "Зарплата",
        ["WAGES_CARD.description"] = "Получите [gold]{gold} золота[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Трудоустройство",
        ["JOB_PLACEMENT_CARD.description"] = "Добавьте [gold]{fee} золота[/gold] к вашему долгу.\nДобавьте карту [gold]Зарплата[/gold] в руку сейчас и в начале каждого хода.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Бонус за платёж",
        ["PAYMENT_BENEFIT_CARD.description"] = "Каждый раз, когда вы совершаете [gold]Платёж[/gold], получите [b]{plate}[/b] [gold]Панциря[/gold].",
        ["REFUND_CARD.title"] = "Возврат",
        ["REFUND_CARD.description"] = "Каждый раз, когда вы совершаете [gold]Платёж[/gold], добавьте карту [gold]Исправный платёж[/gold] в руку.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Исправный платёж",
        ["DILIGENT_PAYMENT_CARD.description"] = "Получите [b]{block}[/b] [gold]Защиты[/gold].{gold:choose(0):|\nВозврат [gold]5 золота[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Расчёт",
        ["SETTLEMENT_CARD.description"] = "Получите [b]{CalculatedBlock}[/b] [gold]Защиты[/gold] — [b]{CalculationExtra}[/b] за каждый [gold]Платёж[/gold] в этом бою.",
        ["INVOICE_CARD.title"] = "Счёт",
        ["INVOICE_CARD.description"] = "Нанесите [b]{Damage}[/b] урона [b]{CalculatedHits}[/b] раз — по разу за каждый [gold]Платёж[/gold] в этом бою.",
        ["BLOOD_PAYMENT_CARD.title"] = "Кровавый платёж",
        ["BLOOD_PAYMENT_CARD.description"] = "Потеряйте [b]{hp}[/b] здоровья и совершите [gold]Платёж[/gold] в [gold]{pay} золота[/gold].",
        ["CREDIT_RESTORED_CARD.title"] = "Кредит восстановлен",
        ["CREDIT_RESTORED_CARD.description"] = "Получите [b]{plate}[/b] [gold]Панциря[/gold].",
    };
    private static readonly Dictionary<string, string> _cardsTha = new()
    {
        ["DEBTOR_CARD.title"] = "บังคับเก็บหนี้",
        ["DEBTOR_CARD.description"] = "เมื่อจบเทิร์น ทำ[gold]การชำระ[/gold] [gold]{gold} ทอง[/gold] — หากจ่ายไม่ไหว เสียพลังชีวิต [b]{hp}[/b] และจ่ายด้วยเลือด",
        ["WAGES_CARD.title"] = "ค่าจ้าง",
        ["WAGES_CARD.description"] = "รับ [gold]{gold} ทอง[/gold]",
        ["JOB_PLACEMENT_CARD.title"] = "จัดหางาน",
        ["JOB_PLACEMENT_CARD.description"] = "เพิ่ม [gold]{fee} ทอง[/gold] เข้าหนี้ของคุณ\nเพิ่มการ์ด[gold]ค่าจ้าง[/gold]เข้ามือทันที และเมื่อเริ่มแต่ละเทิร์น",
        ["PAYMENT_BENEFIT_CARD.title"] = "สิทธิประโยชน์การชำระ",
        ["PAYMENT_BENEFIT_CARD.description"] = "ทุกครั้งที่คุณทำ[gold]การชำระ[/gold] รับ[gold]เกราะโลหะ[/gold] [b]{plate}[/b]",
        ["REFUND_CARD.title"] = "เงินคืน",
        ["REFUND_CARD.description"] = "ทุกครั้งที่คุณทำ[gold]การชำระ[/gold] เพิ่มการ์ด[gold]ชำระตรงเวลา[/gold]เข้ามือ",
        ["DILIGENT_PAYMENT_CARD.title"] = "ชำระตรงเวลา",
        ["DILIGENT_PAYMENT_CARD.description"] = "รับ[gold]บล็อก[/gold] [b]{block}[/b]{gold:choose(0):|\nคืน [gold]5 ทอง[/gold]}",
        ["SETTLEMENT_CARD.title"] = "ชำระบัญชี",
        ["SETTLEMENT_CARD.description"] = "รับ[gold]บล็อก[/gold] [b]{CalculatedBlock}[/b] — [b]{CalculationExtra}[/b] ต่อการ[gold]ชำระ[/gold]แต่ละครั้งในการต่อสู้นี้",
        ["INVOICE_CARD.title"] = "ใบแจ้งหนี้",
        ["INVOICE_CARD.description"] = "สร้างความเสียหาย [b]{Damage}[/b] จำนวน [b]{CalculatedHits}[/b] ครั้ง — หนึ่งครั้งต่อการ[gold]ชำระ[/gold]แต่ละครั้งในการต่อสู้นี้",
        ["BLOOD_PAYMENT_CARD.title"] = "จ่ายด้วยเลือด",
        ["BLOOD_PAYMENT_CARD.description"] = "เสียพลังชีวิต [b]{hp}[/b] และทำ[gold]การชำระ[/gold] [gold]{pay} ทอง[/gold]",
        ["CREDIT_RESTORED_CARD.title"] = "เครดิตกลับคืน",
        ["CREDIT_RESTORED_CARD.description"] = "รับ[gold]เกราะโลหะ[/gold] [b]{plate}[/b]",
    };
    private static readonly Dictionary<string, string> _cardsTur = new()
    {
        ["DEBTOR_CARD.title"] = "Zorla Tahsilat",
        ["DEBTOR_CARD.description"] = "Turunun sonunda [gold]{gold} Altın[/gold] [gold]Ödeme[/gold] yap — ya da ödeyemezsen [b]{hp}[/b] Can kaybet ve kanla öde.",
        ["WAGES_CARD.title"] = "Maaş",
        ["WAGES_CARD.description"] = "[gold]{gold} Altın[/gold] kazan.",
        ["JOB_PLACEMENT_CARD.title"] = "İş Bulma",
        ["JOB_PLACEMENT_CARD.description"] = "Borcuna [gold]{fee} Altın[/gold] ekle.\nElinize hemen ve her turun başında bir [gold]Maaş[/gold] kartı ekle.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Ödeme Avantajı",
        ["PAYMENT_BENEFIT_CARD.description"] = "Bir [gold]Ödeme[/gold] yaptığında [b]{plate}[/b] [gold]Zırh[/gold] kazan.",
        ["REFUND_CARD.title"] = "İade",
        ["REFUND_CARD.description"] = "Bir [gold]Ödeme[/gold] yaptığında eline bir [gold]Özenli Ödeme[/gold] kartı ekle.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Özenli Ödeme",
        ["DILIGENT_PAYMENT_CARD.description"] = "[b]{block}[/b] [gold]Blok[/gold] kazan.{gold:choose(0):|\n[gold]5 Altın[/gold] iade et.}",
        ["SETTLEMENT_CARD.title"] = "Hesaplaşma",
        ["SETTLEMENT_CARD.description"] = "[b]{CalculatedBlock}[/b] [gold]Blok[/gold] kazan — bu savaşta yaptığın her [gold]Ödeme[/gold] için [b]{CalculationExtra}[/b].",
        ["INVOICE_CARD.title"] = "Fatura",
        ["INVOICE_CARD.description"] = "[b]{Damage}[/b] hasarı [b]{CalculatedHits}[/b] kez ver — bu savaşta yaptığın her [gold]Ödeme[/gold] için bir kez.",
        ["BLOOD_PAYMENT_CARD.title"] = "Kan Ödemesi",
        ["BLOOD_PAYMENT_CARD.description"] = "[b]{hp}[/b] Can kaybet ve [gold]{pay} Altın[/gold] [gold]Ödeme[/gold] yap.",
        ["CREDIT_RESTORED_CARD.title"] = "Kredi Düzeldi",
        ["CREDIT_RESTORED_CARD.description"] = "[b]{plate}[/b] [gold]Zırh[/gold] kazan.",
    };
    private static readonly Dictionary<string, string> _powersJpn = new()
    {
        ["BAD_CREDIT_POWER.title"] = "信用不良",
        ["BAD_CREDIT_POWER.description"] = "各ターン開始時、強制徴収カードを手札に加える。3ターンごとに強くなる。",
        ["JOB_PLACEMENT_POWER.title"] = "職業紹介",
        ["JOB_PLACEMENT_POWER.description"] = "各ターン開始時、賃金カードを手札に加える。",
        ["PAYMENT_BENEFIT_POWER.title"] = "支払い特典",
        ["PAYMENT_BENEFIT_POWER.description"] = "支払いを行うたびに、プレートを3得る。",
        ["REFUND_POWER.title"] = "払い戻し",
        ["REFUND_POWER.description"] = "支払いを行うたびに、誠実な支払いカードを手札に加える。",
    };
    private static readonly Dictionary<string, string> _powersZhs = new()
    {
        ["BAD_CREDIT_POWER.title"] = "信用不良",
        ["BAD_CREDIT_POWER.description"] = "每回合开始时，将一张强制征收牌加入手牌。每3回合变得更强。",
        ["JOB_PLACEMENT_POWER.title"] = "就业安置",
        ["JOB_PLACEMENT_POWER.description"] = "每回合开始时，将一张工钱牌加入手牌。",
        ["PAYMENT_BENEFIT_POWER.title"] = "还款福利",
        ["PAYMENT_BENEFIT_POWER.description"] = "每次还款时，获得3层覆甲。",
        ["REFUND_POWER.title"] = "退款",
        ["REFUND_POWER.description"] = "每次还款时，将一张按时还款牌加入手牌。",
    };
    private static readonly Dictionary<string, string> _powersDeu = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Zahlungsunfähig",
        ["BAD_CREDIT_POWER.description"] = "Lege zu Beginn jeder Runde eine Zwangseinziehung-Karte auf deine Hand. Alle 3 Runden wird sie stärker.",
        ["JOB_PLACEMENT_POWER.title"] = "Arbeitsvermittlung",
        ["JOB_PLACEMENT_POWER.description"] = "Lege zu Beginn jeder Runde eine Lohn-Karte auf deine Hand.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Zahlungsvorteil",
        ["PAYMENT_BENEFIT_POWER.description"] = "Immer wenn du eine Zahlung leistest, erhalte 3 Panzerung.",
        ["REFUND_POWER.title"] = "Rückerstattung",
        ["REFUND_POWER.description"] = "Immer wenn du eine Zahlung leistest, lege eine Pünktliche Zahlung-Karte auf deine Hand.",
    };
    private static readonly Dictionary<string, string> _powersFra = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Insolvabilité",
        ["BAD_CREDIT_POWER.description"] = "Au début de chaque tour, ajoute une carte Saisie forcée à ta main. Tous les 3 tours, elle devient plus forte.",
        ["JOB_PLACEMENT_POWER.title"] = "Placement",
        ["JOB_PLACEMENT_POWER.description"] = "Au début de chaque tour, ajoute une carte Salaire à ta main.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Prime de paiement",
        ["PAYMENT_BENEFIT_POWER.description"] = "Chaque fois que tu effectues un Paiement, gagne 3 Blindage.",
        ["REFUND_POWER.title"] = "Remboursement",
        ["REFUND_POWER.description"] = "Chaque fois que tu effectues un Paiement, ajoute une carte Paiement assidu à ta main.",
    };
    private static readonly Dictionary<string, string> _powersSpa = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Insolvencia",
        ["BAD_CREDIT_POWER.description"] = "Al inicio de cada turno, añade una carta de Embargo forzoso a tu mano. Cada 3 turnos se vuelve más fuerte.",
        ["JOB_PLACEMENT_POWER.title"] = "Colocación laboral",
        ["JOB_PLACEMENT_POWER.description"] = "Al inicio de cada turno, añade una carta de Salario a tu mano.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Beneficio de pago",
        ["PAYMENT_BENEFIT_POWER.description"] = "Cada vez que haces un Pago, ganas 3 de Blindaje.",
        ["REFUND_POWER.title"] = "Reembolso",
        ["REFUND_POWER.description"] = "Cada vez que haces un Pago, añade una carta de Pago diligente a tu mano.",
    };
    private static readonly Dictionary<string, string> _powersIta = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Insolvenza",
        ["BAD_CREDIT_POWER.description"] = "All'inizio di ogni turno, aggiungi una carta Riscossione forzata alla tua mano. Ogni 3 turni diventa più forte.",
        ["JOB_PLACEMENT_POWER.title"] = "Collocamento",
        ["JOB_PLACEMENT_POWER.description"] = "All'inizio di ogni turno, aggiungi una carta Salario alla tua mano.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Beneficio pagamento",
        ["PAYMENT_BENEFIT_POWER.description"] = "Ogni volta che effettui un Pagamento, ottieni 3 Placcatura.",
        ["REFUND_POWER.title"] = "Rimborso",
        ["REFUND_POWER.description"] = "Ogni volta che effettui un Pagamento, aggiungi una carta Pagamento diligente alla tua mano.",
    };
    private static readonly Dictionary<string, string> _powersPol = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Niewypłacalność",
        ["BAD_CREDIT_POWER.description"] = "Na początku każdej tury dodaj kartę Przymusowa egzekucja do ręki. Co 3 tury staje się silniejsza.",
        ["JOB_PLACEMENT_POWER.title"] = "Pośrednictwo pracy",
        ["JOB_PLACEMENT_POWER.description"] = "Na początku każdej tury dodaj kartę Wypłata do ręki.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Premia za spłatę",
        ["PAYMENT_BENEFIT_POWER.description"] = "Za każdym razem, gdy dokonasz Spłaty, zyskaj 3 Opancerzenia.",
        ["REFUND_POWER.title"] = "Zwrot",
        ["REFUND_POWER.description"] = "Za każdym razem, gdy dokonasz Spłaty, dodaj kartę Sumienna spłata do ręki.",
    };
    private static readonly Dictionary<string, string> _powersPtb = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Crédito Ruim",
        ["BAD_CREDIT_POWER.description"] = "No início de cada turno, adicione uma carta de Cobrança Forçada à sua mão. A cada 3 turnos fica mais forte.",
        ["JOB_PLACEMENT_POWER.title"] = "Colocação",
        ["JOB_PLACEMENT_POWER.description"] = "No início de cada turno, adicione uma carta de Salário à sua mão.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Benefício de pagamento",
        ["PAYMENT_BENEFIT_POWER.description"] = "Sempre que fizer um Pagamento, ganhe 3 de Blindagem.",
        ["REFUND_POWER.title"] = "Reembolso",
        ["REFUND_POWER.description"] = "Sempre que fizer um Pagamento, adicione uma carta de Pagamento pontual à sua mão.",
    };
    private static readonly Dictionary<string, string> _powersRus = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Неплатёжеспособность",
        ["BAD_CREDIT_POWER.description"] = "В начале каждого хода добавьте карту Принудительное взыскание в руку. Каждый 3-й ход она усиливается.",
        ["JOB_PLACEMENT_POWER.title"] = "Трудоустройство",
        ["JOB_PLACEMENT_POWER.description"] = "В начале каждого хода добавьте карту Зарплата в руку.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Бонус за платёж",
        ["PAYMENT_BENEFIT_POWER.description"] = "Каждый раз, когда вы совершаете Платёж, получите 3 Панциря.",
        ["REFUND_POWER.title"] = "Возврат",
        ["REFUND_POWER.description"] = "Каждый раз, когда вы совершаете Платёж, добавьте карту Исправный платёж в руку.",
    };
    private static readonly Dictionary<string, string> _powersTha = new()
    {
        ["BAD_CREDIT_POWER.title"] = "เครดิตเสีย",
        ["BAD_CREDIT_POWER.description"] = "เมื่อเริ่มแต่ละเทิร์น เพิ่มการ์ดบังคับเก็บหนี้เข้ามือ ทุก 3 เทิร์นจะแข็งแกร่งขึ้น",
        ["JOB_PLACEMENT_POWER.title"] = "จัดหางาน",
        ["JOB_PLACEMENT_POWER.description"] = "เมื่อเริ่มแต่ละเทิร์น เพิ่มการ์ดค่าจ้างเข้ามือ",
        ["PAYMENT_BENEFIT_POWER.title"] = "สิทธิประโยชน์การชำระ",
        ["PAYMENT_BENEFIT_POWER.description"] = "ทุกครั้งที่คุณทำการชำระ รับเกราะโลหะ 3",
        ["REFUND_POWER.title"] = "เงินคืน",
        ["REFUND_POWER.description"] = "ทุกครั้งที่คุณทำการชำระ เพิ่มการ์ดชำระตรงเวลาเข้ามือ",
    };
    private static readonly Dictionary<string, string> _powersTur = new()
    {
        ["BAD_CREDIT_POWER.title"] = "Kredi İflası",
        ["BAD_CREDIT_POWER.description"] = "Her turun başında eline bir Zorla Tahsilat kartı ekle. Her 3 turda bir güçlenir.",
        ["JOB_PLACEMENT_POWER.title"] = "İş Bulma",
        ["JOB_PLACEMENT_POWER.description"] = "Her turun başında eline bir Maaş kartı ekle.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Ödeme Avantajı",
        ["PAYMENT_BENEFIT_POWER.description"] = "Bir Ödeme yaptığında 3 Zırh kazan.",
        ["REFUND_POWER.title"] = "İade",
        ["REFUND_POWER.description"] = "Bir Ödeme yaptığında eline bir Özenli Ödeme kartı ekle.",
    };
    private static readonly Dictionary<string, Dictionary<string, string>> _cardsByLang = new()
    {
        ["eng"] = _cardsEng, ["kor"] = _cardsKor,
        ["jpn"] = _cardsJpn,
        ["zhs"] = _cardsZhs,
        ["deu"] = _cardsDeu,
        ["fra"] = _cardsFra,
        ["spa"] = _cardsSpa,
        ["ita"] = _cardsIta,
        ["pol"] = _cardsPol,
        ["ptb"] = _cardsPtb,
        ["rus"] = _cardsRus,
        ["tha"] = _cardsTha,
        ["tur"] = _cardsTur,
        ["esp"] = _cardsSpa,
    };
    private static readonly Dictionary<string, Dictionary<string, string>> _powersByLang = new()
    {
        ["eng"] = _powersEng, ["kor"] = _powersKor,
        ["jpn"] = _powersJpn,
        ["zhs"] = _powersZhs,
        ["deu"] = _powersDeu,
        ["fra"] = _powersFra,
        ["spa"] = _powersSpa,
        ["ita"] = _powersIta,
        ["pol"] = _powersPol,
        ["ptb"] = _powersPtb,
        ["rus"] = _powersRus,
        ["tha"] = _powersTha,
        ["tur"] = _powersTur,
        ["esp"] = _powersSpa,
    };
}
