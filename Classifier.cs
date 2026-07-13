using System.Collections.Generic;
using EFT;

namespace BotCensus
{
    // Maps vanilla WildSpawnType values (0-67) to a display bucket. Values a name-substring test
    // would get wrong or miss are pinned by number; the well-behaved boss*/follower* families fall
    // through to the substring test. Verified against EFT's WildSpawnType enum.
    internal static class Classifier
    {
        static readonly Dictionary<int, Bucket> Pinned = new Dictionary<int, Bucket>
        {
            // scavs the keyword test misses (10 cursedAssault, 19 assaultGroup) and the
            // 37 crazyAssaultEvent that BotCounter wrongly counted as a boss.
            { 0, Bucket.Scav }, { 1, Bucket.Scav }, { 10, Bucket.Scav }, { 19, Bucket.Scav }, { 37, Bucket.Scav },

            // 9 pmcBot is the Reserve/Labs Raider.
            { 9, Bucket.Raider },

            // rogues: 24 exUsec on Lighthouse, 34/35 the Bloodhound arena fighters.
            { 24, Bucket.Rogue }, { 34, Bucket.Rogue }, { 35, Bucket.Rogue },

            // the Goons hunt as a trio (Knight + Big Pipe + Birdeye); the keyword test would fold
            // them into Boss/follower, so pin them out.
            { 26, Bucket.Goon }, { 27, Bucket.Goon }, { 28, Bucket.Goon },

            // the zombie event faction.
            { 60, Bucket.Infected }, { 61, Bucket.Infected }, { 62, Bucket.Infected }, { 63, Bucket.Infected }, { 64, Bucket.Infected },

            // event bosses whose names carry no boss/follower keyword (38/40 Zryachiy events, 67 Tagilla helper).
            { 38, Bucket.Boss }, { 40, Bucket.Boss }, { 67, Bucket.Boss },

            // 46 shooterBTR — the armoured car's turret gunner.
            { 46, Bucket.Btr },

            // non-combatant / special NPCs (test, gifter, spirits, peacemaker, skier).
            { 18, Bucket.Other }, { 25, Bucket.Other },
            { 48, Bucket.Other }, { 49, Bucket.Other }, { 50, Bucket.Other }, { 53, Bucket.Other },
        };

        public static Bucket Vanilla(WildSpawnType role)
        {
            if (Pinned.TryGetValue((int)role, out var bucket)) return bucket;

            string name = role.ToString().ToLowerInvariant();
            if (name.Contains("boss") || name.Contains("follower")) return Bucket.Boss;
            // "sectact" covers the 39 sectactPriestEvent typo in EFT's own enum.
            if (name.Contains("sectant") || name.Contains("sectact")) return Bucket.Cultist;
            return Bucket.Other;
        }

        // Used only when MoreBotsAPI is not installed: the faction int ranges are stable and
        // self-documenting. Prefer the live registry (MoreBotsBridge) when it is available.
        public static string RangeFallback(int r)
        {
            if (r >= 848400 && r <= 848405) return "RUAF";
            if (r == 848406) return "Remnant";
            if (r >= 848420 && r <= 848423) return "Black Division";
            if (r >= 1170 && r <= 1173) return "UNTAR";
            return null;
        }
    }
}
