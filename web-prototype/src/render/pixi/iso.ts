/** Isometric projection helpers. 2:1 diamond tiles. */
export const TILE_W = 64;
export const TILE_H = 32;

export interface ScreenPos {
  x: number;
  y: number;
}

/** Grid (col,row) -> screen position of the tile's CENTER (world coords). */
export function gridToScreen(col: number, row: number): ScreenPos {
  return {
    x: (col - row) * (TILE_W / 2),
    y: (col + row) * (TILE_H / 2),
  };
}

/** Draw order along the iso axis (back-to-front). */
export function depth(col: number, row: number): number {
  return col + row;
}
