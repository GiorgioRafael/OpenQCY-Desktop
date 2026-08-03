from pathlib import Path

from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets" / "AppIcon-v2.png"


def prepare_master() -> Image.Image:
    source = Image.open(SOURCE).convert("RGBA")
    alpha_bounds = source.getchannel("A").getbbox()
    if alpha_bounds is None:
        raise RuntimeError(f"{SOURCE} has no visible pixels")

    content = source.crop(alpha_bounds)
    side = max(content.size)
    padding = round(side * 0.035)
    master = Image.new("RGBA", (side + padding * 2, side + padding * 2))
    position = (
        (master.width - content.width) // 2,
        (master.height - content.height) // 2,
    )
    master.alpha_composite(content, position)
    return master


def save_icon(master: Image.Image, target: Path, sizes: list[int]) -> None:
    # A light sharpen keeps the mint headset mark legible at notification-area sizes.
    sharpened = master.filter(ImageFilter.UnsharpMask(radius=1.1, percent=115, threshold=3))
    sharpened.save(target, format="ICO", sizes=[(size, size) for size in sizes])


icon = prepare_master()
save_icon(icon, ROOT / "Assets" / "AppIcon-v2.ico", [16, 20, 24, 32, 40, 48, 64, 128, 256])
save_icon(icon, ROOT / "Assets" / "TrayIcon.ico", [16, 20, 24, 32, 40, 48, 64])
