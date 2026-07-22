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
                                        string[] R1, string[] R2, string[] R3, string[] R4);

    private const string KFirst = "DEBT_BARK_FIRST", K200 = "DEBT_BARK_200", K300 = "DEBT_BARK_300",
                         KR1 = "DEBT_REPAY_T1", KR2 = "DEBT_REPAY_T2", KR3 = "DEBT_REPAY_T3", KR4 = "DEBT_REPAY_T4";

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
            new[]{ "Ha! Clawed your way out of that pile of debt. Rare indeed." }),

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
            new[]{ "허! 그 빚더미에서 기어이 빠져나왔군. 좀처럼 없는 일이야." }),

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
            new[]{ "はは！ あの借金の山から這い上がったか。珍しいことよ。" }),

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
            new[]{ "哈！从那堆债里爬出来了。可真少见。" }),

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
            new[]{ "Ha! Dich aus dem Schuldenberg gewühlt. Wahrlich selten." }),

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
            new[]{ "Ha ! Sorti de ce tas de dettes. Rare, vraiment." }),

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
            new[]{ "¡Ja! Saliste de ese montón de deudas. Raro, sí." }),

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
            new[]{ "¡Ja! Saliste de ese montón de deudas. Raro, sí." }),

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
            new[]{ "Ah! Uscito da quel mucchio di debiti. Raro davvero." }),

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
            new[]{ "Ha! Wygrzebałeś się z tej góry długów. Rzadkość." }),

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
            new[]{ "Ha! Escapou daquele monte de dívidas. Coisa rara." }),

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
            new[]{ "Ха! Выбрался из той горы долгов. Редкость, право." }),

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
            new[]{ "ฮ่า! ปีนออกจากกองหนี้นั่นได้ หายากจริงๆ" }),

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
            new[]{ "Ha! Şu borç yığınından sıyrıldın. Nadir iş doğrusu." }),
    };

    internal static Set For(string? lang) => lang != null && Lines.TryGetValue(lang, out var s) ? s : Lines["eng"];

    private static string CurrentLang => LocManager.Instance?.Language ?? "eng";

    /// <summary>All (key → line) pairs for a language, keyed as e.g. DEBT_BARK_FIRST_0 — merged into the
    /// "relics" table by LocInjectionPatch so PlayDialogue's LocString can resolve any variant.</summary>
    internal static Dictionary<string, string> LocKeys(Set s)
    {
        var d = new Dictionary<string, string>();
        void Add(string baseKey, string[] arr) { for (int i = 0; i < arr.Length; i++) d[$"{baseKey}_{i}"] = arr[i]; }
        Add(KFirst, s.First); Add(K200, s.O200); Add(K300, s.O300);
        Add(KR1, s.R1); Add(KR2, s.R2); Add(KR3, s.R3); Add(KR4, s.R4);
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
