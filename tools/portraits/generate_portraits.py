# Generates the ministers' portraits with the Codex in WSL (built-in image generation, the player's ChatGPT quota).
# Generic per office (Lucas's decision on 2026-10-10, to save quota): 12 offices x 3 eras x man/woman = 72.
#   eras: 1 = Ancient/Classical, 2 = Medieval/Early Modern, 3 = Industrial/Contemporary.
# Each office is one "codex exec" call that saves media/retratos/originais/<office>_<era>_<m|f>.png.
# The office keys (mao, chanceler, ...) are part of the portrait file names the mod loads: they are data, keep them.
# Measured: 3 images use ~10% of the 5 h window and ~1% of the week (Plus plan). When the window fills up, the script
# waits for it to reset (resets_at from the Codex sessions) and carries on.
# Usage (in WSL, through generate.sh): python3 generate_portraits.py [office ...] [--parallel N]   (no office: the missing ones).
import concurrent.futures, glob, json, os, subprocess, sys, time

BASE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(BASE, 'media', 'retratos', 'originais')
CODEX = os.path.expanduser('~/.local/bin/codex')

STYLE = ('Square bust portrait (head and shoulders, centered, eyes about one third from the top), painted semi-realistic '
         'digital illustration in the style of the strategy game Humankind: warm desaturated palette, visible painterly '
         'brushwork, soft rim light, plain dark parchment-brown textured background, no text, no frame, no border, no watermark.')

ERAS = {
    1: 'ANTIQUITY (ancient to classical era, bronze and iron age)',
    2: 'MEDIEVAL TO EARLY MODERN era (roughly 800-1700)',
    3: 'INDUSTRIAL TO CONTEMPORARY era (roughly 1850-1970)',
}

OFFICES = {
    'mao': 'Hand of the Throne, the chief advisor who chairs the council: rich robes, a heavy chain of office, a signet ring',
    'chanceler': 'Chancellor of foreign relations, a diplomat: fine layered garments, a sealed letter or wax seal, an ambassador insignia',
    'guerra': 'Minister of War, a general: armor or military uniform of the era, a cloak or sash, battle insignia',
    'financas': 'Treasurer of the realm: dark fine garments, a ledger, coins or an abacus',
    'agricultura': 'Minister of agriculture, prefect of the granaries: sturdy noble clothes, a wheat sheaf, a sickle or seed pouch',
    'obras': 'Master of Works, royal architect and engineer: practical clothes with a leather apron, a compass or measuring rod, plans',
    'comercio': 'Minister of trade and markets: rich imported fabrics, merchant scales, spice or amber pouches',
    'ciencia': 'Minister of science, royal scholar: scholar robes or academic dress of the era, an astrolabe or instruments, books or scrolls',
    'fe': 'Minister of faith, high priest or priestess: ceremonial religious vestments of the era, a sacred symbol, incense',
    'interior': 'Minister of the interior, governor of the provinces: official dress of the era, a ring of keys, a provincial map',
    'cultura': 'Minister of culture, master of the games and rites: festive refined garments, a laurel crown, a lyre or a theatre mask',
    'espionagem': 'Spymaster, master of whispers: dark hooded cloak or dark coat, part of the face in shadow, a folded secret letter',
}

GENDERS = {'m': 'a man', 'f': 'a woman'}


def files_for(office):
    return [f'{office}_{e}_{g}.png' for e in ERAS for g in GENDERS]


def prompt_for(office, wanted=None):
    wanted = wanted or files_for(office)
    lines = [f'Use your built-in image generation tool to create {len(wanted)} separate square images and save each one as PNG in the current folder with exactly the file names below. {STYLE}',
             f'All of them show the same office: {OFFICES[office]}. Middle-aged, dignified; each image a different person with a different ethnicity. Clothing, props and hairstyle must be accurate to the era.', '']
    for e, era in ERAS.items():
        for g, who in GENDERS.items():
            if f'{office}_{e}_{g}.png' in wanted:
                lines.append(f'- {office}_{e}_{g}.png: {who}, {era}.')
    lines += ['', 'Generate them one by one. Do not write any other file. Reply only with the list of saved file names.']
    return '\n'.join(lines)


def quota():
    """(5 h window usage in %, reset time as epoch) from the latest event in the Codex sessions."""
    found = (0.0, 0)
    files = sorted(glob.glob(os.path.expanduser('~/.codex/sessions/**/*.jsonl'), recursive=True), key=os.path.getmtime)[-5:]
    for f in files:
        for line in open(f, encoding='utf-8', errors='ignore'):
            if 'rate_limits' not in line:
                continue
            try:
                e = json.loads(line)
            except Exception:
                continue
            p = e.get('payload', e)
            rl = p.get('rate_limits') or (p.get('info') or {}).get('rate_limits')
            if rl and rl.get('primary'):
                found = (float(rl['primary'].get('used_percent') or 0), int(rl['primary'].get('resets_at') or 0))
    return found


def run(office):
    for attempt in range(4):
        missing = [f for f in files_for(office) if not os.path.exists(os.path.join(OUT, f))]
        if not missing:
            return f'{office}: complete'
        used, resets = quota()
        if used >= 85 and resets > time.time():
            wait = resets - time.time() + 60
            print(f'5 h window at {used:.0f}%: waiting {wait / 60:.0f} min', flush=True)
            time.sleep(wait)
        try:
            subprocess.run([CODEX, 'exec', '--skip-git-repo-check', '--sandbox', 'workspace-write', '--color', 'never', '-C', OUT, prompt_for(office, missing)],
                           capture_output=True, text=True, timeout=2400)
        except subprocess.TimeoutExpired:
            pass
    done = [f for f in files_for(office) if os.path.exists(os.path.join(OUT, f))]
    return f'{office}: {len(done)}/6'


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    argv = sys.argv[1:]
    parallel = 1
    if '--parallel' in argv:
        parallel = int(argv[argv.index('--parallel') + 1])
    offices = [a for a in argv if a in OFFICES] or list(OFFICES)
    with concurrent.futures.ThreadPoolExecutor(max_workers=parallel) as pool:
        for line in pool.map(run, offices):
            print(line, flush=True)
