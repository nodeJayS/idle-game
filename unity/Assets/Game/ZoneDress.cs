#nullable enable
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Zone reskin coordinator (roadmap 4, the "depth feels like travel" beat): when the
    /// current stage (or Tower floor — floors travel the same zones) crosses into another
    /// zone, retint the faceted ground + scattered props from the ZoneDef's engine-free
    /// palette hints. Pure renderer-side: reads config, never touches sim state. Cheap
    /// (a few SetColor calls on shared materials), so callers just Sync on every farm
    /// start / stage change and let the id guard no-op the common case.
    /// </summary>
    public static class ZoneDress
    {
        private static string? _zoneId; // last dressed zone; static resets with domain reload on Play

        /// <summary>Dress the world for the zone owning stage/floor <paramref name="key"/>.
        /// Returns the zone when this call CHANGED the dress (the caller's cue for a feed
        /// line), null when already dressed or no zones exist.</summary>
        public static ZoneDef? Sync(GameConfig cfg, int key)
        {
            var zone = cfg.ZoneForStage(key);
            string id = zone?.Id ?? "";
            if (id == _zoneId) return null;
            _zoneId = id;
            Ground.SetZone(zone);
            // Terraced arena terrain (ROADMAP 8 slice 2): rebuild the floor for this zone's layout
            // (or restore the legacy plane when the zone has none), THEN re-scatter props onto it so
            // nothing floats over the new shore/void. Both hang off the same dress-changed event.
            var arena = cfg.ArenaForStage(key);
            ArenaTerrain.Rebuild(cfg, arena);
            Scenery.SetZone(zone, arena);
            return zone;
        }
    }
}
