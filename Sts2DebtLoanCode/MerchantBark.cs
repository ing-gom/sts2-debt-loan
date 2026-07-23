using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;    // LocString, LocManager
using MegaCrit.Sts2.Core.Nodes.Rooms;     // NMerchantRoom, NMerchantButton

namespace Sts2DebtLoan;

/// <summary>
/// The merchant SPEAKS around the loan — a sly, coin-loving shopkeeper (not a menacing villain). On
/// borrowing: your FIRST loan, and when lifetime borrowing crosses 200 / 300 gold. On repaying: one line by
/// how DEEP in debt you were (tier 1..4). Each trigger holds SEVERAL lines and one is picked at random
/// (First and the 300-cross have three). Purely local display — finds NMerchantRoom.Instance.MerchantButton
/// and calls its native PlayDialogue, fired only for the borrower. Lines are merged into the "relics"
/// LocTable (indexed keys) by LocInjectionPatch so PlayDialogue's LocString resolves them. Never throws.
/// </summary>
internal static class MerchantBark
{
    internal readonly record struct Set(string[] First, string[] O200, string[] O300,
                                        string[] R1, string[] R2, string[] R3, string[] R4, string[] Grant);

    private const string KFirst = "DEBT_BARK_FIRST", K200 = "DEBT_BARK_200", K300 = "DEBT_BARK_300",
                         KR1 = "DEBT_REPAY_T1", KR2 = "DEBT_REPAY_T2", KR3 = "DEBT_REPAY_T3", KR4 = "DEBT_REPAY_T4",
                         KGrant = "DEBT_BARK_GRANT";

    private static readonly Random Rng = new();

    private static readonly Dictionary<string, Set> Lines = new()
    {
        ["eng"] = new(
            new[]{ "A loan, eh? Splendid, splendid. Just sign right here.",
                   "Short on coin? Not to worry — one signature and it's yours.",
                   "Ha, times are hard, I see. Sign here and borrow what you need." },
            new[]{ "Careful now — that's a fair bit of debt you're carrying." },
            new[]{ "Ha! A big spender! Customers like you keep my purse fat.",
                   "My, you do spend freely! A most welcome guest, you are.",
                   "This much debt? Ha, business would dry up without you!" },
            new[]{ "Paid already? Hmph. Well, the account's closed… for now." },
            new[]{ "Settled in full. Your credit stands restored." },
            new[]{ "Cutting it fine — but the debt's cleared, fair and square." },
            new[]{ "Ha! Clawed your way out of that pile of debt. Rare indeed." },
            new[]{ "Here — a little something to help with that debt of yours. Come back next visit and I'll have another for you.",
                   "Take this, it'll help you pay down what you owe. Drop by again and I'll dig up another." }),

        ["kor"] = new(
            new[]{ "대출이라… 좋지, 좋아. 자, 여기 서명만 하게.",
                   "돈이 궁하신가? 걱정 말게. 여기 서명 한 번이면 되니.",
                   "허허, 사정이 딱하구먼. 여기 서명하면 필요한 만큼 빌려주지." },
            new[]{ "허허, 빚이 제법 불었군. 조심하는 게 좋을 걸세." },
            new[]{ "허허, 통이 크시구먼! 이런 손님 덕에 내 주머니가 두둑해진다네.",
                   "이보게, 씀씀이가 아주 시원시원해! 나야 대환영이지.",
                   "빚이 이 정도면… 허허, 자네 없으면 내 장사가 안 되겠어!" },
            new[]{ "벌써 갚았나? 흥, 시시하군. 이번 거래는 여기서 끝일세… 당분간은." },
            new[]{ "완납이군. 자네 신용은 회복됐네." },
            new[]{ "아슬아슬했지만… 빚은 깔끔히 청산됐군." },
            new[]{ "허! 그 빚더미에서 기어이 빠져나왔군. 좀처럼 없는 일이야." },
            new[]{ "자, 그 빚 갚는 데 도움 될 걸세. 다음에 또 들르면 하나 더 챙겨주지.",
                   "이걸 받게 — 상환에 보탬이 될 거야. 다음 방문 때 또 하나 꺼내주겠네." }),

        ["jpn"] = new(
            new[]{ "借金かね？ よしよし、結構結構。ここに署名を。",
                   "先立つものが足りぬか？ 心配ご無用、署名一つで済むぞ。",
                   "はは、大変そうだな。署名すれば必要なだけ貸そう。" },
            new[]{ "ほう、ずいぶん借りが増えたな。気をつけることだ。" },
            new[]{ "はは！ 気前がいいな！ お前さんのおかげで儲かるわい。",
                   "いやはや、豪快に使うのう！ 大歓迎の客じゃ。",
                   "これだけ借りてくれりゃ… はは、お前なしじゃ商売あがったりだ！" },
            new[]{ "もう返すのか？ ふん。取引はこれまで…今のところはな。" },
            new[]{ "完済だ。信用は戻ったぞ。" },
            new[]{ "際どかったが…借金はきれいに片付いたな。" },
            new[]{ "はは！ あの借金の山から這い上がったか。珍しいことよ。" },
            new[]{ "ほれ、その借金の足しになる一枚だ。次に来たら、また一枚用意しておくぞ。",
                   "これを持っていけ、返済の助けになる。また寄っておくれ、もう一枚掘り出しておこう。" }),

        ["zhs"] = new(
            new[]{ "要借钱？好，好得很。来，在这儿签个名。",
                   "手头紧了？别担心，签个名就成。",
                   "哈，日子不好过吧。签了名，要多少借多少。" },
            new[]{ "小心点，你这欠账可不少了。" },
            new[]{ "哈！出手真阔绰！有你这样的客人，我的钱袋才鼓啊。",
                   "哟，花起钱来真痛快！这样的客人我最欢迎。",
                   "欠这么多？哈，没了你我这买卖可做不下去咯！" },
            new[]{ "这就还了？哼。这笔账就此了结…暂时而已。" },
            new[]{ "全部结清。你的信用恢复了。" },
            new[]{ "好险，不过债务清得干干净净。" },
            new[]{ "哈！从那堆债里爬出来了。可真少见。" },
            new[]{ "来，拿着这个，帮你还还债。下次再来，我还给你留一张。",
                   "收下吧，能帮你把欠账还上些。改天再来，我给你再翻出一张。" }),

        ["deu"] = new(
            new[]{ "Ein Darlehen? Vortrefflich, vortrefflich. Hier unterschreiben.",
                   "Knapp bei Kasse? Keine Sorge — eine Unterschrift, und es gehört dir.",
                   "Ha, schwere Zeiten, was? Unterschreib, und leih dir, was du brauchst." },
            new[]{ "Vorsicht — das ist schon eine hübsche Schuld, die du da trägst." },
            new[]{ "Ha! Ein Großkunde! Solche wie du füllen meinen Beutel.",
                   "Nanu, du gibst ja großzügig aus! Ein höchst willkommener Gast.",
                   "So viel Schuld? Ha, ohne dich läge mein Geschäft brach!" },
            new[]{ "Schon bezahlt? Hmpf. Nun, das Konto ist geschlossen… vorerst." },
            new[]{ "Voll beglichen. Dein Kredit ist wiederhergestellt." },
            new[]{ "Knapp — doch die Schuld ist sauber getilgt." },
            new[]{ "Ha! Dich aus dem Schuldenberg gewühlt. Wahrlich selten." },
            new[]{ "Hier — eine kleine Hilfe für deine Schuld. Komm beim nächsten Mal wieder, dann hab ich noch eine für dich.",
                   "Nimm das, es hilft dir beim Abtragen deiner Schulden. Schau wieder vorbei, ich grabe noch eine aus." }),

        ["fra"] = new(
            new[]{ "Un prêt ? Parfait, parfait. Signe donc ici.",
                   "À court de pièces ? Pas d'inquiétude — une signature et c'est à toi.",
                   "Ha, temps durs, hein ? Signe et emprunte ce qu'il te faut." },
            new[]{ "Attention — voilà une jolie dette que tu traînes là." },
            new[]{ "Ha ! Un beau dépensier ! Des clients comme toi remplissent ma bourse.",
                   "Eh bien, tu dépenses sans compter ! Un hôte des plus bienvenus.",
                   "Tant de dettes ? Ha, sans toi mes affaires péricliteraient !" },
            new[]{ "Déjà remboursé ? Pff. Le compte est soldé… pour l'instant." },
            new[]{ "Soldé. Ton crédit est rétabli." },
            new[]{ "Juste à temps — mais la dette est réglée, rubis sur l'ongle." },
            new[]{ "Ha ! Sorti de ce tas de dettes. Rare, vraiment." },
            new[]{ "Tiens — un petit quelque chose pour t'aider avec cette dette. Repasse la prochaine fois, j'en aurai une autre pour toi.",
                   "Prends ça, ça t'aidera à rembourser ce que tu dois. Repasse me voir, j'en dénicherai une autre." }),

        ["spa"] = new(
            new[]{ "¿Un préstamo? Estupendo, estupendo. Firma aquí.",
                   "¿Escaso de monedas? Tranquilo — una firma y es tuyo.",
                   "Ja, tiempos difíciles, ¿eh? Firma y llévate lo que necesites." },
            new[]{ "Cuidado, ya arrastras una buena deuda." },
            new[]{ "¡Ja! ¡Un gran gastador! Clientes como tú me llenan la bolsa.",
                   "Vaya, gastas sin reparos. Un cliente muy bienvenido.",
                   "¿Tanta deuda? ¡Ja, sin ti mi negocio se iría a pique!" },
            new[]{ "¿Ya pagas? Bah. La cuenta queda cerrada… por ahora." },
            new[]{ "Saldada. Tu crédito queda restaurado." },
            new[]{ "Por los pelos, pero la deuda está bien saldada." },
            new[]{ "¡Ja! Saliste de ese montón de deudas. Raro, sí." },
            new[]{ "Toma — algo para ayudarte con esa deuda tuya. Vuelve en tu próxima visita y tendré otra para ti.",
                   "Llévate esto, te ayudará a saldar lo que debes. Pásate otra vez y te sacaré otra." }),

        ["esp"] = new(
            new[]{ "¿Un préstamo? Estupendo, estupendo. Firma aquí.",
                   "¿Escaso de monedas? Tranquilo — una firma y es tuyo.",
                   "Ja, tiempos difíciles, ¿eh? Firma y llévate lo que necesites." },
            new[]{ "Cuidado, ya arrastras una buena deuda." },
            new[]{ "¡Ja! ¡Un gran gastador! Clientes como tú me llenan la bolsa.",
                   "Vaya, gastas sin reparos. Un cliente muy bienvenido.",
                   "¿Tanta deuda? ¡Ja, sin ti mi negocio se iría a pique!" },
            new[]{ "¿Ya pagas? Bah. La cuenta queda cerrada… por ahora." },
            new[]{ "Saldada. Tu crédito queda restaurado." },
            new[]{ "Por los pelos, pero la deuda está bien saldada." },
            new[]{ "¡Ja! Saliste de ese montón de deudas. Raro, sí." },
            new[]{ "Toma — algo para ayudarte con esa deuda tuya. Vuelve en tu próxima visita y tendré otra para ti.",
                   "Llévate esto, te ayudará a saldar lo que debes. Pásate otra vez y te sacaré otra." }),

        ["ita"] = new(
            new[]{ "Un prestito? Ottimo, ottimo. Firma qui.",
                   "A corto di monete? Tranquillo — una firma ed è tuo.",
                   "Ah, tempi duri, eh? Firma e prendi in prestito ciò che ti serve." },
            new[]{ "Attento, ti porti addosso un bel po' di debito ormai." },
            new[]{ "Ah! Un gran spendaccione! Clienti così mi riempiono la borsa.",
                   "Però, spendi senza pensarci! Un ospite graditissimo.",
                   "Tutto questo debito? Ah, senza di te il mio affare crollerebbe!" },
            new[]{ "Già saldi? Bah. Il conto è chiuso… per ora." },
            new[]{ "Saldato. Il tuo credito è ripristinato." },
            new[]{ "Per un pelo — ma il debito è estinto per bene." },
            new[]{ "Ah! Uscito da quel mucchio di debiti. Raro davvero." },
            new[]{ "Tieni — un aiutino per quel tuo debito. Ripassa la prossima volta e ne avrò un'altra per te.",
                   "Prendi questa, ti aiuterà a ripagare ciò che devi. Fatti rivedere e ne scoverò un'altra." }),

        ["pol"] = new(
            new[]{ "Pożyczka? Wyśmienicie, wyśmienicie. Podpisz tutaj.",
                   "Krucho z monetą? Bez obaw — jeden podpis i twoje.",
                   "Ha, ciężkie czasy, co? Podpisz i pożycz, ile trzeba." },
            new[]{ "Ostrożnie — nazbierało ci się już sporo długu." },
            new[]{ "Ha! Rozrzutny klient! Tacy jak ty napełniają mi sakiewkę.",
                   "Oho, wydajesz bez wahania! Nader miły gość.",
                   "Tyle długu? Ha, bez ciebie interes by podupadł!" },
            new[]{ "Już spłacone? Hmpf. Rachunek zamknięty… na razie." },
            new[]{ "Spłacone w całości. Twój kredyt przywrócony." },
            new[]{ "Na styk — ale dług spłacony jak należy." },
            new[]{ "Ha! Wygrzebałeś się z tej góry długów. Rzadkość." },
            new[]{ "Masz — coś, co pomoże ci z tym długiem. Wpadnij następnym razem, a będę miał dla ciebie kolejną.",
                   "Weź to, pomoże ci spłacić, coś winien. Zajrzyj znowu, a wygrzebię następną." }),

        ["ptb"] = new(
            new[]{ "Um empréstimo? Ótimo, ótimo. Assine aqui.",
                   "Sem moedas? Não se preocupe — uma assinatura e é seu.",
                   "Ha, tempos difíceis, hein? Assine e pegue o que precisar." },
            new[]{ "Cuidado, você já carrega uma bela dívida." },
            new[]{ "Ha! Um gastador e tanto! Clientes assim enchem minha bolsa.",
                   "Ora, você gasta à vontade! Um cliente muito bem-vindo.",
                   "Tanta dívida? Ha, sem você meu negócio afundava!" },
            new[]{ "Já pagou? Hmpf. A conta está encerrada… por ora." },
            new[]{ "Quitada. Seu crédito está restaurado." },
            new[]{ "No limite — mas a dívida foi quitada direitinho." },
            new[]{ "Ha! Escapou daquele monte de dívidas. Coisa rara." },
            new[]{ "Toma — algo para ajudar com essa sua dívida. Volte na próxima visita que eu terei outra pra você.",
                   "Leve isto, vai ajudar a abater o que você deve. Passe aqui de novo e eu desencavo outra." }),

        ["rus"] = new(
            new[]{ "Заём? Прекрасно, прекрасно. Подпишите здесь.",
                   "Не хватает монет? Не беда — одна подпись, и оно ваше.",
                   "Ха, времена тяжёлые, а? Подпишите и берите, сколько нужно." },
            new[]{ "Осторожно — долг у вас уже немалый набрался." },
            new[]{ "Ха! Щедрый покупатель! Такие, как ты, набивают мой кошель.",
                   "Ого, тратишь не скупясь! Желаннейший гость.",
                   "Столько долгу? Ха, без тебя моё дело зачахло бы!" },
            new[]{ "Уже платишь? Хм. Что ж, счёт закрыт… пока что." },
            new[]{ "Погашено полностью. Кредит восстановлен." },
            new[]{ "Впритык — но долг закрыт как положено." },
            new[]{ "Ха! Выбрался из той горы долгов. Редкость, право." },
            new[]{ "Вот — кое-что в помощь с твоим долгом. Загляни в следующий раз, и у меня найдётся ещё одна.",
                   "Возьми, поможет расплатиться с тем, что задолжал. Заходи снова, откопаю тебе ещё одну." }),

        ["tha"] = new(
            new[]{ "จะกู้หรือ? ดี ดีมาก มาเซ็นตรงนี้เลย",
                   "ขาดเงินหรือ? ไม่ต้องห่วง เซ็นทีเดียวก็ได้แล้ว",
                   "ฮ่า ลำบากสินะ เซ็นซะ แล้วยืมได้ตามต้องการ" },
            new[]{ "ระวังหน่อยนะ หนี้ของเจ้าก้อนโตพอตัวแล้ว" },
            new[]{ "ฮ่า! ใจถึงจริง! ลูกค้าอย่างเจ้านี่แหละที่ทำให้ถุงเงินข้าตุง",
                   "โอ้โฮ ใช้จ่ายไม่อั้นเลยนะ ลูกค้าแบบนี้ยินดีต้อนรับ",
                   "หนี้ขนาดนี้? ฮ่า ไม่มีเจ้า การค้าข้าคงเจ๊งแน่!" },
            new[]{ "จ่ายแล้วหรือ? ฮึ งั้นบัญชีนี้ก็ปิดไป… ชั่วคราว" },
            new[]{ "ชำระครบแล้ว เครดิตของเจ้ากลับคืนมา" },
            new[]{ "หวุดหวิด แต่หนี้ก็เคลียร์เรียบร้อยดี" },
            new[]{ "ฮ่า! ปีนออกจากกองหนี้นั่นได้ หายากจริงๆ" },
            new[]{ "เอ้า รับไป ช่วยเรื่องหนี้ของเจ้าได้อยู่ มาอีกครั้งหน้า เดี๋ยวข้าหาอีกใบไว้ให้",
                   "เอาไปสิ ช่วยให้เจ้าปลดหนี้ได้บ้าง แวะมาอีกนะ เดี๋ยวข้าขุดอีกใบมาให้" }),

        ["tur"] = new(
            new[]{ "Borç mu? Âlâ, âlâ. Şuraya imzayı at.",
                   "Kesen mi boş? Merak etme — bir imza, gerisi senin.",
                   "Ha, zor günler, ha? İmzala, ne gerekiyorsa al." },
            new[]{ "Dikkat et, hatırı sayılır bir borç birikti sende." },
            new[]{ "Ha! Eli açık bir müşteri! Senin gibiler kesemi doldurur.",
                   "Vay, gönlünce harcıyorsun! Baş tacı müşterisin.",
                   "Bu kadar borç mu? Ha, sen olmasan işim batardı!" },
            new[]{ "Şimdiden mi ödedin? Hmpf. Hesap kapandı… şimdilik." },
            new[]{ "Tamamı ödendi. İtibarın geri geldi." },
            new[]{ "Kıl payı — ama borç tam olarak kapandı." },
            new[]{ "Ha! Şu borç yığınından sıyrıldın. Nadir iş doğrusu." },
            new[]{ "Al bakalım — şu borcuna bir nebze yardımı dokunur. Bir dahaki gelişinde uğra, sana bir tane daha ayırırım.",
                   "Şunu al, borcunu azaltmana yarar. Yine bir uğra, sana bir tane daha çıkarırım." }),
    };

    /// <summary>Per-language, per-card INDIRECT hints. When the merchant hands a debt-payoff card he alludes
    /// to what's coming NEXT without naming it — keyed by the next card's CARD KEY. Injected into the "relics"
    /// LocTable by LocInjectionPatch as DEBT_HINT_&lt;KEY&gt; so PlayDialogue's LocString can resolve them.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> HintsByLang = new()
    {
        ["eng"] = new()
        {
            ["SETTLEMENT"]       = "Next time, I'll set aside something to shield you when the blows rain down.",
            ["INVOICE"]          = "Come back and I'll have a way to bill your foes back for all you've paid.",
            ["INTEREST_SUPPORT"] = "I'll dig up something that lets a bit of your coin find its way home.",
            ["JOB_PLACEMENT"]    = "Next visit I might line up a spot of honest work to fatten your purse.",
            ["PAYMENT_BENEFIT"]  = "I'll keep something that toughens your hide the more you pay.",
            ["REFUND"]           = "For a diligent payer, I'll tuck away a little reward.",
            ["BLOOD_PAYMENT"]    = "Should your coin run dry, I know a way to settle in... redder currency.",
            ["COUNTERCLAIM"]     = "Next time, a trinket so every payment jabs at your enemies.",
            ["STATEMENT"]        = "I'll leave a ledger-trick that turns each payment into a fresh card in hand.",
        },
        ["kor"] = new()
        {
            ["SETTLEMENT"]       = "다음엔 매질이 쏟아질 때 자넬 막아줄 만한 걸 챙겨두지.",
            ["INVOICE"]          = "다음에 오면 자네가 낸 만큼 적에게 되청구할 한 방을 마련해두마.",
            ["INTEREST_SUPPORT"] = "낸 돈이 조금은 자네 주머니로 돌아오게 해줄 물건을 찾아두지.",
            ["JOB_PLACEMENT"]    = "다음엔 자네 손에 일감 쥐여 주머니 불릴 만한 걸 알아봐 두겠네.",
            ["PAYMENT_BENEFIT"]  = "갚으면 갚을수록 몸이 단단해지는 걸 하나 준비해두지.",
            ["REFUND"]           = "성실히 갚는 자에게 돌아갈 소소한 보답을 챙겨두마.",
            ["BLOOD_PAYMENT"]    = "돈이 마르거든, 더 붉은 값으로 빚을 치르는 수를 일러주지.",
            ["COUNTERCLAIM"]     = "다음엔 낼 때마다 적을 쿡 찌르는 물건을 챙겨두겠네.",
            ["STATEMENT"]        = "낼 때마다 장부를 넘겨 새 패를 뽑게 해줄 재주를 남겨두지.",
        },
        ["jpn"] = new()
        {
            ["SETTLEMENT"]       = "次は、殴りが降り注ぐときにお前を守ってくれる一品を取っておこう。",
            ["INVOICE"]          = "また来い。お前が払った分、敵に付け回してやる手を用意しておく。",
            ["INTEREST_SUPPORT"] = "払った銭が少しはお前の懐に戻るような品を掘り出しておくぞ。",
            ["JOB_PLACEMENT"]    = "次の来店じゃ、懐を潤す真っ当な仕事の口を見繕っておこうかの。",
            ["PAYMENT_BENEFIT"]  = "払えば払うほど体が頑丈になる一品を取っておこう。",
            ["REFUND"]           = "真面目に払う者にはな、ちょいとした礼を取っておいてやる。",
            ["BLOOD_PAYMENT"]    = "銭が尽きたときはな…もっと赤い通貨で払う手を教えてやろう。",
            ["COUNTERCLAIM"]     = "次は、払うたびに敵をチクリと刺す小物を取っておこう。",
            ["STATEMENT"]        = "払うたびに帳面をめくって新しい札を引かせる、そんな仕掛けを残しておこう。",
        },
        ["zhs"] = new()
        {
            ["SETTLEMENT"]       = "下回，我给你留件挨打时能替你挡上一挡的东西。",
            ["INVOICE"]          = "再来一趟，我备下一招，把你付的账统统回敬给敌人。",
            ["INTEREST_SUPPORT"] = "我给你翻出件宝贝，让你付的钱有一小部分能回到你兜里。",
            ["JOB_PLACEMENT"]    = "下次来，我或许给你张罗份正经营生，好让你的钱袋鼓起来。",
            ["PAYMENT_BENEFIT"]  = "我给你留件东西，你付得越多，身子骨就越结实。",
            ["REFUND"]           = "对按时还钱的主顾嘛，我会藏下一点小回报。",
            ["BLOOD_PAYMENT"]    = "等你的钱花光了…我有个法子，让你用更红的通货来还账。",
            ["COUNTERCLAIM"]     = "下回给你留个小玩意儿，每回付账都戳敌人一下。",
            ["STATEMENT"]        = "我给你留一手账本的把戏，每付一次账就翻出一张新牌到手里。",
        },
        ["deu"] = new()
        {
            ["SETTLEMENT"]       = "Nächstes Mal lege ich dir etwas beiseite, das dich schützt, wenn die Hiebe niederprasseln.",
            ["INVOICE"]          = "Komm wieder, dann hab ich einen Weg, deinen Feinden alles zurückzustellen, was du gezahlt hast.",
            ["INTEREST_SUPPORT"] = "Ich grabe dir etwas aus, das ein wenig von deinem Geld den Heimweg finden lässt.",
            ["JOB_PLACEMENT"]    = "Beim nächsten Besuch besorge ich dir vielleicht ehrliche Arbeit, die deinen Beutel füllt.",
            ["PAYMENT_BENEFIT"]  = "Ich behalte etwas, das deine Haut härter macht, je mehr du zahlst.",
            ["REFUND"]           = "Für einen fleißigen Zahler lege ich eine kleine Belohnung zurück.",
            ["BLOOD_PAYMENT"]    = "Sollte dein Geld versiegen, kenne ich einen Weg, in… röterer Währung zu begleichen.",
            ["COUNTERCLAIM"]     = "Nächstes Mal ein Schmuckstück, damit jede Zahlung deine Feinde sticht.",
            ["STATEMENT"]        = "Ich hinterlasse dir einen Buchhalter-Trick, der jede Zahlung in eine frische Karte auf der Hand verwandelt.",
        },
        ["fra"] = new()
        {
            ["SETTLEMENT"]       = "La prochaine fois, je te mettrai de côté de quoi te protéger quand les coups pleuvront.",
            ["INVOICE"]          = "Reviens, et j'aurai un moyen de refacturer à tes ennemis tout ce que tu as payé.",
            ["INTEREST_SUPPORT"] = "Je te dénicherai de quoi faire revenir un peu de ta monnaie dans ta poche.",
            ["JOB_PLACEMENT"]    = "À ta prochaine visite, je pourrais te trouver un honnête travail pour garnir ta bourse.",
            ["PAYMENT_BENEFIT"]  = "Je te garde de quoi durcir ta peau à mesure que tu paies.",
            ["REFUND"]           = "Pour un payeur assidu, je mettrai de côté une petite récompense.",
            ["BLOOD_PAYMENT"]    = "Si ta monnaie vient à manquer, je connais un moyen de régler en… monnaie plus rouge.",
            ["COUNTERCLAIM"]     = "La prochaine fois, un bibelot pour que chaque paiement pique tes ennemis.",
            ["STATEMENT"]        = "Je te laisserai un tour de grand livre qui change chaque paiement en une carte fraîche en main.",
        },
        ["spa"] = new()
        {
            ["SETTLEMENT"]       = "La próxima vez te apartaré algo que te proteja cuando lluevan los golpes.",
            ["INVOICE"]          = "Vuelve y tendré un modo de refacturar a tus enemigos todo lo que has pagado.",
            ["INTEREST_SUPPORT"] = "Te desenterraré algo que haga que parte de tu moneda regrese a tu bolsillo.",
            ["JOB_PLACEMENT"]    = "En tu próxima visita quizá te consiga un trabajo honrado para engordar tu bolsa.",
            ["PAYMENT_BENEFIT"]  = "Te guardaré algo que te endurece el pellejo cuanto más pagas.",
            ["REFUND"]           = "Para un pagador diligente, apartaré una pequeña recompensa.",
            ["BLOOD_PAYMENT"]    = "Si tu moneda se agota… conozco un modo de saldar en moneda más roja.",
            ["COUNTERCLAIM"]     = "La próxima vez, un dije para que cada pago pinche a tus enemigos.",
            ["STATEMENT"]        = "Te dejaré un truco de libro de cuentas que convierte cada pago en una carta nueva en la mano.",
        },
        ["esp"] = new()
        {
            ["SETTLEMENT"]       = "La próxima vez te apartaré algo que te proteja cuando lluevan los golpes.",
            ["INVOICE"]          = "Vuelve y tendré un modo de refacturar a tus enemigos todo lo que has pagado.",
            ["INTEREST_SUPPORT"] = "Te desenterraré algo que haga que parte de tu moneda regrese a tu bolsillo.",
            ["JOB_PLACEMENT"]    = "En tu próxima visita quizá te consiga un trabajo honrado para engordar tu bolsa.",
            ["PAYMENT_BENEFIT"]  = "Te guardaré algo que te endurece el pellejo cuanto más pagas.",
            ["REFUND"]           = "Para un pagador diligente, apartaré una pequeña recompensa.",
            ["BLOOD_PAYMENT"]    = "Si tu moneda se agota… conozco un modo de saldar en moneda más roja.",
            ["COUNTERCLAIM"]     = "La próxima vez, un dije para que cada pago pinche a tus enemigos.",
            ["STATEMENT"]        = "Te dejaré un truco de libro de cuentas que convierte cada pago en una carta nueva en la mano.",
        },
        ["ita"] = new()
        {
            ["SETTLEMENT"]       = "La prossima volta ti metterò da parte qualcosa che ti ripari quando piovono i colpi.",
            ["INVOICE"]          = "Torna e avrò un modo per riaddebitare ai tuoi nemici tutto ciò che hai pagato.",
            ["INTEREST_SUPPORT"] = "Ti scoverò qualcosa che faccia tornare un po' delle tue monete in tasca.",
            ["JOB_PLACEMENT"]    = "Alla prossima visita magari ti procuro un onesto lavoro per gonfiarti la borsa.",
            ["PAYMENT_BENEFIT"]  = "Ti terrò qualcosa che ti indurisce la pelle più paghi.",
            ["REFUND"]           = "Per un pagatore diligente, metterò da parte una piccola ricompensa.",
            ["BLOOD_PAYMENT"]    = "Se le monete ti si esauriscono… conosco un modo per saldare in valuta più rossa.",
            ["COUNTERCLAIM"]     = "La prossima volta, un ninnolo perché ogni pagamento punga i tuoi nemici.",
            ["STATEMENT"]        = "Ti lascerò un trucco da libro mastro che trasforma ogni pagamento in una carta fresca in mano.",
        },
        ["pol"] = new()
        {
            ["SETTLEMENT"]       = "Następnym razem odłożę ci coś, co osłoni cię, gdy posypią się ciosy.",
            ["INVOICE"]          = "Wróć, a znajdę sposób, by odbić twoim wrogom wszystko, coś zapłacił.",
            ["INTEREST_SUPPORT"] = "Wygrzebię ci coś, dzięki czemu część twojej monety wróci do kieszeni.",
            ["JOB_PLACEMENT"]    = "Przy następnej wizycie może załatwię ci uczciwą robotę, by napełnić sakiewkę.",
            ["PAYMENT_BENEFIT"]  = "Zachowam ci coś, co hartuje skórę, im więcej płacisz.",
            ["REFUND"]           = "Dla pilnego płatnika odłożę drobną nagrodę.",
            ["BLOOD_PAYMENT"]    = "Gdy zabraknie ci monet… znam sposób, by spłacić w bardziej czerwonej walucie.",
            ["COUNTERCLAIM"]     = "Następnym razem błyskotka, by każda zapłata kłuła twoich wrogów.",
            ["STATEMENT"]        = "Zostawię ci sztuczkę z ksiąg, co zamienia każdą zapłatę w świeżą kartę w ręce.",
        },
        ["ptb"] = new()
        {
            ["SETTLEMENT"]       = "Da próxima vez, vou separar algo que te proteja quando os golpes chovem.",
            ["INVOICE"]          = "Volte, e terei um jeito de cobrar de volta dos teus inimigos tudo o que pagaste.",
            ["INTEREST_SUPPORT"] = "Vou desencavar algo que faça parte da tua moeda voltar pro teu bolso.",
            ["JOB_PLACEMENT"]    = "Na próxima visita talvez eu arranje um trabalho honesto pra encher tua bolsa.",
            ["PAYMENT_BENEFIT"]  = "Vou guardar algo que endurece teu couro quanto mais você paga.",
            ["REFUND"]           = "Para um pagador aplicado, vou reservar uma pequena recompensa.",
            ["BLOOD_PAYMENT"]    = "Se tua moeda secar… conheço um jeito de quitar em moeda mais vermelha.",
            ["COUNTERCLAIM"]     = "Da próxima vez, um berloque pra que cada pagamento espete teus inimigos.",
            ["STATEMENT"]        = "Vou te deixar um truque de livro-caixa que transforma cada pagamento numa carta nova na mão.",
        },
        ["rus"] = new()
        {
            ["SETTLEMENT"]       = "В следующий раз припасу тебе кое-что, что прикроет, когда посыплются удары.",
            ["INVOICE"]          = "Загляни снова, и у меня найдётся способ выставить твоим врагам счёт за всё, что ты заплатил.",
            ["INTEREST_SUPPORT"] = "Откопаю тебе штуковину, чтоб хоть часть твоих монет возвращалась в карман.",
            ["JOB_PLACEMENT"]    = "В следующий заход, глядишь, подыщу тебе честную работёнку, чтоб набить кошель.",
            ["PAYMENT_BENEFIT"]  = "Приберегу тебе кое-что, от чего шкура крепчает, чем больше платишь.",
            ["REFUND"]           = "Прилежному плательщику припрячу небольшую награду.",
            ["BLOOD_PAYMENT"]    = "Коли монета иссякнет… знаю способ расплатиться валютой покраснее.",
            ["COUNTERCLAIM"]     = "В следующий раз — безделицу, чтоб каждая уплата колола твоих врагов.",
            ["STATEMENT"]        = "Оставлю тебе бухгалтерский трюк, что превращает каждую уплату в свежую карту в руке.",
        },
        ["tha"] = new()
        {
            ["SETTLEMENT"]       = "คราวหน้า ข้าจะเก็บของไว้ให้สักอย่าง คอยกำบังเจ้ายามหมัดกระหน่ำลงมา",
            ["INVOICE"]          = "กลับมาอีก แล้วข้าจะมีวิธีทวงคืนศัตรูตามที่เจ้าจ่ายไปทั้งหมด",
            ["INTEREST_SUPPORT"] = "ข้าจะขุดของมาให้ ที่ทำให้เงินของเจ้าบางส่วนหาทางกลับเข้ากระเป๋า",
            ["JOB_PLACEMENT"]    = "คราวหน้าที่แวะมา ข้าอาจหางานสุจริตให้เจ้าทำ เพิ่มพูนถุงเงินเสียหน่อย",
            ["PAYMENT_BENEFIT"]  = "ข้าจะเก็บของไว้ให้ ยิ่งเจ้าจ่ายมาก ตัวก็ยิ่งแกร่ง",
            ["REFUND"]           = "สำหรับคนที่ชำระอย่างขยันขันแข็ง ข้าจะกันรางวัลเล็กๆ ไว้ให้",
            ["BLOOD_PAYMENT"]    = "หากเงินของเจ้าเหือดแห้ง… ข้ารู้วิธีชำระด้วยสกุลเงินที่แดงกว่านั้น",
            ["COUNTERCLAIM"]     = "คราวหน้า ของกระจุกสักชิ้น ให้ทุกครั้งที่จ่ายได้ทิ่มแทงศัตรู",
            ["STATEMENT"]        = "ข้าจะทิ้งกลเม็ดบัญชีไว้ให้ ที่พลิกทุกการจ่ายเป็นไพ่ใบใหม่ในมือ",
        },
        ["tur"] = new()
        {
            ["SETTLEMENT"]       = "Bir dahakine, darbeler yağarken seni koruyacak bir şey ayırırım.",
            ["INVOICE"]          = "Yine gel, ödediğin her kuruşu düşmanlarına geri fatura edecek bir yol bulurum.",
            ["INTEREST_SUPPORT"] = "Sana öyle bir şey çıkarırım ki paranın bir kısmı cebine geri döner.",
            ["JOB_PLACEMENT"]    = "Bir sonraki gelişinde belki kesen dolsun diye sana dürüst bir iş ayarlarım.",
            ["PAYMENT_BENEFIT"]  = "Ödedikçe derini sertleştiren bir şey saklarım sana.",
            ["REFUND"]           = "Gayretli bir ödeyiciye ufak bir ödül ayırırım.",
            ["BLOOD_PAYMENT"]    = "Paran suyunu çekerse… daha kırmızı bir akçeyle ödemenin yolunu bilirim.",
            ["COUNTERCLAIM"]     = "Bir dahakine, her ödeme düşmanlarını dürtsün diye bir ıvır zıvır.",
            ["STATEMENT"]        = "Sana bir defter oyunu bırakırım; her ödemeyi eline taze bir karta çevirir.",
        },
    };

    internal static Set For(string? lang) => lang != null && Lines.TryGetValue(lang, out var s) ? s : Lines["eng"];

    /// <summary>Loc keys (DEBT_HINT_&lt;CARD KEY&gt; → hint) to inject for a language; falls back to English.
    /// Called by LocInjectionPatch when merging into the "relics" LocTable.</summary>
    internal static Dictionary<string, string> HintLocKeys(string? lang)
    {
        var src = (lang != null && HintsByLang.TryGetValue(lang, out var d)) ? d : HintsByLang["eng"];
        var outd = new Dictionary<string, string>();
        foreach (var kv in src) outd[$"DEBT_HINT_{kv.Key}"] = kv.Value;
        return outd;
    }

    private static string CurrentLang => LocManager.Instance?.Language ?? "eng";

    /// <summary>All (key → line) pairs for a language, keyed as e.g. DEBT_BARK_FIRST_0 — merged into the
    /// "relics" table by LocInjectionPatch so PlayDialogue's LocString can resolve any variant.</summary>
    internal static Dictionary<string, string> LocKeys(Set s)
    {
        var d = new Dictionary<string, string>();
        void Add(string baseKey, string[] arr) { for (int i = 0; i < arr.Length; i++) d[$"{baseKey}_{i}"] = arr[i]; }
        Add(KFirst, s.First); Add(K200, s.O200); Add(K300, s.O300);
        Add(KR1, s.R1); Add(KR2, s.R2); Add(KR3, s.R3); Add(KR4, s.R4);
        Add(KGrant, s.Grant);
        return d;
    }

    private static void SayRandom(string baseKey, string[] arr)
    {
        if (arr.Length == 0) return;
        int i = Rng.Next(arr.Length);
        try { NMerchantRoom.Instance?.MerchantButton?.PlayDialogue(new LocString("relics", $"{baseKey}_{i}"), 3.0); }
        catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] merchant bark failed: {e.Message}"); }
    }

    internal static void SayFirst() { var s = For(CurrentLang); SayRandom(KFirst, s.First); }

    /// <summary>Sly line said when the merchant hands the player a debt-payoff card. Given a HINT KEY (the next
    /// card's CARD KEY) he alludes to its effect without naming it; with none, falls back to the generic Grant
    /// lines. Deferred a beat so the merchant room UI is ready when this fires on shop-enter.</summary>
    internal static void SayGrant(string? hintKey)
    {
        var s = For(CurrentLang);
        void Fire()
        {
            try
            {
                if (!string.IsNullOrEmpty(hintKey))
                    NMerchantRoom.Instance?.MerchantButton?.PlayDialogue(new LocString("relics", $"DEBT_HINT_{hintKey}"), 3.0);
                else
                    SayRandom(KGrant, s.Grant);
            }
            catch (Exception e) { MainFile.Logger.Warn($"[{MainFile.ModId}] merchant grant bark failed: {e.Message}"); }
        }
        if (Godot.Engine.GetMainLoop() is Godot.SceneTree tree) { var t = tree.CreateTimer(0.6); t.Timeout += Fire; }
        else Fire();
    }
    internal static void Say200()  { var s = For(CurrentLang); SayRandom(K200,   s.O200); }
    internal static void Say300()  { var s = For(CurrentLang); SayRandom(K300,   s.O300); }

    /// <summary>Repay bark by the tier the player was at when they cleared the debt (1..4).</summary>
    internal static void SayRepay(int tier)
    {
        var s = For(CurrentLang);
        var (key, arr) = tier <= 1 ? (KR1, s.R1) : tier == 2 ? (KR2, s.R2) : tier == 3 ? (KR3, s.R3) : (KR4, s.R4);
        SayRandom(key, arr);
    }
}
