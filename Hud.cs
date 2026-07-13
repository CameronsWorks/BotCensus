using System;
using System.Collections.Generic;
using UnityEngine;

namespace BotCensus
{
    internal struct CensusRow
    {
        public string Label;
        public int Value;
        public bool Accent;   // faction rows are drawn in the accent colour

        public CensusRow(string label, int value, bool accent)
        {
            Label = label;
            Value = value;
            Accent = accent;
        }
    }

    // Tarkov-styled IMGUI overlay; uses the game's Bender font when it's loaded.
    internal static class Hud
    {
        static readonly Color Background = new Color(0.055f, 0.055f, 0.050f, 1.00f);  // alpha set per-draw from config
        static readonly Color Border     = new Color(0.62f, 0.57f, 0.44f, 0.35f);
        static readonly Color Accent     = new Color(0.78f, 0.65f, 0.42f, 1.00f);  // EFT amber / tan
        static readonly Color LabelColor = new Color(0.72f, 0.70f, 0.62f, 1.00f);  // muted khaki
        static readonly Color ValueColor = new Color(0.88f, 0.86f, 0.80f, 1.00f);  // warm white

        static readonly GUIStyle _title = new GUIStyle();
        static readonly GUIStyle _label = new GUIStyle();
        static readonly GUIStyle _value = new GUIStyle();

        static Texture2D _pixel;
        static Font _font;
        static bool _fontSearched;

        public static void Draw(List<CensusRow> rows, int fontSize, int offsetRight, int offsetTop, bool useTarkovFont, float bgOpacity)
        {
            if (rows.Count == 0) return;

            Font font = useTarkovFont ? ResolveFont() : null;
            int pad = Mathf.RoundToInt(fontSize * 0.7f);
            int rowHeight = fontSize + 8;
            int titleSize = Mathf.Max(10, fontSize - 3);
            int titleHeight = titleSize + 10;
            int width = Mathf.RoundToInt(fontSize * 12.5f) + pad * 2;
            int height = titleHeight + rows.Count * rowHeight + pad;

            float x = Screen.width - width - offsetRight;
            float y = offsetTop;
            var panel = new Rect(x, y, width, height);

            Color bg = Background;
            bg.a = bgOpacity;
            DrawRect(panel, bg);
            DrawBorder(panel, Border);
            DrawRect(new Rect(panel.x, panel.y, 2f, panel.height), Accent);   // left accent strip

            Configure(_title, font, titleSize, FontStyle.Bold, TextAnchor.MiddleLeft, Accent);
            Configure(_label, font, fontSize, FontStyle.Normal, TextAnchor.MiddleLeft, LabelColor);
            Configure(_value, font, fontSize, FontStyle.Bold, TextAnchor.MiddleRight, ValueColor);

            float left = panel.x + pad + 3f;
            float right = panel.x + width - pad;
            float innerWidth = right - left;

            GUI.Label(new Rect(left, y + 4f, innerWidth, titleHeight), "RAID CENSUS", _title);
            DrawRect(new Rect(left, y + titleHeight + 2f, innerWidth, 1f), Border);

            float rowTop = y + titleHeight + 6f;
            for (int i = 0; i < rows.Count; i++)
            {
                CensusRow r = rows[i];
                var rect = new Rect(left, rowTop + i * rowHeight, innerWidth, rowHeight);
                _label.normal.textColor = r.Accent ? Accent : LabelColor;
                _value.normal.textColor = r.Accent ? Accent : ValueColor;
                GUI.Label(rect, r.Label.ToUpperInvariant(), _label);
                GUI.Label(rect, r.Value.ToString(), _value);
            }
        }

        static void Configure(GUIStyle style, Font font, int size, FontStyle fontStyle, TextAnchor anchor, Color color)
        {
            style.font = font;              // null lets Unity use its default IMGUI font
            style.fontSize = size;
            style.fontStyle = fontStyle;
            style.alignment = anchor;
            style.normal.textColor = color;
            style.richText = false;
        }

        // Prefer EFT's own Bender font (already loaded in the game). If it can't be found, fall back
        // to a DIN-like Windows face that reads close to Bender rather than the default Arial.
        static Font ResolveFont()
        {
            if (_font != null || _fontSearched) return _font;
            _fontSearched = true;

            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f == null || string.IsNullOrEmpty(f.name)) continue;
                if (f.name.IndexOf("Bender", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _font = f;
                    break;
                }
            }

            if (_font == null)
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Bahnschrift", "Bahnschrift SemiBold", "DIN", "Oswald" }, 16);

            Debug.Log("[Bot Census] overlay font: " + (_font != null ? _font.name : "Unity default"));
            return _font;
        }

        static Texture2D Pixel()
        {
            if (_pixel == null)
            {
                _pixel = new Texture2D(1, 1);
                _pixel.SetPixel(0, 0, Color.white);
                _pixel.Apply();
                _pixel.hideFlags = HideFlags.HideAndDontSave;
            }
            return _pixel;
        }

        static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Pixel());
            GUI.color = previous;
        }

        static void DrawBorder(Rect r, Color color)
        {
            DrawRect(new Rect(r.x, r.y, r.width, 1f), color);
            DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), color);
            DrawRect(new Rect(r.x, r.y, 1f, r.height), color);
            DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), color);
        }
    }
}
