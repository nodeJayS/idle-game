import { Application, Container, Graphics } from 'pixi.js';
import type { GameConfig, SaveState } from '@game/index';
import { TILE_W, TILE_H, gridToScreen, depth } from './iso';
import { makeSprite } from './sprites';

/**
 * The Pixi renderer. The ONLY place Pixi lives. It READS a SaveState and draws
 * the isometric scene; it never mutates game state or decides game rules.
 *
 * M0 scope: a static iso floor, the party's lead hero (from save), and a couple
 * of dummy monsters for scale. A gentle idle-bob keeps it feeling alive.
 * Combat (driving entities from CombatState) lands in M1.
 */

export interface GameHandle {
  destroy: () => void;
}

const FLOOR = 8; // 8x8 tiles
const FLOOR_C1 = 0x2a2438;
const FLOOR_C2 = 0x322a42;
const FLOOR_LINE = 0x4a3f63;

interface Bob {
  node: Container;
  baseY: number;
  phase: number;
  amp: number;
}

function buildFloor(): Graphics {
  const g = new Graphics();
  const hw = TILE_W / 2;
  const hh = TILE_H / 2;
  for (let row = 0; row < FLOOR; row++) {
    for (let col = 0; col < FLOOR; col++) {
      const { x, y } = gridToScreen(col, row);
      const color = (col + row) % 2 === 0 ? FLOOR_C1 : FLOOR_C2;
      g.poly([x, y - hh, x + hw, y, x, y + hh, x - hw, y])
        .fill(color)
        .stroke({ color: FLOOR_LINE, width: 1, alpha: 0.5 });
    }
  }
  g.zIndex = -1000;
  return g;
}

/** A soft highlight diamond under a tile (marks where the hero stands). */
function tileHighlight(col: number, row: number): Graphics {
  const { x, y } = gridToScreen(col, row);
  const hw = TILE_W / 2;
  const hh = TILE_H / 2;
  const g = new Graphics();
  g.poly([x, y - hh, x + hw, y, x, y + hh, x - hw, y]).fill({ color: 0xb388ff, alpha: 0.18 });
  g.zIndex = -999;
  return g;
}

export async function createGame(
  parent: HTMLElement,
  save: SaveState,
  cfg: GameConfig,
): Promise<GameHandle> {
  const app = new Application();
  await app.init({
    background: 0x14101a,
    resizeTo: parent,
    antialias: false,
    roundPixels: true,
    resolution: window.devicePixelRatio || 1,
    autoDensity: true,
  });
  parent.appendChild(app.canvas);

  const world = new Container();
  world.sortableChildren = true;
  app.stage.addChild(world);

  world.addChild(buildFloor());

  const bobs: Bob[] = [];
  const place = (node: Container, col: number, row: number, amp = 0) => {
    const { x, y } = gridToScreen(col, row);
    node.x = x;
    node.y = y;
    node.zIndex = depth(col, row);
    world.addChild(node);
    if (amp > 0) bobs.push({ node, baseY: y, phase: Math.random() * Math.PI * 2, amp });
  };

  // Lead hero from the party (read from save).
  const leadId = save.party[0];
  const lead = leadId ? save.heroes.find((h) => h.id === leadId) : undefined;
  if (lead) {
    const heroDef = cfg.heroes[lead.defId];
    world.addChild(tileHighlight(3, 5));
    place(makeSprite(heroDef.sprite, 5), 3, 5, 1.5);
  }

  // Dummy monsters for scale (decoration only in M0 — not from save).
  place(makeSprite('slime', 4), 5, 2, 1.2);
  place(makeSprite('goblin', 4), 6, 4, 1.5);
  const king = makeSprite('goblin_king', 7);
  place(king, 1, 1, 2);

  // Center the iso floor in the viewport; recenter on resize.
  const layout = () => {
    world.x = app.screen.width / 2;
    world.y = app.screen.height / 2 - ((FLOOR - 1) * TILE_H) / 2;
  };
  layout();
  app.renderer.on('resize', layout);

  // Gentle idle bob so the scene feels alive.
  let t = 0;
  app.ticker.add((ticker) => {
    t += ticker.deltaMS / 1000;
    for (const b of bobs) b.node.y = b.baseY - Math.abs(Math.sin(t * 2 + b.phase)) * b.amp;
  });

  return {
    destroy: () => {
      app.renderer.off('resize', layout);
      app.destroy(true, { children: true });
    },
  };
}
