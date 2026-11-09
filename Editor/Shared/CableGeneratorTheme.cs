using UnityEngine;
using UnityEditor;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// dennoko.dev カラースキーマに基づくテーマ定義 (CableGenerator V2 用)。
    /// </summary>
    internal static class CableGeneratorTheme
    {
        public static readonly Color Surface0 = Hex(0x121212);
        public static readonly Color Surface1 = Hex(0x1e1e1e);
        public static readonly Color Surface2 = Hex(0x2c2c2c);
        public static readonly Color Outline  = Hex(0x3a3a3a);

        public static readonly Color TextPrimary   = Hex(0xffffff);
        public static readonly Color TextSecondary = Hex(0xcccccc);
        public static readonly Color TextTertiary  = Hex(0xaaaaaa);
        public static readonly Color TextDisabled  = Hex(0x555555);
        public static readonly Color AccentActive  = new Color(0.22f, 0.52f, 0.32f);

        private static Texture2D _texSurface0;
        private static Texture2D _texSurface1;
        private static Texture2D _texSurface2;
        private static Texture2D _texCard;
        private static Texture2D _texAccentCard;
        private static Texture2D _texActiveToggle;
        private static Texture2D _texOverlayBar;

        private static bool _initialized;

        public static GUIStyle InspectorRootStyle    { get; private set; }
        public static GUIStyle CardStyle             { get; private set; }
        public static GUIStyle SectionHeaderStyle    { get; private set; }
        public static GUIStyle SecondaryTextStyle    { get; private set; }
        public static GUIStyle CaptionStyle          { get; private set; }
        public static GUIStyle ActionButtonStyle     { get; private set; }
        public static GUIStyle SecondaryButtonStyle  { get; private set; }
        public static GUIStyle ActiveToggleStyle     { get; private set; }
        public static GUIStyle DangerButtonStyle     { get; private set; }
        public static GUIStyle KnotRowStyle          { get; private set; }
        public static GUIStyle KnotRowSelectedStyle  { get; private set; }
        public static GUIStyle KnotLabelButtonStyle  { get; private set; }
        public static GUIStyle KnotDeleteButtonStyle { get; private set; }
        public static GUIStyle OverlayBarStyle       { get; private set; }

        public static void Initialize()
        {
            if (_initialized && _texSurface0 != null) return;
            if (!_initialized)
            {
                // HideAndDontSave テクスチャはドメインリロードで参照を失うとリークするため、リロード前に破棄する
                AssemblyReloadEvents.beforeAssemblyReload -= ReleaseTextures;
                AssemblyReloadEvents.beforeAssemblyReload += ReleaseTextures;
            }
            _initialized = true;
            EnsureTextures();
            BuildStyles();
        }

        static void ReleaseTextures()
        {
            foreach (var tex in new[] { _texSurface0, _texSurface1, _texSurface2, _texCard, _texAccentCard, _texActiveToggle, _texOverlayBar })
                if (tex != null) Object.DestroyImmediate(tex);
        }

        private static void EnsureTextures()
        {
            if (!_texSurface0)     _texSurface0     = MakeTex(Surface0);
            if (!_texSurface1)     _texSurface1     = MakeTex(Surface1);
            if (!_texSurface2)     _texSurface2     = MakeTex(Surface2);
            if (!_texCard)         _texCard         = MakeBorderedTex(Surface1, Outline);
            if (!_texAccentCard)   _texAccentCard   = MakeBorderedTex(Surface2, Outline);
            if (!_texActiveToggle) _texActiveToggle = MakeBorderedTex(AccentActive, new Color(0.35f, 0.8f, 0.5f));
            if (!_texOverlayBar)   _texOverlayBar   = MakeBorderedTex(new Color(0.10f, 0.10f, 0.10f, 0.92f), Outline);
        }

        private static void BuildStyles()
        {
            InspectorRootStyle = new GUIStyle
            {
                normal   = { background = _texSurface0 },
                margin   = new RectOffset(0, 0, 0, 0),
                padding  = new RectOffset(10, 10, 8, 8),
                overflow = new RectOffset(20, 20, 0, 0),
            };

            CardStyle = new GUIStyle
            {
                normal  = { background = _texCard },
                border  = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 8, 8),
                margin  = new RectOffset(0, 0, 0, 10),
            };

            OverlayBarStyle = new GUIStyle
            {
                normal  = { background = _texOverlayBar },
                border  = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(8, 8, 5, 5),
            };

            SectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal   = { textColor = TextTertiary },
                margin   = new RectOffset(0, 0, 0, 2),
            };

            SecondaryTextStyle = new GUIStyle(EditorStyles.label)
            {
                normal   = { textColor = TextSecondary },
                wordWrap = true,
            };

            CaptionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = TextTertiary },
            };

            ActionButtonStyle = new GUIStyle
            {
                normal      = { background = _texAccentCard, textColor = TextPrimary },
                hover       = { background = MakeTex(Color.Lerp(Surface2, Color.white, 0.07f)), textColor = TextPrimary },
                active      = { background = MakeTex(Color.Lerp(Surface2, Color.white, 0.15f)), textColor = TextPrimary },
                border      = new RectOffset(1, 1, 1, 1),
                margin      = new RectOffset(4, 4, 2, 2),
                padding     = new RectOffset(6, 6, 2, 2),
                fontSize    = 12,
                fontStyle   = FontStyle.Bold,
                fixedHeight = 28,
                alignment   = TextAnchor.MiddleCenter,
            };

            SecondaryButtonStyle = new GUIStyle
            {
                normal      = { background = MakeBorderedTex(Surface1, Outline), textColor = TextSecondary },
                hover       = { background = _texAccentCard, textColor = TextPrimary },
                active      = { background = MakeTex(Color.Lerp(Surface1, Color.white, 0.10f)), textColor = TextPrimary },
                border      = new RectOffset(1, 1, 1, 1),
                margin      = new RectOffset(2, 2, 2, 2),
                padding     = new RectOffset(6, 6, 2, 2),
                fontSize    = 11,
                fixedHeight = 24,
                alignment   = TextAnchor.MiddleCenter,
            };

            ActiveToggleStyle = new GUIStyle(SecondaryButtonStyle)
            {
                normal    = { background = _texActiveToggle, textColor = TextPrimary },
                hover     = { background = _texActiveToggle, textColor = TextPrimary },
                fontStyle = FontStyle.Bold,
            };

            DangerButtonStyle = new GUIStyle
            {
                normal      = { background = MakeBorderedTex(Surface1, Outline), textColor = new Color(0.80f, 0.35f, 0.35f) },
                hover       = { background = MakeTex(new Color(0.35f, 0.08f, 0.08f)), textColor = TextPrimary },
                active      = { background = MakeTex(new Color(0.50f, 0.12f, 0.12f)), textColor = TextPrimary },
                border      = new RectOffset(1, 1, 1, 1),
                margin      = new RectOffset(2, 2, 2, 2),
                padding     = new RectOffset(2, 2, 2, 2),
                fontSize    = 11,
                fixedHeight = 20,
                alignment   = TextAnchor.MiddleCenter,
            };

            KnotRowStyle = new GUIStyle
            {
                normal  = { background = _texCard },
                border  = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(8, 8, 4, 4),
                margin  = new RectOffset(0, 0, 0, 3),
            };

            KnotRowSelectedStyle = new GUIStyle(KnotRowStyle)
            {
                normal = { background = _texAccentCard },
            };

            KnotLabelButtonStyle = new GUIStyle(CaptionStyle)
            {
                alignment = TextAnchor.MiddleLeft,
            };

            KnotDeleteButtonStyle = new GUIStyle(DangerButtonStyle)
            {
                fontSize  = 14,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(0, 0, 0, 0),
            };
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D MakeBorderedTex(Color fillColor, Color borderColor)
        {
            const int size = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y,
                        (x == 0 || x == size - 1 || y == 0 || y == size - 1)
                            ? borderColor
                            : fillColor);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >>  8) & 0xFF) / 255f,
            ( rgb        & 0xFF) / 255f);
    }
}

