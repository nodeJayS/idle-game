import { create } from 'zustand';
import { newGame, type SaveState } from '@game/index';
import { gameConfig } from '@game/index';

/**
 * App-layer store (React binding). Lives OUTSIDE game-core on purpose — it
 * imports zustand's React hook. It holds the persisted SaveState and exposes
 * actions that delegate to game-core's pure functions.
 */
interface GameStore {
  save: SaveState | null;
  /** Replace the whole save (e.g. after load or a pure-reducer update). */
  setSave: (save: SaveState) => void;
  /** Start a fresh game with a random seed (the M0 entry point). */
  startNewGame: () => void;
}

export const useGameStore = create<GameStore>((set) => ({
  save: null,
  setSave: (save) => set({ save }),
  startNewGame: () =>
    set({ save: newGame(Math.floor(Math.random() * 0xffffffff), gameConfig, Date.now()) }),
}));
