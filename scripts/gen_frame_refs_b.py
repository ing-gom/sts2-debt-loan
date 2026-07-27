"""2nd motif family of luxury frame/banner references (new directions not in gen_frame_refs.py).
Same ComfyUI pipeline; broadens the reference library for the DebtLoan frame design search.
Usage: python gen_frame_refs_b.py <outdir> [n]   (SEED_BASE env for uniqueness)
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_frame_refs as G   # reuse wf/post/get/main infra

G.PROMPTS = {
 'obsidian_gold':   'an empty ornate card frame border, black obsidian and gold inlay, purple gemstone accents, dark luxury',
 'stained_glass':   'an empty ornate card frame border, purple and gold stained glass, lead came, cathedral luxury',
 'guilloche':       'an empty luxury card frame border, gold guilloche engraving, deep purple enamel, fabrege',
 'motherpearl':     'an empty ornate card frame border, mother of pearl and gold, iridescent purple, art deco luxury',
 'cloisonne':       'an empty ornate card frame border, purple cloisonne enamel and gold wire, byzantine luxury',
 'chain_ledger':    'an empty ornate card frame border of interlocking gold chains and purple wax seals, debt ledger motif',
 'gothic_tracery':  'an empty ornate card frame border, gold gothic tracery, purple velvet, cathedral window',
 'rococo':          'an empty ornate card frame border, gold rococo cartouche scrollwork, purple silk, versailles',
 'coin_stack':      'an empty ornate card frame border built from stacked gold coins and purple gems, treasury vault',
 'damascus':        'an empty ornate card frame border, damascus gold filigree, deep purple, ottoman luxury',
 'wax_seal_medal':  'an ornate gold medallion with a purple wax seal and ribbon, heraldic debt seal, isolated on dark',
 'banner_cartouche':'an ornate empty ribbon banner cartouche, gold and purple, rococo scroll ends, luxury nameplate',
}

if __name__ == '__main__':
    G.main()
