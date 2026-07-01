# Backlog — open work

Current open/near-term items. Live system status is in [`../CLAUDE.md`](../CLAUDE.md);
the durable design + full roadmap in [`game-design.md`](game-design.md). GameCore-first
for anything touching rules (build + `dotnet test gamecore/GameCore.Tests`, then wire Unity).

> The previous backlog (a 2026-06-20 handover) is fully shipped — party cap 3, derived
> DPS/Eff-Life stats + hover compare, auto-salvage selector, in-place boss challenge,
> reward-density pass, the skills MVP, and the Thief hero all landed. History is in git.

## Next focus — content & polish
- **More content:** additional heroes/classes & kits, enemy variety, more normal + rare
  modifiers, deeper/more stages.
- **Balance & tuning:** the geometric curves are tuned by feel; heroes can out-level a
  stage. Wants the deferred **console balance-sim** to chart power vs difficulty.
- **Combat juice:** boss-kill flash, hitstop on big crits, kill-streaks, and SOUND (no
  audio assets in the repo yet).

## Systems / GameCore
- **Progression hooks (Lever 4)** — *slice 1 shipped:* the **achievement ladder** (lifetime
  `AchievementState` under `ProgressState`; `Achievements.Record` fed the same events as quests;
  8 tiered achievements auto-paying gold+scrap+XP; read-only panel on the control bar). Next slices:
  daily-login / streak reward (needs `now`), a manual "collect" claim UX, achievements feeding the
  goal board, achievements/codex polish, and eventually prestige/rebirth.
- **Tower of Ascension — slice 3:** per-floor reward bundles (scrap/gold/gear? — TBD) + juice.

## Deferred (intentionally, until the depth above lands)
- **UI/UX polish pass** — a uGUI layout-group refactor + glyph/font audit + theming + real
  item/hero art hooks. Today's screens are functional hand-placed placeholders.
- **Real Blender hero models** — replace the code-built chibi placeholders (the `CombatView`
  spawn/animator seam is ready).
- **Gacha + live-service** — server-authoritative core, accounts/auth, global chat, gacha,
  store/LiveOps. Additive, not a rewrite (game-design.md §9).
