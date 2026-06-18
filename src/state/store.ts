import { create } from 'zustand';
import type { SaveState } from '@game/index';

/**
 * App-layer store (React binding). Lives OUTSIDE game-core on purpose — it
 * imports zustand's React hook. It holds the persisted SaveState and will
 * expose actions that delegate to game-core's pure functions as systems land.
 */
interface GameStore {
  save: SaveState | null;
  /** Replace the whole save (e.g. after load or a pure-reducer update). */
  setSave: (save: SaveState) => void;
}

export const useGameStore = create<GameStore>((set) => ({
  save: null,
  setSave: (save) => set({ save }),
}));
