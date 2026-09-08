using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Where things live. Each species is restricted to terrain that suits it,
    /// so the forest reads as an ecosystem rather than a random scatter: fungi
    /// in damp shade, berries on sunny edges, grazers in meadows, predators in
    /// the deep woods.
    /// </summary>
    public static class Habitat
    {
        public enum Zone
        {
            DeepWoods,   // dense canopy, far from camp — bear, snakes
            Woodland,    // ordinary forest — squirrels, foxes, most mushrooms
            Meadow,      // open, sunlit clearings — deer, berries, flowers
            Waterside,   // near the pond — damp wood, chanterelles, frogs
            Camp         // the clearing itself — kept clear of wildlife
        }

        public static Zone ZoneAt(Vector3 p) => ZoneAt(p.x, p.z);

        public static Zone ZoneAt(float x, float z)
        {
            var forest = ForestGenerator.Instance;
            if (forest == null) return Zone.Woodland;

            float fromCamp = new Vector2(x, z).magnitude;
            if (fromCamp < forest.campRadius) return Zone.Camp;

            float fromPond = Vector2.Distance(new Vector2(x, z), forest.pondCenter);
            if (fromPond < forest.pondRadius + 6f) return Zone.Waterside;

            float density = forest.DensityAt(x, z);
            if (density < 0.3f) return Zone.Meadow;
            if (density > 0.72f && fromCamp > 28f) return Zone.DeepWoods;
            return Zone.Woodland;
        }

        /// <summary>Does this spot suit the given species?</summary>
        public static bool Suits(Zone zone, params Zone[] wanted)
        {
            foreach (var w in wanted)
                if (zone == w) return true;
            return false;
        }

        /// <summary>
        /// Find a spot in the world that belongs to one of the wanted zones.
        /// Returns false if no suitable ground was found in the attempts given.
        /// </summary>
        public static bool TryFindSpot(System.Random rand, float minRadius, float maxRadius,
            out Vector3 position, params Zone[] wanted)
        {
            var forest = ForestGenerator.Instance;
            // waterside species are searched around the pond, not the camp —
            // radial sampling from the origin almost never lands on the shore
            bool wantsWater = System.Array.IndexOf(wanted, Zone.Waterside) >= 0;
            Vector2 origin = wantsWater && forest != null ? forest.pondCenter : Vector2.zero;
            float searchMax = wantsWater ? Mathf.Min(maxRadius, forest.pondRadius + 10f) : maxRadius;
            float searchMin = wantsWater ? forest.pondRadius + 1.5f : minRadius;
            return TryFindSpotAround(rand, origin, searchMin, searchMax, out position, wanted);
        }

        /// <summary>Search for suitable ground in a ring around an arbitrary centre.</summary>
        public static bool TryFindSpotAround(System.Random rand, Vector2 centre,
            float minRadius, float maxRadius, out Vector3 position, params Zone[] wanted)
        {
            position = Vector3.zero;
            var forest = ForestGenerator.Instance;
            if (forest == null) return false;

            for (int tries = 0; tries < 40; tries++)
            {
                float a = (float)rand.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(minRadius, maxRadius, (float)rand.NextDouble());
                float x = centre.x + Mathf.Cos(a) * r, z = centre.y + Mathf.Sin(a) * r;

                // never inside the camp clearing
                if (new Vector2(x, z).magnitude < forest.campRadius) continue;

                // stay inside the map
                float half = forest.worldSize * 0.5f - 6f;
                if (Mathf.Abs(x) > half || Mathf.Abs(z) > half) continue;

                // never inside the pond itself
                if (Vector2.Distance(new Vector2(x, z), forest.pondCenter) < forest.pondRadius + 1f) continue;

                if (!Suits(ZoneAt(x, z), wanted)) continue;

                position = new Vector3(x, forest.HeightAt(x, z), z);
                return true;
            }
            return false;
        }
    }
}
