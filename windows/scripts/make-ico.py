# Builds windows/Resources/Peeko.ico from the macOS app icon, so both apps share one icon.
# Usage (from the repo root, needs Pillow): python3 windows/scripts/make-ico.py
import io
import struct
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ICNS = ROOT / "Resources" / "AppIcon.icns"
ICO = ROOT / "windows" / "Resources" / "Peeko.ico"

# The 1024 px PNG inside the .icns.
data = ICNS.read_bytes()
i, art = 8, None
while i < len(data):
    kind, length = struct.unpack(">4sI", data[i : i + 8])
    if kind == b"ic10":
        art = Image.open(io.BytesIO(data[i + 8 : i + length])).convert("RGBA")
    i += length
assert art is not None, "no 1024 px image (ic10) in AppIcon.icns"

# The macOS grid leaves a 100 px margin for the shadow; Windows icons fill the square.
art = art.crop((100, 100, 924, 924))
art.save(ICO, sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print(f"wrote {ICO.relative_to(ROOT)}")
