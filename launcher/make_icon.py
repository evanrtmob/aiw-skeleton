from PIL import Image, ImageDraw
import math
import os
import struct

out_dir = r"c:\Users\evan.romero\Desktop\Archivos\AppInWhats\launcher"
os.makedirs(out_dir, exist_ok=True)


def make_wa(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = max(1, int(size * 0.04))
    d.ellipse([pad, pad, size - pad - 1, size - pad - 1], fill=(37, 211, 102, 255))

    bx0, by0 = size * 0.22, size * 0.18
    bx1, by1 = size * 0.82, size * 0.72
    radius = max(2, int(size * 0.18))
    d.rounded_rectangle([bx0, by0, bx1, by1], radius=radius, fill=(255, 255, 255, 255))
    d.polygon(
        [
            (size * 0.28, size * 0.62),
            (size * 0.18, size * 0.88),
            (size * 0.48, size * 0.70),
        ],
        fill=(255, 255, 255, 255),
    )

    green = (37, 211, 102, 255)
    cx, cy = size * 0.52, size * 0.45
    r = max(2, size * 0.07)
    d.ellipse(
        [cx - size * 0.22 - r, cy - size * 0.16 - r, cx - size * 0.22 + r, cy - size * 0.16 + r],
        fill=green,
    )
    d.ellipse(
        [cx + size * 0.18 - r, cy + size * 0.14 - r, cx + size * 0.18 + r, cy + size * 0.14 + r],
        fill=green,
    )
    for t in range(0, 21):
        a = t / 20.0
        x = (cx - size * 0.22) * (1 - a) + (cx + size * 0.18) * a
        y = (cy - size * 0.16) * (1 - a) + (cy + size * 0.14) * a
        bow = math.sin(a * math.pi) * size * 0.08
        x -= bow * 0.3
        y -= bow
        rr = max(1.5, size * 0.055)
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=green)
    return img


def save_ico(path: str, images: list[Image.Image]) -> None:
    """Escribe un ICO multi-tamaño válido (PNG frames)."""
    frames = []
    for im in images:
        import io

        buf = io.BytesIO()
        im.save(buf, format="PNG")
        frames.append(buf.getvalue())

    count = len(frames)
    # ICONDIR
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = []
    for im, data in zip(images, frames):
        w = 0 if im.width >= 256 else im.width
        h = 0 if im.height >= 256 else im.height
        entries.append(struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset))
        offset += len(data)

    with open(path, "wb") as f:
        f.write(header)
        for e in entries:
            f.write(e)
        for data in frames:
            f.write(data)


sizes = [16, 24, 32, 48, 64, 128, 256]
images = [make_wa(s) for s in sizes]
png_path = os.path.join(out_dir, "appinwhats-icon.png")
ico_path = os.path.join(out_dir, "appinwhats.ico")
images[-1].save(png_path)
save_ico(ico_path, images)
print("PNG", png_path, os.path.getsize(png_path))
print("ICO", ico_path, os.path.getsize(ico_path))
