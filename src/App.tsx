import { useEffect } from 'react';
import { gameConfig } from '@game/index';
import { useGameStore } from './state/store';
import { GameCanvas } from './components/GameCanvas';

/**
 * M0 shell: ensure a SaveState exists, then render the isometric scene from it
 * plus a tiny party HUD. Combat / loot / idle UI arrive in later milestones.
 */
export default function App() {
  const save = useGameStore((s) => s.save);
  const startNewGame = useGameStore((s) => s.startNewGame);

  useEffect(() => {
    if (!save) startNewGame();
  }, [save, startNewGame]);

  if (!save) return <p style={{ padding: 24 }}>Loading…</p>;

  return (
    <>
      <header className="topbar">
        <h1>⚔️ Idle ARPG</h1>
        <button className="btn" onClick={startNewGame}>
          New game
        </button>
      </header>

      <GameCanvas save={save} />

      <div className="party-bar">
        {save.party.map((heroId, i) => {
          const hero = heroId ? save.heroes.find((h) => h.id === heroId) : null;
          const def = hero ? gameConfig.heroes[hero.defId] : null;
          return (
            <div key={i} className={`slot ${def ? 'filled' : 'empty'}`}>
              {def ? (
                <>
                  <span className="slot-name">{def.name}</span>
                  <span className="slot-sub">Lv {hero!.level}</span>
                </>
              ) : (
                <span className="slot-sub">Empty</span>
              )}
            </div>
          );
        })}
      </div>

      <p className="hint">
        M0 — scene renders from <code>SaveState</code>. The Warrior on the highlighted tile is your
        party lead; the slime, goblin, and goblin king are placeholder scale. Combat comes in M1.
      </p>
    </>
  );
}
