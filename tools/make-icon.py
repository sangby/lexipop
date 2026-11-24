from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent.parent
out = root / "src" / "LexiPop" / "Assets" / "lexipop.ico"
out.parent.mkdir(parents=True, exist_ok=True)

size = 256
img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

draw.ellipse([8, 8, size - 8, size - 8], fill=(107, 138, 254, 255))

font = None
for candidate in ("C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/arialbd.ttf"):
    try:
        font = ImageFont.truetype(candidate, 150)
        break
    except OSError:
        continue

if font is not None:
    bbox = draw.textbbox((0, 0), "L", font=font)
    w = bbox[2] - bbox[0]
    h = bbox[3] - bbox[1]
    draw.text(
        ((size - w) / 2 - bbox[0], (size - h) / 2 - bbox[1]),
        "L",
        font=font,
        fill="white",
    )
else:
    draw.text((size / 2, size / 2), "L", fill="white", anchor="mm")

img.save(
    out,
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print("saved", out)
