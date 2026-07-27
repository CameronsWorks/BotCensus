using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using UnityEngine;

namespace BotCensus
{
    // MoreBotsAPI's FactionManager lives in MoreBotsPlugin.dll, but its CustomWildSpawnType
    // metadata types live in a separate prepatch assembly. To avoid a brittle build-time
    // dependency on either, we bind FactionManager.GetFactionsByRole by reflection. It simply
    // returns null when MoreBotsAPI is absent, so the caller falls back to the int-range table.
    internal static class MoreBotsBridge
    {
        const string PluginId = "com.morebotsapi.tacticaltoaster";

        // Absence is already handled by the Chainloader checks below. This covers the other case: MoreBots
        // present but shaped differently to what we bound, where reflection throws rather than answers.
        // One warning, then the bridge stays down and custom roles fall back to the name ranges.
        static bool _broken;

        static void Disable(Exception ex)
        {
            if (_broken) return;
            _broken = true;
            Debug.LogWarning("[Bot Census] MoreBotsAPI bridge disabled, custom factions fall back to " +
                             "name ranges: " + ex.Message);
        }

        static bool _resolved;
        static bool _ready;
        static PropertyInfo _instance;
        static MethodInfo _getFactionsByRole;

        static void Resolve()
        {
            _resolved = true;
            if (!Chainloader.PluginInfos.ContainsKey(PluginId)) return;

            Type manager = FindType("MoreBotsAPI.Components.FactionManager");
            if (manager == null) return;

            _instance = FindStaticProperty(manager, "Instance");
            _getFactionsByRole = manager.GetMethod("GetFactionsByRole", new[] { typeof(WildSpawnType) });
            _ready = _instance != null && _getFactionsByRole != null;
        }

        // The singleton exists from chainload, but its role-to-faction table stays empty until the raid
        // starts and MoreBots fetches the faction list, so this answers null well past the main menu.
        static object Manager()
        {
            if (_broken) return null;
            if (!_resolved) Resolve();
            if (!_ready) return null;

            try { return _instance.GetValue(null, null); }
            catch (Exception ex) { Disable(ex); return null; }
        }

        public static string GetFaction(WildSpawnType role)
        {
            object manager = Manager();
            if (manager == null) return null;

            try
            {
                var names = _getFactionsByRole.Invoke(manager, new object[] { role }) as IList;
                if (names == null || names.Count == 0) return null;
                return Prettify(names[0] as string);
            }
            catch (Exception ex)
            {
                Disable(ex);
                return null;
            }
        }

        // Every custom role MoreBotsAPI knows about, keyed by its WildSpawnType int. The prepatcher fills
        // this in at load, well before a raid, and MoreBotsPlugin reads the same static from inside the
        // game, so it is reachable from here despite living in the patcher assembly.
        static bool _registryResolved;
        static MethodInfo _getRegistry;
        static PropertyInfo _isFollower;
        static PropertyInfo _sain;
        static FieldInfo _section;

        static void ResolveRegistry()
        {
            _registryResolved = true;
            if (!Chainloader.PluginInfos.ContainsKey(PluginId)) return;

            Type manager = FindType("MoreBotsAPI.CustomWildSpawnTypeManager");
            Type custom = FindType("MoreBotsAPI.CustomWildSpawnType");
            if (manager == null || custom == null) return;

            _getRegistry = manager.GetMethod("GetCustomWildSpawnTypeDict",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            _isFollower = custom.GetProperty("IsFollower", BindingFlags.Public | BindingFlags.Instance);

            // Optional: the faction's display name, for when the live registry can't answer. Bound
            // separately from the two above so a reshaped SAIN block costs us the names and not the
            // boss/escort split as well. Section is a field, not a property.
            _sain = custom.GetProperty("SAINSettings", BindingFlags.Public | BindingFlags.Instance);
            Type sain = FindType("MoreBotsAPI.SAINSettings");
            if (sain != null) _section = sain.GetField("Section", BindingFlags.Public | BindingFlags.Instance);
        }

        // The dictionary handed back is the live one, so hold it rather than re-invoking per bot per tick.
        static IDictionary _registry;

        static IDictionary Registry()
        {
            if (_registry != null) return _registry;
            if (_broken) return null;
            if (!_registryResolved) ResolveRegistry();
            if (_getRegistry == null || _isFollower == null) return null;

            try { return _registry = _getRegistry.Invoke(null, null) as IDictionary; }
            catch (Exception ex) { Disable(ex); return null; }
        }

        // Which factions field a boss rather than a flat squad. Black Division, RUAF and UNTAR register
        // every role as a follower — they are loadout variants of one another, with nobody in charge — so
        // they stay on a single line. A faction that also registers a non-follower has a boss worth
        // separating from his escort, which is what splits Wedge off from his guards.
        //
        // Keyed off the registry rather than off who is currently breathing, so the guards keep reading as
        // guards once the boss is dead. Thrown away each recount: it is keyed by the same Label() the tally
        // uses, and those names change once the faction list lands mid-raid — hold this across that and the
        // two sets of keys drift apart, which silently merges the split back together.
        static Dictionary<string, bool> _bossFactions;

        public static void Invalidate()
        {
            _bossFactions = null;
            _labels.Clear();
        }

        static Dictionary<string, bool> BossFactions()
        {
            if (_bossFactions != null) return _bossFactions;

            var shape = new Dictionary<string, bool>();
            IDictionary registry = Registry();
            if (registry == null) return _bossFactions = shape;     // no MoreBotsAPI, so no custom roles either

            try
            {
                foreach (DictionaryEntry entry in registry)
                {
                    if (!(entry.Key is int) || entry.Value == null) continue;
                    if (!(_isFollower.GetValue(entry.Value, null) is bool follower)) continue;

                    string faction = Label((int)entry.Key);
                    bool leads = !follower;
                    bool had;
                    shape[faction] = shape.TryGetValue(faction, out had) ? had || leads : leads;
                }
            }
            catch (Exception ex)
            {
                Disable(ex);
            }
            return _bossFactions = shape;
        }

        public static bool FactionHasBoss(string faction)
        {
            bool has;
            return BossFactions().TryGetValue(faction, out has) && has;
        }

        public static bool IsEscort(WildSpawnType role)
        {
            IDictionary registry = Registry();
            if (registry == null) return false;

            try
            {
                object custom = registry[(int)role];
                return custom != null && _isFollower.GetValue(custom, null) is bool follower && follower;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
        }

        // The name each faction mod hands its prepatcher, which is where its own SAIN settings get their
        // heading — and which is exactly the string this panel wants. Unlike the faction list above it
        // needs no server round trip and no raid, because BepInEx fills it during preloading on every
        // machine. That is what makes it the answer on a Fika client, where the faction list is never
        // fetched at all and every custom bot would otherwise read as "Custom".
        //
        // Its own failure latch: this path is a fallback, and letting it trip the bridge-wide one would
        // let a reshaped SAIN block take down the authoritative source on the host as well.
        static bool _sectionBroken;

        static string RegistryFaction(int role)
        {
            IDictionary registry = Registry();
            if (registry == null || _sectionBroken || _sain == null || _section == null) return null;

            try
            {
                object custom = registry[role];
                if (custom == null) return null;

                object sain = _sain.GetValue(custom, null);
                return sain == null ? null : Prettify(_section.GetValue(sain) as string);
            }
            catch (Exception ex)
            {
                _sectionBroken = true;
                Debug.LogWarning("[Bot Census] MoreBotsAPI faction names unavailable, falling back to " +
                                 "name ranges: " + ex.Message);
                return null;
            }
        }

        // The row a custom role lands on. Kept in one place so the faction shape above is keyed by exactly
        // the same string the panel prints. Memoised: three reflection hops per bot per recount, and
        // BossFactions() asks again for every registered role on top of that.
        static readonly Dictionary<int, string> _labels = new Dictionary<int, string>();

        public static string Label(int role)
        {
            string label;
            if (_labels.TryGetValue(role, out label)) return label;

            // Known ranges sit ahead of the registry on purpose. A faction mod's SAIN section is whatever
            // reads well in SAIN's own settings menu, which is not always a faction name — Blackout files
            // its boss under "Bosses" and its guards under "Black Division", which on a client would put a
            // row called BOSSES on the panel and quietly add its guards to the real Black Division's
            // count. Our own names are also what the glyph table is keyed by. The registry stays as the
            // answer for factions nobody has written a range for.
            return _labels[role] = GetFaction((WildSpawnType)role)
                                ?? Classifier.RangeFallback(role)
                                ?? RegistryFaction(role)
                                ?? "Custom";
        }

        static string Prettify(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            switch (raw.ToLowerInvariant())
            {
                case "ruaf": return "RUAF";
                case "remnant": return "Remnant";
                case "untar": return "UNTAR";
                case "blackdiv": return "Black Division";
                // Blackout's Labs garrison. Its faction is named for who they are rather than the mod, and
                // "Blackdivision" one word next to Black Division's own row reads like a typo.
                case "blackdivision": return "Blackout";
                default: return char.ToUpperInvariant(raw[0]) + raw.Substring(1);
            }
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // A dynamic or half-loaded assembly can throw instead of answering, and it is never ours.
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        // Instance is a public static member of the generic MonoBehaviourSingleton<T> base,
        // so walk the hierarchy explicitly rather than trust FlattenHierarchy alone.
        static PropertyInfo FindStaticProperty(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            for (Type t = type; t != null; t = t.BaseType)
            {
                var property = t.GetProperty(name, flags);
                if (property != null) return property;
            }
            return null;
        }
    }
}
