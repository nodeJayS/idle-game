import { gameConfig } from '@game/index';

/**
 * Placeholder landing page for the scaffold. The real game (Pixi canvas + HUD)
 * replaces this starting at M0. For now it confirms the build boots and that
 * game-core config is importable through the @game alias.
 */
const milestones: { id: string; name: string; status: 'next' | 'todo' }[] = [
  { id: 'M0', name: 'Skeleton — Pixi renders a static iso scene + Warrior', status: 'next' },
  { id: 'M1', name: 'Auto-combat — deterministic stepCombat clears a pack + boss', status: 'todo' },
  { id: 'M2', name: 'Loot — drops w/ rarity + affixes, inventory, equip → stats', status: 'todo' },
  { id: 'M3', name: 'Rifts — difficulty tiers scale monsters + loot', status: 'todo' },
  { id: 'M4', name: 'Idle — offline yield as math + claim modal', status: 'todo' },
  { id: 'M5', name: 'Persistence — Supabase save/load + RLS', status: 'todo' },
  { id: 'M6', name: 'Feel pass — pixel juice, gear-compare, number formatting', status: 'todo' },
];

export default function App() {
  const heroCount = Object.keys(gameConfig.heroes).length;
  const monsterCount = Object.keys(gameConfig.monsters).length;
  const riftCount = gameConfig.rifts.length;

  return (
    <>
      <h1>⚔️ Idle ARPG</h1>
      <p className="tagline">
        Cute-pixel-art idle ARPG — Diablo/PoE-style loot, Minecraft-friendly surface.
      </p>

      <div className="panel">
        <h2>Scaffold is alive</h2>
        <p style={{ margin: 0, color: 'var(--muted)' }}>
          Build boots and <code>game-core</code> config loads through the <code>@game</code> alias:{' '}
          <strong>{heroCount}</strong> hero, <strong>{monsterCount}</strong> monsters,{' '}
          <strong>{riftCount}</strong> rift tiers defined. Gameplay systems are stubbed and
          implemented milestone by milestone.
        </p>
      </div>

      <div className="panel">
        <h2>Milestones</h2>
        <ul className="milestones">
          {milestones.map((m) => (
            <li key={m.id}>
              <span className={`tag ${m.status}`}>{m.status === 'next' ? 'NEXT' : 'TODO'}</span>
              <span>
                <strong>{m.id}</strong> — {m.name}
              </span>
            </li>
          ))}
        </ul>
      </div>

      <div className="panel">
        <h2>Architecture rule</h2>
        <p style={{ margin: 0, color: 'var(--muted)' }}>
          All combat / loot / idle / progression logic lives in <code>src/game</code> (pure TS,
          zero React / Pixi / DOM / Supabase). The renderer only reads state. That boundary keeps
          the Pixi→3D swap, the mobile port, and the gacha layer all additive. See{' '}
          <code>docs/</code>.
        </p>
      </div>
    </>
  );
}
