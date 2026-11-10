using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal sealed partial class ShapeTab
    {
        // ================================================================
        //  たるみ（全体 / 区間ごと / V1 互換のたわみ制御点）と、床や物に沿わせる
        // ================================================================

        private Slider _sagAll, _sagDrop, _sagHandle;
        private Label _sagMixed;
        private VisualElement _sagSegments, _sagAdjust, _sagChips;
        private Toggle _sagMirrored, _collisionToggle;
        private VisualElement _collisionContent;
        private readonly List<Slider> _segmentSliders = new List<Slider>();
        private string _sagChipsKey;
        private bool _segmentsEnglish;

        private void InitializeSag()
        {
            _sagAll = Q<Slider>("sag-all");
            DennokoSceneUI.BindUndoSlider(_sagAll, Tr("undo.sag"), v => { if (Gen != null) CableCommands.SetAllSag(Gen, v); });
            _sagMixed = Q<Label>("sag-mixed");
            var reset = DennokoIcons.Apply(Q<Button>("sag-reset"), DennokoIcons.Refresh, "↺");
            reset.clicked += () =>
            {
                if (Gen == null) return;
                CableCommands.SetAllSag(Gen, 0f);   // Undo で戻せるので確認は出さない（スキル B2 の ↺）
                Window.RefreshAll();
            };

            DennokoBlockUI.BindDetails(Q("sag-details"));
            _sagChips = Q("sag-chips");
            _sagSegments = Q("sag-segments");
            OnClick("sag-insert", gen => Report(CableCommands.InsertSagKnots(gen)));
            _sagAdjust = Q("sag-adjust");

            _sagDrop = Q<Slider>("sag-drop");
            _sagDrop.SetValueWithoutNotify(CableCommands.SagDropDistance);
            DennokoSceneUI.BindUndoSlider(_sagDrop, Tr("undo.sagKnots"), v =>
            {
                CableCommands.SagDropDistance = v;
                CableCommands.UpdateSagKnots(Gen);
            });
            _sagMirrored = Q<Toggle>("sag-mirrored");
            _sagMirrored.SetValueWithoutNotify(CableCommands.SagUseMirrored);
            _sagMirrored.RegisterValueChangedCallback(e =>
            {
                CableCommands.SagUseMirrored = e.newValue;
                CableCommands.UpdateSagKnots(Gen);
                Window.RefreshAll();
            });
            _sagHandle = Q<Slider>("sag-handle");
            _sagHandle.SetValueWithoutNotify(CableCommands.SagHandleLength);
            DennokoSceneUI.BindUndoSlider(_sagHandle, Tr("undo.sagKnots"), v =>
            {
                CableCommands.SagHandleLength = v;
                CableCommands.UpdateSagKnots(Gen);
            });

            _collisionToggle = Q<Toggle>("collision-toggle");
            _collisionContent = Q("collision-content");
            BindToggleSection(_collisionToggle, _collisionContent);
        }

        private void RefreshSag(CableGenerator gen)
        {
            var spline = gen.Spline;
            bool uniform = CableCommands.TryGetUniformSag(gen, out float sag);
            if (uniform) SetValueIfChanged(_sagAll, sag);
            Show(_sagMixed, !uniform);
            _sagAll.SetEnabled(spline.SegmentCount > 0);

            // 区間ごとのスライダー（区間の数が変わったときだけ作り直す）
            int segCount = spline.SegmentCount;
            if (_segmentSliders.Count != segCount || _segmentsEnglish != English)
            {
                _segmentsEnglish = English;
                _sagSegments.Clear();
                _segmentSliders.Clear();
                for (int i = 0; i < segCount; i++)
                {
                    int segment = i;
                    var slider = new Slider(Tr("shape.segment", i, (i + 1) % spline.Count), 0f, 3f) { showInputField = true };
                    DennokoSceneUI.BindUndoSlider(slider, Tr("undo.sag"), v => { if (Gen != null) CableCommands.SetSegmentSag(Gen, segment, v); });
                    _segmentSliders.Add(slider);
                    _sagSegments.Add(slider);
                }
            }
            for (int i = 0; i < segCount; i++) SetValueIfChanged(_segmentSliders[i], spline.GetSegmentSag(i));

            bool adjusting = CableCommands.IsAdjustingSagKnots(gen);
            Show(_sagAdjust, adjusting);
            _sagHandle.SetEnabled(CableCommands.SagUseMirrored);

            string key = $"{English}|{uniform}|{segCount}";
            if (key != _sagChipsKey)
            {
                _sagChipsKey = key;
                _sagChips.Clear();
                if (!uniform) _sagChips.Add(DennokoBlockUI.Chip(null, Tr("shape.sag.chip.mixed"), Tr("shape.sag.mixed")));
            }

            // トグル付きセクション: バインドの値でも折りたたみを合わせる
            _collisionContent.parent.EnableInClassList("dennoko-toggle-section--collapsed", !gen.enableSurfaceCollision);
        }

        public override void Bind(CableGenerator gen, UnityEditor.SerializedObject so)
        {
            // 区間スライダーのラベル（番号）を対象ごとに作り直す
            _sagSegments?.Clear();
            _segmentSliders.Clear();
            // アタッチメントの行も作り直す（ウィンドウの Unbind で変更の監視が外れるため）
            _attachKey = null;
        }
    }
}
