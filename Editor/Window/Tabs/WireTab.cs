using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>配線タブ — どこを通すか（設計書 §5.1）。</summary>
    internal sealed class WireTab : CableTab
    {
        protected override string UxmlGuid => "67dfb2be75b543918b3e802e8ed9edf4";   // WireTab.uxml

        private Button _tilePick, _tileDraw, _restart, _snapRun;
        private Label _pickDesc, _drawDesc, _knotEmpty;
        private Toggle _loop;
        private KnotListView _knots;
        private Vector3Field _snapCustom;
        private Action<int> _snapDirection, _drawKnotMode, _addedMode;
        private VisualElement _methodChips, _moreChips, _snapChips;
        private string _chipsKey;

        protected override void Initialize()
        {
            var s = Settings;

            // ─── 引き方 ───
            _tilePick = OnClick("tile-pick", CableSceneHandles.TogglePicking);
            _tileDraw = OnClick("tile-draw", CableSceneHandles.ToggleDrawing);
            _pickDesc = Q<Label>("tile-pick-desc");
            _drawDesc = Q<Label>("tile-draw-desc");
            _restart = Q<Button>("pick-restart");
            _restart.clicked += CableSceneHandles.RestartPicking;

            BindLayerMask(Q<LayerMaskField>("hit-layers"), () => s.hitLayerMask, v => s.hitLayerMask = v);
            DennokoBlockUI.BindDetails(Q("method-details"));
            _methodChips = Q("method-chips");
            BindSetting(Q<Slider>("pick-tangent"), () => s.pickTangentScale, v =>
            {
                s.pickTangentScale = v;
                if (Gen != null) CableCommands.ApplyTangentScale(Gen, v);   // 今のケーブルの手動の接線にもすぐ反映する
            });
            BindSetting(Q<FloatField>("draw-offset"), () => s.drawSurfaceOffset, v => s.drawSurfaceOffset = Mathf.Max(0f, v));
            BindSetting(Q<Slider>("draw-simplify"), () => s.drawSimplifyTol, v => s.drawSimplifyTol = v);
            BindSetting(Q<Slider>("draw-spacing"), () => s.drawMinPixelSpacing, v => s.drawMinPixelSpacing = v);
            _drawKnotMode = Choice("draw-knot-mode", i => { s.drawKnotMode = i; s.Save(); });

            // ─── 制御点 ───
            DennokoIcons.Apply(OnClick("knot-add", CableCommands.AddKnot), DennokoIcons.Plus, "+");
            DennokoIcons.Apply(OnClick("knot-flip", CableCommands.Flip), "d_RotateTool|RotateTool", "⇄");
            DennokoIcons.Apply(OnClick("knot-even", CableCommands.RedistributeEvenly), "d_align_horizontally_center|align_horizontally_center", "⟺");
            _loop = Q<Toggle>("knot-loop");
            _loop.RegisterValueChangedCallback(e =>
            {
                if (Gen == null) return;
                CableCommands.SetClosed(Gen, e.newValue);
                Window.RefreshAll();
            });
            _knots = new KnotListView(Q("knot-list"), () => Gen, FrameKnot);
            _knotEmpty = Q<Label>("knot-empty");

            DennokoBlockUI.BindDetails(Q("more-details"));
            _moreChips = Q("more-chips");
            BindSetting(Q<SliderInt>("divisions"), () => s.divisionCount, v => s.divisionCount = Mathf.Max(1, v));
            _addedMode = Choice("added-mode", i => { s.addedKnotMode = i; s.Save(); });
            OnClick("divide", gen => Report(CableCommands.DivideEndpoints(gen, s.divisionCount, CableCommands.FromSimpleIndex(s.addedKnotMode))));
            OnClick("subdivide", gen => Report(CableCommands.Subdivide(gen, CableCommands.FromSimpleIndex(s.addedKnotMode))));

            // ─── 面に吸着 ───
            _snapDirection = Choice("snap-direction", i => { s.snapDirectionMode = i; s.Save(); });
            _snapCustom = Q<Vector3Field>("snap-custom");
            BindSetting(_snapCustom, () => s.snapCustomDirection, v => s.snapCustomDirection = v);
            BindSetting(Q<FloatField>("snap-distance"), () => s.snapMaxDistance, v => s.snapMaxDistance = Mathf.Max(0f, v));
            _snapRun = OnClick("snap-run", gen => Report(CableCommands.SnapKnots(gen, CableKnotSelection.Sorted(gen.Spline.Count))));
            DennokoBlockUI.BindDetails(Q("snap-details"));
            _snapChips = Q("snap-chips");
            BindSetting(Q<FloatField>("snap-offset"), () => s.snapSurfaceOffset, v => s.snapSurfaceOffset = Mathf.Max(0f, v));
            BindSetting(Q<Toggle>("snap-local"), () => s.snapDirectionIsLocal, v => s.snapDirectionIsLocal = v);
            BindLayerMask(Q<LayerMaskField>("snap-layers"), () => s.snapLayerMask, v => s.snapLayerMask = v);
        }

        public override void Refresh()
        {
            var gen = Gen;
            var s = Settings;
            bool hasTarget = gen != null && gen.Spline != null;
            Root.SetEnabled(hasTarget);

            // 引き方: 作業中のタイルに selection 色の枠。説明は次の操作に変わる
            bool picking = CableSceneHandles.IsPicking(gen);
            bool drawing = CableSceneHandles.IsDrawing(gen);
            _tilePick.EnableInClassList(DennokoBlockUI.ActiveClass, picking);
            _tileDraw.EnableInClassList(DennokoBlockUI.ActiveClass, drawing);
            _pickDesc.text = picking ? CableSceneHandles.GuideText(gen) : Tr("wire.pick.desc");
            _drawDesc.text = drawing ? CableSceneHandles.GuideText(gen) : Tr("wire.draw.desc");
            Show(_restart, picking);
            _drawKnotMode(s.drawKnotMode);

            // 制御点
            _knots.Refresh();
            if (hasTarget) _loop.SetValueWithoutNotify(gen.Spline.Closed);
            Show(_knotEmpty, hasTarget && gen.Spline.Count == 0);
            _addedMode(s.addedKnotMode);

            // 面に吸着
            _snapDirection(s.snapDirectionMode);
            Show(_snapCustom, s.snapDirectionMode == 2);
            int selected = hasTarget && CableKnotSelection.Owner == gen ? CableKnotSelection.Sorted(gen.Spline.Count).Count : 0;
            _snapRun.text = Tr("wire.snapRun", selected);
            _snapRun.SetEnabled(selected > 0);
            _snapRun.tooltip = selected > 0 ? Tr("wire.snapRun.tip") : Tr("msg.selectKnotsFirst");

            RefreshChips(s);
        }

        /// <summary>閉じている「詳細」に、初期値から変えた値だけチップで出す（スキル B2）。</summary>
        private void RefreshChips(CableToolSettings s)
        {
            string key = $"{English}|{s.pickTangentScale}|{s.drawSurfaceOffset}|{s.drawSimplifyTol}|{s.drawMinPixelSpacing}|{s.drawKnotMode}|" +
                         $"{s.divisionCount}|{s.addedKnotMode}|{s.snapSurfaceOffset}|{s.snapDirectionIsLocal}|{s.snapLayerMask}";
            if (key == _chipsKey) return;
            _chipsKey = key;

            _methodChips.Clear();
            if (!Mathf.Approximately(s.pickTangentScale, 1f)) _methodChips.Add(DennokoBlockUI.Chip(null, $"×{s.pickTangentScale:0.##}", Tr("wire.tangentScale")));
            if (!Mathf.Approximately(s.drawSurfaceOffset, 0.003f)) _methodChips.Add(DennokoBlockUI.Chip(null, $"{s.drawSurfaceOffset * 1000f:0.#}mm", Tr("wire.surfaceOffset")));
            if (s.drawKnotMode != 0) _methodChips.Add(DennokoBlockUI.Chip(null, Tr("mode.smooth"), Tr("wire.drawKnotMode")));

            _moreChips.Clear();
            _moreChips.Add(DennokoBlockUI.Chip(null, Tr("wire.divisions.chip", s.divisionCount), Tr("wire.divisions")));
            if (s.addedKnotMode != 0) _moreChips.Add(DennokoBlockUI.Chip(null, Tr(s.addedKnotMode == 1 ? "mode.smooth" : "mode.corner"), Tr("wire.addedMode")));

            _snapChips.Clear();
            if (!Mathf.Approximately(s.snapSurfaceOffset, 0.003f)) _snapChips.Add(DennokoBlockUI.Chip(null, $"{s.snapSurfaceOffset * 1000f:0.#}mm", Tr("wire.surfaceOffset")));
            if (s.snapDirectionIsLocal) _snapChips.Add(DennokoBlockUI.Chip(null, Tr("wire.dir.local"), Tr("wire.dir.local.tip")));
        }

        private void FrameKnot(int index)
        {
            var gen = Gen;
            var view = SceneView.lastActiveSceneView;
            if (gen == null || view == null || index < 0 || index >= gen.Spline.Count) return;
            Vector3 world = gen.transform.TransformPoint(gen.Spline[index].position);
            view.Frame(new Bounds(world, Vector3.one * 0.3f), false);
        }
    }
}
