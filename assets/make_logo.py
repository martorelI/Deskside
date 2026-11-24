# Draws assets/logo.png and assets/Deckside.ico (sky-blue tile, triangle of three linked nodes).
# Usage: python3 assets/make_logo.py assets <preview-folder>
import math, sys
from PIL import Image, ImageDraw

SKY = (43, 166, 227)      # sky blue background
WHITE = (255, 255, 255)
SKY_DARK = (24, 122, 178)  # inner detail

def draw(size):
    if size <= 24:
        return draw_small(size)
    k = 4  # supersampling
    W = size * k
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    m = W * 0.04
    d.rounded_rectangle([m, m, W - m, W - m], radius=W * 0.20, fill=SKY)
    # equilateral triangle of nodes, centered (slightly lowered for optical balance)
    cx, cy = W / 2, W * 0.535
    R = W * 0.30
    pts = [(cx + R * math.cos(math.radians(a)), cy + R * math.sin(math.radians(a))) for a in (-90, 30, 150)]
    edge = W * 0.075
    for i in range(3):
        a, b = pts[i], pts[(i + 1) % 3]
        d.line([a, b], fill=WHITE, width=int(edge))
    spoke = W * 0.045
    for p in pts:
        d.line([(cx, cy), p], fill=WHITE, width=int(spoke))
    rv = W * 0.095
    for (x, y) in pts:
        d.ellipse([x - rv, y - rv, x + rv, y + rv], fill=WHITE)
        ri = rv * 0.42
        d.ellipse([x - ri, y - ri, x + ri, y + ri], fill=SKY_DARK)
    rc = W * 0.075
    d.ellipse([cx - rc, cy - rc, cx + rc, cy + rc], fill=WHITE)
    return img.resize((size, size), Image.LANCZOS)

def draw_small(size):
    """16-24 px: thicker strokes, solid nodes, no spokes, so it stays readable in the taskbar."""
    k = 8
    W = size * k
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, W - 1, W - 1], radius=W * 0.22, fill=SKY)
    cx, cy = W / 2, W * 0.56
    R = W * 0.33
    pts = [(cx + R * math.cos(math.radians(a)), cy + R * math.sin(math.radians(a))) for a in (-90, 30, 150)]
    for i in range(3):
        d.line([pts[i], pts[(i + 1) % 3]], fill=WHITE, width=int(W * 0.11))
    rv = W * 0.13
    for (x, y) in pts:
        d.ellipse([x - rv, y - rv, x + rv, y + rv], fill=WHITE)
    rc = W * 0.08
    d.ellipse([cx - rc, cy - rc, cx + rc, cy + rc], fill=WHITE)
    return img.resize((size, size), Image.LANCZOS)

out = sys.argv[1]
draw(256).save(out + "/logo.png")
sizes = [16, 24, 32, 48, 64, 128, 256]
draw(256).save(out + "/Deckside.ico", sizes=[(s, s) for s in sizes], append_images=[draw(s) for s in sizes])
draw(512).save(sys.argv[2] + "/logo-preview.png")
