"""Draw the app icon (assets/app.ico): a battery filled to the limit line.

Run: .venv/bin/python assets/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).with_name("app.ico")
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
S = 1024  # draw large, scale down

BODY = (38, 50, 56, 255)        # dark slate outline
FILL = (46, 204, 113, 255)      # green charge
FILL_DARK = (34, 160, 88, 255)  # bottom band of the charge
LIMIT = (255, 255, 255, 255)    # white limit marker
FILL_RATIO = 0.8                # the 80% limit


def draw(size_hint: int) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # thicker strokes for tiny sizes so the shape survives downscaling
    stroke = int(S * (0.075 if size_hint <= 24 else 0.06))
    left, top, right, bottom = int(S * 0.06), int(S * 0.22), int(S * 0.86), int(S * 0.78)
    radius = int(S * 0.09)
    # terminal nub
    nub_w, nub_h = int(S * 0.08), int(S * 0.22)
    d.rounded_rectangle(
        (right - stroke // 2, S // 2 - nub_h // 2, right + nub_w, S // 2 + nub_h // 2),
        radius=int(S * 0.03), fill=BODY)
    # body outline
    d.rounded_rectangle((left, top, right, bottom), radius=radius, outline=BODY, width=stroke, fill=(255, 255, 255, 255))
    # charge fill up to the limit
    pad = stroke + int(S * 0.025)
    inner_l, inner_t, inner_r, inner_b = left + pad, top + pad, right - pad, bottom - pad
    fill_r = inner_l + int((inner_r - inner_l) * FILL_RATIO)
    d.rounded_rectangle((inner_l, inner_t, fill_r, inner_b), radius=int(radius * 0.5), fill=FILL)
    band = int((inner_b - inner_t) * 0.28)
    d.rounded_rectangle((inner_l, inner_b - band, fill_r, inner_b), radius=int(radius * 0.5), fill=FILL_DARK)
    d.rectangle((inner_l, inner_b - band, fill_r, inner_b - band + int(radius * 0.5)), fill=FILL_DARK)
    # limit marker: white bar right after the fill, slightly taller than the fill
    if size_hint >= 24:
        bar_w = int(S * 0.035)
        d.rounded_rectangle((fill_r + int(S * 0.02), inner_t - int(S * 0.04), fill_r + int(S * 0.02) + bar_w, inner_b + int(S * 0.04)),
                            radius=bar_w // 2, fill=BODY)
    return img


def main() -> None:
    frames = [draw(n).resize((n, n), Image.LANCZOS) for n in SIZES]
    frames[-1].save(OUT, format="ICO", sizes=[(n, n) for n in SIZES], append_images=frames[:-1])
    frames[-1].save(OUT.with_name("app-256.png"))
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
