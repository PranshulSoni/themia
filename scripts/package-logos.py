"""
Draw the MSIX package logos ([S-1060]) into src/Themia.App/Assets/Package/.

The art is the Themia mark from lometics.com/public/logos/themia.svg (a 64-unit grid: one cyan
widget with a soft glow and four grey ones), drawn at every size Windows asks a package for rather
than scaled from one bitmap, so a 16 px taskbar icon and a 620 px splash are both sharp. The mark sits
on the rounded dark tile of Assets/themia-logo.png.

Run after changing the mark:  python scripts/package-logos.py   (needs Pillow)
The PNGs are checked in; packaging does not run this.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parent.parent / "src" / "Themia.App" / "Assets" / "Package"
SS = 4  # supersampling factor

# (x, y, w, h, radius, rgba) on the 64-unit grid of themia.svg
WIDGETS = [
    (33, 11, 20, 10, 3, (255, 255, 255, int(0.18 * 255))),
    (33, 25, 20, 14, 3, (255, 255, 255, int(0.12 * 255))),
    (11, 33, 14, 20, 3, (255, 255, 255, int(0.14 * 255))),
    (29, 43, 24, 10, 3, (255, 255, 255, int(0.10 * 255))),
]
ACCENT = (11, 11, 18, 18, 4)
ACCENT_FROM, ACCENT_TO = (0x7A, 0xD9, 0xFF), (0x60, 0xCD, 0xFF)
BG_FROM, BG_TO = (0x1A, 0x1A, 0x1C), (0x0A, 0x0A, 0x0C)


def diagonal(size, start, end):
    """A top-left to bottom-right linear gradient, as the SVG's bg and accent fills."""
    grad = Image.new("RGBA", (size, size))
    px = grad.load()
    for y in range(size):
        for x in range(size):
            t = (x + y) / (2 * (size - 1)) if size > 1 else 0
            px[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(start, end)) + (255,)
    return grad


def mark(size):
    """The rounded tile with the widget grid, `size` px square, transparent outside the tile."""
    s = size * SS
    u = s / 64.0
    art = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Tile: rounded square with the dark gradient and a faint rim, as in themia-logo.png.
    tile_mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(tile_mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=14 * u, fill=255)
    art.paste(diagonal(64, BG_FROM, BG_TO).resize((s, s), Image.BILINEAR), (0, 0), tile_mask)
    ImageDraw.Draw(art).rounded_rectangle(
        [0, 0, s - 1, s - 1], radius=14 * u, outline=(255, 255, 255, 28), width=max(1, round(0.6 * u)))

    # Grey widgets.
    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for x, y, w, h, r, fill in WIDGETS:
        d.rounded_rectangle([x * u, y * u, (x + w) * u, (y + h) * u], radius=r * u, fill=fill)
    art = Image.alpha_composite(art, layer)

    # Accent widget with its glow (feGaussianBlur stdDeviation 1.4 merged under the shape).
    x, y, w, h, r = ACCENT
    accent_mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(accent_mask).rounded_rectangle(
        [x * u, y * u, (x + w) * u, (y + h) * u], radius=r * u, fill=255)
    accent = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    accent.paste(diagonal(64, ACCENT_FROM, ACCENT_TO).resize((s, s), Image.BILINEAR), (0, 0), accent_mask)
    glow = accent.filter(ImageFilter.GaussianBlur(1.4 * u * 1.6))
    art = Image.alpha_composite(art, glow)
    art = Image.alpha_composite(art, accent)

    # Keep everything inside the tile.
    clipped = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    clipped.paste(art, (0, 0), tile_mask)
    return clipped.resize((size, size), Image.LANCZOS)


def canvas(width, height, art_share):
    """A transparent canvas with the mark centred, `art_share` of the shorter side."""
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    side = max(1, round(min(width, height) * art_share))
    img.alpha_composite(mark(side), ((width - side) // 2, (height - side) // 2))
    return img


def save(img, name):
    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / name, optimize=True)


def main():
    scales = {100: 1.0, 125: 1.25, 150: 1.5, 200: 2.0, 400: 4.0}

    # App list / taskbar icon: plated (scale-*) and unplated (targetsize-*).
    for scale, f in scales.items():
        n = round(44 * f)
        save(canvas(n, n, 0.92), f"Square44x44Logo.scale-{scale}.png")
    for target in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
        img = canvas(target, target, 1.0)
        save(img, f"Square44x44Logo.targetsize-{target}.png")
        save(img, f"Square44x44Logo.targetsize-{target}_altform-unplated.png")
        save(img, f"Square44x44Logo.targetsize-{target}_altform-lightunplated.png")

    for scale in (100, 125, 150, 200, 400):
        f = scales[scale]
        save(canvas(round(150 * f), round(150 * f), 0.6), f"Square150x150Logo.scale-{scale}.png")
        save(canvas(round(310 * f), round(150 * f), 0.6), f"Wide310x150Logo.scale-{scale}.png")
        save(canvas(round(71 * f), round(71 * f), 0.66), f"SmallTile.scale-{scale}.png")
        save(canvas(round(310 * f), round(310 * f), 0.5), f"LargeTile.scale-{scale}.png")
        save(canvas(round(50 * f), round(50 * f), 0.92), f"StoreLogo.scale-{scale}.png")
        save(canvas(round(620 * f), round(300 * f), 0.5), f"SplashScreen.scale-{scale}.png")


if __name__ == "__main__":
    main()
