using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 制御点タブ — 点ごとの調整（設計書 §5.2）。たるみと床判定は ShapeTab.Sag.cs、アタッチメントは ShapeTab.Attachments.cs。
    /// クラス名は旧タブ名「形」のまま。
    /// </summary>
    internal sealed partial class ShapeTab : CableTab
    {
        protected override string UxmlGuid => "d884f3b559d1494d8fab9bf9f81639a4";   // ShapeTab.uxml

        private Label _title, _autoHint;
        private Button _prev, _next, _multiDelete;
        private VisualElement _empty, _single, _multi;
        private Vector3Field _position, _rotation;
        private Slider _length, _lengthIn, _lengthOut, _segmentSag;
        private Action<int> _space, _mode, _multiMode;

        /// <summary>単体選択のときの制御点。無ければ -1。</summary>
        private int SingleIndex
        {
            get
            {
                var sel = Selected();
                return sel.Count == 1 ? sel[0] : -1;
            }
        }

        private List<int> Selected()
        {
            var gen = Gen;
            return gen != null && gen.Spline != null && CableKnotSelection.Owner == gen
                ? CableKnotSelection.Sorted(gen.Spline.Count)
                : new List<int>();
        }

        protected override void Initialize()
        {
            _title = Q<Label>("sel-title");
            _prev = DennokoIcons.Apply(Q<Button>("sel-prev"), "d_back|back", "◀");
            _next = DennokoIcons.Apply(Q<Button>("sel-next"), "d_forward|forward", "▶");
            _prev.clicked += () => Step(-1);
            _next.clicked += () => Step(1);
            _empty = Q("sel-empty");
            _single = Q("sel-single");
            _multi = Q("sel-multi");

            _position = Q<Vector3Field>("sel-position");
            _position.RegisterValueChangedCallback(e => EditKnot("undo.moveKnot", (gen, k) =>
            {
                k.position = Settings.useGlobalKnotCoords ? gen.transform.InverseTransformPoint(e.newValue) : e.newValue;
                return k;
            }));

            _space = Choice("sel-space", i => { Settings.useGlobalKnotCoords = i == 0; Settings.Save(); });
            _mode = Choice("sel-mode", i => { if (Gen != null) CableCommands.SetTangentMode(Gen, Selected(), i); });
            _multiMode = Choice("sel-multi-mode", i => { if (Gen != null) CableCommands.SetTangentMode(Gen, Selected(), i); });

            _length = Q<Slider>("sel-length");
            DennokoSceneUI.BindUndoSlider(_length, Tr("undo.tangentLength"), v => EditKnot(null, (_, k) =>
            {
                k.tangentOut = Dir(k.tangentOut, Vector3.forward) * v;
                k.tangentIn = Dir(k.tangentIn, Vector3.back) * v;
                return k;
            }));
            _lengthIn = Q<Slider>("sel-length-in");
            DennokoSceneUI.BindUndoSlider(_lengthIn, Tr("undo.tangentLength"), v => EditKnot(null, (_, k) =>
            {
                k.tangentIn = Dir(k.tangentIn, Vector3.back) * v;
                return k;
            }));
            _lengthOut = Q<Slider>("sel-length-out");
            DennokoSceneUI.BindUndoSlider(_lengthOut, Tr("undo.tangentLength"), v => EditKnot(null, (_, k) =>
            {
                k.tangentOut = Dir(k.tangentOut, Vector3.forward) * v;
                return k;
            }));
            _autoHint = Q<Label>("sel-auto-hint");

            _rotation = Q<Vector3Field>("sel-rotation");
            _rotation.RegisterValueChangedCallback(e => EditKnot("undo.rotateKnot", (gen, k) =>
            {
                k.rotation = Settings.useGlobalKnotCoords
                    ? Quaternion.Inverse(gen.transform.rotation) * Quaternion.Euler(e.newValue)
                    : Quaternion.Euler(e.newValue);
                return k;
            }));
            var rotationReset = DennokoIcons.Apply(Q<Button>("sel-rotation-reset"), "d_Refresh|Refresh", "0");
            rotationReset.clicked += () => EditKnot("undo.rotateKnot", (gen, k) =>
            {
                k.rotation = Settings.useGlobalKnotCoords ? Quaternion.Inverse(gen.transform.rotation) : Quaternion.identity;
                return k;
            });

            _segmentSag = Q<Slider>("sel-sag");
            DennokoSceneUI.BindUndoSlider(_segmentSag, Tr("undo.sag"), v =>
            {
                if (Gen != null && SingleIndex >= 0) CableCommands.SetSegmentSag(Gen, SingleIndex, v);
            });

            _multiDelete = OnClick("sel-multi-delete", gen => Report(CableCommands.DeleteKnots(gen, Selected())));

            InitializeSag();
            InitializeAttachments();
        }

        public override void Refresh()
        {
            var gen = Gen;
            bool hasTarget = gen != null && gen.Spline != null;
            Root.SetEnabled(hasTarget);
            if (!hasTarget) return;

            var selected = Selected();
            _title.text = selected.Count == 0 ? Tr("shape.sel.title")
                : selected.Count == 1 ? Tr("shape.sel.one", selected[0])
                : Tr("shape.sel.many", selected.Count);
            _prev.SetEnabled(gen.Spline.Count > 0);
            _next.SetEnabled(gen.Spline.Count > 0);
            Show(_empty, selected.Count == 0);
            Show(_single, selected.Count == 1);
            Show(_multi, selected.Count > 1);

            if (selected.Count == 1) RefreshSingle(gen, selected[0]);
            else if (selected.Count > 1)
            {
                _multiMode(CableCommands.CommonSimpleMode(gen, selected));
                _multiDelete.text = Tr("shape.multi.delete", selected.Count);
                _multiDelete.SetEnabled(CableCommands.CanDelete(gen, selected.Count));
            }

            RefreshAttachments(gen, selected);
            RefreshSag(gen);
        }

        private void RefreshSingle(CableGenerator gen, int index)
        {
            var knot = gen.Spline[index];
            var tf = gen.transform;
            bool world = Settings.useGlobalKnotCoords;
            _space(world ? 0 : 1);

            if (!IsEditing(_position)) SetValueIfChanged(_position, Round(world ? tf.TransformPoint(knot.position) : knot.position));
            if (!IsEditing(_rotation))
                SetValueIfChanged(_rotation, Round((world ? tf.rotation * knot.rotation : knot.rotation).eulerAngles, 2));

            int mode = CableCommands.ToSimpleIndex(knot.mode);
            _mode(mode);
            Show(_length, mode == 1);
            Show(_lengthIn, mode == 2);
            Show(_lengthOut, mode == 2);
            Show(_autoHint, mode == 0);
            SetValueIfChanged(_length, Mathf.Max(0.01f, knot.tangentOut.magnitude));
            SetValueIfChanged(_lengthIn, Mathf.Max(0.01f, knot.tangentIn.magnitude));
            SetValueIfChanged(_lengthOut, Mathf.Max(0.01f, knot.tangentOut.magnitude));

            bool hasSegment = index < gen.Spline.SegmentCount;
            Show(_segmentSag, hasSegment);
            if (hasSegment) SetValueIfChanged(_segmentSag, gen.Spline.GetSegmentSag(index));
        }

        private void Step(int delta)
        {
            var gen = Gen;
            if (gen == null || gen.Spline.Count == 0) return;
            var sel = Selected();
            int current = sel.Count > 0 ? (delta < 0 ? sel[0] : sel[sel.Count - 1]) : CableKnotSelection.Active;
            int next = sel.Count == 0 ? current : current + delta;
            int count = gen.Spline.Count;
            next = gen.Spline.Closed ? (next % count + count) % count : Mathf.Clamp(next, 0, count - 1);
            CableKnotSelection.SelectSingle(gen, next);
        }

        /// <summary>単体選択の制御点を書き換える。undoKey が null のときは呼び出し側（BindUndoSlider）が Undo グループを持つ。</summary>
        private void EditKnot(string undoKey, Func<CableGenerator, CableKnot, CableKnot> edit)
        {
            var gen = Gen;
            int index = SingleIndex;
            if (gen == null || index < 0) return;
            Undo.RecordObject(gen, Tr(undoKey ?? "undo.editKnot"));
            gen.Spline.SetKnot(index, edit(gen, gen.Spline[index]));
            EditorUtility.SetDirty(gen);
            SceneView.RepaintAll();
        }

        private static Vector3 Dir(Vector3 v, Vector3 fallback) => v.sqrMagnitude > 1e-6f ? v.normalized : fallback;

        /// <summary>欄に入力中なら外から値を書き換えない（カーソルが飛ばないように）。</summary>
        private static bool IsEditing(VisualElement field)
        {
            var focused = field?.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && field.Contains(focused);
        }
    }
}
