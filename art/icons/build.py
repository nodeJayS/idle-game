"""Bake the UI slot icons from their SVG sources into tintable PNG sprites.

Source art is game-icons.net (CC BY 3.0) — see CREDITS in the repo README. The
SVGs ship white-on-black, which is exactly what we want: rasterise as-is, then
take the LUMINANCE as the alpha channel and force RGB to white. That gives a
clean antialiased silhouette with no background, and leaves every pixel white so
Unity's Image.color tints it to whatever the theme asks for — the same trick the
kit already uses for its rounded-rect sprites.

Run:  uv run --with svglib --with reportlab --with pillow python art/icons/build.py

Deliberately NOT wired into any Unity import step: icons change about never, so
the PNGs are committed and this script exists to redo them, not to run on build.
"""
from pathlib import Path

from PIL import Image
from reportlab.graphics import renderPM
from svglib.svglib import svg2rlg

SRC = Path(__file__).parent / "src"
OUT = Path(__file__).parents[2] / "unity" / "Assets" / "Game" / "Resources" / "Icons"
SIZE = 128  # 4x the largest on-screen use (the 32px doll tile), so it stays crisp scaled


def render(svg_path: Path, out_path: Path) -> None:
    drawing = svg2rlg(str(svg_path))
    if drawing is None:
        raise SystemExit(f"svglib could not parse {svg_path}")

    # svg2rlg keeps the 512x512 viewBox; scale the whole drawing to SIZE.
    scale = SIZE / max(drawing.width, drawing.height)
    drawing.scale(scale, scale)
    drawing.width *= scale
    drawing.height *= scale

    tmp = out_path.with_suffix(".tmp.png")
    renderPM.drawToFile(drawing, str(tmp), fmt="PNG", bg=0x000000)

    # Luminance -> alpha, RGB -> white. The source is a white glyph on a black
    # plate, so the grey ramp at the glyph edge IS the antialiasing we want.
    grey = Image.open(tmp).convert("L")
    white = Image.new("L", grey.size, 255)
    Image.merge("RGBA", (white, white, white, grey)).save(out_path)
    tmp.unlink()
    print(f"  {out_path.name}  {out_path.stat().st_size:,} bytes")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    svgs = sorted(SRC.glob("*.svg"))
    if not svgs:
        raise SystemExit(f"no SVG sources in {SRC}")
    print(f"baking {len(svgs)} icons -> {OUT}")
    for svg in svgs:
        render(svg, OUT / f"{svg.stem}.png")


if __name__ == "__main__":
    main()
