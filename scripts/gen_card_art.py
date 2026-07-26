"""Regenerate DebtLoan card-art OUTLIERS in the deck's comic / cel-shaded style.

Task 1 (art retouch): the priority-A outliers were painterly / low-readability and broke
the set's bold-comic look. This drives the local ComfyUI (Animagine XL 4.0) to produce a few
candidates per card so a human can pick. It does NOT overwrite shipped art — candidates land
in <scratchpad>/cardart_candidates/ and are montaged for review.

Concept per card is kept faithful to what the card DOES (see loc in DebtLoanLoc.cs); only the
rendering style is normalised toward the good comic cards (invoice / job_placement / diligent_payment).

Usage: python gen_card_art.py <out_dir> [n_per_card]   (ComfyUI must be up on 127.0.0.1:8188)
"""
import json, os, sys, time, urllib.request

HOST = 'http://127.0.0.1:8188'

# Shared style suffix — LOCKS the deck's signature look: purple gradient background, thick black
# outlines, flat cel shading, ONE clear centred subject, generous negative space. Matches the good
# cards (invoice / job_placement / blood_payment / diligent_payment). No dynamic/explosion tags —
# those are what made the first pass chaotic.
STYLE = ('purple background, simple gradient background, flat color, bold thick black outline, '
         'cel shading, limited color palette, single clear subject, '
         'subject fills the frame, close-up, bold large foreground subject, '
         'clean iconic illustration, masterpiece, high score, great score, absurdres')

# Aggressively banish the clutter that broke the first pass.
NEG = ('cluttered, busy background, chaotic, explosion, shattered, debris, flying fragments, '
       'motion blur, speed lines, many rays, light rays, sparkle spam, detailed background, '
       'background objects, extra objects, realistic, photorealistic, oil painting, 3d render, '
       'text, watermark, signature, letters, numbers, ui, border, frame, '
       'lowres, bad anatomy, bad hands, worst quality, low quality, blurry, '
       'multiple views, cropped, crystal ball, glowing orb, magic sphere, fortune orb, '
       'deformed hands, mutated hands, malformed hands, fused fingers, extra fingers, '
       'extra digits, missing fingers, six fingers, too many fingers, long fingers, '
       'twisted fingers, bad fingers, disfigured hands, extra hands')

# Per-card subject — ONE clear thing, leaning on the deck's hand+coins motif so the set coheres.
# The STS2 SHOP MERCHANT's hand (see selftest.sp.8_merchant_bark.png): a hooded fortune-teller
# with a crescent-moon mask, indigo-blue robe, and slender LIGHT-BLUE / cyan skin. Using the
# merchant's hands ties the loan/shop deck to the NPC you actually borrow from.
HAND = ('a slender light blue-skinned cyan hand of a hooded fortune-teller merchant, '
        'indigo blue robe sleeve, bold black outline')

# v4: subjects fill the frame AND the awkward bare hands are replaced with the deck's gloved hand.
# credit_restored / loan_strike keep the purple+gold GAUNTLET (matches diligent_payment) — no bare hands.
CARDS = {
    # 신용 불량 — ominous debt curse. Gloved hands holding a big cursed coin.
    'bad_credit': (f'a close-up of two {HAND}, holding up a large cracked cursed coin with a '
                   'glowing red crack, filling the frame, ominous, dark purple background'),
    # 신용 회복 — gain Plating (armor). A big glowing crest, purple+gold gauntlet.
    'credit_restored': ('a large golden winged shield crest filling the frame, '
                        'a purple and gold gauntlet presenting it, softly glowing, restored'),
    # 대출 강타 — deal damage AND add debt: a big coin-edged blade, purple+gold gauntlet.
    'loan_strike': ('a close-up of a purple and gold gauntlet thrusting a large coin-edged blade '
                    'toward the viewer, the blade filling the frame, two gold coins'),
    # 환급 — coins flow back to you. Big cupped gloved hands.
    'refund': (f'a close-up of two open cupped {HAND}, filling the frame, '
               'several gold coins falling into them'),
    # 명세서 — a bank STATEMENT DOCUMENT held by the gloved hand.
    'statement': (f'a {HAND} holding up a large bank statement paper filling the frame, '
                  'neat printed ledger lines and a red stamp, a few gold coins at the bottom edge'),
    # 품삯 — gain Gold. Big envelope stuffed with coins, gloved hands.
    'wages': (f'a close-up of two {HAND}, holding up a big pay envelope overflowing with a pile '
              'of gold coins, filling the frame'),
    # 저당 — gain Block by pledging collateral, adds debt. Property deed + shield emblem.
    'mortgage': (f'a {HAND} pressing a red wax seal onto a property deed printed with a house '
                 'emblem, a bold blue shield emblem behind it, two gold coins, filling the frame, '
                 'purple background'),
    # 이자 지원 — merchant SUBSIDISES you: a giving gesture. SINGLE clean hand (fewer hands = better
    # anatomy), natural five-fingered.
    'interest_support': (f'a single {HAND}, a natural well-drawn five-fingered hand with correct '
                         'anatomy, palm up offering a small pile of gold coins toward the viewer, '
                         'a giving supportive gesture, soft warm golden glow, '
                         'filling the frame, purple background'),
    # 이자 지원 (hands-free variant) — sidestep the AI-hand problem entirely.
    'interest_support_nohand': ('no humans, no hands, a tilted leather coin purse pouring a bright '
                                'stream of gold coins into a neat glowing pile, warm golden glow, '
                                'filling the frame, purple background'),
    # 파산 선언 — wipe your Debt, gain Strength, but can't earn gold. An empty coin purse turned inside
    # out over a broken/crossed-out ledger — "nothing left to lose". Hands-free (avoids AI-hand issues).
    'bankruptcy': ('no humans, no hands, a large empty leather coin purse turned upside-down and inside-out '
                   'with nothing falling out, over a torn ledger paper stamped with a big red cross mark, '
                   'a single last coin rolling away, filling the frame, purple background'),
    # 파산 선언 (variant) — a big red BANKRUPT stamp slamming down onto an empty ledger, dust puff.
    'bankruptcy_stamp': ('no humans, no hands, a heavy round red wax stamp pressed onto a torn empty ledger '
                         'leaving a bold red cross seal, a couple of coins tipping off the edge, '
                         'filling the frame, purple background'),
    # 파산 선언 v2 — an open empty iron vault, door swung wide, cobwebs, a lone coin on the floor. "금고가 텅 빔."
    'bankruptcy_vault': ('no humans, no hands, a heavy iron vault safe with its door swung wide open, '
                         'completely empty inside with cobwebs, one lone gold coin on the floor, dusty, '
                         'filling the frame, purple background'),
    # 파산 선언 v3 — a judge wooden gavel striking down onto a torn ledger with a red seal, coins scattering.
    'bankruptcy_gavel': ('no humans, no hands, a wooden judge gavel striking down hard onto a torn ledger '
                         'with a red wax seal, gold coins scattering from the impact, '
                         'filling the frame, purple background'),
    # 파산 선언 v4 — a toppled collapsing tower/stack of gold coins spilling apart. "무너지는 동전탑."
    'bankruptcy_collapse': ('no humans, no hands, a tall tower stack of gold coins toppling and collapsing '
                            'apart, coins tumbling down, filling the frame, purple background'),
    # 납부 혜택 (Payment Benefit) — grants Plating (판금=armor) on payment. Current art is too flashy (golden
    # angel wings); the name is a calm "benefit for paying" = protection. A sturdy shield/armor-plate with a coin.
    'payment_benefit': ('no humans, no hands, a sturdy round metal shield emblem with a single gold coin embossed '
                        'at its center, calm, protective, plain, bold black outline, filling the frame, purple background'),
    # 납부 혜택 v2 — layered armor plating (판금) with a coin motif, defensive and understated.
    'payment_benefit_plate': ('no humans, no hands, layered overlapping metal armor plates forming a chestpiece, '
                              'a small gold coin set in the center plate, defensive, understated, '
                              'filling the frame, purple background'),
    # 차환 (Refinance) — convert Debt curses into Payment cards. Dark cursed debt papers being reshaped into clean
    # golden payment receipt slips, a looping renewal arrow. Hands-free (avoids AI-hand issues).
    'refinance': ('no humans, no hands, a stack of dark torn cursed debt papers with red marks transforming into '
                  'a neat clean stack of golden payment receipt slips, a big curved renewal arrow looping around '
                  'them, one gold coin, filling the frame, purple background'),
    # 차환 v2 — two ledgers, an old torn dark one and a fresh clean golden one, linked by a swap arrow.
    'refinance_swap': ('no humans, no hands, an old torn dark ledger book and a fresh clean golden ledger book side '
                       'by side, a bold curved arrow swapping between them, a couple of gold coins, '
                       'filling the frame, purple background'),
    # ── 차환 REDO (v1/v2 both read as abstract yellow ribbon mush — too many concepts per prompt). Each variant
    #    below is ONE object doing ONE thing, which is what the readable cards in this deck all have in common.
    # v3 — the debt CHAIN breaking into coins. Most iconic "빚 → 납부" metaphor, single diagonal subject.
    'refinance_chain': ('no humans, no hands, one thick dark iron chain running diagonally across the frame, '
                        'snapped in the middle, the broken links turning into bright gold coins, '
                        'filling the frame, purple background'),
    # v4 — the SEAL swap: one contract, its old cracked red seal falling away, a new gold seal in its place.
    'refinance_seal': ('no humans, no hands, one large parchment contract, a cracked dark red wax seal breaking '
                       'off its corner and a bright gold wax seal pressed in its place, '
                       'filling the frame, purple background'),
    # v5 — the shackle opening: debt released, a coin where the lock was. Very few shapes, very readable.
    'refinance_shackle': ('no humans, no hands, one heavy iron shackle sprung wide open, a single bright gold coin '
                          'sitting where its lock used to be, filling the frame, purple background'),
    # v6 — the renewal symbol: a bold circular arrow of gold around a small stack of coins. Pure icon.
    'refinance_loop': ('no humans, no hands, one bold thick circular renewal arrow made of gold curving all the way '
                       'around, a small neat stack of gold coins in its center, clean iconic symbol, '
                       'filling the frame, purple background'),
    # v7 — the paper swap done as ONE object: a single ledger page, its dark torn left half turning into a clean
    #      golden receipt on the right. The transformation reads left-to-right instead of as two piles.
    'refinance_page': ('no humans, no hands, one single sheet of paper, its left half a dark torn cursed ledger '
                       'page and its right half a clean bright golden receipt slip, one continuous sheet, '
                       'filling the frame, purple background'),
}

# ── OBJECT style (차환 redo) ────────────────────────────────────────────────────────────────────────
# The STYLE above pushes 'close-up / subject fills the frame / bold large foreground subject'. For a card
# whose subject is an OBJECT rather than a hand, that push makes SDXL crop into the object until it reads
# as abstract ribbons (exactly what killed refinance v1/v2 and the first redo pass). The shipped art that
# actually reads — settlement (balance scale), bankruptcy (gavel), payment_benefit (medallion) — is all
# MEDIUM-distance with the whole object inside the frame. So object cards get their own style suffix.
STYLE_OBJ = ('purple background, simple gradient background, flat color, bold thick black outline, '
             'cel shading, limited color palette, single clear subject, centered composition, '
             'medium shot, the whole object visible inside the frame, generous negative space around it, '
             'clean iconic illustration, masterpiece, high score, great score, absurdres')

# Cards rendered with STYLE_OBJ instead of STYLE.
OBJECT_CARDS = {'refinance_chain2', 'refinance_scroll', 'refinance_lock', 'refinance_swap3',
                'refinance_hourglass', 'refinance_coinswap', 'refinance_vaultkey'}

CARDS.update({
    # 차환 REDO-2 — same four ideas, but framed as whole objects at medium distance (see STYLE_OBJ).
    # v8 — the debt chain snapped, its links becoming coins. Clearest "빚 → 납부" metaphor in the set.
    'refinance_chain2': ('a heavy iron chain lying broken in two across a stone table, the snapped links at the '
                         'break turning into bright gold coins, purple background'),
    # v9 — the swap read as two crossed scrolls: the old debt contract and the new one, side by side.
    'refinance_scroll': ('two rolled paper contract scrolls crossed over each other, the lower one dark and '
                         'tattered with a broken red wax seal, the upper one clean cream with a bright gold wax '
                         'seal, a few gold coins resting at their base, purple background'),
    # v10 — release: a golden key turning in the open padlock that held the debt chain.
    'refinance_lock': ('a large open iron padlock hanging from a short chain, a big golden key turned in its '
                       'keyhole, a few gold coins on the ground below it, purple background'),
    # v11 — the ledger swap, both books fully in frame with a gold arrow between them.
    'refinance_swap3': ('an old dark tattered ledger book lying open on the left and a clean new golden ledger '
                        'book on the right, a bold curved gold arrow running from the old one to the new one, '
                        'a few gold coins between them, purple background'),
    # ── 차환 REDO-3. Learning from redo-2: this checkpoint renders HARD OBJECTS WITH FAMILIAR SILHOUETTES well
    #    (see the shipped gavel / balance scale / medallion) and fails completely on paper, scrolls and
    #    "A transforming into B". So: pick one famous silhouette and let the coins carry the debt theme.
    # v12 — the hourglass. 차환's real meaning is BUYING TIME on the debt; flipping the glass is that, exactly,
    #       and an hourglass is one of the most legible silhouettes there is.
    'refinance_hourglass': ('a large golden hourglass standing upright, its sand falling as tiny gold coins, '
                            'a small pile of gold coins around its base, purple background'),
    # v13 — the coin swap, using the deck's own coin motif instead of paper: one cracked dark cursed coin, one
    #       clean bright coin, an arrow between them.
    'refinance_coinswap': ('a big cracked dark cursed coin lying on the left and a big clean bright gold coin '
                           'standing on the right, a bold curved gold arrow between them, purple background'),
    # v14 — a heavy vault door swinging open with a golden key in its lock and coins spilling out (bankruptcy_vault
    #       proved the vault silhouette renders; here it OPENS with money instead of being empty).
    'refinance_vaultkey': ('a heavy round iron vault door swung open with a big golden key in its lock, bright '
                           'gold coins spilling out of the opening, purple background'),
    # ── 차환 REDO-4 (the one that landed). Diagnosis after 33 rejected candidates: STYLE_OBJ was not the
    #    problem — the CONCEPTS were. Every 차환 idea so far was "object A transforming into object B", and this
    #    checkpoint cannot render a transformation; it blends the two into abstract ribbons. The art that DID
    #    ship (bankruptcy: purse + ledger + coin) is a PLAIN STILL LIFE of conventional objects in a conventional
    #    arrangement. So: ordinary still lifes, and back on the original STYLE that produced the shipped cards.
    # v15 — the hourglass still life. 차환 = buying more TIME on the debt; hourglass + coins says that plainly.
    'refinance_hourglass2': ('no humans, no hands, a large golden hourglass standing upright with its sand '
                             'running through, a neat stack of gold coins beside it, '
                             'filling the frame, purple background'),
    # v16 — the unlocked padlock still life (refinance_lock_0 was the strongest of the rejected batch; this drops
    #       the meaningless cone that shared its frame).
    'refinance_lock2': ('no humans, no hands, a big golden padlock hanging open and unlocked on a broken iron '
                        'chain, a small pile of gold coins beneath it, filling the frame, purple background'),
    # ── 돌려막기 (Kiting) — burn one Debt card for 30 gold. "Plug one hole by digging another." Economy card, so
    #    it uses the deck's merchant-HAND motif like 품삯 / 환급 (those hand cards render well), plus a plain
    #    still-life alternative in case the hands come out mangled.
    'kiting_purse': (f'a close-up of two {HAND}, one holding a torn empty leather coin purse with a hole in it '
                     'and the other dropping a handful of gold coins into it, filling the frame, purple background'),
    'kiting_patch': ('no humans, no hands, an old torn leather coin purse with a bright gold patch sewn over the '
                     'hole in its side, a few gold coins spilling out from under the patch, '
                     'filling the frame, purple background'),
    'kiting_note': ('no humans, no hands, a single folded old debt promissory note with a small stack of bright '
                    'gold coins resting on top of it, filling the frame, purple background'),
    # ── 도파민 3종 (어음 / 레버리지 / 채무 조정). Following the 차환 REDO-4 lesson: PLAIN STILL LIFES of hard
    #    objects with famous silhouettes, on the original STYLE. No "A transforming into B", no loose paper as
    #    the sole subject (this checkpoint cannot render either).
    # 어음 (Promissory Note) — borrow tempo on credit. ★SHIPPED = promissory_note_0.
    'promissory_note': ('no humans, no hands, a folded parchment promissory note stamped with a large red wax '
                        'seal lying on a dark wooden desk, a black ink quill standing in a brass inkpot beside '
                        'it, a few gold coins scattered in front, filling the frame, purple background'),
    'promissory_note_seal': ('no humans, no hands, a heavy brass wax-seal stamp resting on a folded parchment '
                             'note with a fresh red wax seal, a small stack of gold coins beside it, '
                             'filling the frame, purple background'),
    # 레버리지 (Leverage) — debt as firepower.
    # ★REDO NOTE: both lever ideas below FAILED — this checkpoint renders neither a crowbar nor a plank-and-fulcrum
    #   and just returns a heap of ingots (the tool vanishes, only the gold survives). Same class of failure as the
    #   차환 transformations. Fix = drop the machine, keep a WEAPON silhouette (자본 타격 shipped a sword + coin, so
    #   blades render) and let the gold carry the "debt is the damage" read. ★SHIPPED = leverage_sword_0.
    'leverage_crowbar': ('no humans, no hands, a long iron crowbar wedged under a heavy stack of gold ingots and '
                         'prying it up off a stone block, a few gold coins on the ground, '
                         'filling the frame, purple background'),
    'leverage_fulcrum': ('no humans, no hands, a wooden plank balanced on a stone fulcrum, one small gold coin '
                         'on the short end lifting a huge heavy pile of gold bars on the long end, '
                         'filling the frame, purple background'),
    'leverage_flail': ('no humans, no hands, a heavy leather coin purse tied to the end of a thick iron chain '
                       'swung like a flail, bright gold coins flying out of it, '
                       'filling the frame, purple background'),
    'leverage_sword': ('no humans, no hands, a heavy iron sword driven point-down into a tall pile of bright gold '
                       'coins, the blade buried deep in the money, filling the frame, purple background'),
    'leverage_tower': ('no humans, no hands, a very tall precarious tower of stacked gold coins leaning over a '
                       'small iron sword lying at its base, filling the frame, purple background'),
    # 채무 조정 (Restructuring) — the debt written off.
    # ★REDO NOTES: shears/burn/shackle/scale all under-read — "cutting" and "burning" don't say WRITTEN OFF, the
    #   shackle silhouette never rendered, and a balance scale collides with 정산's shipped art. The ledger book
    #   (the mod's own 빚 장부 motif) + a red cancel mark is the one that lands. First pass put an UPRIGHT cross on
    #   it (reads religious), so the final prompts force a DIAGONAL slash / X — the universal write-off mark.
    'restructuring_shears': ('no humans, no hands, a pair of heavy iron shears biting through a thick iron chain '
                             'and snapping a link, gold coins scattered beneath, '
                             'filling the frame, purple background'),
    'restructuring_stamp': ('no humans, no hands, a heavy wooden stamp handle pressed down on an open ledger '
                            'book, a bold red stamped mark across the page, a few gold coins beside the book, '
                            'filling the frame, purple background'),
    'restructuring_burn': ('no humans, no hands, an old thick ledger book lying open and burning with bright '
                           'flames, its pages curling to ash, a few gold coins on the table beside it, '
                           'filling the frame, purple background'),
    'restructuring_shackle': ('no humans, no hands, a heavy broken iron shackle lying open on the ground with its '
                              'snapped chain falling away, bright gold coins scattered around it, '
                              'filling the frame, purple background'),
    'restructuring_ledger_x': ('no humans, no hands, a thick dark closed ledger book with a bold red cross mark '
                               'painted across its cover, a large gold wax seal on the corner, a small stack of '
                               'gold coins beside it, filling the frame, purple background'),
    'restructuring_scale': ('no humans, no hands, a golden balance scale with both pans hanging perfectly level '
                            'and empty, a few gold coins on the table beneath it, '
                            'filling the frame, purple background'),
    'restructuring_slash': ('no humans, no hands, a thick dark closed ledger book with one bold diagonal red '
                            'paint slash struck across its cover from corner to corner, a gold wax seal on the '
                            'corner, small stacks of gold coins on both sides, filling the frame, purple background'),
    'restructuring_openx': ('no humans, no hands, an open ledger book with a bold red X drawn across the written '
                            'page cancelling it, a black ink quill lying on top, a few gold coins beside the book, '
                            'filling the frame, purple background'),
    # ★The diagonal-slash retry ALSO failed: asked for a corner-to-corner slash, the model returns an upright cross
    #   or a red drip every time. Conclusion: this checkpoint cannot draw a diagonal cancel mark. Final two concepts
    #   therefore carry "the account is closed" WITHOUT any cross at all — a sealed book, or a contract torn in two.
    'restructuring_sealed': ('no humans, no hands, a thick closed ledger book bound shut with a red ribbon tied '
                             'in a knot and a large gold wax seal pressed over it, small stacks of gold coins on '
                             'both sides, filling the frame, purple background'),
    'restructuring_torn': ('no humans, no hands, an old debt contract with a red wax seal torn cleanly into two '
                           'halves lying apart on a dark wooden desk, a few gold coins between the pieces, '
                           'filling the frame, purple background'),
})


def workflow(seed, pos, style=STYLE):
    return {
        '4': {'class_type': 'CheckpointLoaderSimple',
              'inputs': {'ckpt_name': 'animagine-xl-4.0.safetensors'}},
        '5': {'class_type': 'EmptyLatentImage',
              'inputs': {'width': 1152, 'height': 896, 'batch_size': 1}},
        '6': {'class_type': 'CLIPTextEncode', 'inputs': {'text': pos + ', ' + style, 'clip': ['4', 1]}},
        '7': {'class_type': 'CLIPTextEncode', 'inputs': {'text': NEG, 'clip': ['4', 1]}},
        '3': {'class_type': 'KSampler',
              'inputs': {'seed': seed, 'steps': 28, 'cfg': 5.5, 'sampler_name': 'euler_ancestral',
                         'scheduler': 'normal', 'denoise': 1.0,
                         'model': ['4', 0], 'positive': ['6', 0], 'negative': ['7', 0],
                         'latent_image': ['5', 0]}},
        '8': {'class_type': 'VAEDecode', 'inputs': {'samples': ['3', 0], 'vae': ['4', 2]}},
        '9': {'class_type': 'SaveImage', 'inputs': {'filename_prefix': 'debtart', 'images': ['8', 0]}},
    }

def post(path, payload):
    req = urllib.request.Request(HOST + path, data=json.dumps(payload).encode(),
                                 headers={'Content-Type': 'application/json'})
    return json.loads(urllib.request.urlopen(req, timeout=30).read())

def get(path):
    return json.loads(urllib.request.urlopen(HOST + path, timeout=30).read())

def main():
    out = sys.argv[1]
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    only = set(sys.argv[3].split(',')) if len(sys.argv) > 3 and sys.argv[3] else None
    os.makedirs(out, exist_ok=True)
    jobs = {}  # prompt_id -> (card, seed)
    for card, pos in CARDS.items():
        if only and card not in only:
            continue
        for i in range(n):
            seed = 700_000 + i * 1013
            st = STYLE_OBJ if card in OBJECT_CARDS else STYLE
            r = post('/prompt', {'prompt': workflow(seed, pos, st)})
            jobs[r['prompt_id']] = (card, i)
            print('queued', card, 'seed', seed, r['prompt_id'])
    done, t0 = set(), time.time()
    while len(done) < len(jobs) and time.time() - t0 < 1800:
        time.sleep(5)
        for pid, (card, i) in jobs.items():
            if pid in done:
                continue
            h = get(f'/history/{pid}')
            if pid in h and h[pid].get('outputs'):
                for _, o in h[pid]['outputs'].items():
                    for img in o.get('images', []):
                        url = (f"{HOST}/view?filename={img['filename']}"
                               f"&subfolder={img.get('subfolder','')}&type={img['type']}")
                        dst = os.path.join(out, f"{card}_{i}.png")
                        urllib.request.urlretrieve(url, dst)
                        print('saved', dst)
                done.add(pid)
    print(f'done {len(done)}/{len(jobs)} in {time.time()-t0:.0f}s')

if __name__ == '__main__':
    main()
