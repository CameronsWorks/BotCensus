using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BotCensus
{
    // Row glyphs. They ship inside the DLL as white-on-transparent PNGs and get tinted at draw time,
    // so each one picks up its row's colour and the raid-start fade without needing a variant per shade.
    // Vector source and the page that rasterises it are in Icons/regenerate.html.
    internal static class Icons
    {
        public const string Pmc = "pmc";
        public const string Scav = "scav";
        public const string Raider = "raider";
        public const string Rogue = "rogue";
        public const string Boss = "boss";
        public const string Guard = "guard";
        public const string Goons = "goons";
        public const string Cultist = "cultist";
        public const string Infected = "infected";
        public const string Btr = "btr";
        public const string Faction = "faction";
        public const string Other = "other";
        public const string Total = "total";

        // Custom factions share the generic Faction shield unless they have their own art here, keyed by
        // the display name Bump() tallies under (MoreBotsBridge.Prettify / RangeFallback).
        static readonly Dictionary<string, string> ByFaction = new Dictionary<string, string>
        {
            { "Black Division", "blackdivision" },
        };

        public static string ForFaction(string faction)
        {
            string icon;
            return ByFaction.TryGetValue(faction, out icon) ? icon : Faction;
        }

        // Null is a legitimate cached value: a glyph that failed to load stays missing rather than
        // being retried on every frame OnGUI runs.
        static readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            Texture2D texture;
            if (Loaded.TryGetValue(name, out texture)) return texture;
            texture = Load(name);
            Loaded[name] = texture;
            return texture;
        }

        static Texture2D Load(string name)
        {
            byte[] png = Read("BotCensus.Icons." + name + ".png");
            if (png == null) return null;

            // Mipmapped: a 64px glyph is drawn at whatever the font size is (10-30), and point-sampling
            // that down crawls. Trilinear off the mip chain keeps it steady.
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, true);
            if (!texture.LoadImage(png))
            {
                Object.Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Trilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        static byte[] Read(string resource)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    Debug.LogWarning("[Bot Census] missing icon resource: " + resource);
                    return null;
                }

                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int got = stream.Read(bytes, read, bytes.Length - read);
                    if (got <= 0) break;
                    read += got;
                }
                return read == bytes.Length ? bytes : null;
            }
        }
    }
}
