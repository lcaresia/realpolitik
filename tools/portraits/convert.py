# Converts the generated portraits (media/retratos/originais/*.png, ~2 MB each) to the in-game size:
# src/CurrencyMod/Retratos/<office>_<era>_<m|f>.jpg, 256 px, embedded in the DLL (CurrencyMod.csproj).
# The media/retratos and src/CurrencyMod/Retratos folders are data folders and keep their names.
# Usage (in WSL): python3 convert.py
import glob, os
from PIL import Image

BASE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(BASE, 'media', 'retratos', 'originais')
DST = os.path.join(BASE, 'src', 'CurrencyMod', 'Retratos')
SIZE = 256

os.makedirs(DST, exist_ok=True)
count = 0
for path in sorted(glob.glob(os.path.join(SRC, '*.png'))):
    name = os.path.splitext(os.path.basename(path))[0]
    target = os.path.join(DST, name + '.jpg')
    if os.path.exists(target) and os.path.getmtime(target) >= os.path.getmtime(path):
        continue
    with Image.open(path) as image:
        image.convert('RGB').resize((SIZE, SIZE), Image.LANCZOS).save(target, quality=86, optimize=True)
    count += 1
print(f'{count} converted; {len(glob.glob(os.path.join(DST, "*.jpg")))} in total in {DST}')
