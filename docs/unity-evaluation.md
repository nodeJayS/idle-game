# Unity Evaluation — should the idle ARPG move to Unity?

> Context: we have a TS web project (Vite + React + PixiJS) with an engine-agnostic
> `game-core` (pure TS). Goals stated over the project: cozy low-poly **3D** look
> (à la *Tunic*), eventual **global mobile** release, "production-ready, scalable"
> systems. This doc evaluates pivoting the client to **Unity (C#)**.

## TL;DR
- The **3D look does not *require* Unity** — react-three-fiber can do it on the web.
- But your two headline goals — **that polished 3D aesthetic + native global mobile** —
  are genuinely a *good fit* for Unity, and it ships many "global gacha non-negotiables"
  as first-party packages.
- The real price is **rewriting the simulation in C#** (losing the TS `game-core`) and
  **giving up frictionless web play**. Plus a **workflow shift**: Unity is editor/scene-driven,
  less "LLM writes files," more inspector/prefab work.
- **Timing matters most.** We're at M0 — the `game-core` is tiny (types + rng + save +
  stubs). The port cost is near its *lifetime minimum right now* and grows with every
  milestone we build in TS. **If Unity is likely, switch soon.**

---

## What Unity buys this project

| Area | Benefit |
|------|---------|
| **3D look** | Turnkey pipeline: URP, baked + real-time lighting, soft shadows, post-processing (vignette/bloom), particle VFX, Animator. The *Tunic* aesthetic is much easier to reach and maintain. |
| **Native mobile** | First-class iOS/Android builds with real performance headroom on weak phones — directly serves the global-release goal. |
| **Art sourcing** | Asset Store low-poly nature/character packs get you to the look cheaply. |
| **LiveOps / gacha non-negotiables** | Many come as packages: **Addressables** (content delivery without store resubmission), **Localization**, **Unity IAP** (+ receipt validation), **Remote Config**, **Analytics**. We listed these as must-haves; Unity hands them to you. |
| **Server authority (later)** | The C# sim can run on a **.NET server too** — so "shared sim code, client + server" is still achievable, just in C# on both sides (arguably cleaner than TS for a robust backend). |
| **Ecosystem** | Huge docs/tutorials/talent; very AI-assistable for C#. |

## What it costs

| Cost | Detail |
|------|--------|
| **Rewrite the sim in C#** | `game-core` logic doesn't transfer as code — only as *design*. Small today, larger every milestone. |
| **Lose frictionless web** | Unity WebGL exists but is heavy (large download, slow cold load) — bad for an idle game you might want instantly playable via a link. If web is a real channel, this hurts. |
| **Workflow shift** | Unity is scene/prefab/inspector-driven. Less of the "LLM authors files end-to-end" loop you've been using; more GUI/editor work that's harder to fully automate. |
| **Learning curve** | C# + Unity editor + scene lifecycle is a real ramp for a web dev (very learnable, but not free). |
| **Iteration** | Web hot-reload beats Unity's domain reload for pure code tweaks (Unity wins on content iteration). |
| **Repo weight** | 3D assets need Git LFS; Unity projects are heavier to version. |

---

## What transfers vs. what gets rewritten

| Asset in the current repo | Fate in Unity |
|---|---|
| `docs/` (game plan, game-core design, this file) | **Transfers** — the spec/source of truth |
| Data model + formulas (idle math, loot/affix, gacha) | **Transfers as design**; re-encode as C# / ScriptableObjects |
| `src/game/config/*` content (heroes, items, affixes, monsters, rifts, balance) | **Transfers as data** — re-author as ScriptableObjects or keep JSON |
| `src/game/**` logic (rng, save, systems) | **Rewritten in C#** (small now) |
| Determinism approach (seeded RNG, pure reducers) | **Transfers as a principle**; re-implement in C# (mind float determinism if sharing with a server) |
| React UI (`App.tsx`, components) | **Rewritten** in Unity UI Toolkit / uGUI |
| Pixi renderer (`src/render/pixi`) | **Replaced** by Unity rendering |
| Supabase backend + RLS | **Stays usable** from Unity via C# REST client |
| Vitest tests | **Rewritten** in Unity Test Framework / NUnit |
| Vite/web tooling | **Gone** |

**Key:** the *architecture rule survives the move.* In Unity, keep a pure-C# `GameCore`
assembly with **no `UnityEngine` references** — same discipline as our pure-TS rule. That
keeps the sim testable and reusable on a .NET server. Don't let game logic leak into MonoBehaviours.

---

## Hybrid options (and why most are bad)

- **A) Unity client + TS sim on a server.** Client sends actions, server runs our TS
  `game-core`. Awkward for an idle game (latency, needs client prediction anyway). ❌
- **B) Full Unity, all C# (client now; optional .NET server reusing the C# sim later).**
  Clean and conventional. ✅ This is *the* way to do Unity here.
- **C) Keep TS for web + Unity for mobile.** Two clients sharing no code (different
  languages). Worst of both — double the work. ❌

If you go Unity, go **all-in C# (option B).**

---

## Decision criteria

**Choose Unity if:**
1. The polished **3D look** and **native mobile** are top priorities (they are, per your goals).
2. You accept **web becomes secondary or dropped**.
3. You're willing to **learn C#/Unity** and do editor-driven work (less pure "vibe coding").
4. You **switch soon** to minimize the C# port.

**Stay web (react-three-fiber) if:**
1. **Instant browser play** matters (shareable links, no install).
2. You want to **keep the TS `game-core`** and the fast file-authoring workflow.
3. "**Good** cozy 3D" is enough vs. "max fidelity / conventional pipeline."

> Your stated goals (that exact 3D aesthetic + global mobile) lean **Unity**. The main
> casualties are web-first distribution and the (currently small) TS codebase.

---

## If we go Unity — migration plan

**Phase 0 — Setup**
- Unity LTS + **URP** (Universal Render Pipeline) for the stylized look.
- Repo hygiene: Unity `.gitignore` + **Git LFS** for art. New repo or a `unity/` subtree.
- Define assemblies: **`GameCore` (pure C#, no UnityEngine)**, `Game` (MonoBehaviours/render), `Tests`.

**Phase 1 — Port `game-core` to C#** (cheap now)
- Mirror `types.ts` → C# records/classes; `rng/weightedRoll` → C# (deterministic);
  `save` (versioned + migrations); stubs for combat/loot/idle/progression/inventory/acquisition.
- Port the rng + save tests to NUnit. Keep the docs as the spec.

**Phase 2 — Content**
- Re-author config as **ScriptableObjects** (designer-friendly) or load the existing JSON.

**Phase 3 — M0 in Unity**
- 3D iso scene: ground, camera rig (orthographic/angled), a placeholder character on tiles,
  party selection. The Unity equivalent of what we just shipped.

**Phase 4+ — Milestones in C#**
- Same order: M1 combat → M2 loot → M3 rifts → M4 idle → M5 persistence (Supabase from C#) → M6 feel.

**Cross-cutting (map to the "global gacha non-negotiables")**
- Addressables (content delivery), Localization, Unity IAP (+ server receipt validation),
  Remote Config, Analytics. Add as you reach each need.

**Art**
- Asset Store low-poly nature + character packs; URP + baked lighting + soft shadows + a
  light post stack to nail the cozy *Tunic* feel.

---

## Recommendation
Given your goals, **Unity is a defensible — arguably good — choice, *if* you commit to it
now while the port is tiny and you're OK trading away web-first + the TS workflow.** If
you're not yet sure the 3D/mobile vision is worth the C# rewrite and editor workflow,
the lower-regret path is to keep proving the loop on web and revisit — but know the port
bill grows each milestone. The one thing to avoid is building M1–M6 in TS *and then*
switching; that's paying for the same systems twice.
