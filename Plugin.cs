using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using EFT;
using UnityEngine;

namespace BotCensus
{
    [BepInPlugin(PluginId, "Bot Census", "1.1.0")]
    [BepInDependency("com.morebotsapi.tacticaltoaster", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    public class BotCensusPlugin : BaseUnityPlugin
    {
        public const string PluginId = "com.sipto.botcensus";

        static readonly ConfigDescription RowHelp = new ConfigDescription(
            "Always = show even at zero. WhenPresent = only while some are alive. Hidden = never.");

        ConfigEntry<bool> _enabled;
        ConfigEntry<bool> _onlyInRaid;
        ConfigEntry<int> _fontSize;
        ConfigEntry<int> _offsetRight;
        ConfigEntry<int> _offsetTop;
        ConfigEntry<bool> _tarkovFont;
        ConfigEntry<float> _bgOpacity;
        ConfigEntry<bool> _splitRogue;
        ConfigEntry<string> _interval;

        ConfigEntry<RowVis> _showPmc, _showScav, _showRaider, _showRogue, _showBoss,
                            _showGoon, _showCultist, _showInfected, _showBtr, _showOther;

        readonly int[] _counts = new int[System.Enum.GetValues(typeof(Bucket)).Length];  // indexed by Bucket
        readonly Dictionary<string, int> _factions = new Dictionary<string, int>();
        readonly List<CensusRow> _rows = new List<CensusRow>(12);
        float _timer;
        float _raidTimer;
        bool _inRaid;
        bool? _fika;

        void Awake()
        {
            _enabled = Config.Bind("1. General", "Enable", true,
                "Toggle the on-screen bot census on or off.");
            _onlyInRaid = Config.Bind("1. General", "Only In Raid", true,
                "Hide the overlay in the menu and hideout; only draw it once you're in a raid.");

            _fontSize = Config.Bind("2. Display", "Font Size", 16,
                new ConfigDescription("Text size of the overlay.", new AcceptableValueRange<int>(10, 30)));
            _offsetRight = Config.Bind("2. Display", "Offset Right", 20,
                "Distance from the right edge of the screen.");
            _offsetTop = Config.Bind("2. Display", "Offset Top", 40,
                "Distance from the top edge of the screen.");
            _tarkovFont = Config.Bind("2. Display", "Use Tarkov Font", true,
                "Render with the game's Bender font (falls back to a DIN-like face, then Unity default).");
            _bgOpacity = Config.Bind("2. Display", "Background Opacity", 0.72f,
                new ConfigDescription("Panel backing opacity. 0 is fully see-through, 1 is solid.",
                    new AcceptableValueRange<float>(0f, 1f)));

            _splitRogue = Config.Bind("3. Rows", "Split Rogue And Raider", true,
                "On: separate Raider and Rogue rows. Off: one 'Raider / Rogue' row to save space.");
            _showPmc      = Config.Bind("3. Rows", "PMC",      RowVis.Always,      RowHelp);
            _showScav     = Config.Bind("3. Rows", "Scav",     RowVis.Always,      RowHelp);
            _showRaider   = Config.Bind("3. Rows", "Raider",   RowVis.WhenPresent, RowHelp);
            _showRogue    = Config.Bind("3. Rows", "Rogue",    RowVis.WhenPresent, RowHelp);
            _showBoss     = Config.Bind("3. Rows", "Boss",     RowVis.WhenPresent, RowHelp);
            _showGoon     = Config.Bind("3. Rows", "Goons",    RowVis.WhenPresent, RowHelp);
            _showCultist  = Config.Bind("3. Rows", "Cultist",  RowVis.WhenPresent, RowHelp);
            _showInfected = Config.Bind("3. Rows", "Infected", RowVis.WhenPresent, RowHelp);
            _showBtr      = Config.Bind("3. Rows", "BTR",      RowVis.WhenPresent, RowHelp);
            _showOther    = Config.Bind("3. Rows", "Other",    RowVis.WhenPresent, RowHelp);

            _interval = Config.Bind("4. Performance", "Update Interval", "5s",
                new ConfigDescription("How often the raid is recounted.",
                    new AcceptableValueList<string>("5s", "10s", "15s", "30s", "1min")));

            Logger.LogInfo("Bot Census loaded");
        }

        void Update()
        {
            if (!_enabled.Value) return;

            // Cheap raid-state poll so the overlay knows when to hide, decoupled from the recount cadence.
            _raidTimer += Time.deltaTime;
            if (_raidTimer >= 0.5f)
            {
                _raidTimer = 0f;
                bool was = _inRaid;
                _inRaid = Object.FindObjectOfType<GameWorld>() != null;
                if (was && !_inRaid && _onlyInRaid.Value)   // left the raid: drop the last tally
                {
                    _rows.Clear();
                    _factions.Clear();
                    _timer = 0f;
                }
            }
            if (!_inRaid) return;

            _timer += Time.deltaTime;
            if (_timer < IntervalSeconds(_interval.Value)) return;
            _timer = 0f;
            Recount();
        }

        static float IntervalSeconds(string value)
        {
            switch (value)
            {
                case "10s": return 10f;
                case "15s": return 15f;
                case "30s": return 30f;
                case "1min": return 60f;
                default: return 5f;
            }
        }

        void Recount()
        {
            for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
            _factions.Clear();

            if (FikaLoaded && FikaSource.TryClassify(this))
            {
                BuildRows();
                return;
            }

            CountSolo();
            BuildRows();
        }

        bool FikaLoaded
        {
            get
            {
                if (_fika == null) _fika = Chainloader.PluginInfos.ContainsKey("com.fika.core");
                return _fika.Value;
            }
        }

        void CountSolo()
        {
            var world = Object.FindObjectOfType<GameWorld>();
            var players = world != null ? world.RegisteredPlayers : null;
            if (players == null) return;
            for (int i = 0; i < players.Count; i++)
                Classify(players[i], false);
        }

        // Called from CountSolo and from FikaSource (which passes IsObservedAI for remote AI).
        public void Classify(IPlayer player, bool forceAi)
        {
            if (player == null || player.IsYourPlayer) return;
            if (!player.IsAI && !forceAi) return;

            var profile = player.Profile;
            if (profile == null || profile.Info == null || profile.Info.Settings == null) return;
            if (player is Player p && p.HealthController != null && !p.HealthController.IsAlive) return;

            WildSpawnType role = profile.Info.Settings.Role;
            int r = (int)role;

            // (A) Custom factions register WildSpawnType integers above the vanilla ceiling (67).
            //     Resolve these first so a Savage-side faction bot is never shadowed into another bucket.
            if (r > 67)
            {
                string faction = MoreBotsBridge.GetFaction(role) ?? Classifier.RangeFallback(r) ?? "Custom";
                Bump(faction);
                return;
            }

            // (B) AI PMCs (pmcUSEC / pmcBEAR) are only distinguishable by side, not role.
            EPlayerSide side = profile.Side;
            if (side == EPlayerSide.Usec || side == EPlayerSide.Bear)
            {
                _counts[(int)Bucket.Pmc]++;
                return;
            }

            // (C) Explicit vanilla map, with a loud "Other" catch-all so nothing is invisible.
            _counts[(int)Classifier.Vanilla(role)]++;
        }

        void Bump(string faction)
        {
            _factions.TryGetValue(faction, out int c);
            _factions[faction] = c + 1;
        }

        void BuildRows()
        {
            _rows.Clear();
            Row("PMC", Bucket.Pmc, _showPmc);
            Row("Scav", Bucket.Scav, _showScav);

            if (_splitRogue.Value)
            {
                Row("Raider", Bucket.Raider, _showRaider);
                Row("Rogue", Bucket.Rogue, _showRogue);
            }
            else
            {
                int both = _counts[(int)Bucket.Raider] + _counts[(int)Bucket.Rogue];
                AddRow("Raider / Rogue", both, _showRaider, false);
            }

            Row("Boss / Guard", Bucket.Boss, _showBoss);
            AddRow("Goons", _counts[(int)Bucket.Goon], _showGoon, true);   // accent — a marquee target like the factions
            Row("Cultist", Bucket.Cultist, _showCultist);
            Row("Infected", Bucket.Infected, _showInfected);
            Row("BTR", Bucket.Btr, _showBtr);

            foreach (var faction in _factions)
                _rows.Add(new CensusRow(faction.Key, faction.Value, true));

            Row("Other", Bucket.Other, _showOther);
        }

        void Row(string label, Bucket bucket, ConfigEntry<RowVis> vis)
        {
            AddRow(label, _counts[(int)bucket], vis, false);
        }

        void AddRow(string label, int value, ConfigEntry<RowVis> vis, bool accent)
        {
            if (vis.Value == RowVis.Hidden) return;
            if (vis.Value == RowVis.WhenPresent && value <= 0) return;
            _rows.Add(new CensusRow(label, value, accent));
        }

        void OnGUI()
        {
            if (!_enabled.Value) return;
            if (_onlyInRaid.Value && !_inRaid) return;
            Hud.Draw(_rows, _fontSize.Value, _offsetRight.Value, _offsetTop.Value, _tarkovFont.Value, _bgOpacity.Value);
        }
    }

    public enum Bucket
    {
        Pmc,
        Scav,
        Raider,
        Rogue,
        Boss,
        Goon,
        Cultist,
        Infected,
        Btr,
        Other
    }

    public enum RowVis
    {
        WhenPresent,
        Always,
        Hidden
    }
}
