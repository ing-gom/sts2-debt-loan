using System.Collections.Generic;

namespace Sts2DebtLoan;

/// <summary>
/// Localized strings for the Ledger relic + the Debt curse cards (빚 독촉 / 연체 / 차압 / 신용 불량 / 강제 징수)
/// in every language the game ships (13 + English). Keys are the game's 3-letter language codes; LocInjection
/// Patch picks the row for the current language and merges it into the "relics"/"cards" LocTables.
///
/// The relic description is a per-relic template ({borrowed}/{paid}) whose middle line uses SmartFormat's
/// choose() on {cards} (the current tier 1..4) to list the CUMULATIVE set of curses injected at that tier —
/// so the description grows as the debt escalates. Dunning uses {draw}/{play}; the Forced Collection
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
            "Borrowed [gold]{borrowed} Gold[/gold]. Paid [gold]{paid} Gold[/gold] so far.\nInjected each combat: [b]{cards:choose(1|2|3|4):Dunning|Dunning, Delinquency|Dunning, Delinquency, Seizure|Dunning, Delinquency, Seizure, Bad Credit|Dunning}[/b]. More curses pile on the longer you owe.\nRepay the debt at a shop to remove this relic.",
            "Every signature is a small surrender.",
            "Dunning", "At the end of your turn, if it's still in your hand, makes a [gold]{draw} Gold[/gold] [gold]Payment[/gold].\nOr play it to make a [gold]{play} Gold[/gold] [gold]Payment[/gold].",
            "Delinquency", "While this is in your hand, enemy attacks deal 50% more damage.",
            "Seizure", "While this is in your hand, you can only play cards of the first type you play each turn.",
            "Bad Credit", "While this is in your hand, at the start of each turn it puts a Forced Collection into your hand — escalating each turn.",
            "Forced Collection", "At the end of your turn, lose [b]{hp}[/b] HP and repay [gold]{principal} Gold[/gold] of principal, then Exhaust."),

        ["kor"] = new("상인의 장부",
            "빌린 금액 [gold]{borrowed} 골드[/gold]. 지금까지 지불 [gold]{paid} 골드[/gold].\n전투마다 주입되는 저주: [b]{cards:choose(1|2|3|4):빚 독촉|빚 독촉, 연체|빚 독촉, 연체, 차압|빚 독촉, 연체, 차압, 신용 불량|빚 독촉}[/b]. 오래 갚지 않을수록 늘어납니다.\n상점에서 빚을 갚으면 이 유물이 제거됩니다.",
            "모든 서명은 작은 항복이다.",
            "빚 독촉", "턴 종료 시 손에 있으면 [gold]{draw} 골드[/gold]를 [gold]납부[/gold]합니다.\n또는 플레이하여 [gold]{play} 골드[/gold]를 [gold]납부[/gold]합니다.",
            "연체", "이 카드가 손에 있는 동안, 적의 공격이 50% 더 큰 피해를 줍니다.",
            "차압", "이 카드가 손에 있는 동안, 이번 턴 처음 낸 카드 종류만 사용할 수 있습니다.",
            "신용 불량", "이 카드가 손에 있는 동안, 매 턴 시작 시 강제 징수 카드를 손에 넣습니다 — 턴마다 점점 강해집니다.",
            "강제 징수", "턴 종료 시, 체력을 [b]{hp}[/b] 잃고 원금 [gold]{principal} 골드[/gold]를 상환한 뒤 소멸합니다."),

        ["jpn"] = new("商人の元帳",
            "借入 [gold]{borrowed} ゴールド[/gold]。これまでの支払い [gold]{paid} ゴールド[/gold]。\n戦闘ごとに加わる呪い: [b]{cards:choose(1|2|3|4):督促|督促・延滞|督促・延滞・差し押さえ|督促・延滞・差し押さえ・信用不良|督促}[/b]。返済が遅れるほど増える。\nショップで借金を返済すると、このレリックは取り除かれる。",
            "署名はすべて、小さな降伏だ。",
            "督促", "ターン終了時、手札にあれば[gold]{draw} ゴールド[/gold]を徴収する。またはプレイして[gold]{play} ゴールド[/gold]を支払い、借金をより早く返済する。",
            "延滞", "この手札にある間、敵の攻撃ダメージが50%増加する。",
            "差し押さえ", "この手札にある間、そのターン最初にプレイしたタイプのカードしかプレイできない。",
            "信用不良", "この手札にある間、各ターン開始時に「強制徴収」を手札に加える — ターンごとに激しくなる。",
            "強制徴収", "ターン終了時、体力を[b]{hp}[/b]失い、元金[gold]{principal} ゴールド[/gold]を返済してから廃棄。"),

        ["zhs"] = new("商人的账簿",
            "已借入 [gold]{borrowed} 金币[/gold]。已偿还 [gold]{paid} 金币[/gold]。\n每场战斗注入的诅咒：[b]{cards:choose(1|2|3|4):催债|催债、拖欠|催债、拖欠、扣押|催债、拖欠、扣押、信用不良|催债}[/b]。拖欠越久，诅咒越多。\n在商店还清债务即可移除此遗物。",
            "每一个签名都是一次小小的屈服。",
            "催债", "回合结束时若仍在手牌中，征收 [gold]{draw} 金币[/gold]。或打出它，支付 [gold]{play} 金币[/gold]，更快偿还贷款。",
            "拖欠", "当此牌在你手牌中时，敌人的攻击造成的伤害提高 50%。",
            "扣押", "当此牌在你手牌中时，你每回合只能打出你本回合第一张打出的类型的牌。",
            "信用不良", "当此牌在你手牌中时，每回合开始将一张“强制征收”置入你的手牌——每回合不断升级。",
            "强制征收", "回合结束时，失去 [b]{hp}[/b] 点生命并偿还 [gold]{principal} 金币[/gold] 本金，然后消耗。"),

        ["deu"] = new("Händlerbuch",
            "Geliehen: [gold]{borrowed} Gold[/gold]. Zurückgezahlt: [gold]{paid} Gold[/gold].\nJeder Kampf schleust ein: [b]{cards:choose(1|2|3|4):Mahnung|Mahnung, Verzug|Mahnung, Verzug, Pfändung|Mahnung, Verzug, Pfändung, Zahlungsunfähig|Mahnung}[/b]. Je länger du schuldest, desto mehr Flüche.\nZahle die Schuld in einem Laden zurück, um dieses Relikt zu entfernen.",
            "Jede Unterschrift ist eine kleine Kapitulation.",
            "Mahnung", "Am Ende deiner Runde zieht sie, wenn noch auf der Hand, [gold]{draw} Gold[/gold] ein. Oder spiele sie, um [gold]{play} Gold[/gold] zu zahlen und die Schuld schneller zu tilgen.",
            "Verzug", "Solange sie auf deiner Hand ist, richten gegnerische Angriffe 50% mehr Schaden an.",
            "Pfändung", "Solange sie auf deiner Hand ist, kannst du nur Karten des ersten Typs spielen, den du in dieser Runde spielst.",
            "Zahlungsunfähig", "Solange sie auf deiner Hand ist, legt sie zu Beginn jeder Runde eine Zwangseinziehung auf deine Hand — jede Runde schlimmer.",
            "Zwangseinziehung", "Am Ende deiner Runde verlierst du [b]{hp}[/b] LP und tilgst [gold]{principal} Gold[/gold] der Schuld, dann verbraucht."),

        ["fra"] = new("Grand livre du marchand",
            "Emprunté : [gold]{borrowed} or[/gold]. Remboursé : [gold]{paid} or[/gold].\nInjecté à chaque combat : [b]{cards:choose(1|2|3|4):Relance|Relance, Défaut|Relance, Défaut, Saisie|Relance, Défaut, Saisie, Insolvabilité|Relance}[/b]. Plus tu tardes, plus il y a de malédictions.\nRembourse la dette dans une boutique pour retirer cette relique.",
            "Chaque signature est une petite reddition.",
            "Relance", "À la fin de ton tour, si elle est encore en main, elle prélève [gold]{draw} or[/gold]. Ou joue-la pour payer [gold]{play} or[/gold] et rembourser la dette plus vite.",
            "Défaut", "Tant qu'elle est dans ta main, les attaques ennemies infligent 50% de dégâts en plus.",
            "Saisie", "Tant qu'elle est dans ta main, tu ne peux jouer que des cartes du premier type que tu joues ce tour.",
            "Insolvabilité", "Tant qu'elle est dans ta main, au début de chaque tour elle ajoute une Saisie forcée à ta main — de pire en pire.",
            "Saisie forcée", "À la fin de ton tour, perds [b]{hp}[/b] PV et rembourse [gold]{principal} or[/gold] de la dette, puis Épuise."),

        ["spa"] = new("Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nInyectado cada combate: [b]{cards:choose(1|2|3|4):Reclamación|Reclamación, Morosidad|Reclamación, Morosidad, Embargo|Reclamación, Morosidad, Embargo, Insolvencia|Reclamación}[/b]. Cuanto más debas, más maldiciones.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Reclamación", "Al final de tu turno, si sigue en tu mano, recauda [gold]{draw} de oro[/gold]. O juégala para pagar [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno.",
            "Insolvencia", "Mientras esté en tu mano, al inicio de cada turno añade un Embargo forzoso a tu mano — cada vez peor.",
            "Embargo forzoso", "Al final de tu turno, pierde [b]{hp}[/b] de vida y salda [gold]{principal} de oro[/gold] de la deuda; luego Agota."),

        ["esp"] = new("Libro del mercader",
            "Prestado: [gold]{borrowed} de oro[/gold]. Pagado: [gold]{paid} de oro[/gold].\nInyectado cada combate: [b]{cards:choose(1|2|3|4):Reclamación|Reclamación, Morosidad|Reclamación, Morosidad, Embargo|Reclamación, Morosidad, Embargo, Insolvencia|Reclamación}[/b]. Cuanto más debas, más maldiciones.\nSalda la deuda en una tienda para eliminar esta reliquia.",
            "Cada firma es una pequeña rendición.",
            "Reclamación", "Al final de tu turno, si sigue en tu mano, recauda [gold]{draw} de oro[/gold]. O juégala para pagar [gold]{play} de oro[/gold] y saldar la deuda más rápido.",
            "Morosidad", "Mientras esté en tu mano, los ataques enemigos infligen un 50% más de daño.",
            "Embargo", "Mientras esté en tu mano, solo puedes jugar cartas del primer tipo que juegues cada turno.",
            "Insolvencia", "Mientras esté en tu mano, al inicio de cada turno añade un Embargo forzoso a tu mano — cada vez peor.",
            "Embargo forzoso", "Al final de tu turno, pierde [b]{hp}[/b] de vida y salda [gold]{principal} de oro[/gold] de la deuda; luego Agota."),

        ["ita"] = new("Registro del mercante",
            "Preso in prestito: [gold]{borrowed} Oro[/gold]. Pagato: [gold]{paid} Oro[/gold].\nInserito ogni combattimento: [b]{cards:choose(1|2|3|4):Sollecito|Sollecito, Morosità|Sollecito, Morosità, Pignoramento|Sollecito, Morosità, Pignoramento, Insolvenza|Sollecito}[/b]. Più tardi, più maledizioni.\nSalda il debito in un negozio per rimuovere questo cimelio.",
            "Ogni firma è una piccola resa.",
            "Sollecito", "Alla fine del turno, se è ancora in mano, riscuote [gold]{draw} Oro[/gold]. Oppure giocala per pagare [gold]{play} Oro[/gold] e saldare il debito più in fretta.",
            "Morosità", "Finché è nella tua mano, gli attacchi nemici infliggono il 50% di danni in più.",
            "Pignoramento", "Finché è nella tua mano, puoi giocare solo carte del primo tipo che giochi ogni turno.",
            "Insolvenza", "Finché è nella tua mano, all'inizio di ogni turno aggiunge una Riscossione forzata alla tua mano — sempre peggio.",
            "Riscossione forzata", "Alla fine del turno, perdi [b]{hp}[/b] PV e ripaghi [gold]{principal} Oro[/gold] di debito, poi Consuma."),

        ["pol"] = new("Księga kupca",
            "Pożyczono: [gold]{borrowed} złota[/gold]. Spłacono: [gold]{paid} złota[/gold].\nDodawane w każdej walce: [b]{cards:choose(1|2|3|4):Ponaglenie|Ponaglenie, Zaległość|Ponaglenie, Zaległość, Zajęcie|Ponaglenie, Zaległość, Zajęcie, Niewypłacalność|Ponaglenie}[/b]. Im dłużej zwlekasz, tym więcej klątw.\nSpłać dług w sklepie, aby usunąć ten relikt.",
            "Każdy podpis to mała kapitulacja.",
            "Ponaglenie", "Na końcu tury, jeśli wciąż jest w ręce, pobiera [gold]{draw} złota[/gold]. Albo zagraj ją, aby zapłacić [gold]{play} złota[/gold] i szybciej spłacić dług.",
            "Zaległość", "Gdy jest w twojej ręce, ataki wrogów zadają 50% więcej obrażeń.",
            "Zajęcie", "Gdy jest w twojej ręce, możesz grać tylko karty pierwszego typu zagranego w danej turze.",
            "Niewypłacalność", "Gdy jest w twojej ręce, na początku każdej tury dodaje Przymusową egzekucję do ręki — coraz gorzej.",
            "Przymusowa egzekucja", "Na końcu tury tracisz [b]{hp}[/b] PŻ i spłacasz [gold]{principal} złota[/gold] długu, potem Zużywa się."),

        ["ptb"] = new("Livro-razão do mercador",
            "Emprestado: [gold]{borrowed} de Ouro[/gold]. Pago: [gold]{paid} de Ouro[/gold].\nInjetado a cada combate: [b]{cards:choose(1|2|3|4):Cobrança|Cobrança, Inadimplência|Cobrança, Inadimplência, Penhora|Cobrança, Inadimplência, Penhora, Crédito Ruim|Cobrança}[/b]. Quanto mais você deve, mais maldições.\nQuite a dívida em uma loja para remover esta relíquia.",
            "Cada assinatura é uma pequena rendição.",
            "Cobrança", "No fim do seu turno, se ainda estiver na mão, cobra [gold]{draw} de Ouro[/gold]. Ou jogue-a para pagar [gold]{play} de Ouro[/gold] e quitar a dívida mais rápido.",
            "Inadimplência", "Enquanto estiver na sua mão, ataques inimigos causam 50% mais dano.",
            "Penhora", "Enquanto estiver na sua mão, você só pode jogar cartas do primeiro tipo que jogar no turno.",
            "Crédito Ruim", "Enquanto estiver na sua mão, no início de cada turno adiciona uma Cobrança Forçada à sua mão — cada vez pior.",
            "Cobrança Forçada", "No fim do seu turno, perca [b]{hp}[/b] de Vida e quite [gold]{principal} de Ouro[/gold] da dívida, então Exaure."),

        ["rus"] = new("Гроссбух торговца",
            "Взято в долг: [gold]{borrowed} золота[/gold]. Выплачено: [gold]{paid} золота[/gold].\nДобавляется каждый бой: [b]{cards:choose(1|2|3|4):Взыскание|Взыскание, Просрочка|Взыскание, Просрочка, Арест|Взыскание, Просрочка, Арест, Неплатёжеспособность|Взыскание}[/b]. Чем дольше долг, тем больше проклятий.\nПогасите долг в магазине, чтобы убрать эту реликвию.",
            "Каждая подпись — маленькая капитуляция.",
            "Взыскание", "В конце хода, если она ещё в руке, взимает [gold]{draw} золота[/gold]. Или разыграйте её, чтобы заплатить [gold]{play} золота[/gold] и быстрее погасить долг.",
            "Просрочка", "Пока она в руке, атаки врагов наносят на 50% больше урона.",
            "Арест", "Пока она в руке, за ход вы можете разыгрывать только карты того типа, что разыграли первым.",
            "Неплатёжеспособность", "Пока она в руке, в начале каждого хода добавляет Принудительное взыскание в руку — всё хуже.",
            "Принудительное взыскание", "В конце хода теряете [b]{hp}[/b] здоровья и гасите [gold]{principal} золота[/gold] долга, затем Истощается."),

        ["tha"] = new("บัญชีของพ่อค้า",
            "ยืมมา [gold]{borrowed} ทอง[/gold] จ่ายไปแล้ว [gold]{paid} ทอง[/gold]\nใส่ทุกการต่อสู้: [b]{cards:choose(1|2|3|4):ทวงหนี้|ทวงหนี้, ค้างชำระ|ทวงหนี้, ค้างชำระ, ยึดทรัพย์|ทวงหนี้, ค้างชำระ, ยึดทรัพย์, เครดิตเสีย|ทวงหนี้}[/b] ยิ่งค้างนานยิ่งมากขึ้น\nชำระหนี้ที่ร้านค้าเพื่อนำวัตถุโบราณนี้ออก",
            "ทุกลายเซ็นคือการยอมจำนนเล็กๆ",
            "ทวงหนี้", "เมื่อจบเทิร์นหากยังอยู่ในมือ จะเก็บ [gold]{draw} ทอง[/gold] หรือเล่นเพื่อจ่าย [gold]{play} ทอง[/gold] และชำระหนี้เร็วขึ้น",
            "ค้างชำระ", "ขณะอยู่ในมือ การโจมตีของศัตรูสร้างความเสียหายเพิ่ม 50%",
            "ยึดทรัพย์", "ขณะอยู่ในมือ คุณเล่นได้เฉพาะการ์ดประเภทแรกที่คุณเล่นในเทิร์นนั้น",
            "เครดิตเสีย", "ขณะอยู่ในมือ เมื่อเริ่มแต่ละเทิร์นจะใส่การ์ดบังคับเก็บหนี้ลงในมือ — รุนแรงขึ้นทุกเทิร์น",
            "บังคับเก็บหนี้", "เมื่อจบเทิร์น เสียพลังชีวิต [b]{hp}[/b] และชำระเงินต้น [gold]{principal} ทอง[/gold] จากนั้นเผาไหม้"),

        ["tur"] = new("Tüccarın Defteri",
            "Borç alınan: [gold]{borrowed} Altın[/gold]. Ödenen: [gold]{paid} Altın[/gold].\nHer savaş eklenir: [b]{cards:choose(1|2|3|4):İhtar|İhtar, Temerrüt|İhtar, Temerrüt, Haciz|İhtar, Temerrüt, Haciz, Kredi İflası|İhtar}[/b]. Borç uzadıkça daha fazla lanet.\nBu kalıntıyı kaldırmak için borcu bir dükkânda öde.",
            "Her imza küçük bir teslimiyettir.",
            "İhtar", "Turunun sonunda hâlâ elindeyse [gold]{draw} Altın[/gold] tahsil eder. Ya da oyna: [gold]{play} Altın[/gold] öde ve borcu daha hızlı kapat.",
            "Temerrüt", "Elindeyken düşman saldırıları %50 daha fazla hasar verir.",
            "Haciz", "Elindeyken, o tur yalnızca oynadığın ilk türden kart oynayabilirsin.",
            "Kredi İflası", "Elindeyken, her turun başında eline bir Zorla Tahsilat ekler — her tur daha kötü.",
            "Zorla Tahsilat", "Turunun sonunda [b]{hp}[/b] Can kaybeder ve [gold]{principal} Altın[/gold] anapara ödersin, sonra Tükenir."),
    };

    // ── 독촉장 (Dunning Letter) leverage card + power ─────────────────────────────
    // Kept in a SEPARATE table (not the Row) so languages can be filled without touching the Row constructor;
    // a missing language falls back to English. The "{plate}" placeholder is filled from the card's own
    // DynamicVars (DunningLetterCard.CanonicalVars). Each references the local name of the 빚 독촉 (Dunning) card.
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
        ["eng"] = new("Dunning Letter",
            "At the start of each of your turns, add a [gold]{card}[/gold] card to your hand.",
            "At the start of each of your turns, add a Dunning card to your hand."),
        ["kor"] = new("독촉장",
            "매 턴 시작 시 [gold]{card}[/gold]을 손에 넣습니다.",
            "매 턴 시작 시 빚 독촉을 손에 넣습니다."),
        ["jpn"] = new("督促状",
            "各ターン開始時、[gold]{card}[/gold]カードを手札に加える。\n[gold]{card}[/gold]カードをプレイするたびに、[gold]板金[/gold] [b]{plate}[/b] を得る。",
            "各ターン開始時、督促カードを手札に加える。"),
        ["zhs"] = new("催债信",
            "每个回合开始时，将一张[gold]{card}[/gold]牌加入手牌。\n每次打出[gold]{card}[/gold]牌时，获得 [b]{plate}[/b] [gold]层板甲[/gold]。",
            "每个回合开始时，将一张催债牌加入手牌。"),
        ["deu"] = new("Mahnschreiben",
            "Zu Beginn jeder deiner Runden lege eine [gold]{card}[/gold]-Karte auf deine Hand.\nImmer wenn du eine [gold]{card}[/gold]-Karte spielst, erhalte [b]{plate}[/b] [gold]Panzerung[/gold].",
            "Zu Beginn jeder deiner Runden lege eine Mahnung-Karte auf deine Hand."),
        ["fra"] = new("Lettre de relance",
            "Au début de chacun de tes tours, ajoute une carte [gold]{card}[/gold] à ta main.\nChaque fois que tu joues une carte [gold]{card}[/gold], gagne [b]{plate}[/b] [gold]Blindage[/gold].",
            "Au début de chacun de tes tours, ajoute une carte Relance à ta main."),
        ["spa"] = new("Carta de cobro",
            "Al inicio de cada uno de tus turnos, añade una carta de [gold]{card}[/gold] a tu mano.\nCada vez que juegas una carta de [gold]{card}[/gold], ganas [b]{plate}[/b] de [gold]Blindaje[/gold].",
            "Al inicio de cada uno de tus turnos, añade una carta de Reclamación a tu mano."),
        ["esp"] = new("Carta de cobro",
            "Al inicio de cada uno de tus turnos, añade una carta de [gold]{card}[/gold] a tu mano.\nCada vez que juegas una carta de [gold]{card}[/gold], ganas [b]{plate}[/b] de [gold]Blindaje[/gold].",
            "Al inicio de cada uno de tus turnos, añade una carta de Reclamación a tu mano."),
        ["ita"] = new("Lettera di sollecito",
            "All'inizio di ogni tuo turno, aggiungi una carta [gold]{card}[/gold] alla tua mano.\nOgni volta che giochi una carta [gold]{card}[/gold], ottieni [b]{plate}[/b] [gold]Corazza[/gold].",
            "All'inizio di ogni tuo turno, aggiungi una carta Sollecito alla tua mano."),
        ["pol"] = new("Wezwanie do zapłaty",
            "Na początku każdej twojej tury dodaj kartę [gold]{card}[/gold] do ręki.\nZa każdym razem, gdy zagrasz kartę [gold]{card}[/gold], zyskaj [b]{plate}[/b] [gold]Pancerza[/gold].",
            "Na początku każdej twojej tury dodaj kartę Ponaglenie do ręki."),
        ["ptb"] = new("Carta de cobrança",
            "No início de cada um dos seus turnos, adicione uma carta de [gold]{card}[/gold] à sua mão.\nSempre que jogar uma carta de [gold]{card}[/gold], ganhe [b]{plate}[/b] de [gold]Blindagem[/gold].",
            "No início de cada um dos seus turnos, adicione uma carta de Cobrança à sua mão."),
        ["rus"] = new("Требование об оплате",
            "В начале каждого вашего хода добавьте карту [gold]{card}[/gold] в руку.\nКаждый раз, когда вы разыгрываете карту [gold]{card}[/gold], получите [b]{plate}[/b] [gold]брони[/gold].",
            "В начале каждого вашего хода добавьте карту Взыскание в руку."),
        ["tha"] = new("จดหมายทวงหนี้",
            "เมื่อเริ่มแต่ละเทิร์นของคุณ เพิ่มการ์ด[gold]{card}[/gold]เข้ามือ\nทุกครั้งที่เล่นการ์ด[gold]{card}[/gold] รับ[gold]เกราะ[/gold] [b]{plate}[/b]",
            "เมื่อเริ่มแต่ละเทิร์นของคุณ เพิ่มการ์ดทวงหนี้เข้ามือ"),
        ["tur"] = new("İhtarname",
            "Her turunun başında eline bir [gold]{card}[/gold] kartı ekle.\nBir [gold]{card}[/gold] kartı her oynadığında [b]{plate}[/b] [gold]Zırh[/gold] kazan.",
            "Her turunun başında eline bir İhtar kartı ekle."),
    };

    // ── 납부 (Payment) tooltip: what happens to the gold a Debt card takes ─────────────────────────────
    // Shown on the 빚 독촉 hover (custom HoverTip). English fallback for unfilled languages. TODO: 12 more.
    internal readonly struct PayRow { public readonly string Title, Desc; public PayRow(string t, string d) { Title = t; Desc = d; } }

    internal static PayRow PaymentFor(string? lang)
        => lang != null && PayByLang.TryGetValue(lang, out var r) ? r : PayByLang["eng"];

    private static readonly Dictionary<string, PayRow> PayByLang = new()
    {
        ["eng"] = new("Payment", "Half of the gold paid goes toward the loan's [b]principal[/b], the other half is [b]interest[/b]. Paying down the principal shrinks the shop repay cost."),
        ["kor"] = new("납부", "납부한 골드의 절반은 대출 [b]원금[/b] 상환에, 나머지 절반은 [b]이자[/b]로 쓰입니다. 원금을 갚을수록 상점 상환액이 줄어듭니다."),
    };

    // ── New payment-set cards + powers (loc keys = ClassName → SCREAMING_SNAKE). EN + KO; English fallback for
    //    the other 12 languages (TODO). Cards → "cards" table, powers → "powers" table (LocInjectionPatch).
    internal static Dictionary<string, string> ExtraCardLoc(string? lang) => lang == "kor" ? _cardsKor : _cardsEng;
    internal static Dictionary<string, string> ExtraPowerLoc(string? lang) => lang == "kor" ? _powersKor : _powersEng;

    private static readonly Dictionary<string, string> _cardsEng = new()
    {
        ["WAGES_CARD.title"] = "Wages",
        ["WAGES_CARD.description"] = "Gain [gold]{gold} Gold[/gold].",
        ["JOB_PLACEMENT_CARD.title"] = "Job Placement",
        ["JOB_PLACEMENT_CARD.description"] = "Borrow [gold]{fee} Gold[/gold] onto your loan.\nAt the start of each turn, add a [gold]Wages[/gold] card to your hand.",
        ["PAYMENT_BENEFIT_CARD.title"] = "Payment Benefit",
        ["PAYMENT_BENEFIT_CARD.description"] = "Whenever you make a [gold]Payment[/gold], gain [b]{plate}[/b] [gold]Plating[/gold].",
        ["REFUND_CARD.title"] = "Refund",
        ["REFUND_CARD.description"] = "Whenever you make a [gold]Payment[/gold], add a 0-cost [gold]Diligent Payment[/gold] card to your hand.",
        ["DILIGENT_PAYMENT_CARD.title"] = "Diligent Payment",
        ["DILIGENT_PAYMENT_CARD.description"] = "Gain [b]{block}[/b] [gold]Block[/gold].",
        ["SETTLEMENT_CARD.title"] = "Settlement",
        ["SETTLEMENT_CARD.description"] = "Gain [gold]Block[/gold] equal to the [gold]Payments[/gold] you've made this combat × [b]{mult}[/b].",
        ["INVOICE_CARD.title"] = "Invoice",
        ["INVOICE_CARD.description"] = "Deal damage equal to the [gold]Payments[/gold] you've made this combat × [b]{mult}[/b].",
        ["BLOOD_PAYMENT_CARD.title"] = "Blood Payment",
        ["BLOOD_PAYMENT_CARD.description"] = "Lose [b]{hp}[/b] HP and make a [gold]{pay} Gold[/gold] [gold]Payment[/gold].",
    };

    private static readonly Dictionary<string, string> _cardsKor = new()
    {
        ["WAGES_CARD.title"] = "품삯",
        ["WAGES_CARD.description"] = "[gold]{gold} 골드[/gold]를 얻습니다.",
        ["JOB_PLACEMENT_CARD.title"] = "취업알선",
        ["JOB_PLACEMENT_CARD.description"] = "[gold]{fee} 골드[/gold]를 빚으로 빌립니다.\n매 턴 시작 시 [gold]품삯[/gold]을 손에 넣습니다.",
        ["PAYMENT_BENEFIT_CARD.title"] = "납부 혜택",
        ["PAYMENT_BENEFIT_CARD.description"] = "[gold]납부[/gold]할 때마다 [gold]판금[/gold] [b]{plate}[/b]을 얻습니다.",
        ["REFUND_CARD.title"] = "환급",
        ["REFUND_CARD.description"] = "[gold]납부[/gold]할 때마다 0코스트 [gold]성실 납부[/gold] 카드를 손에 넣습니다.",
        ["DILIGENT_PAYMENT_CARD.title"] = "성실 납부",
        ["DILIGENT_PAYMENT_CARD.description"] = "[gold]블록[/gold] [b]{block}[/b]을 얻습니다.",
        ["SETTLEMENT_CARD.title"] = "정산",
        ["SETTLEMENT_CARD.description"] = "이번 전투에서 한 [gold]납부[/gold] 횟수 × [b]{mult}[/b]만큼 [gold]블록[/gold]을 얻습니다.",
        ["INVOICE_CARD.title"] = "청구서",
        ["INVOICE_CARD.description"] = "이번 전투에서 한 [gold]납부[/gold] 횟수 × [b]{mult}[/b]만큼 피해를 줍니다.",
        ["BLOOD_PAYMENT_CARD.title"] = "혈납",
        ["BLOOD_PAYMENT_CARD.description"] = "체력 [b]{hp}[/b]을 잃고 [gold]{pay} 골드[/gold]를 [gold]납부[/gold]합니다.",
    };

    private static readonly Dictionary<string, string> _powersEng = new()
    {
        ["JOB_PLACEMENT_POWER.title"] = "Job Placement",
        ["JOB_PLACEMENT_POWER.description"] = "At the start of each turn, add a Wages card to your hand.",
        ["PAYMENT_BENEFIT_POWER.title"] = "Payment Benefit",
        ["PAYMENT_BENEFIT_POWER.description"] = "Whenever you make a Payment, gain 3 Plating.",
        ["REFUND_POWER.title"] = "Refund",
        ["REFUND_POWER.description"] = "Whenever you make a Payment, add a Diligent Payment card to your hand.",
    };

    private static readonly Dictionary<string, string> _powersKor = new()
    {
        ["JOB_PLACEMENT_POWER.title"] = "취업알선",
        ["JOB_PLACEMENT_POWER.description"] = "매 턴 시작 시 품삯을 손에 넣습니다.",
        ["PAYMENT_BENEFIT_POWER.title"] = "납부 혜택",
        ["PAYMENT_BENEFIT_POWER.description"] = "납부할 때마다 판금 3을 얻습니다.",
        ["REFUND_POWER.title"] = "환급",
        ["REFUND_POWER.description"] = "납부할 때마다 성실 납부 카드를 손에 넣습니다.",
    };
}
