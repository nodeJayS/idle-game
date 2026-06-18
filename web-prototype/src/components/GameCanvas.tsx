import { useEffect, useRef } from 'react';
import { gameConfig, type SaveState } from '@game/index';
import { createGame, type GameHandle } from '../render/pixi/game';

/**
 * Hosts the Pixi canvas. Mounts the renderer on first paint and rebuilds it if
 * the save identity changes. The renderer only READS the save — all game logic
 * stays in game-core.
 */
export function GameCanvas({ save }: { save: SaveState }) {
  const hostRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;

    let handle: GameHandle | null = null;
    let disposed = false;

    createGame(host, save, gameConfig).then((h) => {
      if (disposed) h.destroy();
      else handle = h;
    });

    return () => {
      disposed = true;
      handle?.destroy();
    };
  }, [save]);

  return <div ref={hostRef} className="game-canvas" />;
}
