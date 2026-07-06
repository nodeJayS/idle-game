#nullable enable
using System.Collections.Generic;
using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The dungeon LOOK table — pure data, no behaviour (roguelite slice 2). GameCore treats the
    /// theme as an OPAQUE string; the client owns every colour/light constant here. Five themes,
    /// transcribed verbatim from the MIT reference's look-dev recipe (crypt/molten/frost/grim/
    /// verdant): background/fog, hemisphere ambient, directional key, the floor→cap palette, the
    /// flame emissives, the torch point-light, and the per-room-type floor TINT.
    ///
    /// All colours are authored as 0xRRGGBB hex and used RAW (see <see cref="Hex"/> for why — the
    /// reference is a gamma-pipeline three.js app, so raw floats reproduce its on-screen result);
    /// light INTENSITIES instead carry explicit Urp*Scale factors, screenshot-tuned. Emissive
    /// colours also stay as-authored (they feed bloom directly).
    /// </summary>
    public sealed class DungeonTheme
    {
        public string Key = "crypt";

        // Staging / atmosphere.
        public Color Bg;                 // camera clear + fog colour (near-black)
        public Color Fog;                // fog colour (may differ from bg for the tinted haze)
        public float FogDensity;         // RenderSettings Exp2 density
        public Color HemiSky, HemiGround;
        public float HemiIntensity;
        public Color DirColor;
        public float DirIntensity;

        // Level palette.
        public Color Floor, Corridor, Wall, Cap, Pillar;
        public Color DebrisA, DebrisB;   // two debris rock tints

        // Emissives + torch light.
        public Color Flame, FlameCore;
        public Color TorchLight;
        public float TorchIntensity, TorchDistance;

        // Room-type floor tint (lerp target). Keyed by RoomType.
        public readonly Dictionary<RoomType, Color> Tint = new Dictionary<RoomType, Color>();

        // ---- decode helpers -------------------------------------------------

        // three.js (r128, gamma pipeline) → URP linear conversion factors, screenshot-tuned against
        // the reference preview.jpg. The reference's light INTENSITIES were authored in gamma-space
        // units; in URP's linear HDR they read several times dimmer, so point/sun/ambient each get
        // a scale. Colour VALUES stay raw (see Hex below).
        public const float UrpLightScale   = 15.0f; // torch + key point lights (gamma-display lift: a
                                                    // 0.1 linear pool showed as ~0.35 in the reference)
        public const float UrpSunScale     = 1.6f;  // the faint directional
        public const float UrpAmbientScale = 2.2f;  // hemisphere → Trilight ambient

        /// <summary>0xRRGGBB → Color with the sRGB floats used AS-IS (no .linear gamma decode).
        /// Deliberate: the reference renders in three.js's gamma pipeline, where the hex values are
        /// used raw — a .linear decode here made every surface read ~4× darker than preview.jpg
        /// (screenshot-compared both ways). Feeding the raw floats reproduces the authored look.</summary>
        public static Color Hex(int rgb)
        {
            float r = ((rgb >> 16) & 0xFF) / 255f;
            float g = ((rgb >> 8) & 0xFF) / 255f;
            float b = (rgb & 0xFF) / 255f;
            return new Color(r, g, b);
        }

        /// <summary>0xRRGGBB → sRGB Color (NO gamma decode) — for emissive/HDR values fed to bloom
        /// where we want the authored hue at face value.</summary>
        public static Color HexRaw(int rgb)
        {
            float r = ((rgb >> 16) & 0xFF) / 255f;
            float g = ((rgb >> 8) & 0xFF) / 255f;
            float b = (rgb & 0xFF) / 255f;
            return new Color(r, g, b);
        }

        // ---- theme table ----------------------------------------------------

        /// <summary>The shared per-room-type tint table (identical across themes in the reference).</summary>
        private static void FillTint(DungeonTheme t)
        {
            t.Tint[RoomType.Entrance] = Hex(0x3fd0bb);
            t.Tint[RoomType.Combat]   = Hex(0x8f95a3);
            t.Tint[RoomType.Elite]    = Hex(0x9b6cf0);
            t.Tint[RoomType.Treasure] = Hex(0xd9a441);
            t.Tint[RoomType.Shrine]   = Hex(0x5a8fe8);
            t.Tint[RoomType.Boss]     = Hex(0xd8433a);
        }

        /// <summary>Look up a theme by key (case-insensitive). Unknown keys fall back to crypt.</summary>
        public static DungeonTheme Get(string key)
        {
            switch ((key ?? "crypt").ToLowerInvariant())
            {
                case "molten":  return Molten();
                case "frost":   return Frost();
                case "grim":    return Grim();
                case "verdant": return Verdant();
                case "ancient": // "ancient" is the reference's alias for crypt
                case "crypt":
                default:        return Crypt();
            }
        }

        /// <summary>The five theme keys the preview dropdown offers, in reference order.</summary>
        public static readonly string[] Keys = { "crypt", "molten", "frost", "grim", "verdant" };

        private static DungeonTheme Crypt()
        {
            var t = new DungeonTheme { Key = "crypt" };
            t.Bg = HexRaw(0x07080d); t.Fog = HexRaw(0x07080d); t.FogDensity = 0.0021f;
            t.HemiSky = Hex(0x2e3a52); t.HemiGround = Hex(0x0a0b10); t.HemiIntensity = 0.55f;
            t.DirColor = Hex(0xffe8c8); t.DirIntensity = 0.85f;
            t.Floor = Hex(0x8a8f9c); t.Corridor = Hex(0x6d7380); t.Wall = Hex(0x5c626e);
            t.Cap = Hex(0x757b88); t.Pillar = Hex(0x6a707e);
            t.DebrisA = Hex(0x4c515e); t.DebrisB = Hex(0x60584a);
            t.Flame = HexRaw(0xffa640); t.FlameCore = HexRaw(0xfff3c8);
            t.TorchLight = Hex(0xff8c3a); t.TorchIntensity = 1.5f; t.TorchDistance = 9.5f;
            FillTint(t);
            return t;
        }

        private static DungeonTheme Molten()
        {
            var t = new DungeonTheme { Key = "molten" };
            t.Bg = HexRaw(0x0c0605); t.Fog = HexRaw(0x1a0b04); t.FogDensity = 0.0028f;
            t.HemiSky = Hex(0x6b3419); t.HemiGround = Hex(0x160503); t.HemiIntensity = 0.55f;
            t.DirColor = Hex(0xffd9b0); t.DirIntensity = 0.5f;
            t.Floor = Hex(0x7a685c); t.Corridor = Hex(0x614f44); t.Wall = Hex(0x503e34);
            t.Cap = Hex(0x6b5546); t.Pillar = Hex(0x5e4a3e);
            t.DebrisA = Hex(0x4a382e); t.DebrisB = Hex(0x60462f);
            t.Flame = HexRaw(0xff8c26); t.FlameCore = HexRaw(0xffe9b0);
            t.TorchLight = Hex(0xff7326); t.TorchIntensity = 1.7f; t.TorchDistance = 10f;
            FillTint(t);
            return t;
        }

        private static DungeonTheme Frost()
        {
            var t = new DungeonTheme { Key = "frost" };
            t.Bg = HexRaw(0x060a12); t.Fog = HexRaw(0x0b1522); t.FogDensity = 0.0024f;
            t.HemiSky = Hex(0x3a5a80); t.HemiGround = Hex(0x0a0e18); t.HemiIntensity = 0.5f;
            t.DirColor = Hex(0xcfe4ff); t.DirIntensity = 0.82f;
            t.Floor = Hex(0x93a0b2); t.Corridor = Hex(0x78848f); t.Wall = Hex(0x60708a);
            t.Cap = Hex(0x8194ac); t.Pillar = Hex(0x70809a);
            t.DebrisA = Hex(0x55617a); t.DebrisB = Hex(0x6d7a90);
            t.Flame = HexRaw(0x86d9ff); t.FlameCore = HexRaw(0xe8f7ff);
            t.TorchLight = Hex(0x6fc4ff); t.TorchIntensity = 1.35f; t.TorchDistance = 9.5f;
            FillTint(t);
            return t;
        }

        private static DungeonTheme Grim()
        {
            var t = new DungeonTheme { Key = "grim" };
            t.Bg = HexRaw(0x070a07); t.Fog = HexRaw(0x0a130a); t.FogDensity = 0.0030f;
            t.HemiSky = Hex(0x2c4030); t.HemiGround = Hex(0x070a06); t.HemiIntensity = 0.52f;
            t.DirColor = Hex(0xbfd8b0); t.DirIntensity = 0.45f;
            t.Floor = Hex(0x7c8276); t.Corridor = Hex(0x62685c); t.Wall = Hex(0x4f5549);
            t.Cap = Hex(0x666c5e); t.Pillar = Hex(0x5c6254);
            t.DebrisA = Hex(0x4a4f44); t.DebrisB = Hex(0x5e5c48);
            t.Flame = HexRaw(0x8fe05a); t.FlameCore = HexRaw(0xe9ffd0);
            t.TorchLight = Hex(0x77d94a); t.TorchIntensity = 1.35f; t.TorchDistance = 9f;
            FillTint(t);
            return t;
        }

        private static DungeonTheme Verdant()
        {
            var t = new DungeonTheme { Key = "verdant" };
            t.Bg = HexRaw(0x060c09); t.Fog = HexRaw(0x091510); t.FogDensity = 0.0023f;
            t.HemiSky = Hex(0x2f5a46); t.HemiGround = Hex(0x08120c); t.HemiIntensity = 0.6f;
            t.DirColor = Hex(0xd8f0c8); t.DirIntensity = 0.8f;
            t.Floor = Hex(0x848e7e); t.Corridor = Hex(0x6a7560); t.Wall = Hex(0x556050);
            t.Cap = Hex(0x6e7a66); t.Pillar = Hex(0x606c5c);
            t.DebrisA = Hex(0x49543f); t.DebrisB = Hex(0x5c644c);
            t.Flame = HexRaw(0x62e0a8); t.FlameCore = HexRaw(0xe6fff0);
            t.TorchLight = Hex(0x4ad98e); t.TorchIntensity = 1.3f; t.TorchDistance = 9f;
            FillTint(t);
            return t;
        }

        // ---- prop colours (shared across themes; from the constants doc "Misc") -------------

        public static readonly Color ChestBody  = Hex(0x8a5a2c);
        public static readonly Color ChestTrim  = Hex(0xc8a24a);
        public static readonly Color ChestGlow  = HexRaw(0xffd27a);
        public static readonly Color CrystalCol = HexRaw(0x8fbcff);
        public static readonly Color PortalCol  = HexRaw(0x3fd0bb);
        public static readonly Color BrazierBody = Hex(0x3a3f4a);
        public static readonly Color BrazierCoal = HexRaw(0xff7a30);
        public static readonly Color BossGlowA   = HexRaw(0xff4636);
        public static readonly Color BossGlowB   = HexRaw(0xff6a45);

        // Key light colours (the 3 named lights, from the Lights section).
        public static readonly Color KeyEntrance = Hex(0x3fd0bb);
        public static readonly Color KeyBoss     = Hex(0xff4030);
        public static readonly Color KeyShrine   = Hex(0x6f9dff);

        // Debug overlay line colours (graph overlay).
        public static readonly Color OverlayMst  = HexRaw(0xdfe4f0);
        public static readonly Color OverlayLoop = HexRaw(0x39d5e0);
        public static readonly Color OverlayCrit = HexRaw(0xff4d4d);

        // Heatmap gradient endpoints.
        public static readonly Color HeatA = Hex(0x2f4bb0);
        public static readonly Color HeatB = Hex(0xe8502f);

        // Spawn marker tints (0 normal dim, 1 elite purple, 2 boss red).
        public static readonly Color SpawnNormal = HexRaw(0x9aa0ad);
        public static readonly Color SpawnElite  = HexRaw(0x9b6cf0);
        public static readonly Color SpawnBoss   = HexRaw(0xff4030);
    }
}
