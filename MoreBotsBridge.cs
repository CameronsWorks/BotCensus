using System;
using System.Collections;
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

        public static string GetFaction(WildSpawnType role)
        {
            if (!_resolved) Resolve();
            if (!_ready) return null;

            object manager = _instance.GetValue(null, null);
            if (manager == null) return null;                       // singleton not spawned yet (pre-raid)

            var names = _getFactionsByRole.Invoke(manager, new object[] { role }) as IList;
            if (names == null || names.Count == 0) return null;
            return Prettify(names[0] as string);
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
