# Converte os retratos gerados (media/retratos/originais/*.png, ~2 MB cada) para o tamanho do jogo:
# src/CurrencyMod/Retratos/<cargo>_<época>_<m|f>.jpg, 256 px, embutidos na DLL (CurrencyMod.csproj).
# Uso (no WSL): python3 converter.py
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
print(f'{count} convertido(s); {len(glob.glob(os.path.join(DST, "*.jpg")))} no total em {DST}')
