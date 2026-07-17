using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;

namespace BotCensus
{
    // MoreBotsAPI's FactionManager lives in MoreBotsPlugin.dll, but its CustomWildSpawnType
    // metadata types live in a separate prepatch assembly. To avoid a brittle build-time
    // dependency on either, we bind FactionManager.GetFactionsByRole by reflection. It simply
    // returns null when MoreBotsAPI is absent, so the caller falls back to the int-range table.
    internal static class MoreBotsBridge
    {
        const string PluginId = "com.morebotsapi.tacticaltoaster";

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
            if (!_resolved) Resolve();
            if (!_ready) return null;
            return _instance.GetValue(null, null);
        }

        public static string GetFaction(WildSpawnType role)
        {
            object manager = Manager();
            if (manager == null) return null;

            var names = _getFactionsByRole.Invoke(manager, new object[] { role }) as IList;
            if (names == null || names.Count == 0) return null;
            return Prettify(names[0] as string);
        }

        // Every custom role MoreBotsAPI knows about, keyed by its WildSpawnType int. The prepatcher fills
        // this in at load, well before a raid, and MoreBotsPlugin reads the same static from inside the
        // game, so it is reachable from here despite living in the patcher assembly.
        static bool _registryResolved;
        static MethodInfo _getRegistry;
        static PropertyInfo _isFollower;

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
        }

        // The dictionary handed back is the live one, so hold it rather than re-invoking per bot per tick.
        static IDictionary _registry;

        static IDictionary Registry()
        {
            if (_registry != null) return _registry;
            if (!_registryResolved) ResolveRegistry();
            if (_getRegistry == null || _isFollower == null) return null;
            return _registry = _getRegistry.Invoke(null, null) as IDictionary;
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
        }

        static Dictionary<string, bool> BossFactions()
        {
            if (_bossFactions != null) return _bossFactions;

            var shape = new Dictionary<string, bool>();
            IDictionary registry = Registry();
            if (registry == null) return _bossFactions = shape;     // no MoreBotsAPI, so no custom roles either

            foreach (DictionaryEntry entry in registry)
            {
                if (!(entry.Key is int) || entry.Value == null) continue;

                string faction = Label((int)entry.Key);
                bool leads = !(bool)_isFollower.GetValue(entry.Value, null);
                bool had;
                shape[faction] = shape.TryGetValue(faction, out had) ? had || leads : leads;
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

            object custom = registry[(int)role];
            return custom != null && (bool)_isFollower.GetValue(custom, null);
        }

        // The row a custom role lands on. Kept in one place so the faction shape above is keyed by exactly
        // the same string the panel prints.
        public static string Label(int role)
        {
            return GetFaction((WildSpawnType)role) ?? Classifier.RangeFallback(role) ?? "Custom";
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
                default: return char.ToUpperInvariant(raw[0]) + raw.Substring(1);
            }
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
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
