using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.UI.Screens;
using UnityEngine;

namespace BotCensus
{
    [BepInPlugin(PluginId, "Bot Census", "1.4.0")]
    [BepInDependency("com.morebotsapi.tacticaltoaster", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    public class BotCensusPlugin : BaseUnityPlugin
    {
        public const string PluginId = "com.sipto.botcensus";

        static readonly ConfigDescription RowHelp = new ConfigDescription(
            "Always = show even at zero. WhenPresent = only while some are alive. Hidden = never.");

        ConfigEntry<bool> _enabled;
        ConfigEntry<bool> _onlyInRaid;
        ConfigEntry<KeyboardShortcut> _toggleKey;
        ConfigEntry<int> _fontSize;
        ConfigEntry<int> _offsetRight;
        ConfigEntry<int> _offsetTop;
        ConfigEntry<bool> _tarkovFont;
        ConfigEntry<float> _bgOpacity;
        ConfigEntry<bool> _splitRogue;
        ConfigEntry<bool> _splitBoss;
        ConfigEntry<string> _interval;

        ConfigEntry<RowVis> _showPmc, _showScav, _showRaider, _showRogue, _showBoss, _showGuard,
                            _showGoon, _showCultist, _showInfected, _showBtr, _showOther, _showTotal;
        ConfigEntry<bool> _icons;
        ConfigEntry<float> _iconScale;

        readonly int[] _counts = new int[System.Enum.GetValues(typeof(Bucket)).Length];  // indexed by Bucket
        readonly Dictionary<string, FactionTally> _factions = new Dictionary<string, FactionTally>();
        readonly List<CensusRow> _rows = new List<CensusRow>(14);
        const float FadeSeconds = 0.35f;   // matches Dynamic Maps' minimap arrival (its DOTween transition time)

        float _timer;
        float _raidTimer;
        float _fade;
        bool _inRaid;
        bool? _fika;
        GameWorld _world;

        void Awake()
        {
            _enabled = Config.Bind("1. General", "Enable", true,
                "Toggle the on-screen bot census on or off.");
            _onlyInRaid = Config.Bind("1. General", "Only In Raid", true,
                "Hide the overlay in the menu and hideout; only draw it once you're in a raid.");
            _toggleKey = Config.Bind("1. General", "Toggle Key", KeyboardShortcut.Empty,
                "Show or hide the census from the keyboard mid-raid. Drives the same switch as Enable, "
                + "so the checkbox above follows along.");

            // Everything in the panel keys off this — row height, glyph size, the icon column and the panel
            // width are all derived from it, so it doubles as the overall scale. The ceiling is the glyph
            // art's own resolution: at 64 a row glyph draws about 1:1 with its source and stays crisp.
            _fontSize = Config.Bind("2. Display", "Font Size", 16,
                new ConfigDescription("Overall size of the panel. Text, glyphs, row height and width all "
                    + "scale from this, so raise it on a large or high-resolution display.",
                    new AcceptableValueRange<int>(10, 64)));
            _offsetRight = Config.Bind("2. Display", "Offset Right", 20,
                "Distance from the right edge of the screen.");
            _offsetTop = Config.Bind("2. Display", "Offset Top", 40,
                "Distance from the top edge of the screen.");
            _tarkovFont = Config.Bind("2. Display", "Use Tarkov Font", true,
                "Render with the game's Bender font (falls back to a DIN-like face, then Unity default).");
            _bgOpacity = Config.Bind("2. Display", "Background Opacity", 0.72f,
                new ConfigDescription("Panel backing opacity. 0 is fully see-through, 1 is solid.",
                    new AcceptableValueRange<float>(0f, 1f)));
            _icons = Config.Bind("2. Display", "Show Icons", true,
                "Draw a glyph beside each row. Off narrows the panel to text only.");
            _iconScale = Config.Bind("2. Display", "Icon Scale", 1f,
                new ConfigDescription("Glyph size relative to the text. 1 keeps them level with the font; "
                    + "raise it if the icons read small next to the numbers. Capped at the row height so "
                    + "a glyph can never spill into the row above or below.",
                    new AcceptableValueRange<float>(0.6f, 1.5f)));

            _splitRogue = Config.Bind("3. Rows", "Split Rogue And Raider", true,
                "On: separate Raider and Rogue rows. Off: one 'Raider / Rogue' row to save space.");
            _splitBoss = Config.Bind("3. Rows", "Split Boss And Guard", true,
                "On: separate Boss and Guard rows so you can tell if only the guards are left. Off: one 'Boss / Guard' row.");
            _showPmc      = Config.Bind("3. Rows", "PMC",      RowVis.Always,      RowHelp);
            _showScav     = Config.Bind("3. Rows", "Scav",     RowVis.Always,      RowHelp);
            _showRaider   = Config.Bind("3. Rows", "Raider",   RowVis.WhenPresent, RowHelp);
            _showRogue    = Config.Bind("3. Rows", "Rogue",    RowVis.WhenPresent, RowHelp);
            _showBoss     = Config.Bind("3. Rows", "Boss",     RowVis.WhenPresent, RowHelp);
            _showGuard    = Config.Bind("3. Rows", "Guard",    RowVis.WhenPresent, RowHelp);
            _showGoon     = Config.Bind("3. Rows", "Goons",    RowVis.WhenPresent, RowHelp);
            _showCultist  = Config.Bind("3. Rows", "Cultist",  RowVis.WhenPresent, RowHelp);
            _showInfected = Config.Bind("3. Rows", "Infected", RowVis.WhenPresent, RowHelp);
            _showBtr      = Config.Bind("3. Rows", "BTR",      RowVis.WhenPresent, RowHelp);
            _showOther    = Config.Bind("3. Rows", "Other",    RowVis.WhenPresent, RowHelp);
            _showTotal    = Config.Bind("3. Rows", "Total",    RowVis.Always,
                new ConfigDescription("Every bot on the map, counted under a rule at the foot of the panel. "
                    + "Rows you've hidden still count towards it."));

            _interval = Config.Bind("4. Performance", "Update Interval", "5s",
                new ConfigDescription("How often the raid is recounted.",
                    new AcceptableValueList<string>("5s", "10s", "15s", "30s", "1min")));

            Logger.LogInfo("Bot Census loaded");
        }

        void Update()
        {
            // KeyboardShortcut.IsDown refuses to fire while any unrelated key is held, and in a raid one
            // always is (W, usually). Check the bind by hand: main key just pressed, chosen modifiers down,
            // everything else left out of it. Modifiers only allocates on the press frame.
            KeyboardShortcut key = _toggleKey.Value;
            if (key.MainKey != KeyCode.None && Input.GetKeyDown(key.MainKey)
                && key.Modifiers.All(Input.GetKey))
                _enabled.Value = !_enabled.Value;

            // Drops the world reference as well: this returns above the raid poll, so without it switching
            // the panel off mid-raid pins that raid's GameWorld, and every player hanging off it, until the
            // panel is switched back on. Re-enabling still fades in rather than snapping.
            if (!_enabled.Value) { _fade = 0f; _world = null; return; }

            // Raid-state poll. The game registers the live world in Singleton<GameWorld> and swaps it on
            // every scene change, so read it fresh each poll — it's a static field, there's nothing worth
            // caching. The old cached FindObjectOfType lookup could outlive its raid: the hideout runs a
            // GameWorld too (HideoutGameWorld), and once the cache latched onto that one, its null check
            // held and the panel spent every following raid counting an empty world until the enable
            // toggle dropped the reference. The hideout is not a raid; treat it as no world at all.
            _raidTimer += Time.deltaTime;
            if (_raidTimer >= 0.5f)
            {
                _raidTimer = 0f;
                bool was = _inRaid;
                var world = Singleton<GameWorld>.Instance;
                _world = (world != null && !(world is HideoutGameWorld)) ? world : null;
                _inRaid = _world != null;
                if (was && !_inRaid)   // left the raid
                {
                    _fade = 0f;        // re-arm the fade-in for the next one
                    if (_onlyInRaid.Value)   // drop the last tally
                    {
                        _rows.Clear();
                        _factions.Clear();
                        _timer = 0f;
                    }
                }
            }

            // Ease the panel in the first time it actually lands on the battle HUD — the way Dynamic Maps'
            // minimap arrives — instead of snapping on. Game-time delta, matching its DOTween default clock.
            if (PaintEligible())
                _fade = Mathf.Min(1f, _fade + Time.deltaTime / FadeSeconds);

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
            ResetTally();
            MoreBotsBridge.Invalidate();

            if (FikaLoaded && !_fikaFailed && TryFika())
            {
                BuildRows();
                return;
            }

            ResetTally();   // a Fika pass that threw part-way leaves its partial tally behind
            CountSolo();
            BuildRows();
        }

        void ResetTally()
        {
            for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
            _factions.Clear();
        }

        // FikaSource binds Fika's types at compile time, so a rename or a moved namespace arrives here as a
        // throw rather than a false. Latch it and stay on the local count for the rest of the session: that
        // still sees every bot, it only loses the ability to tell a remote client's AI apart, which beats a
        // frozen panel and a stack trace every few seconds for the whole raid.
        bool _fikaFailed;

        bool TryFika()
        {
            try
            {
                return FikaSource.TryClassify(this);
            }
            catch (System.Exception ex)
            {
                _fikaFailed = true;
                Logger.LogWarning("[Bot Census] Fika bot source unavailable, counting locally: " + ex.Message);
                return false;
            }
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
            var players = _world != null ? _world.RegisteredPlayers : null;
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
            //     A faction registers a role per rank, so keep the boss and his escort apart here rather
            //     than folding both into one tally the way this used to.
            if (r > 67)
            {
                Bump(MoreBotsBridge.Label(r), MoreBotsBridge.IsEscort(role));
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

        void Bump(string faction, bool escort)
        {
            _factions.TryGetValue(faction, out FactionTally tally);
            if (escort) tally.Escorts++;
            else tally.Bosses++;
            _factions[faction] = tally;
        }

        void BuildRows()
        {
            _rows.Clear();
            Row("PMC", Bucket.Pmc, _showPmc, Icons.Pmc);
            Row("Scav", Bucket.Scav, _showScav, Icons.Scav);

            if (_splitRogue.Value)
            {
                Row("Raider", Bucket.Raider, _showRaider, Icons.Raider);
                Row("Rogue", Bucket.Rogue, _showRogue, Icons.Rogue);
            }
            else
            {
                int both = _counts[(int)Bucket.Raider] + _counts[(int)Bucket.Rogue];
                AddRow("Raider / Rogue", both, _showRaider, false, Icons.Raider);
            }

            if (_splitBoss.Value)
            {
                Row("Boss", Bucket.Boss, _showBoss, Icons.Boss);
                Row("Guard", Bucket.Guard, _showGuard, Icons.Guard);
            }
            else
            {
                int both = _counts[(int)Bucket.Boss] + _counts[(int)Bucket.Guard];
                AddRow("Boss / Guard", both, _showBoss, false, Icons.Boss);
            }
            AddRow("Goons", _counts[(int)Bucket.Goon], _showGoon, true, Icons.Goons);   // accent — a marquee target like the factions
            Row("Cultist", Bucket.Cultist, _showCultist, Icons.Cultist);
            Row("Infected", Bucket.Infected, _showInfected, Icons.Infected);
            Row("BTR", Bucket.Btr, _showBtr, Icons.Btr);

            FactionRows();
            Row("Other", Bucket.Other, _showOther, Icons.Other);
            TotalRow();
        }

        // A faction that fields a boss gets him and his escort on separate lines, on the same toggle the
        // vanilla bosses use — so you can see whether the man himself is still standing or you're just
        // mopping up guards. Factions that are a flat squad (Black Division, RUAF, UNTAR) have no boss to
        // separate and keep their single line.
        void FactionRows()
        {
            foreach (var faction in _factions)
            {
                FactionTally tally = faction.Value;
                if (_splitBoss.Value && MoreBotsBridge.FactionHasBoss(faction.Key))
                {
                    if (tally.Bosses > 0) _rows.Add(new CensusRow(faction.Key, tally.Bosses, true, Icons.ForFactionBoss(faction.Key)));
                    if (tally.Escorts > 0) _rows.Add(new CensusRow(faction.Key + " Guard", tally.Escorts, true, Icons.ForFactionGuard(faction.Key)));
                }
                else
                {
                    _rows.Add(new CensusRow(faction.Key, tally.Bosses + tally.Escorts, true, Icons.ForFaction(faction.Key)));
                }
            }
        }

        // Counted off the tallies rather than the rows above it, so a row you've hidden or merged doesn't
        // quietly change the total.
        void TotalRow()
        {
            if (_showTotal.Value == RowVis.Hidden) return;

            int total = 0;
            for (int i = 0; i < _counts.Length; i++) total += _counts[i];
            foreach (var faction in _factions) total += faction.Value.Bosses + faction.Value.Escorts;

            if (_showTotal.Value == RowVis.WhenPresent && total <= 0) return;
            _rows.Add(new CensusRow("Total Bots", total, false, Icons.Total, true));
        }

        void Row(string label, Bucket bucket, ConfigEntry<RowVis> vis, string icon)
        {
            AddRow(label, _counts[(int)bucket], vis, false, icon);
        }

        void AddRow(string label, int value, ConfigEntry<RowVis> vis, bool accent, string icon)
        {
            if (vis.Value == RowVis.Hidden) return;
            if (vis.Value == RowVis.WhenPresent && value <= 0) return;
            _rows.Add(new CensusRow(label, value, accent, icon));
        }

        void OnGUI()
        {
            if (!_enabled.Value || _fade <= 0f || !PaintEligible()) return;
            float alpha = _fade * (2f - _fade);   // ease-out quadratic — DOTween's default, the minimap's curve
            Hud.Draw(_rows, _fontSize.Value, _offsetRight.Value, _offsetTop.Value, _tarkovFont.Value,
                _bgOpacity.Value, alpha, _icons.Value, _iconScale.Value);
        }

        // The panel is allowed on screen once you're in a raid with the battle HUD up (not the menu/hideout,
        // and not behind a full-screen menu). Update() eases the fade in while this holds; OnGUI() draws while
        // it holds — one predicate keeps the two in lockstep.
        bool PaintEligible()
        {
            if (_onlyInRaid.Value && !_inRaid) return false;
            if (_inRaid && MenuCoveringHud()) return false;   // step aside for the inventory / menu screens
            return true;
        }

        // A full-screen menu (Tab inventory, map, trader, settings, death…) becomes the current screen and
        // hides the game's own battle HUD. Key off that same signal so the overlay isn't left sitting on top.
        static bool MenuCoveringHud()
        {
            var screens = CurrentScreenSingletonClass.Instance;
            return screens == null || !screens.CheckCurrentScreen(EEftScreenType.BattleUI);
        }
    }

    // A custom faction registers one role per rank. Splitting the tally at count time is what lets a boss
    // and his escort land on separate lines later.
    internal struct FactionTally
    {
        public int Bosses;
        public int Escorts;
    }

    public enum Bucket
    {
        Pmc,
        Scav,
        Raider,
        Rogue,
        Boss,
        Guard,
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
