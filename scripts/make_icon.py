# The application icon: a rainy neon skyline. python3 scripts/make_icon.py icon  ->  icon.png + icon.ico (needs Pillow).
import random, sys
from PIL import Image, ImageDraw, ImageFilter
out = sys.argv[1]
S = 1024
rnd = random.Random(7)
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
# Rounded dark tile with a vertical night gradient.
bg = Image.new("RGBA", (S, S))
d = ImageDraw.Draw(bg)
for y in range(S):
    t = y / S
    d.line([(0, y), (S, y)], fill=(int(10 + 30 * t), int(6 + 8 * t), int(28 + 30 * (1 - t)), 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=190, fill=255)
img.paste(bg, (0, 0), mask)
# A big magenta sun behind the towers.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([262, 170, 762, 670], fill=(255, 40, 140, 255))
glow = glow.filter(ImageFilter.GaussianBlur(40))
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S)), mask))
d = ImageDraw.Draw(img)
d.ellipse([302, 210, 722, 630], fill=(255, 52, 150, 255))
for k, y in enumerate(range(450, 640, 34)):
    d.rectangle([290, y, 740, y + 8 + k * 3], fill=(18, 10, 40, 255))
# Towers: dark silhouettes with cyan and pink windows, one antenna spire.
towers = [(110, 560, 230), (215, 440, 330), (320, 600, 400), (390, 300, 520), (505, 500, 600), (590, 380, 690), (680, 540, 790), (770, 470, 900)]
for x0, top, x1 in towers:
    d.rectangle([x0, top, x1, S - 70], fill=(8, 6, 20, 255))
    d.line([(x0, top), (x1, top)], fill=(0, 230, 255, 255), width=6)
    for wy in range(top + 26, S - 110, 38):
        for wx in range(x0 + 18, x1 - 18, 30):
            if rnd.random() < 0.38:
                c = (0, 230, 255, 255) if rnd.random() < 0.7 else (255, 70, 170, 255)
                d.rectangle([wx, wy, wx + 12, wy + 16], fill=c)
d.rectangle([452, 150, 460, 300], fill=(8, 6, 20, 255))
d.ellipse([444, 136, 468, 160], fill=(255, 60, 80, 255))
# Rain.
rain = Image.new("RGBA", (S, S), (0, 0, 0, 0))
rd = ImageDraw.Draw(rain)
for _ in range(45):
    x = rnd.randint(60, S - 60); y = rnd.randint(60, S - 140); L = rnd.randint(30, 70)
    rd.line([(x, y), (x - 10, y + L)], fill=(120, 220, 255, 70), width=3)
img = Image.alpha_composite(img, rain)
# Clip to the tile again and add a neon rim.
clipped = Image.new("RGBA", (S, S), (0, 0, 0, 0))
clipped.paste(img, (0, 0), mask)
rim = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(rim).rounded_rectangle([30, 30, S - 30, S - 30], radius=185, outline=(0, 230, 255, 255), width=18)
rimglow = rim.filter(ImageFilter.GaussianBlur(10))
final = Image.alpha_composite(Image.alpha_composite(clipped, rimglow), rim)
final = final.resize((512, 512), Image.LANCZOS)
final.save(out + ".png")
final.save(out + ".ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
