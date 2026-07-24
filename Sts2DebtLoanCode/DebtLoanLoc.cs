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
        ["eng"] = new("Debt Ledger",
            "Owed [gold]{owed} Gold[/gold]. Paid [gold]{paid} Gold[/gold] so far.\nInjected each combat: [gold]{cards:choose(1|2|3|4):None|Delinquency|Delinquency, Seizure|Bad Credit|None}[/gold]. More curses pile on the longer you owe.\nRepay the debt at a shop to remove this relic.",
            "Every signature is a small surrender.",
            "Payment", "Play it to make a [gold]{play} Gold[/gold] [gold]Payment[/gold].",
            "Delinquency", "While this is in your hand, enemy attacks deal 50% more damage.",
            "Seizure", "While this is in your hand, you can only play cards of the first type you play each turn.",
            "Bad Credit", "The instant it reaches your hand you gain [gold]Bad Credit[/gold], then vanishes — a [gold]Forced Collection[/gold] card is added to your hand each turn, growing stronger every 3rd turn.",
            "Forced Collection", "At the end of your turn, lose [b]{hp}[/b] HP and repay [gold]{principal} Gold[/gold] of principal, then Exhaust."),

        ["kor"] = new("빚 장부",
            "남은 상환액 [gold]{owed} 골드[/gold]. 지금까지 지불 [gold]{paid} 골드[/gold].\n전투마다 주입되는 저주: [gold]{cards:choose(1|2|3|4):없음|연체|연체, 차압|신용 불량|없음}[/gold]. 오래 갚지 않을수록 늘어납니다.\n상점에서 빚을 갚으면 이 유물이 제거됩니다.",
            "모든 서명은 작은 항복이다.",
            "납부", "사용 시 [gold]{play} 골드[/gold]를 [gold]납부[/gold]합니다.",
            "연체", "이 카드가 손에 있는 동안, 적의 공격에 50% 더 강한 피해를 입습니다.",
            "차압", "이 카드가 손에 있는 동안, 이번 턴 처음 낸 카드 종류만 사용할 수 있습니다.",
            "신용 불량", "손에 들어오는 즉시 [gold]신용 불량[/gold] 상태가 되고 사라집니다 — 이후 매 턴 [gold]강제 징수[/gold] 카드가 손에 추가되며, 3턴마다 강해집니다.",
            "강제 징수", "턴 종료 시, 체력을 [b]{hp}[/b] 잃고 원금 [gold]{principal} 골드[/gold]를 상환한 뒤 소멸합니다."),

        ["jpn"] = new("借金台帳",
            "残債 [gold]{owed} ゴールド[/gold]。これまでの支払い [gold]{paid} ゴールド[/gold]。\n戦闘ごとに加わる呪い: [gold]{cards:choose(1|2|3|4):なし|延滞|延滞・差し押さえ|信用不良|なし}[/gold]。返済が遅れるほど増える。\nショップで借金を返済すると、このレリックは取り除かれる。",
            "署名はすべて、小さな降伏だ。",
            "支払い", "プレイして[gold]{play} ゴールド[/gold]を支払い、借金をより早く返済する。",
            "延滞", "この手札にある間、敵の攻撃ダメージが50%増加する。",
            "差し押さえ", "この手札にある間、そのターン最初にプレイしたタイプのカードしかプレイできない。",
            "信用不良", "手札に入った瞬間[gold]信用不良[/gold]になって消滅する — 以降毎ターン[gold]強制徴収[/gold]カードが手札に加わり、3ターンごとに強くなる。",
            "強制徴収", "ターン終了時、体力を[b]{hp}[/b]失い、元金[gold]{principal} ゴールド[/gold]を返済してから廃棄。"),

        ["zhs"] = new("债务账簿",
            "待还 [gold]{owed} 金币[/gold]。已偿还 [gold]{paid} 金币[/gold]。\n每场战斗注入的诅咒：[gold]{cards:choose(1|2|3|4):无|拖欠|拖欠、扣押|信用不良|无}[/gold]。拖欠越久，诅咒越多。\n在商店还清债务即可移除此遗物。",
            "每一个签名都是一次小小的屈服。",
            "还款", "打出它，支付 [gold]{play} 金币[/gold]，更快偿还贷款。",
            "拖欠", "当此牌在你手牌中时，敌人的攻击造成的伤害提高 50%。",
            "扣押", "当此牌在你手牌中时，你每回合只能打出你本回合第一张打出的类型的牌。",
            "信用不良", "进入手牌的瞬间获得[gold]信用不良[/gold]并消失——此后每回合将一张[gold]强制征收[/gold]牌加入手牌，每3回合变强。",
            "强制征收", "回合结束时，失去 [b]{hp}[/b] 点生命并偿还 [gold]{principal} 金币[/gold] 本金，然后消耗。"),

        ["deu"] = new("Schuldenbuch",
            "Offen: [gold]{owed} Gold[/gold]. Zurückgezahlt: [gold]{paid} Gold[/gold].\nJeder Kampf schleust ein: [gold]{cards:choose(1|2|3|4):Keine|Verzug|Verzug, Pfändung|Zahlungsunfähig|Keine}[/gold]. Je länger du schuldest, desto mehr Flüche.\nZahle die Schuld in einem Laden zurück, um dieses Relikt zu entfernen.",
            "Jede Unterschrift ist eine kleine Kapitulation.",
            "Zahlung", "Spiele sie, um [gold]{play} Gold[/gold] zu zahlen und die Schuld schneller zu tilgen.",
            "Verzug", "Solange sie auf deiner Hand ist, richten gegnerische Angriffe 50% mehr Schaden an.",
            "Pfändung", "Solange sie auf deiner Hand ist, kannst du nur Karten des ersten Typs spielen, den du in dieser Runde spielst.",
            "Zahlungsunfähig", "Sobald sie auf deine Hand kommt, erhältst du [gold]Zahlungsunfähig[/gold] und sie verschwindet — danach kommt jede Runde eine [gold]Zwangseinziehung[/gold]-Karte auf deine Hand, alle 3 Runden stärker.",
            "Zwangseinziehung", "Am Ende deiner Runde verlierst du [b]{hp}[/b] LP und tilgst [gold]{principal} Gold[/gold] der Schuld, dann verbraucht."),

        ["fra"] = new("Grand livre des dettes",
            "Dû : [gold]{owed} or[/gold]. Remboursé : [gold]{paid} or[/gold].\nInjecté à chaque combat : [gold]{cards:choose(1|2|3|4):Aucune|Défaut|Défaut, Saisie|Insolvabilité|Aucune}[/gold]. Plus tu tardes, plus il y a de malédictions.\nRembourse la dette dans une boutique pour retirer cette relique.",
            "Chaque signature est une petite reddition.",
            "Paiement", "Joue-la pour payer [gold]{play} or[/gold] et rembourser la dette plus vite.",
            "Défaut", "Tant qu'elle est dans ta main, les attaques ennemies infligent 50% de dégâts en plus.",
            "Saisie", "Tant qu'elle est dans ta main, tu ne peux jouer que des cartes du premier type que tu joues ce tour.",
            "Insolvabilité", "Dès qu'elle arrive en main, tu gagnes [gold]Insolvabilité[/gold] et elle disparaît — ensuite une carte [gold]Saisie forcée[/gold] est ajoutée à ta main chaque tour, plus forte tous les 3 tours.",
            "Saisie forcée", "À la fin de ton tour, perds [b]{hp}[/b] PV et rembourse [gold]{principal} or[/gold] de la dette, puis Épuise."),

        ["spa"] = new("Libro de deudas",
            "Pendiente: [gold]{owed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nInyectado cada combate: [gold]{cards:choose(1|2|3|4):Ninguna|Morosidad|Morosidad, Embargo|Insolvencia|Ninguna}[/gold]. Cuanto más debas, más maldiciones.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Pago", "Juégala para pagar [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno.",
            "Insolvencia", "En cuanto llega a tu mano obtienes [gold]Insolvencia[/gold] y desaparece — después cada turno se añade una carta de [gold]Embargo forzoso[/gold] a tu mano, más fuerte cada 3 turnos.",
            "Embargo forzoso", "Al final de tu turno, pierde [b]{hp}[/b] de vida y salda [gold]{principal} de oro[/gold] de la deuda; luego Agota."),

        ["esp"] = new("Libro de deudas",
            "Pendiente: [gold]{owed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nInyectado cada combate: [gold]{cards:choose(1|2|3|4):Ninguna|Morosidad|Morosidad, Embargo|Insolvencia|Ninguna}[/gold]. Cuanto más debas, más maldiciones.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Pago", "Juégala para pagar [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno.",
            "Insolvencia", "En cuanto llega a tu mano obtienes [gold]Insolvencia[/gold] y desaparece — después cada turno se añade una carta de [gold]Embargo forzoso[/gold] a tu mano, más fuerte cada 3 turnos.",
            "Embargo forzoso", "Al final de tu turno, pierde [b]{hp}[/b] de vida y salda [gold]{principal} de oro[/gold] de la deuda; luego Agota."),

        ["ita"] = new("Registro dei debiti",
            "Dovuto: [gold]{owed} Oro[/gold]. Pagato: [gold]{paid} Oro[/gold].\nInserito ogni combattimento: [gold]{cards:choose(1|2|3|4):Nessuna|Morosità|Morosità, Pignoramento|Insolvenza|Nessuna}[/gold]. Più tardi, più maledizioni.\nSalda il debito in un negozio per rimuovere questo cimelio.",
            "Ogni firma è una piccola resa.",
            "Pagamento", "Giocala per pagare [gold]{play} Oro[/gold] e saldare il debito più in fretta.",
            "Morosità", "Finché è nella tua mano, gli attacchi nemici infliggono il 50% di danni in più.",
            "Pignoramento", "Finché è nella tua mano, puoi giocare solo carte del primo tipo che giochi ogni turno.",
            "Insolvenza", "Appena arriva in mano ottieni [gold]Insolvenza[/gold] e svanisce — poi ogni turno una carta [gold]Riscossione forzata[/gold] viene aggiunta alla tua mano, più forte ogni 3 turni.",
            "Riscossione forzata", "Alla fine del turno, perdi [b]{hp}[/b] PV e ripaghi [gold]{principal} Oro[/gold] di debito, poi Consuma."),

        ["pol"] = new("Księga długów",
            "Do spłaty: [gold]{owed} złota[/gold]. Spłacono: [gold]{paid} złota[/gold].\nDodawane w każdej walce: [gold]{cards:choose(1|2|3|4):Brak|Zaległość|Zaległość, Zajęcie|Niewypłacalność|Brak}[/gold]. Im dłużej zwlekasz, tym więcej klątw.\nSpłać dług w sklepie, aby usunąć ten relikt.",
            "Każdy podpis to mała kapitulacja.",
            "Spłata", "Zagraj ją, aby zapłacić [gold]{play} złota[/gold] i szybciej spłacić dług.",
            "Zaległość", "Gdy jest w twojej ręce, ataki wrogów zadają 50% więcej obrażeń.",
            "Zajęcie", "Gdy jest w twojej ręce, możesz grać tylko karty pierwszego typu zagranego w danej turze.",
            "Niewypłacalność", "Gdy trafi do ręki, zyskujesz [gold]Niewypłacalność[/gold] i znika — potem co turę do ręki trafia karta [gold]Przymusowa egzekucja[/gold], silniejsza co 3 tury.",
            "Przymusowa egzekucja", "Na końcu tury tracisz [b]{hp}[/b] PŻ i spłacasz [gold]{principal} złota[/gold] długu, potem Zużywa się."),

        ["ptb"] = new("Livro-razão de dívidas",
            "Devido: [gold]{owed} de Ouro[/gold]. Pago: [gold]{paid} de Ouro[/gold].\nInjetado a cada combate: [gold]{cards:choose(1|2|3|4):Nenhuma|Inadimplência|Inadimplência, Penhora|Crédito Ruim|Nenhuma}[/gold]. Quanto mais você deve, mais maldições.\nQuite a dívida em uma loja para remover esta relíquia.",
            "Cada assinatura é uma pequena rendição.",
            "Pagamento", "Jogue-a para pagar [gold]{play} de Ouro[/gold] e quitar a dívida mais rápido.",
            "Inadimplência", "Enquanto estiver na sua mão, ataques inimigos causam 50% mais dano.",
            "Penhora", "Enquanto estiver na sua mão, você só pode jogar cartas do primeiro tipo que jogar no turno.",
            "Crédito Ruim", "Assim que chega à sua mão você ganha [gold]Crédito Ruim[/gold] e ela some — depois uma carta de [gold]Cobrança Forçada[/gold] é adicionada à sua mão a cada turno, mais forte a cada 3 turnos.",
            "Cobrança Forçada", "No fim do seu turno, perca [b]{hp}[/b] de Vida e quite [gold]{principal} de Ouro[/gold] da dívida, então Exaure."),

        ["rus"] = new("Долговая книга",
            "К оплате: [gold]{owed} золота[/gold]. Выплачено: [gold]{paid} золота[/gold].\nДобавляется каждый бой: [gold]{cards:choose(1|2|3|4):Нет|Просрочка|Просрочка, Арест|Неплатёжеспособность|Нет}[/gold]. Чем дольше долг, тем больше проклятий.\nПогасите долг в магазине, чтобы убрать эту реликвию.",
            "Каждая подпись — маленькая капитуляция.",
            "Платёж", "Разыграйте её, чтобы заплатить [gold]{play} золота[/gold] и быстрее погасить долг.",
            "Просрочка", "Пока она в руке, атаки врагов наносят на 50% больше урона.",
            "Арест", "Пока она в руке, за ход вы можете разыгрывать только карты того типа, что разыграли первым.",
            "Неплатёжеспособность", "Как только попадает в руку, вы получаете [gold]Неплатёжеспособность[/gold] и она исчезает — затем каждый ход в руку добавляется карта [gold]Принудительное взыскание[/gold], усиливаясь каждый 3-й ход.",
            "Принудительное взыскание", "В конце хода теряете [b]{hp}[/b] здоровья и гасите [gold]{principal} золота[/gold] долга, затем Истощается."),

        ["tha"] = new("บัญชีหนี้",
            "ค้างชำระ [gold]{owed} ทอง[/gold] จ่ายไปแล้ว [gold]{paid} ทอง[/gold]\nใส่ทุกการต่อสู้: [gold]{cards:choose(1|2|3|4):ไม่มี|ค้างชำระ|ค้างชำระ, ยึดทรัพย์|เครดิตเสีย|ไม่มี}[/gold] ยิ่งค้างนานยิ่งมากขึ้น\nชำระหนี้ที่ร้านค้าเพื่อนำวัตถุโบราณนี้ออก",
            "ทุกลายเซ็นคือการยอมจำนนเล็กๆ",
            "การชำระ", "เล่นเพื่อจ่าย [gold]{play} ทอง[/gold] และชำระหนี้เร็วขึ้น",
            "ค้างชำระ", "ขณะอยู่ในมือ การโจมตีของศัตรูสร้างความเสียหายเพิ่ม 50%",
            "ยึดทรัพย์", "ขณะอยู่ในมือ คุณเล่นได้เฉพาะการ์ดประเภทแรกที่คุณเล่นในเทิร์นนั้น",
            "เครดิตเสีย", "ทันทีที่เข้ามือ คุณจะได้รับ[gold]เครดิตเสีย[/gold]แล้วหายไป — จากนั้นทุกเทิร์นจะเพิ่มการ์ด[gold]บังคับเก็บหนี้[/gold]เข้ามือ แข็งแกร่งขึ้นทุก 3 เทิร์น",
            "บังคับเก็บหนี้", "เมื่อจบเทิร์น เสียพลังชีวิต [b]{hp}[/b] และชำระเงินต้น [gold]{principal} ทอง[/gold] จากนั้นเผาไหม้"),

        ["tur"] = new("Borç Defteri",
            "Kalan: [gold]{owed} Altın[/gold]. Ödenen: [gold]{paid} Altın[/gold].\nHer savaş eklenir: [gold]{cards:choose(1|2|3|4):Yok|Temerrüt|Temerrüt, Haciz|Kredi İflası|Yok}[/gold]. Borç uzadıkça daha fazla lanet.\nBu kalıntıyı kaldırmak için borcu bir dükkânda öde.",
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
            "매 턴 시작 시 [gold]{card}[/gold] 카드를 손에 넣습니다.",
            "매 턴 시작 시 납부 카드를 손에 넣습니다."),
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
        ["eng"] = new("Payment", "Every gold you pay comes off what you [gold]owe[/gold] (interest included). The longer you carry the debt, the more [gold]interest[/gold] it gathers — repay early to keep the shop cost down. Each payment also earns you 1 [gold]Receipt[/gold]."),
        ["kor"] = new("납부", "납부하는 골드는 [gold]갚을 금액[/gold]에서 그대로 차감됩니다(이자 포함). 빚을 오래 짊어질수록 [gold]이자[/gold]가 늘어나니 일찍 갚을수록 상점 상환액이 줄어듭니다. 납부할 때마다 [gold]영수증[/gold]을 1 얻습니다."),
        ["jpn"] = new("支払い", "支払ったゴールドは全額が[gold]返済額[/gold]から差し引かれる（利息込み）。借金を長く抱えるほど[gold]利息[/gold]が増えるので、早く返すほどショップでの返済額が減る。 支払うたびに[gold]レシート[/gold]を1枚得る。"),
        ["zhs"] = new("还款", "所付金币全部从你的[gold]欠款[/gold]中扣除（含利息）。欠债越久，[gold]利息[/gold]越多——尽早偿还可降低商店还款额。 每次还款还会获得 1 张[gold]收据[/gold]。"),
        ["deu"] = new("Zahlung", "Jedes gezahlte Gold wird von deiner [gold]Schuld[/gold] abgezogen (Zinsen inklusive). Je länger du die Schuld trägst, desto mehr [gold]Zinsen[/gold] fallen an — zahle früh zurück, um die Ladenkosten gering zu halten. Jede Zahlung bringt dir außerdem 1 [gold]Beleg[/gold] ein."),
        ["fra"] = new("Paiement", "Chaque or payé est déduit de ce que tu [gold]dois[/gold] (intérêts inclus). Plus tu gardes la dette longtemps, plus les [gold]intérêts[/gold] s'accumulent — rembourse tôt pour réduire le coût en boutique. Chaque paiement te rapporte aussi 1 [gold]reçu[/gold]."),
        ["spa"] = new("Pago", "Cada oro pagado se descuenta de lo que [gold]debes[/gold] (intereses incluidos). Cuanto más tiempo mantengas la deuda, más [gold]intereses[/gold] se acumulan; paga pronto para reducir el coste en la tienda. Cada pago también te otorga 1 [gold]recibo[/gold]."),
        ["esp"] = new("Pago", "Cada oro pagado se descuenta de lo que [gold]debes[/gold] (intereses incluidos). Cuanto más tiempo mantengas la deuda, más [gold]intereses[/gold] se acumulan; paga pronto para reducir el coste en la tienda. Cada pago también te otorga 1 [gold]recibo[/gold]."),
        ["ita"] = new("Pagamento", "Ogni oro pagato viene sottratto da ciò che [gold]devi[/gold] (interessi inclusi). Più a lungo porti il debito, più [gold]interessi[/gold] si accumulano — rimborsa presto per ridurre il costo nel negozio. Ogni pagamento ti dà anche 1 [gold]ricevuta[/gold]."),
        ["pol"] = new("Spłata", "Każde zapłacone złoto odejmuje się od twojego [gold]długu[/gold] (z odsetkami). Im dłużej masz dług, tym więcej [gold]odsetek[/gold] narasta — spłać wcześnie, by obniżyć koszt w sklepie. Każda spłata daje ci też 1 [gold]paragon[/gold]."),
        ["ptb"] = new("Pagamento", "Cada ouro pago é descontado do que você [gold]deve[/gold] (juros incluídos). Quanto mais tempo mantiver a dívida, mais [gold]juros[/gold] se acumulam — quite cedo para reduzir o custo na loja. Cada pagamento também rende 1 [gold]recibo[/gold]."),
        ["rus"] = new("Платёж", "Каждое уплаченное золото вычитается из вашего [gold]долга[/gold] (с процентами). Чем дольше вы держите долг, тем больше [gold]процентов[/gold] набегает — гасите раньше, чтобы снизить стоимость в магазине. Каждый платёж также приносит 1 [gold]чек[/gold]."),
        ["tha"] = new("การชำระ", "ทองที่จ่ายทุกเหรียญจะถูกหักจาก[gold]ยอดที่ค้าง[/gold] (รวมดอกเบี้ย) ยิ่งเป็นหนี้นาน [gold]ดอกเบี้ย[/gold]ยิ่งเพิ่ม — ชำระเร็วเพื่อลดค่าใช้จ่ายที่ร้านค้า การชำระแต่ละครั้งยังได้รับ[gold]ใบเสร็จ[/gold] 1 ใบด้วย"),
        ["tur"] = new("Ödeme", "Ödediğin her altın [gold]borcundan[/gold] düşülür (faiz dahil). Borcu ne kadar uzun taşırsan o kadar çok [gold]faiz[/gold] birikir — erken öde, dükkân maliyetini düşük tut. Her ödeme sana ayrıca 1 [gold]makbuz[/gold] kazandırır."),
    };

    // ── 영수증 (Receipt) keyword tooltip (14 languages). Shown on cards that SPEND Receipts, explaining that you
    //    earn them by making Payments and this card consumes them. Injected as DEBT_RECEIPT.* in the "relics" table.
    internal static PayRow ReceiptFor(string? lang)
        => lang != null && ReceiptByLang.TryGetValue(lang, out var r) ? r : ReceiptByLang["eng"];

    private static readonly Dictionary<string, PayRow> ReceiptByLang = new()
    {
        ["eng"] = new("Receipt", "You earn 1 [gold]Receipt[/gold] every time you make a payment. This card spends [gold]Receipts[/gold]."),
        ["kor"] = new("영수증", "납부할 때마다 [gold]영수증[/gold]을 1개 얻습니다. 이 카드는 [gold]영수증[/gold]을 소비합니다."),
        ["jpn"] = new("レシート", "支払うたびに[gold]レシート[/gold]を1枚得る。このカードは[gold]レシート[/gold]を消費する。"),
        ["zhs"] = new("收据", "每次还款都会获得1张[gold]收据[/gold]。此牌会消耗[gold]收据[/gold]。"),
        ["deu"] = new("Beleg", "Bei jeder Zahlung erhältst du 1 [gold]Beleg[/gold]. Diese Karte verbraucht [gold]Belege[/gold]."),
        ["fra"] = new("Reçu", "Chaque paiement te rapporte 1 [gold]reçu[/gold]. Cette carte dépense des [gold]reçus[/gold]."),
        ["spa"] = new("Recibo", "Cada pago te otorga 1 [gold]recibo[/gold]. Esta carta gasta [gold]recibos[/gold]."),
        ["esp"] = new("Recibo", "Cada pago te otorga 1 [gold]recibo[/gold]. Esta carta gasta [gold]recibos[/gold]."),
        ["ita"] = new("Ricevuta", "Ogni pagamento ti dà 1 [gold]ricevuta[/gold]. Questa carta consuma [gold]ricevute[/gold]."),
        ["pol"] = new("Paragon", "Każda spłata daje 1 [gold]paragon[/gold]. Ta karta zużywa [gold]paragony[/gold]."),
        ["ptb"] = new("Recibo", "Cada pagamento rende 1 [gold]recibo[/gold]. Esta carta gasta [gold]recibos[/gold]."),
        ["rus"] = new("Чек", "Каждый платёж приносит 1 [gold]чек[/gold]. Эта карта тратит [gold]чеки[/gold]."),
        ["tha"] = new("ใบเสร็จ", "ทุกครั้งที่ชำระเงินจะได้รับ[gold]ใบเสร็จ[/gold] 1 ใบ การ์ดนี้ใช้[gold]ใบเสร็จ[/gold]"),
        ["tur"] = new("Makbuz", "Her ödemede 1 [gold]makbuz[/gold] kazanırsın. Bu kart [gold]makbuz[/gold] harcar."),
    };

    // ── Receipt COUNTER hover tip (HUD counter, like the game's STAR_COUNT/ENERGY_COUNT): a "current count" phrasing.
    //    Same title as the receipt keyword; description injected as DEBT_RECEIPT_COUNT.description.
    internal static string ReceiptCountFor(string? lang)
        => lang != null && ReceiptCountByLang.TryGetValue(lang, out var d) ? d : ReceiptCountByLang["eng"];

    private static readonly Dictionary<string, string> ReceiptCountByLang = new()
    {
        ["eng"] = "Your current [gold]Receipt[/gold] count. You earn 1 with every payment, and some cards spend Receipts.",
        ["kor"] = "현재 보유한 [gold]영수증[/gold] 개수. 납부할 때마다 1개 얻으며, 일부 카드가 영수증을 소비합니다.",
        ["jpn"] = "現在の[gold]レシート[/gold]の数。支払うたびに1枚得られ、一部のカードがレシートを消費する。",
        ["zhs"] = "你当前的[gold]收据[/gold]数量。每次还款获得1张，部分卡牌会消耗收据。",
        ["deu"] = "Deine aktuelle Anzahl an [gold]Belegen[/gold]. Du erhältst 1 pro Zahlung; manche Karten verbrauchen Belege.",
        ["fra"] = "Ton nombre actuel de [gold]reçus[/gold]. Tu en gagnes 1 par paiement ; certaines cartes dépensent des reçus.",
        ["spa"] = "Tu cantidad actual de [gold]recibos[/gold]. Ganas 1 con cada pago; algunas cartas gastan recibos.",
        ["esp"] = "Tu cantidad actual de [gold]recibos[/gold]. Ganas 1 con cada pago; algunas cartas gastan recibos.",
        ["ita"] = "Il tuo numero attuale di [gold]ricevute[/gold]. Ne guadagni 1 a ogni pagamento; alcune carte consumano ricevute.",
        ["pol"] = "Twoja aktualna liczba [gold]paragonów[/gold]. Zdobywasz 1 za każdą spłatę; niektóre karty zużywają paragony.",
        ["ptb"] = "Sua quantidade atual de [gold]recibos[/gold]. Você ganha 1 a cada pagamento; algumas cartas gastam recibos.",
        ["rus"] = "Ваше текущее количество [gold]чеков[/gold]. Вы получаете 1 за каждый платёж; некоторые карты тратят чеки.",
        ["tha"] = "จำนวน[gold]ใบเสร็จ[/gold]ที่คุณมีตอนนี้ ได้รับ 1 ใบทุกครั้งที่ชำระเงิน การ์ดบางใบใช้ใบเสร็จ",
        ["tur"] = "Mevcut [gold]makbuz[/gold] sayın. Her ödemede 1 kazanırsın; bazı kartlar makbuz harcar.",
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
        ["eng"] = new("Repay Loan", "Pay back {0} gold to retire the Debt Ledger and clear all Debt cards.", "Not enough gold — you owe {0}.", "No loan to repay."),
        ["kor"] = new("빚 갚기", "{0} 골드를 갚아 빚 장부를 반납하고 모든 빚 카드를 제거합니다.", "골드가 부족합니다 — {0} 골드를 빚지고 있습니다.", "갚을 빚이 없습니다."),
        ["jpn"] = new("借金返済", "{0} ゴールドを返済して「借金台帳」を手放し、すべての借金カードを取り除く。", "ゴールドが足りない — {0} の借りがある。", "返済する借金がない。"),
        ["zhs"] = new("偿还贷款", "偿还 {0} 金币以归还债务账簿，并清除所有债务牌。", "金币不足——你欠 {0}。", "没有需要偿还的贷款。"),
        ["deu"] = new("Kredit zurückzahlen", "Zahle {0} Gold zurück, um das Schuldenbuch abzugeben und alle Schuldkarten zu entfernen.", "Nicht genug Gold — du schuldest {0}.", "Kein Kredit zum Zurückzahlen."),
        ["fra"] = new("Rembourser le prêt", "Rembourse {0} or pour rendre le Grand livre des dettes et retirer toutes les cartes de Dette.", "Pas assez d'or — tu dois {0}.", "Aucun prêt à rembourser."),
        ["spa"] = new("Saldar préstamo", "Paga {0} de oro para devolver el Libro de deudas y eliminar todas las cartas de Deuda.", "No tienes suficiente oro — debes {0}.", "No hay préstamo que saldar."),
        ["esp"] = new("Saldar préstamo", "Paga {0} de oro para devolver el Libro de deudas y eliminar todas las cartas de Deuda.", "No tienes suficiente oro — debes {0}.", "No hay préstamo que saldar."),
        ["ita"] = new("Ripaga il prestito", "Ripaga {0} oro per restituire il Registro dei debiti e rimuovere tutte le carte Debito.", "Oro insufficiente — devi {0}.", "Nessun prestito da ripagare."),
        ["pol"] = new("Spłać pożyczkę", "Spłać {0} złota, aby oddać Księgę długów i usunąć wszystkie karty Długu.", "Za mało złota — jesteś winien {0}.", "Brak pożyczki do spłaty."),
        ["ptb"] = new("Quitar empréstimo", "Pague {0} de ouro para devolver o Livro-razão de dívidas e remover todas as cartas de Dívida.", "Ouro insuficiente — você deve {0}.", "Nenhum empréstimo para quitar."),
        ["rus"] = new("Погасить заём", "Верните {0} золота, чтобы сдать Долговую книгу и убрать все карты Долга.", "Недостаточно золота — вы должны {0}.", "Нет займа для погашения."),
        ["tha"] = new("ชำระหนี้", "จ่ายคืน {0} ทองเพื่อคืนบัญชีหนี้และกำจัดการ์ดหนี้ทั้งหมด", "ทองไม่พอ — คุณติดหนี้ {0}", "ไม่มีหนี้ให้ชำระ"),
        ["tur"] = new("Krediyi Öde", "Borç Defteri'ni iade etmek ve tüm Borç kartlarını kaldırmak için {0} altın öde.", "Yeterli altın yok — {0} borcun var.", "Ödenecek kredi yok."),
    };

    // ── Debt-card shop panel UI (Buy on credit). {0} = the card's debt price. ──────────────────────────
    internal readonly struct DebtShopUiRow
    {
        public readonly string Title, Hint, Price, Sold, Close;
        public DebtShopUiRow(string title, string hint, string price, string sold, string close)
        { Title = title; Hint = hint; Price = price; Sold = sold; Close = close; }
    }

    internal static DebtShopUiRow DebtShopUiFor(string? lang)
        => lang != null && DebtShopUiByLang.TryGetValue(lang, out var r) ? r : DebtShopUiByLang["eng"];

    private static readonly Dictionary<string, DebtShopUiRow> DebtShopUiByLang = new()
    {
        ["eng"] = new("Buy on Credit", "Press to enter the debt shop.", "Debt {0}", "SOLD", "Close"),
        ["kor"] = new("외상 구매", "누르면 빚 상점으로 이동됩니다.", "빚 {0}", "품절", "닫기"),
        ["jpn"] = new("ツケで購入", "押すと借金ショップへ移動します。", "借金 {0}", "売切れ", "閉じる"),
        ["zhs"] = new("赊购", "点击进入赊账商店。", "债务 {0}", "售罄", "关闭"),
        ["deu"] = new("Auf Kredit kaufen", "Drücken, um den Schuldenladen zu öffnen.", "Schuld {0}", "VERKAUFT", "Schließen"),
        ["fra"] = new("Acheter à crédit", "Appuyer pour ouvrir la boutique de crédit.", "Dette {0}", "VENDU", "Fermer"),
        ["spa"] = new("Comprar a crédito", "Pulsa para ir a la tienda de crédito.", "Deuda {0}", "VENDIDO", "Cerrar"),
        ["esp"] = new("Comprar a crédito", "Pulsa para ir a la tienda de crédito.", "Deuda {0}", "VENDIDO", "Cerrar"),
        ["ita"] = new("Compra a credito", "Premi per aprire il negozio a credito.", "Debito {0}", "VENDUTO", "Chiudi"),
        ["pol"] = new("Kup na kredyt", "Naciśnij, aby przejść do sklepu na kredyt.", "Dług {0}", "SPRZEDANE", "Zamknij"),
        ["ptb"] = new("Comprar fiado", "Pressione para ir à loja fiado.", "Dívida {0}", "VENDIDO", "Fechar"),
        ["rus"] = new("Купить в долг", "Нажмите, чтобы открыть долговой магазин.", "Долг {0}", "ПРОДАНО", "Закрыть"),
        ["tha"] = new("ซื้อเงินเชื่อ", "กดเพื่อไปยังร้านค้าเงินเชื่อ", "หนี้ {0}", "ขายแล้ว", "ปิด"),
        ["tur"] = new("Borçla Satın Al", "Borç dükkânına gitmek için bas.", "Borç {0}", "SATILDI", "Kapat"),
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
        ["JOB_PLACEMENT_CARD.description"] = "Debt [gold]+{fee} Gold[/gold].\nGain a [gold]{card}[/gold] card now, and one at the start of each turn.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Payment Benefit",
        ["PAYMENT_BENEFIT_CARD.description"] = "Whenever you make a [gold]Payment[/gold], gain [b]{plate}[/b] [gold]Plating[/gold].",
        ["REFUND_CARD.title"] = "Refund",
        ["REFUND_CARD.description"] = "Whenever you make a [gold]Payment[/gold], add a [gold]{card}[/gold] card to your hand.",
        ["COLLECTION_CARD.title"] = "Collections",
        ["COLLECTION_CARD.description"] = "At the start of each turn, add a [gold]{card}[/gold] card to your hand.",
        ["SHAKEDOWN_CARD.title"] = "Execution",
        ["SHAKEDOWN_CARD.description"] = "Gain [b]{VigorPower}[/b] [gold]Vigor[/gold].",
        ["DILIGENT_PAYMENT_CARD.title"] = "Diligent Payment",
        ["DILIGENT_PAYMENT_CARD.description"] = "Gain [gold]Block[/gold] equal to the [gold]Payment[/gold] cards exhausted this combat (currently [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nRefund [gold]5 Gold[/gold].}",
        ["BAILOUT_CARD.title"] = "Bailout",
        ["BAILOUT_CARD.description"] = "Pay [b]20[/b] [gold]Gold[/gold] toward the targeted ally's debt.",
        ["SETTLEMENT_CARD.title"] = "Settlement",
        ["SETTLEMENT_CARD.description"] = "Gain [gold]Block[/gold] equal to [gold]Receipts[/gold] held × [b]{CalculationExtra}[/b] (currently [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Invoice",
        ["INVOICE_CARD.description"] = "Deal [b]{Damage}[/b] damage [b]X[/b] times.",
        ["GARNISHMENT_CARD.title"] = "Distraint",
        ["GARNISHMENT_CARD.description"] = "Deal [b]{Damage}[/b] damage to ALL enemies.",
        ["LOAN_STRIKE_CARD.title"] = "Loan Strike",
        ["LOAN_STRIKE_CARD.description"] = "Deal [b]{Damage}[/b] damage.\nAdd [b]{debt}[/b] to what you owe.",
        ["MORTGAGE_CARD.title"] = "Mortgage",
        ["MORTGAGE_CARD.description"] = "Gain [b]{block}[/b] Block.\nAdd [b]{debt}[/b] to what you owe.",
        ["BLOOD_PAYMENT_CARD.title"] = "Blood Payment",
        ["BLOOD_PAYMENT_CARD.description"] = "Lose [b]{hp}[/b] HP and make a [gold]{pay} Gold[/gold] [gold]Payment[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Money Attack",
        ["COUNTERCLAIM_CARD.description"] = "Whenever you make a [gold]Payment[/gold], deal [b]{dmg}[/b] damage to a random enemy.",
        ["STATEMENT_CARD.title"] = "Statement",
        ["STATEMENT_CARD.description"] = "Whenever you make a [gold]Payment[/gold], draw a card.",
        ["INTEREST_SUPPORT_CARD.title"] = "Interest Support",
        ["INTEREST_SUPPORT_CARD.description"] = "Whenever you make a [gold]Payment[/gold], gain [gold]Gold[/gold] equal to half of it.",
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
        ["JOB_PLACEMENT_CARD.description"] = "빚 [gold]+{fee} 골드[/gold].\n[gold]{card}[/gold] 1장을 지금 손에 넣고, 매 턴 시작 시 [gold]{card}[/gold] 1장을 받습니다.",
        ["PAYMENT_BENEFIT_CARD.title"] = "납부 혜택",
        ["PAYMENT_BENEFIT_CARD.description"] = "[gold]납부[/gold]할 때마다 [gold]판금[/gold] [b]{plate}[/b]을 얻습니다.",
        ["REFUND_CARD.title"] = "환급",
        ["REFUND_CARD.description"] = "[gold]납부[/gold]할 때마다 [gold]{card}[/gold] 카드를 손에 넣습니다.",
        ["COLLECTION_CARD.title"] = "추심",
        ["COLLECTION_CARD.description"] = "매 턴 시작 시 [gold]{card}[/gold] 카드를 손에 넣습니다.",
        ["SHAKEDOWN_CARD.title"] = "집행",
        ["SHAKEDOWN_CARD.description"] = "[gold]활력[/gold] [b]{VigorPower}[/b]을 얻습니다.",
        ["DILIGENT_PAYMENT_CARD.title"] = "성실 납부",
        ["DILIGENT_PAYMENT_CARD.description"] = "소멸된 [gold]납부[/gold] 카드 수만큼 [gold]방어도[/gold]를 얻습니다 (현재 [b]{CalculatedBlock}[/b]).{gold:choose(0):|\n[gold]5 골드[/gold]를 환급합니다.}",
        ["BAILOUT_CARD.title"] = "대납",
        ["BAILOUT_CARD.description"] = "대상 아군의 빚을 [gold]20 골드[/gold]만큼 대신 갚습니다.",
        ["SETTLEMENT_CARD.title"] = "정산",
        ["SETTLEMENT_CARD.description"] = "사용 시 보유한 [gold]영수증[/gold] × [b]{CalculationExtra}[/b]만큼 [gold]방어도[/gold]를 얻습니다 (현재 [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "청구서",
        ["INVOICE_CARD.description"] = "적에게 [b]{Damage}[/b]의 피해를 [b]X[/b]번 줍니다.",
        ["GARNISHMENT_CARD.title"] = "가압류",
        ["GARNISHMENT_CARD.description"] = "모든 적에게 [b]{Damage}[/b]의 피해를 줍니다.",
        ["LOAN_STRIKE_CARD.title"] = "대출 강타",
        ["LOAN_STRIKE_CARD.description"] = "[b]{Damage}[/b]의 피해를 줍니다.\n빚이 [b]{debt}[/b] 늘어납니다.",
        ["MORTGAGE_CARD.title"] = "저당",
        ["MORTGAGE_CARD.description"] = "[b]{block}[/b]의 방어도를 얻습니다.\n빚이 [b]{debt}[/b] 늘어납니다.",
        ["BLOOD_PAYMENT_CARD.title"] = "혈납",
        ["BLOOD_PAYMENT_CARD.description"] = "체력 [b]{hp}[/b]을 잃고 [gold]{pay} 골드[/gold]를 [gold]납부[/gold]합니다.",
        ["COUNTERCLAIM_CARD.title"] = "자본 타격",
        ["COUNTERCLAIM_CARD.description"] = "[gold]납부[/gold]할 때마다 무작위 적에게 [b]{dmg}[/b]의 피해를 줍니다.",
        ["STATEMENT_CARD.title"] = "명세서",
        ["STATEMENT_CARD.description"] = "[gold]납부[/gold]할 때마다 카드를 1장 뽑습니다.",
        ["INTEREST_SUPPORT_CARD.title"] = "이자 지원",
        ["INTEREST_SUPPORT_CARD.description"] = "[gold]납부[/gold]할 때마다 그 절반을 [gold]골드[/gold]로 돌려받습니다.",
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
        ["COLLECTION_POWER.title"] = "Collections",
        ["COLLECTION_POWER.description"] = "At the start of each turn, add an Execution card to your hand.",
        ["COUNTERCLAIM_POWER.title"] = "Money Attack",
        ["COUNTERCLAIM_POWER.description"] = "Whenever you make a Payment, deal 5 damage to a random enemy.",
        ["STATEMENT_POWER.title"] = "Statement",
        ["STATEMENT_POWER.description"] = "Whenever you make a Payment, draw a card.",
        ["INTEREST_SUPPORT_POWER.title"] = "Interest Support",
        ["INTEREST_SUPPORT_POWER.description"] = "Whenever you make a Payment, gain Gold equal to half of it.",
        ["PAYMENT_STACK_POWER.title"] = "Tally",
        ["PAYMENT_STACK_POWER.description"] = "Gains 1 each time you make a Payment.",
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
        ["COLLECTION_POWER.title"] = "추심",
        ["COLLECTION_POWER.description"] = "매 턴 시작 시 집행 카드를 손에 넣습니다.",
        ["COUNTERCLAIM_POWER.title"] = "자본 타격",
        ["COUNTERCLAIM_POWER.description"] = "납부할 때마다 무작위 적에게 5의 피해를 줍니다.",
        ["STATEMENT_POWER.title"] = "명세서",
        ["STATEMENT_POWER.description"] = "납부할 때마다 카드를 1장 뽑습니다.",
        ["INTEREST_SUPPORT_POWER.title"] = "이자 지원",
        ["INTEREST_SUPPORT_POWER.description"] = "납부할 때마다 그 절반을 골드로 돌려받습니다.",
        ["PAYMENT_STACK_POWER.title"] = "영수증",
        ["PAYMENT_STACK_POWER.description"] = "납부할 때마다 영수증을 1개 얻습니다.",
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
        ["JOB_PLACEMENT_CARD.description"] = "借金 [gold]+{fee} ゴールド[/gold]。\n[gold]{card}[/gold]カードを1枚今すぐ、そして各ターン開始時に1枚手札に加える。",
        ["PAYMENT_BENEFIT_CARD.title"] = "支払い特典",
        ["PAYMENT_BENEFIT_CARD.description"] = "[gold]支払い[/gold]を行うたびに、[gold]プレート[/gold] [b]{plate}[/b] を得る。",
        ["REFUND_CARD.title"] = "払い戻し",
        ["REFUND_CARD.description"] = "[gold]支払い[/gold]を行うたびに、[gold]{card}[/gold]カードを手札に加える。",
        ["DILIGENT_PAYMENT_CARD.title"] = "誠実な支払い",
        ["DILIGENT_PAYMENT_CARD.description"] = "この戦闘で消滅した[gold]支払い[/gold]カードの数だけ[gold]ブロック[/gold]を得る（現在 [b]{CalculatedBlock}[/b]）。{gold:choose(0):|\n[gold]5 ゴールド[/gold]を払い戻す。}",
        ["SETTLEMENT_CARD.title"] = "精算",
        ["SETTLEMENT_CARD.description"] = "所持している[gold]レシート[/gold] × [b]{CalculationExtra}[/b] 分の[gold]ブロック[/gold]を得る（現在 [b]{CalculatedBlock}[/b]）。",
        ["INVOICE_CARD.title"] = "請求書",
        ["INVOICE_CARD.description"] = "[b]{Damage}[/b]のダメージを[b]X[/b]回与える。",
        ["GARNISHMENT_CARD.title"] = "仮差押え",
        ["GARNISHMENT_CARD.description"] = "すべての敵に[b]{Damage}[/b]のダメージを与える。",
        ["LOAN_STRIKE_CARD.title"] = "ローン強打",
        ["LOAN_STRIKE_CARD.description"] = "[b]{Damage}[/b]のダメージを与える。\n[b]{debt}[/b]を借金に加える。",
        ["MORTGAGE_CARD.title"] = "抵当",
        ["MORTGAGE_CARD.description"] = "[b]{block}[/b] ブロックを得る。\n[b]{debt}[/b]を借金に加える。",
        ["BLOOD_PAYMENT_CARD.title"] = "血の支払い",
        ["BLOOD_PAYMENT_CARD.description"] = "体力を [b]{hp}[/b] 失い、[gold]{pay} ゴールド[/gold]の[gold]支払い[/gold]を行う。",
        ["COUNTERCLAIM_CARD.title"] = "資本打撃",
        ["COUNTERCLAIM_CARD.description"] = "[gold]支払い[/gold]を行うたびに、ランダムな敵に[b]{dmg}[/b]のダメージを与える。",
        ["STATEMENT_CARD.title"] = "明細書",
        ["STATEMENT_CARD.description"] = "[gold]支払い[/gold]を行うたびに、カードを1枚引く。",
        ["INTEREST_SUPPORT_CARD.title"] = "利息補助",
        ["INTEREST_SUPPORT_CARD.description"] = "[gold]支払い[/gold]を行うたびに、その半分の[gold]ゴールド[/gold]を得る。",
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
        ["JOB_PLACEMENT_CARD.description"] = "债务 [gold]+{fee} 金币[/gold]。\n立即将一张[gold]{card}[/gold]牌加入手牌，之后每回合开始时再加入一张。",
        ["PAYMENT_BENEFIT_CARD.title"] = "还款福利",
        ["PAYMENT_BENEFIT_CARD.description"] = "每次[gold]还款[/gold]时，获得 [b]{plate}[/b] [gold]覆甲[/gold]。",
        ["REFUND_CARD.title"] = "退款",
        ["REFUND_CARD.description"] = "每次[gold]还款[/gold]时，将一张[gold]{card}[/gold]牌加入手牌。",
        ["DILIGENT_PAYMENT_CARD.title"] = "按时还款",
        ["DILIGENT_PAYMENT_CARD.description"] = "获得等同于本次战斗中已消耗[gold]还款[/gold]牌数量的[gold]格挡[/gold]（当前 [b]{CalculatedBlock}[/b]）。{gold:choose(0):|\n返还 [gold]5 金币[/gold]。}",
        ["SETTLEMENT_CARD.title"] = "结算",
        ["SETTLEMENT_CARD.description"] = "获得等同于你持有的[gold]收据[/gold] × [b]{CalculationExtra}[/b] 的[gold]格挡[/gold]（当前 [b]{CalculatedBlock}[/b]）。",
        ["INVOICE_CARD.title"] = "账单",
        ["INVOICE_CARD.description"] = "造成 [b]{Damage}[/b] 点伤害，共 [b]X[/b] 次。",
        ["GARNISHMENT_CARD.title"] = "查封",
        ["GARNISHMENT_CARD.description"] = "对所有敌人造成 [b]{Damage}[/b] 点伤害。",
        ["LOAN_STRIKE_CARD.title"] = "贷款重击",
        ["LOAN_STRIKE_CARD.description"] = "造成 [b]{Damage}[/b] 点伤害。\n将 [b]{debt}[/b] 计入你的债务。",
        ["MORTGAGE_CARD.title"] = "抵押",
        ["MORTGAGE_CARD.description"] = "获得 [b]{block}[/b] 格挡。\n将 [b]{debt}[/b] 计入你的债务。",
        ["BLOOD_PAYMENT_CARD.title"] = "血债",
        ["BLOOD_PAYMENT_CARD.description"] = "失去 [b]{hp}[/b] 点生命，并进行一次 [gold]{pay} 金币[/gold] [gold]还款[/gold]。",
        ["COUNTERCLAIM_CARD.title"] = "金钱打击",
        ["COUNTERCLAIM_CARD.description"] = "每次[gold]还款[/gold]时，对随机敌人造成 [b]{dmg}[/b] 点伤害。",
        ["STATEMENT_CARD.title"] = "对账单",
        ["STATEMENT_CARD.description"] = "每次[gold]还款[/gold]时，抽一张牌。",
        ["INTEREST_SUPPORT_CARD.title"] = "利息补贴",
        ["INTEREST_SUPPORT_CARD.description"] = "每次[gold]还款[/gold]时，获得其一半的[gold]金币[/gold]。",
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
        ["JOB_PLACEMENT_CARD.description"] = "Schuld [gold]+{fee} Gold[/gold].\nErhalte jetzt eine [gold]{card}[/gold]-Karte und zu Beginn jeder Runde eine weitere.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Zahlungsvorteil",
        ["PAYMENT_BENEFIT_CARD.description"] = "Immer wenn du eine [gold]Zahlung[/gold] leistest, erhalte [b]{plate}[/b] [gold]Panzerung[/gold].",
        ["REFUND_CARD.title"] = "Rückerstattung",
        ["REFUND_CARD.description"] = "Immer wenn du eine [gold]Zahlung[/gold] leistest, lege eine [gold]{card}[/gold]-Karte auf deine Hand.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pünktliche Zahlung",
        ["DILIGENT_PAYMENT_CARD.description"] = "Erhalte [gold]Block[/gold] gleich der Anzahl der in diesem Kampf verbrauchten [gold]Zahlung[/gold]-Karten (aktuell [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nErstatte [gold]5 Gold[/gold] zurück.}",
        ["SETTLEMENT_CARD.title"] = "Abrechnung",
        ["SETTLEMENT_CARD.description"] = "Erhalte [gold]Block[/gold] gleich gehaltenen [gold]Belegen[/gold] × [b]{CalculationExtra}[/b] (aktuell [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Rechnung",
        ["INVOICE_CARD.description"] = "Füge [b]{Damage}[/b] Schaden [b]X[/b]-mal zu.",
        ["GARNISHMENT_CARD.title"] = "Zwangspfändung",
        ["GARNISHMENT_CARD.description"] = "Füge allen Gegnern [b]{Damage}[/b] Schaden zu.",
        ["LOAN_STRIKE_CARD.title"] = "Kreditschlag",
        ["LOAN_STRIKE_CARD.description"] = "Füge [b]{Damage}[/b] Schaden zu.\nFüge deiner Schuld [b]{debt}[/b] hinzu.",
        ["MORTGAGE_CARD.title"] = "Hypothek",
        ["MORTGAGE_CARD.description"] = "Erhalte [b]{block}[/b] Block.\nFüge deiner Schuld [b]{debt}[/b] hinzu.",
        ["BLOOD_PAYMENT_CARD.title"] = "Blutzahlung",
        ["BLOOD_PAYMENT_CARD.description"] = "Verliere [b]{hp}[/b] LP und leiste eine [gold]Zahlung[/gold] von [gold]{pay} Gold[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Geldangriff",
        ["COUNTERCLAIM_CARD.description"] = "Immer wenn du eine [gold]Zahlung[/gold] leistest, füge einem zufälligen Gegner [b]{dmg}[/b] Schaden zu.",
        ["STATEMENT_CARD.title"] = "Kontoauszug",
        ["STATEMENT_CARD.description"] = "Immer wenn du eine [gold]Zahlung[/gold] leistest, ziehe eine Karte.",
        ["INTEREST_SUPPORT_CARD.title"] = "Zinszuschuss",
        ["INTEREST_SUPPORT_CARD.description"] = "Immer wenn du eine [gold]Zahlung[/gold] leistest, erhalte [gold]Gold[/gold] in Höhe der Hälfte davon.",
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
        ["JOB_PLACEMENT_CARD.description"] = "Dette [gold]+{fee} or[/gold].\nGagne une carte [gold]{card}[/gold] maintenant, et une au début de chaque tour.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Prime de paiement",
        ["PAYMENT_BENEFIT_CARD.description"] = "Chaque fois que tu effectues un [gold]Paiement[/gold], gagne [b]{plate}[/b] [gold]Blindage[/gold].",
        ["REFUND_CARD.title"] = "Remboursement",
        ["REFUND_CARD.description"] = "Chaque fois que tu effectues un [gold]Paiement[/gold], ajoute une carte [gold]{card}[/gold] à ta main.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Paiement assidu",
        ["DILIGENT_PAYMENT_CARD.description"] = "Gagne de l'[gold]Armure[/gold] égale au nombre de cartes [gold]Paiement[/gold] épuisées ce combat (actuellement [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nRembourse [gold]5 or[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Règlement",
        ["SETTLEMENT_CARD.description"] = "Gagne de l'[gold]Armure[/gold] égale aux [gold]reçus[/gold] possédés × [b]{CalculationExtra}[/b] (actuellement [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Facture",
        ["INVOICE_CARD.description"] = "Inflige [b]{Damage}[/b] dégâts [b]X[/b] fois.",
        ["GARNISHMENT_CARD.title"] = "Saisie conservatoire",
        ["GARNISHMENT_CARD.description"] = "Inflige [b]{Damage}[/b] dégâts à tous les ennemis.",
        ["LOAN_STRIKE_CARD.title"] = "Frappe à crédit",
        ["LOAN_STRIKE_CARD.description"] = "Inflige [b]{Damage}[/b] dégâts.\nAjoute [b]{debt}[/b] à ta dette.",
        ["MORTGAGE_CARD.title"] = "Hypothèque",
        ["MORTGAGE_CARD.description"] = "Gagne [b]{block}[/b] Armure.\nAjoute [b]{debt}[/b] à ta dette.",
        ["BLOOD_PAYMENT_CARD.title"] = "Paiement de sang",
        ["BLOOD_PAYMENT_CARD.description"] = "Perds [b]{hp}[/b] PV et effectue un [gold]Paiement[/gold] de [gold]{pay} or[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Attaque financière",
        ["COUNTERCLAIM_CARD.description"] = "Chaque fois que tu effectues un [gold]Paiement[/gold], inflige [b]{dmg}[/b] dégâts à un ennemi au hasard.",
        ["STATEMENT_CARD.title"] = "Relevé",
        ["STATEMENT_CARD.description"] = "Chaque fois que tu effectues un [gold]Paiement[/gold], pioche une carte.",
        ["INTEREST_SUPPORT_CARD.title"] = "Aide aux intérêts",
        ["INTEREST_SUPPORT_CARD.description"] = "Chaque fois que tu effectues un [gold]Paiement[/gold], gagne [gold]or[/gold] égal à la moitié de celui-ci.",
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
        ["JOB_PLACEMENT_CARD.description"] = "Deuda [gold]+{fee} de oro[/gold].\nGana una carta de [gold]{card}[/gold] ahora y otra al inicio de cada turno.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Beneficio de pago",
        ["PAYMENT_BENEFIT_CARD.description"] = "Cada vez que haces un [gold]Pago[/gold], ganas [b]{plate}[/b] de [gold]Blindaje[/gold].",
        ["REFUND_CARD.title"] = "Reembolso",
        ["REFUND_CARD.description"] = "Cada vez que haces un [gold]Pago[/gold], añade una carta de [gold]{card}[/gold] a tu mano.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pago diligente",
        ["DILIGENT_PAYMENT_CARD.description"] = "Gana [gold]Bloqueo[/gold] igual al número de cartas de [gold]Pago[/gold] agotadas en este combate (actualmente [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nReembolsa [gold]5 de oro[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Liquidación",
        ["SETTLEMENT_CARD.description"] = "Gana [gold]Bloqueo[/gold] igual a los [gold]recibos[/gold] que tengas × [b]{CalculationExtra}[/b] (actualmente [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Factura",
        ["INVOICE_CARD.description"] = "Inflige [b]{Damage}[/b] de daño [b]X[/b] veces.",
        ["GARNISHMENT_CARD.title"] = "Embargo preventivo",
        ["GARNISHMENT_CARD.description"] = "Inflige [b]{Damage}[/b] de daño a todos los enemigos.",
        ["LOAN_STRIKE_CARD.title"] = "Golpe a crédito",
        ["LOAN_STRIKE_CARD.description"] = "Inflige [b]{Damage}[/b] de daño.\nAñade [b]{debt}[/b] a tu deuda.",
        ["MORTGAGE_CARD.title"] = "Hipoteca",
        ["MORTGAGE_CARD.description"] = "Gana [b]{block}[/b] de Bloqueo.\nAñade [b]{debt}[/b] a tu deuda.",
        ["BLOOD_PAYMENT_CARD.title"] = "Pago de sangre",
        ["BLOOD_PAYMENT_CARD.description"] = "Pierde [b]{hp}[/b] de vida y haz un [gold]Pago[/gold] de [gold]{pay} de oro[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Ataque monetario",
        ["COUNTERCLAIM_CARD.description"] = "Cada vez que haces un [gold]Pago[/gold], inflige [b]{dmg}[/b] de daño a un enemigo al azar.",
        ["STATEMENT_CARD.title"] = "Extracto",
        ["STATEMENT_CARD.description"] = "Cada vez que haces un [gold]Pago[/gold], roba una carta.",
        ["INTEREST_SUPPORT_CARD.title"] = "Ayuda de intereses",
        ["INTEREST_SUPPORT_CARD.description"] = "Cada vez que haces un [gold]Pago[/gold], ganas [gold]oro[/gold] igual a la mitad del mismo.",
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
        ["JOB_PLACEMENT_CARD.description"] = "Debito [gold]+{fee} Oro[/gold].\nOttieni una carta [gold]{card}[/gold] ora e una all'inizio di ogni turno.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Beneficio pagamento",
        ["PAYMENT_BENEFIT_CARD.description"] = "Ogni volta che effettui un [gold]Pagamento[/gold], ottieni [b]{plate}[/b] [gold]Placcatura[/gold].",
        ["REFUND_CARD.title"] = "Rimborso",
        ["REFUND_CARD.description"] = "Ogni volta che effettui un [gold]Pagamento[/gold], aggiungi una carta [gold]{card}[/gold] alla tua mano.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pagamento diligente",
        ["DILIGENT_PAYMENT_CARD.description"] = "Ottieni [gold]Blocco[/gold] pari al numero di carte [gold]Pagamento[/gold] consumate in questo combattimento (attualmente [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nRimborsa [gold]5 Oro[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Saldo",
        ["SETTLEMENT_CARD.description"] = "Ottieni [gold]Blocco[/gold] pari alle [gold]ricevute[/gold] possedute × [b]{CalculationExtra}[/b] (attualmente [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Fattura",
        ["INVOICE_CARD.description"] = "Infliggi [b]{Damage}[/b] danni [b]X[/b] volte.",
        ["GARNISHMENT_CARD.title"] = "Sequestro",
        ["GARNISHMENT_CARD.description"] = "Infliggi [b]{Damage}[/b] danni a tutti i nemici.",
        ["LOAN_STRIKE_CARD.title"] = "Colpo a credito",
        ["LOAN_STRIKE_CARD.description"] = "Infliggi [b]{Damage}[/b] danni.\nAggiungi [b]{debt}[/b] al tuo debito.",
        ["MORTGAGE_CARD.title"] = "Ipoteca",
        ["MORTGAGE_CARD.description"] = "Ottieni [b]{block}[/b] Blocco.\nAggiungi [b]{debt}[/b] al tuo debito.",
        ["BLOOD_PAYMENT_CARD.title"] = "Pagamento di sangue",
        ["BLOOD_PAYMENT_CARD.description"] = "Perdi [b]{hp}[/b] PV ed effettua un [gold]Pagamento[/gold] di [gold]{pay} Oro[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Attacco monetario",
        ["COUNTERCLAIM_CARD.description"] = "Ogni volta che effettui un [gold]Pagamento[/gold], infliggi [b]{dmg}[/b] danni a un nemico casuale.",
        ["STATEMENT_CARD.title"] = "Estratto conto",
        ["STATEMENT_CARD.description"] = "Ogni volta che effettui un [gold]Pagamento[/gold], pesca una carta.",
        ["INTEREST_SUPPORT_CARD.title"] = "Sostegno agli interessi",
        ["INTEREST_SUPPORT_CARD.description"] = "Ogni volta che effettui un [gold]Pagamento[/gold], ottieni [gold]Oro[/gold] pari alla metà di esso.",
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
        ["JOB_PLACEMENT_CARD.description"] = "Dług [gold]+{fee} złota[/gold].\nZyskaj kartę [gold]{card}[/gold] teraz i jedną na początku każdej tury.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Premia za spłatę",
        ["PAYMENT_BENEFIT_CARD.description"] = "Za każdym razem, gdy dokonasz [gold]Spłaty[/gold], zyskaj [b]{plate}[/b] [gold]Opancerzenia[/gold].",
        ["REFUND_CARD.title"] = "Zwrot",
        ["REFUND_CARD.description"] = "Za każdym razem, gdy dokonasz [gold]Spłaty[/gold], dodaj kartę [gold]{card}[/gold] do ręki.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Sumienna spłata",
        ["DILIGENT_PAYMENT_CARD.description"] = "Zyskaj [gold]Blok[/gold] równy liczbie kart [gold]Spłata[/gold] zużytych w tej walce (obecnie [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nZwróć [gold]5 złota[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Rozliczenie",
        ["SETTLEMENT_CARD.description"] = "Zyskaj [gold]Blok[/gold] równy posiadanym [gold]paragonom[/gold] × [b]{CalculationExtra}[/b] (obecnie [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Faktura",
        ["INVOICE_CARD.description"] = "Zadaj [b]{Damage}[/b] obrażeń [b]X[/b] razy.",
        ["GARNISHMENT_CARD.title"] = "Zajęcie komornicze",
        ["GARNISHMENT_CARD.description"] = "Zadaj [b]{Damage}[/b] obrażeń wszystkim wrogom.",
        ["LOAN_STRIKE_CARD.title"] = "Cios na kredyt",
        ["LOAN_STRIKE_CARD.description"] = "Zadaj [b]{Damage}[/b] obrażeń.\nDodaj [b]{debt}[/b] do swojego długu.",
        ["MORTGAGE_CARD.title"] = "Hipoteka",
        ["MORTGAGE_CARD.description"] = "Zyskaj [b]{block}[/b] Bloku.\nDodaj [b]{debt}[/b] do swojego długu.",
        ["BLOOD_PAYMENT_CARD.title"] = "Krwawa spłata",
        ["BLOOD_PAYMENT_CARD.description"] = "Strać [b]{hp}[/b] PŻ i dokonaj [gold]Spłaty[/gold] [gold]{pay} złota[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Atak pieniężny",
        ["COUNTERCLAIM_CARD.description"] = "Za każdym razem, gdy dokonasz [gold]Spłaty[/gold], zadaj [b]{dmg}[/b] obrażeń losowemu wrogowi.",
        ["STATEMENT_CARD.title"] = "Wyciąg",
        ["STATEMENT_CARD.description"] = "Za każdym razem, gdy dokonasz [gold]Spłaty[/gold], dobierz kartę.",
        ["INTEREST_SUPPORT_CARD.title"] = "Dopłata do odsetek",
        ["INTEREST_SUPPORT_CARD.description"] = "Za każdym razem, gdy dokonasz [gold]Spłaty[/gold], zyskaj [gold]złoto[/gold] równe połowie tej kwoty.",
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
        ["JOB_PLACEMENT_CARD.description"] = "Dívida [gold]+{fee} de Ouro[/gold].\nGanhe uma carta de [gold]{card}[/gold] agora e uma no início de cada turno.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Benefício de pagamento",
        ["PAYMENT_BENEFIT_CARD.description"] = "Sempre que fizer um [gold]Pagamento[/gold], ganhe [b]{plate}[/b] de [gold]Blindagem[/gold].",
        ["REFUND_CARD.title"] = "Reembolso",
        ["REFUND_CARD.description"] = "Sempre que fizer um [gold]Pagamento[/gold], adicione uma carta de [gold]{card}[/gold] à sua mão.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Pagamento pontual",
        ["DILIGENT_PAYMENT_CARD.description"] = "Ganhe [gold]Proteção[/gold] igual ao número de cartas de [gold]Pagamento[/gold] exauridas neste combate (atualmente [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nReembolsa [gold]5 de Ouro[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Acerto",
        ["SETTLEMENT_CARD.description"] = "Ganhe [gold]Proteção[/gold] igual aos [gold]recibos[/gold] que você possui × [b]{CalculationExtra}[/b] (atualmente [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Fatura",
        ["INVOICE_CARD.description"] = "Cause [b]{Damage}[/b] de dano [b]X[/b] vezes.",
        ["GARNISHMENT_CARD.title"] = "Arresto",
        ["GARNISHMENT_CARD.description"] = "Cause [b]{Damage}[/b] de dano a todos os inimigos.",
        ["LOAN_STRIKE_CARD.title"] = "Golpe a crédito",
        ["LOAN_STRIKE_CARD.description"] = "Cause [b]{Damage}[/b] de dano.\nAdicione [b]{debt}[/b] à sua dívida.",
        ["MORTGAGE_CARD.title"] = "Hipoteca",
        ["MORTGAGE_CARD.description"] = "Ganhe [b]{block}[/b] de Proteção.\nAdicione [b]{debt}[/b] à sua dívida.",
        ["BLOOD_PAYMENT_CARD.title"] = "Pagamento de sangue",
        ["BLOOD_PAYMENT_CARD.description"] = "Perca [b]{hp}[/b] de Vida e faça um [gold]Pagamento[/gold] de [gold]{pay} de Ouro[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Ataque monetário",
        ["COUNTERCLAIM_CARD.description"] = "Sempre que fizer um [gold]Pagamento[/gold], cause [b]{dmg}[/b] de dano a um inimigo aleatório.",
        ["STATEMENT_CARD.title"] = "Extrato",
        ["STATEMENT_CARD.description"] = "Sempre que fizer um [gold]Pagamento[/gold], compre uma carta.",
        ["INTEREST_SUPPORT_CARD.title"] = "Auxílio de juros",
        ["INTEREST_SUPPORT_CARD.description"] = "Sempre que fizer um [gold]Pagamento[/gold], ganhe [gold]Ouro[/gold] igual à metade dele.",
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
        ["JOB_PLACEMENT_CARD.description"] = "Долг [gold]+{fee} золота[/gold].\nПолучите карту [gold]{card}[/gold] сейчас и ещё одну в начале каждого хода.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Бонус за платёж",
        ["PAYMENT_BENEFIT_CARD.description"] = "Каждый раз, когда вы совершаете [gold]Платёж[/gold], получите [b]{plate}[/b] [gold]Панциря[/gold].",
        ["REFUND_CARD.title"] = "Возврат",
        ["REFUND_CARD.description"] = "Каждый раз, когда вы совершаете [gold]Платёж[/gold], добавьте карту [gold]{card}[/gold] в руку.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Исправный платёж",
        ["DILIGENT_PAYMENT_CARD.description"] = "Получите [gold]Защиту[/gold], равную числу карт [gold]Платёж[/gold], истощённых в этом бою (сейчас [b]{CalculatedBlock}[/b]).{gold:choose(0):|\nВозврат [gold]5 золота[/gold].}",
        ["SETTLEMENT_CARD.title"] = "Расчёт",
        ["SETTLEMENT_CARD.description"] = "Получите [gold]Защиту[/gold], равную имеющимся [gold]чекам[/gold] × [b]{CalculationExtra}[/b] (сейчас [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Счёт",
        ["INVOICE_CARD.description"] = "Нанесите [b]{Damage}[/b] урона [b]X[/b] раз.",
        ["GARNISHMENT_CARD.title"] = "Опись имущества",
        ["GARNISHMENT_CARD.description"] = "Нанесите [b]{Damage}[/b] урона всем врагам.",
        ["LOAN_STRIKE_CARD.title"] = "Удар в кредит",
        ["LOAN_STRIKE_CARD.description"] = "Нанесите [b]{Damage}[/b] урона.\nДобавьте [b]{debt}[/b] к вашему долгу.",
        ["MORTGAGE_CARD.title"] = "Ипотека",
        ["MORTGAGE_CARD.description"] = "Получите [b]{block}[/b] Защиты.\nДобавьте [b]{debt}[/b] к вашему долгу.",
        ["BLOOD_PAYMENT_CARD.title"] = "Кровавый платёж",
        ["BLOOD_PAYMENT_CARD.description"] = "Потеряйте [b]{hp}[/b] здоровья и совершите [gold]Платёж[/gold] в [gold]{pay} золота[/gold].",
        ["COUNTERCLAIM_CARD.title"] = "Денежная атака",
        ["COUNTERCLAIM_CARD.description"] = "Каждый раз, когда вы совершаете [gold]Платёж[/gold], нанесите [b]{dmg}[/b] урона случайному врагу.",
        ["STATEMENT_CARD.title"] = "Выписка",
        ["STATEMENT_CARD.description"] = "Каждый раз, когда вы совершаете [gold]Платёж[/gold], возьмите карту.",
        ["INTEREST_SUPPORT_CARD.title"] = "Субсидия процентов",
        ["INTEREST_SUPPORT_CARD.description"] = "Каждый раз, когда вы совершаете [gold]Платёж[/gold], получите [gold]золото[/gold] в размере его половины.",
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
        ["JOB_PLACEMENT_CARD.description"] = "หนี้ [gold]+{fee} ทอง[/gold]\nรับการ์ด[gold]{card}[/gold]ทันทีหนึ่งใบ และอีกหนึ่งใบเมื่อเริ่มแต่ละเทิร์น",
        ["PAYMENT_BENEFIT_CARD.title"] = "สิทธิประโยชน์การชำระ",
        ["PAYMENT_BENEFIT_CARD.description"] = "ทุกครั้งที่คุณทำ[gold]การชำระ[/gold] รับ[gold]เกราะโลหะ[/gold] [b]{plate}[/b]",
        ["REFUND_CARD.title"] = "เงินคืน",
        ["REFUND_CARD.description"] = "ทุกครั้งที่คุณทำ[gold]การชำระ[/gold] เพิ่มการ์ด[gold]{card}[/gold]เข้ามือ",
        ["DILIGENT_PAYMENT_CARD.title"] = "ชำระตรงเวลา",
        ["DILIGENT_PAYMENT_CARD.description"] = "รับ[gold]บล็อก[/gold]เท่ากับจำนวนการ์ด[gold]การชำระ[/gold]ที่ถูกเผาไหม้ในการต่อสู้นี้ (ปัจจุบัน [b]{CalculatedBlock}[/b]){gold:choose(0):|\nคืน [gold]5 ทอง[/gold]}",
        ["SETTLEMENT_CARD.title"] = "ชำระบัญชี",
        ["SETTLEMENT_CARD.description"] = "รับ[gold]บล็อก[/gold]เท่ากับ[gold]ใบเสร็จ[/gold]ที่ถืออยู่ × [b]{CalculationExtra}[/b] (ปัจจุบัน [b]{CalculatedBlock}[/b])",
        ["INVOICE_CARD.title"] = "ใบแจ้งหนี้",
        ["INVOICE_CARD.description"] = "สร้างความเสียหาย [b]{Damage}[/b] จำนวน [b]X[/b] ครั้ง",
        ["GARNISHMENT_CARD.title"] = "อายัดทรัพย์",
        ["GARNISHMENT_CARD.description"] = "สร้างความเสียหาย [b]{Damage}[/b] แก่ศัตรูทั้งหมด",
        ["LOAN_STRIKE_CARD.title"] = "โจมตีเงินกู้",
        ["LOAN_STRIKE_CARD.description"] = "สร้างความเสียหาย [b]{Damage}[/b]\nเพิ่ม [b]{debt}[/b] เข้าหนี้ของคุณ",
        ["MORTGAGE_CARD.title"] = "จำนอง",
        ["MORTGAGE_CARD.description"] = "รับบล็อก [b]{block}[/b]\nเพิ่ม [b]{debt}[/b] เข้าหนี้ของคุณ",
        ["BLOOD_PAYMENT_CARD.title"] = "จ่ายด้วยเลือด",
        ["BLOOD_PAYMENT_CARD.description"] = "เสียพลังชีวิต [b]{hp}[/b] และทำ[gold]การชำระ[/gold] [gold]{pay} ทอง[/gold]",
        ["COUNTERCLAIM_CARD.title"] = "การโจมตีด้วยเงิน",
        ["COUNTERCLAIM_CARD.description"] = "ทุกครั้งที่คุณทำ[gold]การชำระ[/gold] สร้างความเสียหาย [b]{dmg}[/b] แก่ศัตรูแบบสุ่ม",
        ["STATEMENT_CARD.title"] = "ใบแจ้งยอด",
        ["STATEMENT_CARD.description"] = "ทุกครั้งที่คุณทำ[gold]การชำระ[/gold] จั่วการ์ด 1 ใบ",
        ["INTEREST_SUPPORT_CARD.title"] = "เงินช่วยดอกเบี้ย",
        ["INTEREST_SUPPORT_CARD.description"] = "ทุกครั้งที่คุณทำ[gold]การชำระ[/gold] รับ[gold]ทอง[/gold]เท่ากับครึ่งหนึ่งของยอดนั้น",
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
        ["JOB_PLACEMENT_CARD.description"] = "Borç [gold]+{fee} Altın[/gold].\nHemen bir [gold]{card}[/gold] kartı, her turun başında bir tane daha kazan.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Ödeme Avantajı",
        ["PAYMENT_BENEFIT_CARD.description"] = "Bir [gold]Ödeme[/gold] yaptığında [b]{plate}[/b] [gold]Zırh[/gold] kazan.",
        ["REFUND_CARD.title"] = "İade",
        ["REFUND_CARD.description"] = "Bir [gold]Ödeme[/gold] yaptığında eline bir [gold]{card}[/gold] kartı ekle.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Özenli Ödeme",
        ["DILIGENT_PAYMENT_CARD.description"] = "Bu savaşta tükenen [gold]Ödeme[/gold] kartı sayısı kadar [gold]Blok[/gold] kazan (şu an [b]{CalculatedBlock}[/b]).{gold:choose(0):|\n[gold]5 Altın[/gold] iade et.}",
        ["SETTLEMENT_CARD.title"] = "Hesaplaşma",
        ["SETTLEMENT_CARD.description"] = "Elinde tuttuğun [gold]makbuz[/gold] × [b]{CalculationExtra}[/b] kadar [gold]Blok[/gold] kazan (şu an [b]{CalculatedBlock}[/b]).",
        ["INVOICE_CARD.title"] = "Fatura",
        ["INVOICE_CARD.description"] = "[b]{Damage}[/b] hasarı [b]X[/b] kez ver.",
        ["GARNISHMENT_CARD.title"] = "İhtiyati Haciz",
        ["GARNISHMENT_CARD.description"] = "Tüm düşmanlara [b]{Damage}[/b] hasar ver.",
        ["LOAN_STRIKE_CARD.title"] = "Kredi Darbesi",
        ["LOAN_STRIKE_CARD.description"] = "[b]{Damage}[/b] hasar ver.\nBorcuna [b]{debt}[/b] ekle.",
        ["MORTGAGE_CARD.title"] = "İpotek",
        ["MORTGAGE_CARD.description"] = "[b]{block}[/b] Blok kazan.\nBorcuna [b]{debt}[/b] ekle.",
        ["BLOOD_PAYMENT_CARD.title"] = "Kan Ödemesi",
        ["BLOOD_PAYMENT_CARD.description"] = "[b]{hp}[/b] Can kaybet ve [gold]{pay} Altın[/gold] [gold]Ödeme[/gold] yap.",
        ["COUNTERCLAIM_CARD.title"] = "Para Saldırısı",
        ["COUNTERCLAIM_CARD.description"] = "Bir [gold]Ödeme[/gold] yaptığında rastgele bir düşmana [b]{dmg}[/b] hasar ver.",
        ["STATEMENT_CARD.title"] = "Ekstre",
        ["STATEMENT_CARD.description"] = "Bir [gold]Ödeme[/gold] yaptığında bir kart çek.",
        ["INTEREST_SUPPORT_CARD.title"] = "Faiz Desteği",
        ["INTEREST_SUPPORT_CARD.description"] = "Bir [gold]Ödeme[/gold] yaptığında, onun yarısı kadar [gold]Altın[/gold] kazan.",
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
        ["COUNTERCLAIM_POWER.title"] = "資本打撃",
        ["COUNTERCLAIM_POWER.description"] = "支払いを行うたびに、ランダムな敵に5のダメージを与える。",
        ["STATEMENT_POWER.title"] = "明細書",
        ["STATEMENT_POWER.description"] = "支払いを行うたびに、カードを1枚引く。",
        ["INTEREST_SUPPORT_POWER.title"] = "利息補助",
        ["INTEREST_SUPPORT_POWER.description"] = "支払いを行うたびに、その半分のゴールドを得る。",
        ["PAYMENT_STACK_POWER.title"] = "支払い実績",
        ["PAYMENT_STACK_POWER.description"] = "支払いを行うたびに1増える。",
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
        ["COUNTERCLAIM_POWER.title"] = "金钱打击",
        ["COUNTERCLAIM_POWER.description"] = "每次还款时，对随机敌人造成 5 点伤害。",
        ["STATEMENT_POWER.title"] = "对账单",
        ["STATEMENT_POWER.description"] = "每次还款时，抽一张牌。",
        ["INTEREST_SUPPORT_POWER.title"] = "利息补贴",
        ["INTEREST_SUPPORT_POWER.description"] = "每次还款时，获得其一半的金币。",
        ["PAYMENT_STACK_POWER.title"] = "还款记录",
        ["PAYMENT_STACK_POWER.description"] = "每次还款时增加 1。",
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
        ["COUNTERCLAIM_POWER.title"] = "Geldangriff",
        ["COUNTERCLAIM_POWER.description"] = "Immer wenn du eine Zahlung leistest, füge einem zufälligen Gegner 5 Schaden zu.",
        ["STATEMENT_POWER.title"] = "Kontoauszug",
        ["STATEMENT_POWER.description"] = "Immer wenn du eine Zahlung leistest, ziehe eine Karte.",
        ["INTEREST_SUPPORT_POWER.title"] = "Zinszuschuss",
        ["INTEREST_SUPPORT_POWER.description"] = "Immer wenn du eine Zahlung leistest, erhalte Gold in Höhe der Hälfte davon.",
        ["PAYMENT_STACK_POWER.title"] = "Zahlungszähler",
        ["PAYMENT_STACK_POWER.description"] = "Erhält 1 bei jeder Zahlung, die du leistest.",
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
        ["COUNTERCLAIM_POWER.title"] = "Attaque financière",
        ["COUNTERCLAIM_POWER.description"] = "Chaque fois que tu effectues un Paiement, inflige 5 dégâts à un ennemi au hasard.",
        ["STATEMENT_POWER.title"] = "Relevé",
        ["STATEMENT_POWER.description"] = "Chaque fois que tu effectues un Paiement, pioche une carte.",
        ["INTEREST_SUPPORT_POWER.title"] = "Aide aux intérêts",
        ["INTEREST_SUPPORT_POWER.description"] = "Chaque fois que tu effectues un Paiement, gagne de l'or égal à la moitié de celui-ci.",
        ["PAYMENT_STACK_POWER.title"] = "Décompte",
        ["PAYMENT_STACK_POWER.description"] = "Gagne 1 à chaque Paiement que tu effectues.",
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
        ["COUNTERCLAIM_POWER.title"] = "Ataque monetario",
        ["COUNTERCLAIM_POWER.description"] = "Cada vez que haces un Pago, inflige 5 de daño a un enemigo al azar.",
        ["STATEMENT_POWER.title"] = "Extracto",
        ["STATEMENT_POWER.description"] = "Cada vez que haces un Pago, roba una carta.",
        ["INTEREST_SUPPORT_POWER.title"] = "Ayuda de intereses",
        ["INTEREST_SUPPORT_POWER.description"] = "Cada vez que haces un Pago, ganas oro igual a la mitad del mismo.",
        ["PAYMENT_STACK_POWER.title"] = "Recuento de pagos",
        ["PAYMENT_STACK_POWER.description"] = "Gana 1 cada vez que haces un Pago.",
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
        ["COUNTERCLAIM_POWER.title"] = "Attacco monetario",
        ["COUNTERCLAIM_POWER.description"] = "Ogni volta che effettui un Pagamento, infliggi 5 danni a un nemico casuale.",
        ["STATEMENT_POWER.title"] = "Estratto conto",
        ["STATEMENT_POWER.description"] = "Ogni volta che effettui un Pagamento, pesca una carta.",
        ["INTEREST_SUPPORT_POWER.title"] = "Sostegno agli interessi",
        ["INTEREST_SUPPORT_POWER.description"] = "Ogni volta che effettui un Pagamento, ottieni Oro pari alla metà di esso.",
        ["PAYMENT_STACK_POWER.title"] = "Conteggio pagamenti",
        ["PAYMENT_STACK_POWER.description"] = "Ottiene 1 ogni volta che effettui un Pagamento.",
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
        ["COUNTERCLAIM_POWER.title"] = "Atak pieniężny",
        ["COUNTERCLAIM_POWER.description"] = "Za każdym razem, gdy dokonasz Spłaty, zadaj 5 obrażeń losowemu wrogowi.",
        ["STATEMENT_POWER.title"] = "Wyciąg",
        ["STATEMENT_POWER.description"] = "Za każdym razem, gdy dokonasz Spłaty, dobierz kartę.",
        ["INTEREST_SUPPORT_POWER.title"] = "Dopłata do odsetek",
        ["INTEREST_SUPPORT_POWER.description"] = "Za każdym razem, gdy dokonasz Spłaty, zyskaj złoto równe połowie tej kwoty.",
        ["PAYMENT_STACK_POWER.title"] = "Licznik spłat",
        ["PAYMENT_STACK_POWER.description"] = "Zyskuje 1 za każdym razem, gdy dokonasz Spłaty.",
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
        ["COUNTERCLAIM_POWER.title"] = "Ataque monetário",
        ["COUNTERCLAIM_POWER.description"] = "Sempre que fizer um Pagamento, cause 5 de dano a um inimigo aleatório.",
        ["STATEMENT_POWER.title"] = "Extrato",
        ["STATEMENT_POWER.description"] = "Sempre que fizer um Pagamento, compre uma carta.",
        ["INTEREST_SUPPORT_POWER.title"] = "Auxílio de juros",
        ["INTEREST_SUPPORT_POWER.description"] = "Sempre que fizer um Pagamento, ganhe Ouro igual à metade dele.",
        ["PAYMENT_STACK_POWER.title"] = "Contagem de pagamentos",
        ["PAYMENT_STACK_POWER.description"] = "Ganha 1 cada vez que você faz um Pagamento.",
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
        ["COUNTERCLAIM_POWER.title"] = "Денежная атака",
        ["COUNTERCLAIM_POWER.description"] = "Каждый раз, когда вы совершаете Платёж, нанесите 5 урона случайному врагу.",
        ["STATEMENT_POWER.title"] = "Выписка",
        ["STATEMENT_POWER.description"] = "Каждый раз, когда вы совершаете Платёж, возьмите карту.",
        ["INTEREST_SUPPORT_POWER.title"] = "Субсидия процентов",
        ["INTEREST_SUPPORT_POWER.description"] = "Каждый раз, когда вы совершаете Платёж, получите золото в размере его половины.",
        ["PAYMENT_STACK_POWER.title"] = "Учёт платежей",
        ["PAYMENT_STACK_POWER.description"] = "Получает 1 каждый раз, когда вы совершаете Платёж.",
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
        ["COUNTERCLAIM_POWER.title"] = "การโจมตีด้วยเงิน",
        ["COUNTERCLAIM_POWER.description"] = "ทุกครั้งที่คุณทำการชำระ สร้างความเสียหาย 5 แก่ศัตรูแบบสุ่ม",
        ["STATEMENT_POWER.title"] = "ใบแจ้งยอด",
        ["STATEMENT_POWER.description"] = "ทุกครั้งที่คุณทำการชำระ จั่วการ์ด 1 ใบ",
        ["INTEREST_SUPPORT_POWER.title"] = "เงินช่วยดอกเบี้ย",
        ["INTEREST_SUPPORT_POWER.description"] = "ทุกครั้งที่คุณทำการชำระ รับทองเท่ากับครึ่งหนึ่งของยอดนั้น",
        ["PAYMENT_STACK_POWER.title"] = "ยอดการชำระ",
        ["PAYMENT_STACK_POWER.description"] = "เพิ่มขึ้น 1 ทุกครั้งที่คุณทำการชำระ",
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
        ["COUNTERCLAIM_POWER.title"] = "Para Saldırısı",
        ["COUNTERCLAIM_POWER.description"] = "Bir Ödeme yaptığında rastgele bir düşmana 5 hasar ver.",
        ["STATEMENT_POWER.title"] = "Ekstre",
        ["STATEMENT_POWER.description"] = "Bir Ödeme yaptığında bir kart çek.",
        ["INTEREST_SUPPORT_POWER.title"] = "Faiz Desteği",
        ["INTEREST_SUPPORT_POWER.description"] = "Bir Ödeme yaptığında, onun yarısı kadar Altın kazan.",
        ["PAYMENT_STACK_POWER.title"] = "Ödeme Sayacı",
        ["PAYMENT_STACK_POWER.description"] = "Yaptığın her Ödeme'de 1 kazanır.",
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
