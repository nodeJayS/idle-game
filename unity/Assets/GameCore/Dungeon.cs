#nullable enable
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    // ========================================================================
    // Roguelite dungeon: PURE-DATA procedural generation (ROADMAP roguelite,
    // slice 1). This file is the data CONTRACT only — the pipeline lives in
    // DungeonGen.cs. A Unity client slice renders this data next; NOTHING here
    // references UnityEngine, and NO combat/sim/roguelite rules read it yet.
    //
    // Determinism is the whole point: one int Seed ⇒ a bit-identical Dungeon
    // (same grid, same rooms, same props, same Checksum) on any platform. The
    // generator drives a local Rng (mulberry32) seeded ONLY from Params.Seed —
    // never the save cursor — so a dungeon is reproducible from its seed alone.
    // ========================================================================

    /// <summary>
    /// Generation inputs. <see cref="Seed"/> is the sole source of randomness; the rest are
    /// knobs the roguelite mode will vary. <see cref="Theme"/> is an OPAQUE string — the client
    /// owns the look tables (palette/props); the generator only flavours the seeded name from it.
    /// </summary>
    public sealed class DungeonParams
    {
        public int Seed;
        public int RoomCount = 42;
        public double LoopChance = 0.15;
        public double DecorDensity = 0.6;
        public string Theme = "crypt"; // opaque string, client interprets
    }

    /// <summary>Grid cell codes packed into <see cref="Dungeon.Grid"/> (byte per cell, i = y*W + x).</summary>
    public static class DungeonCell
    {
        public const byte Void = 0, Floor = 1, Wall = 2;
    }

    /// <summary>Room footprint stamped into the grid — Rectangle, Ellipse, or chamfered Octagon.</summary>
    public enum RoomShape { Rectangle, Ellipse, Octagon }

    /// <summary>Semantic role assigned before carving (drives spawns, props, difficulty).</summary>
    public enum RoomType { Entrance, Combat, Elite, Treasure, Shrine, Boss }

    /// <summary>Decoration/interaction markers — data only; the client picks meshes per <see cref="DungeonProp.Kind"/>.</summary>
    public enum PropKind { Pillar, Torch, Debris, Brazier, Chest, ShrineCrystal, PortalRing }

    /// <summary>
    /// One room. Centre/extents are GRID coordinates after rasterisation (top-left origin, +y down).
    /// <see cref="Depth"/> is graph distance from the entrance along the room graph; <see cref="Difficulty"/>
    /// is derived from depth (boss forced 1.0); <see cref="Degree"/> is its edge count in the final graph.
    /// </summary>
    public sealed class DungeonRoom
    {
        public int Id;
        public int Cx, Cy, W, H;
        public RoomShape Shape;
        public RoomType Type;
        public int Depth;
        public double Difficulty;
        public int Degree;
    }

    /// <summary>
    /// A connection between rooms <see cref="A"/> and <see cref="B"/> (room ids). <see cref="IsLoop"/> marks a
    /// non-MST edge re-added for a cycle; <see cref="IsCritical"/> marks edges on the entrance→boss path.
    /// </summary>
    public sealed class DungeonEdge
    {
        public int A, B;
        public bool IsLoop, IsCritical;
    }

    /// <summary>An (x, y) grid coordinate — used for doorway and corridor cell lists.</summary>
    public struct GridCell
    {
        public int X, Y;
        public GridCell(int x, int y) { X = x; Y = y; }
    }

    /// <summary>A placed decoration. <see cref="Rot"/> is radians, <see cref="Scale"/> a size multiplier; both are
    /// deterministic client hints. <see cref="RoomId"/> is the owning room (-1 if none).</summary>
    public sealed class DungeonProp
    {
        public PropKind Kind;
        public int X, Y;
        public double Rot, Scale;
        public int RoomId;
    }

    /// <summary>An enemy spawn point on a free floor cell. <see cref="Tier"/>: 0 normal, 1 elite, 2 boss.</summary>
    public sealed class DungeonSpawn
    {
        public int X, Y;
        public int Tier;
        public int RoomId;
    }

    /// <summary>Post-generation counts (plus real generation time) — for tests, tuning, and telemetry.</summary>
    public sealed class DungeonStats
    {
        public int Rooms, Edges, Loops, CriticalLength, FloorTiles, WallTiles, Props;
        public double GenMs;
    }

    /// <summary>
    /// The generated dungeon. <see cref="Grid"/> is a W×H byte map (index i = y*W + x) of
    /// VOID/FLOOR/WALL; <see cref="Bfs"/> is the 4-connected distance field from the entrance
    /// centre over FLOOR cells (-1 for any non-floor cell); <see cref="BossBfs"/> is the same field
    /// seeded from the boss room, and <see cref="CellRoom"/> maps each cell to its owning room. All
    /// lists are in a deterministic order and <see cref="Checksum"/> (FNV-1a 32) pins the grid/rooms/
    /// props/spawns payload for regression tests (the new fields ride alongside, not hashed).
    /// </summary>
    public sealed class Dungeon
    {
        public DungeonParams Params = new DungeonParams();
        public string Name = "";
        public int W, H;
        public byte[] Grid = System.Array.Empty<byte>();  // W*H, index i = y*W + x — VOID|FLOOR|WALL
        public short[] Bfs = System.Array.Empty<short>();  // per-cell distance from entrance, -1 = non-floor
        // Per-cell owning room id (index i = y*W + x), -1 for corridor/void cells. Combat's room-gated
        // targeting (an anti-wallhack rule) reads this to tell "same room" from "across a wall".
        public short[] CellRoom = System.Array.Empty<short>();
        // 4-connected BFS distance field over FLOOR cells seeded from the BOSS room's centre, -1 for
        // non-floor. Combat's leader-travel flow field descends this toward the boss (the exit).
        public short[] BossBfs = System.Array.Empty<short>();
        public List<DungeonRoom> Rooms = new List<DungeonRoom>();
        public List<DungeonEdge> Edges = new List<DungeonEdge>();
        public List<GridCell> Doorways = new List<GridCell>();
        public List<GridCell> CorridorCells = new List<GridCell>();
        public List<DungeonProp> Props = new List<DungeonProp>();
        public List<DungeonSpawn> Spawns = new List<DungeonSpawn>();
        public DungeonStats Stats = new DungeonStats();
        public uint Checksum; // FNV-1a 32 over grid + rooms + props + spawns, deterministic serialization order
    }
}
