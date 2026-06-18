import { Container, Graphics } from 'pixi.js';

/**
 * Tiny pixel-art sprite factory. A sprite is a grid of single-char rows; each
 * char maps to a palette color (or is transparent if absent from the palette).
 * The returned Container's origin (0,0) is the sprite's BOTTOM-CENTER (its
 * "feet"), so it can be dropped straight onto an iso tile center.
 *
 * These are placeholder art for M0 — real sprite atlases swap in later without
 * touching game-core (sprites are chosen by the `sprite` hint on each def).
 */
export function pixelSprite(rows: string[], palette: Record<string, number>, px = 4): Container {
  const c = new Container();
  const g = new Graphics();
  const w = rows[0].length * px;
  const h = rows.length * px;

  rows.forEach((row, y) => {
    for (let x = 0; x < row.length; x++) {
      const color = palette[row[x]];
      if (color === undefined) continue;
      g.rect(x * px, y * px, px, px).fill(color);
    }
  });

  g.x = -w / 2; // center horizontally
  g.y = -h; // lift so feet sit at origin
  c.addChild(g);
  return c;
}

// --- Placeholder sprite definitions, keyed by the `sprite` hint in config ---

const WARRIOR = [
  '..hhhh..',
  '.hhhhhh.',
  '.hffffh.',
  '.ffffff.',
  '..ffff..',
  '.bbbbbbw',
  'bbbbbbbw',
  '.bbbbb.w',
  '.bb.bb..',
  '.ll.ll..',
  '.LL.LL..',
];

const SLIME = [
  '..gggg..',
  '.gggggg.',
  'ggwggwgg',
  'gggggggg',
  '.gg..gg.',
];

const GOBLIN = [
  '.k....k.',
  '.kkkkkk.',
  '.krrkrk.',
  '.kkkkkk.',
  '..kkkk..',
  '.cccccc.',
  'cccccccc',
  '.cc..cc.',
  '.dd..dd.',
  '.dd..dd.',
];

const PALETTE: Record<string, number> = {
  h: 0x9aa0b5, // helmet
  f: 0xf1c9a5, // face
  b: 0x5b8dd9, // armor (blue)
  l: 0x3a3f5c, // legs
  L: 0x2a2e44, // boots
  w: 0xffcc66, // weapon / slime eyes (white-ish gold)
  g: 0x6fcf7f, // slime body
  k: 0x7bb661, // goblin skin
  r: 0xff5555, // goblin eyes
  c: 0x8a5a3b, // goblin cloth
  d: 0x3a2e22, // goblin legs
};

const SPRITES: Record<string, string[]> = {
  warrior: WARRIOR,
  slime: SLIME,
  goblin: GOBLIN,
  goblin_king: GOBLIN, // reuse for now; scaled up by caller
};

/** Build a placeholder sprite by its config `sprite` hint. Falls back to a blob. */
export function makeSprite(spriteKey: string, px = 4): Container {
  const rows = SPRITES[spriteKey] ?? SLIME;
  return pixelSprite(rows, PALETTE, px);
}
