using UnityEditor;
using UnityEngine;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 提案 S5「シーン描画の色と線」の試作。Handles は USS の変数を読めないので colors.json の値を C# 定数で持つ。
    /// 線は「黒 0.5 の外線 + 色の内線」の 2 重にして、明るい背景でも暗い背景でも見えるようにする。
    /// </summary>
    internal static class DennokoSceneColors
    {
        public static readonly Color HoverAdd     = new Color(1f, 1f, 1f, 0.9f);                // accent
        public static readonly Color HoverRemove  = Hex(0xff6b6b, 0.95f);                        // semantic-error-text
        public static readonly Color SelectedFill = Hex(0x64b5f6, 0.35f);                        // selection
        public static readonly Color SelectedLine = Hex(0x64b5f6, 0.9f);
        public static readonly Color BrushInner   = new Color(1f, 1f, 1f, 0.8f);
        public static readonly Color BrushOuter   = new Color(1f, 1f, 1f, 0.3f);
        public static readonly Color InfluenceLow  = Hex(0x64b5f6, 0.25f);                       // selection (弱)
        public static readonly Color InfluenceHigh = Hex(0xffb74d, 0.9f);                        // warning (強)
        public static readonly Color Warning      = Hex(0xffb74d, 1f);
        public static readonly Color Structure    = new Color(1f, 1f, 1f, 0.14f);               // 補助線 (作業面の格子など)
        public static readonly Color Shadow       = new Color(0f, 0f, 0f, 0.5f);

        public const float Bold = 2f;   // ホバー・選択の縁
        public const float Thin = 1f;   // 補助線

        private static Color Hex(int rgb, float a) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        private static float Px(float width) => width * EditorGUIUtility.pixelsPerPoint;

        /// <summary>折れ線を 2 重線で描く。dotted は「外す」予告やミラー側に使う。</summary>
        public static void Polyline(Vector3[] points, Color color, float width = Bold, bool dotted = false)
        {
            var prev = Handles.color;
            if (dotted)
            {
                var segments = new Vector3[(points.Length - 1) * 2];
                for (int i = 0; i < points.Length - 1; i++)
                {
                    segments[i * 2] = points[i];
                    segments[i * 2 + 1] = points[i + 1];
                }
                Handles.color = Shadow;
                Handles.DrawDottedLines(segments, 5f);
                Handles.color = color;
                Handles.DrawDottedLines(segments, 4f);
            }
            else
            {
                Handles.color = Shadow;
                Handles.DrawAAPolyLine(Px(width + 2f), points);
                Handles.color = color;
                Handles.DrawAAPolyLine(Px(width), points);
            }
            Handles.color = prev;
        }

        /// <summary>四角形の縁 (閉じた折れ線)。</summary>
        public static void Quad(Vector3[] corners, Color color, float width = Bold, bool dotted = false)
        {
            Polyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] }, color, width, dotted);
        }

        /// <summary>円 (ブラシ・ミラー側のブラシ)。</summary>
        public static void Circle(Vector3 center, Vector3 normal, float radius, Color color, float width = Bold, bool dotted = false)
        {
            const int n = 48;
            var axisA = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var axisB = Vector3.Cross(normal, axisA).normalized;
            var pts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                pts[i] = center + (axisA * Mathf.Cos(a) + axisB * Mathf.Sin(a)) * radius;
            }
            Polyline(pts, color, width, dotted);
        }

        /// <summary>影響度 (0〜1) の色。selection (弱) → warning (強)。</summary>
        public static Color Influence(float weight) => Color.Lerp(InfluenceLow, InfluenceHigh, Mathf.Clamp01(weight));
    }
}
