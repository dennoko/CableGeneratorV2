using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 断面プロファイルの形を描く要素（見た目タブのプレビューとサムネイル。設計書 §5.3）。
    /// generateVisualContent + Painter2D で描くので軽く、値を変えるたびに MarkDirtyRepaint() で即時に描き直せる。
    /// 1 ループ = 1 本のワイヤー（GetVerticesPerLoop 個ずつ）。枠に収まるよう最大半径で正規化する。
    /// 線と塗りの色は USS の --cable-preview-line / --cable-preview-fill（CableGeneratorWindow.uss）から読む。
    /// </summary>
    internal sealed class ProfilePreviewElement : VisualElement
    {
        private static readonly CustomStyleProperty<Color> LineColorProperty = new CustomStyleProperty<Color>("--cable-preview-line");
        private static readonly CustomStyleProperty<Color> FillColorProperty = new CustomStyleProperty<Color>("--cable-preview-fill");

        private CableProfile _profile;
        private Color _line = new Color(1f, 1f, 1f, 0.9f);
        private Color _fill = new Color(0.39f, 0.71f, 0.96f, 0.16f);

        public float LineWidth { get; set; } = 1.5f;
        public float Padding { get; set; } = 8f;

        public CableProfile Profile
        {
            get => _profile;
            set
            {
                _profile = value;
                MarkDirtyRepaint();
            }
        }

        public ProfilePreviewElement()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("cable-profile-preview");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(LineColorProperty, out var line)) _line = line;
                if (e.customStyle.TryGetValue(FillColorProperty, out var fill)) _fill = fill;
                MarkDirtyRepaint();
            });
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_profile == null) return;
            Vector2[] verts;
            int perLoop;
            try
            {
                verts = _profile.GetVertices();
                perLoop = Mathf.Max(2, _profile.GetVerticesPerLoop());
            }
            catch
            {
                return;   // 編集途中の値で例外になっても UI は落とさない
            }
            if (verts == null || verts.Length < 2) return;

            float maxR = 0f;
            foreach (var v in verts) maxR = Mathf.Max(maxR, v.magnitude);
            if (maxR <= 1e-6f) return;

            Rect r = contentRect;
            float size = Mathf.Min(r.width, r.height) * 0.5f - Padding;
            if (size <= 1f) return;
            Vector2 center = r.center;
            float scale = size / maxR;
            Vector2 ToUI(Vector2 p) => center + new Vector2(p.x, -p.y) * scale;   // UI は Y が下向き

            var painter = context.painter2D;
            painter.lineWidth = LineWidth;
            painter.lineJoin = LineJoin.Round;
            painter.strokeColor = _line;
            painter.fillColor = _fill;

            int loops = Mathf.Max(1, verts.Length / perLoop);
            for (int loop = 0; loop < loops; loop++)
            {
                int start = loop * perLoop;
                int end = Mathf.Min(verts.Length, start + perLoop);
                if (end - start < 2) continue;

                painter.BeginPath();
                painter.MoveTo(ToUI(verts[start]));
                for (int i = start + 1; i < end; i++) painter.LineTo(ToUI(verts[i]));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }
        }
    }
}
