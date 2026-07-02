#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// Code-painted ground detail (the Tunic "someone drew this" pass): tileable
    /// 256px brightness maps — grass strokes, chunky stone tiles, sand ripples,
    /// snow speckle, cooled-crack rock — projected in world XZ by TunicSurface's
    /// _DetailTex (0.5 = neutral, up-faces only). Same philosophy as the procedural
    /// dapple cookie: no shipped image assets, everything authored in code.
    /// Deterministic per style, cached, and cheap (one 256x256 per style ever).
    /// </summary>
    public static class GroundDetail
    {
        public enum Style { Grass, Tiles, Ripples, Speckle, Cracks }

        private const int Size = 256;
        private static readonly Dictionary<Style, Texture2D> _cache = new Dictionary<Style, Texture2D>();

        /// <summary>Which detail style a zone's PropSet reads as (unknown sets = grass).</summary>
        public static Style StyleFor(string propSet) => propSet switch
        {
            "ruins" or "astral" or "summit" => Style.Tiles,
            "desert" or "coast" => Style.Ripples,
            "tundra" => Style.Speckle,
            "volcano" or "cavern" => Style.Cracks,
            _ => Style.Grass, // forest, swamp, and anything new
        };

        /// <summary>Per-style projection scale (world units per texture tile) and strength.
        /// CHUNKY on purpose: fine marks mip away to flat colour at the game's camera
        /// distance (verified by screenshot) — Tunic reads because its marks are big.</summary>
        public static (float scale, float strength) TuningFor(Style style) => style switch
        {
            Style.Tiles => (11f, 0.70f),   // 4 tiles per repeat = chunky ~2.75u pavers
            Style.Ripples => (14f, 0.60f),
            Style.Speckle => (10f, 0.55f),
            Style.Cracks => (12f, 0.65f),
            _ => (12f, 0.70f),             // grass strokes
        };

        public static Texture2D Get(Style style)
        {
            if (_cache.TryGetValue(style, out var t)) return t;
            var tex = Paint(style);
            _cache[style] = tex;
            return tex;
        }

        // --- painting ------------------------------------------------------------

        private static Texture2D Paint(Style style)
        {
            var v = new float[Size * Size];
            for (int i = 0; i < v.Length; i++) v[i] = 0.5f;
            var rng = new System.Random(7041 + (int)style);

            switch (style)
            {
                case Style.Grass: PaintGrass(v, rng); break;
                case Style.Tiles: PaintTiles(v, rng); break;
                case Style.Ripples: PaintRipples(v, rng); break;
                case Style.Speckle: PaintSpeckle(v, rng); break;
                case Style.Cracks: PaintCracks(v, rng); break;
            }

            var tex = new Texture2D(Size, Size, TextureFormat.R8, true)
            {
                name = "GroundDetail_" + style,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4, // the iso camera grazes the ground — keep marks crisp
            };
            var px = new Color32[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v[i] * 255f), 0, 255);
                px[i] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // Tileable write: coordinates wrap, values accumulate (clamped on bake).
        private static void Put(float[] v, int x, int y, float delta)
        {
            x &= Size - 1; y &= Size - 1;
            v[y * Size + x] += delta;
        }

        private static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>A short meandering stroke — the workhorse for grass blades and cracks.</summary>
        private static void Stroke(float[] v, System.Random rng, int x, int y,
                                   float angle, int len, float delta, float wobble, bool thick = false)
        {
            float fx = x, fy = y, a = angle;
            for (int i = 0; i < len; i++)
            {
                Put(v, (int)fx, (int)fy, delta);
                if (thick) Put(v, (int)fx + 1, (int)fy, delta * 0.7f);
                a += R(rng, -wobble, wobble);
                fx += Mathf.Cos(a); fy += Mathf.Sin(a);
            }
        }

        /// <summary>Broad soft tonal patches — the coarse variation that still reads after
        /// mip minification at gameplay camera distance (fine marks alone vanish).</summary>
        private static void Blotches(float[] v, System.Random rng, int count, int rMin, int rMax,
                                     float dMin, float dMax)
        {
            for (int i = 0; i < count; i++)
            {
                int cx = rng.Next(Size), cy = rng.Next(Size), r = rng.Next(rMin, rMax);
                float d = R(rng, dMin, dMax);
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                        if (x * x + y * y <= r * r)
                            Put(v, cx + x, cy + y, d * (1f - Mathf.Sqrt(x * x + y * y) / r));
            }
        }

        private static void PaintGrass(float[] v, System.Random rng)
        {
            // broad meadow patches first (the tone that survives distance)...
            Blotches(v, rng, 26, 18, 44, -0.07f, 0.07f);
            // ...then CHUNKY painted blades in two tones (thick strokes — thin ones mip away)
            for (int i = 0; i < 700; i++)
            {
                int x = rng.Next(Size), y = rng.Next(Size);
                float tone = rng.Next(3) == 0 ? R(rng, 0.10f, 0.15f) : R(rng, -0.15f, -0.09f);
                Stroke(v, rng, x, y, R(rng, 1.2f, 1.9f), rng.Next(5, 11), tone, 0.22f, thick: true);
            }
            for (int i = 0; i < 220; i++) // seeds/pebbles
                Put(v, rng.Next(Size), rng.Next(Size), R(rng, -0.13f, 0.13f));
        }

        private static void PaintTiles(float[] v, System.Random rng)
        {
            const int tile = 64; // 4x4 pavers per repeat
            // per-tile value jitter (worn vs fresh stone)
            for (int ty = 0; ty < Size / tile; ty++)
                for (int tx = 0; tx < Size / tile; tx++)
                {
                    float jitter = R(rng, -0.05f, 0.05f);
                    for (int y = 0; y < tile; y++)
                        for (int x = 0; x < tile; x++)
                            Put(v, tx * tile + x, ty * tile + y, jitter);
                    // a soft worn highlight blob off-centre
                    int cx = tx * tile + rng.Next(12, tile - 12), cy = ty * tile + rng.Next(12, tile - 12);
                    int r = rng.Next(8, 16);
                    for (int y = -r; y <= r; y++)
                        for (int x = -r; x <= r; x++)
                            if (x * x + y * y <= r * r)
                                Put(v, cx + x, cy + y, 0.03f * (1f - Mathf.Sqrt(x * x + y * y) / r));
                }
            // grout lines with a hand-laid wobble
            for (int g = 0; g <= Size; g += tile)
            {
                float w1 = R(rng, -1.5f, 1.5f), w2 = R(rng, -1.5f, 1.5f);
                for (int i = 0; i < Size; i++)
                {
                    int off = (int)Mathf.Lerp(w1, w2, i / (float)Size);
                    Put(v, g + off - 1, i, -0.10f); Put(v, g + off, i, -0.18f); Put(v, g + off + 1, i, -0.12f); // vertical grout
                    Put(v, i, g + off - 1, -0.10f); Put(v, i, g + off, -0.18f); Put(v, i, g + off + 1, -0.12f); // horizontal grout
                }
            }
            // chips and cracks off the grout
            for (int i = 0; i < 26; i++)
                Stroke(v, rng, rng.Next(Size), rng.Next(Size), R(rng, 0f, 6.28f), rng.Next(6, 16), -0.09f, 0.5f);
        }

        private static void PaintRipples(float[] v, System.Random rng)
        {
            // wind-combed sand: soft drifting bands + sparse grain flecks
            float phase = R(rng, 0f, 6.28f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float wave = Mathf.Sin((y + 10f * Mathf.Sin(x * (2f * Mathf.PI / Size) * 2f + phase))
                                           * (2f * Mathf.PI / Size) * 9f);
                    // sharpen the crests a touch so they read as combed ridges, not a blur
                    float crest = Mathf.Sign(wave) * Mathf.Pow(Mathf.Abs(wave), 0.7f);
                    Put(v, x, y, crest * 0.08f);
                }
            for (int i = 0; i < 200; i++)
                Put(v, rng.Next(Size), rng.Next(Size), R(rng, -0.06f, 0.08f));
        }

        private static void PaintSpeckle(float[] v, System.Random rng)
        {
            // broad drifts first, then snow glitter + wind streaks
            Blotches(v, rng, 20, 20, 46, -0.05f, 0.06f);
            for (int i = 0; i < 420; i++)
            {
                int x = rng.Next(Size), y = rng.Next(Size);
                float d = R(rng, 0.10f, 0.16f);
                Put(v, x, y, d); Put(v, x + 1, y, d * 0.6f); Put(v, x, y + 1, d * 0.6f);
            }
            for (int i = 0; i < 70; i++)
                Put(v, rng.Next(Size), rng.Next(Size), R(rng, -0.11f, -0.06f)); // pressed dimples
            for (int i = 0; i < 26; i++) // long soft drift streaks
                Stroke(v, rng, rng.Next(Size), rng.Next(Size), R(rng, -0.2f, 0.2f), rng.Next(20, 50), 0.03f, 0.06f, thick: true);
        }

        private static void PaintCracks(float[] v, System.Random rng)
        {
            // cooled rock: broad tonal patches + long meandering dark cracks
            Blotches(v, rng, 34, 10, 26, -0.05f, 0.06f);
            for (int i = 0; i < 34; i++)
                Stroke(v, rng, rng.Next(Size), rng.Next(Size), R(rng, 0f, 6.28f), rng.Next(40, 95), -0.14f, 0.35f, thick: true);
        }
    }
}
