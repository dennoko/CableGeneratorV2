using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal static partial class CableSceneHandles
    {
        // ================================================================
        //  通常の編集 — 球は常に表示、XYZ の矢印は選択中だけ（複数選択は中心に 1 つ）
        // ================================================================

        static void HandleEdit(CableGenerator gen)
        {
            var spline = gen.Spline;
            Transform tf = gen.transform;
            Vector3 localGravity = tf.InverseTransformVector(Vector3.down);
            Event e = Event.current;

            int count = spline.Count;
            CableKnotSelection.Validate(gen, count);

            // 矩形選択の既定コントロール。ID は他のハンドルより先に取り、処理は最後に回す（ハンドルやボタンを優先する）
            int rectSelectId = GUIUtility.GetControlID(s_rectSelectHash, FocusType.Passive);

            // プレビュー線は描画イベントでのみ評価する（Layout / マウスイベント毎の曲線評価を省く）
            if (e.type == EventType.Repaint) DrawSplinePreview(spline, tf, localGravity);
            DrawSegmentMidpointGizmos(gen, spline, tf, localGravity);

            bool removePreview = IsRemoveModifier(e);
            for (int i = 0; i < count; i++)
            {
                var knot = spline[i];
                Vector3 worldPos = tf.TransformPoint(knot.position);
                float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.1f;
                bool isSelected = CableKnotSelection.Contains(i);

                // 番号ラベル ([0]▶ / ◀[N] / [i])
                string label = i == 0 ? $"[{i}]▶" : (!spline.Closed && i == count - 1) ? $"◀[{i}]" : $"[{i}]";
                Handles.Label(worldPos + Vector3.up * (handleSize * 2.2f), label,
                    isSelected ? s_indexLabelSelectedStyle : s_indexLabelStyle);

                // 制御点の球: 常に表示。クリックで単体選択、Shift / Ctrl + クリックで足す / 外す
                Handles.color = IsInDragRect(worldPos)
                    ? (removePreview ? DennokoSceneColors.HoverRemove : DennokoSceneColors.HoverAdd)
                    : isSelected ? DennokoSceneColors.SelectedLine
                    : KnotColor(knot.mode);

                if (Handles.Button(worldPos, Quaternion.identity, handleSize * 1.8f, handleSize * 2.3f, Handles.SphereHandleCap))
                {
                    if (e.shift || e.control || e.command) CableKnotSelection.Toggle(gen, i);
                    else CableKnotSelection.SelectSingle(gen, i);
                }

                // 手動モードのときだけベジェの接線ハンドルを出す（自動のときは隠す）
                if (knot.mode != CableTangentMode.AutoSmooth)
                {
                    DrawTangentHandle(gen, spline, tf, i, knot, true);
                    DrawTangentHandle(gen, spline, tf, i, knot, false);
                }
            }

            DrawSelectionMoveHandle(gen, spline, tf);
            HandleRectSelection(gen, spline, tf, rectSelectId);
        }

        // ─── 選択中の制御点を 1 つの矢印でまとめて動かす ──────────────

        static void DrawSelectionMoveHandle(CableGenerator gen, CableSpline spline, Transform tf)
        {
            List<int> selected = CableKnotSelection.Sorted(spline.Count);
            if (selected.Count == 0) return;

            Vector3 center = Vector3.zero;
            foreach (int i in selected) center += tf.TransformPoint(spline[i].position);
            center /= selected.Count;

            Quaternion rotation = Tools.pivotRotation == PivotRotation.Local ? tf.rotation : Quaternion.identity;

            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, rotation);
            if (!EditorGUI.EndChangeCheck()) return;

            Vector3 delta = newCenter - center;
            if (delta.sqrMagnitude < CableCommands.kVectorEpsilonSqr) return;

            Undo.RecordObject(gen, Tr(selected.Count > 1 ? "undo.moveKnots" : "undo.moveKnot"));
            for (int n = 0; n < selected.Count; n++)
            {
                int i = selected[n];
                var knot = spline[i];
                knot.position = tf.InverseTransformPoint(tf.TransformPoint(knot.position) + delta);
                spline.SetKnot(i, knot, n == selected.Count - 1);   // 再構築の通知は最後の 1 回だけ
            }
            EditorUtility.SetDirty(gen);
        }

        // ─── 区間の ＋（制御点を追加）と ▼（たるみを直接ドラッグ） ─────

        static void DrawSegmentMidpointGizmos(CableGenerator gen, CableSpline spline, Transform tf, Vector3 localGravity)
        {
            int segCount = spline.SegmentCount;
            for (int seg = 0; seg < segCount; seg++)
            {
                spline.EvaluateSegment(seg, 0.5f, localGravity, out Vector3 midLocal, out _, out _);
                Vector3 midWorld = tf.TransformPoint(midLocal);
                float size = HandleUtility.GetHandleSize(midWorld);

                Handles.color = DennokoSceneColors.SelectedLine;
                Vector3 sagHandlePos = midWorld + Vector3.down * (size * 0.22f);
                EditorGUI.BeginChangeCheck();
                Vector3 newSagPos = Handles.Slider(sagHandlePos, Vector3.down, size * 0.14f, Handles.ConeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    float deltaDown = Vector3.Dot(newSagPos - sagHandlePos, Vector3.down);
                    CableCommands.SetSegmentSag(gen, seg, spline.GetSegmentSag(seg) + deltaDown);
                }

                Vector2 guiPt = HandleUtility.WorldToGUIPoint(midWorld);
                Handles.BeginGUI();
                var addRect = new Rect(guiPt.x - 11f, guiPt.y - 24f, 22f, 18f);
                bool add = GUI.Button(addRect, new GUIContent("＋", Tr("scene.addKnotHere")), s_midpointButtonStyle);
                Handles.EndGUI();
                if (add)
                {
                    CableCommands.InsertKnotAfter(gen, seg);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // ─── 接線ハンドルとプレビュー線 ─────────────────────────────────

        static void DrawTangentHandle(CableGenerator gen, CableSpline spline, Transform tf, int knotIndex, CableKnot knot, bool isIn)
        {
            Vector3 tangent = isIn ? knot.tangentIn : knot.tangentOut;
            Vector3 knotWorld = tf.TransformPoint(knot.position);
            Vector3 tangentWorld = knotWorld + tf.TransformDirection(knot.rotation * tangent);
            float handleSize = HandleUtility.GetHandleSize(tangentWorld) * 0.06f;

            Handles.color = isIn ? new Color(0.2f, 0.6f, 1f, 0.8f) : new Color(1f, 0.6f, 0.2f, 0.8f);
            Handles.DrawLine(knotWorld, tangentWorld);

            EditorGUI.BeginChangeCheck();
            Vector3 newTangentWorld = Handles.FreeMoveHandle(tangentWorld, handleSize, Vector3.zero, Handles.SphereHandleCap);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(gen, Tr("undo.tangent"));
            Vector3 newTangent = Quaternion.Inverse(knot.rotation) * tf.InverseTransformDirection(newTangentWorld - knotWorld);
            bool mirrored = knot.mode == CableTangentMode.Mirrored;
            if (isIn)
            {
                knot.tangentIn = newTangent;
                if (mirrored) knot.tangentOut = -newTangent;
            }
            else
            {
                knot.tangentOut = newTangent;
                if (mirrored) knot.tangentIn = -newTangent;
            }
            spline.SetKnot(knotIndex, knot);
            EditorUtility.SetDirty(gen);
        }

        static void DrawSplinePreview(CableSpline spline, Transform tf, Vector3 localGravity)
        {
            const int steps = 64;
            Handles.color = new Color(1f, 1f, 0f, 0.5f);
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                spline.Evaluate((float)i / steps, localGravity, out Vector3 pos, out _, out _);
                Vector3 world = tf.TransformPoint(pos);
                if (i > 0) Handles.DrawLine(prev, world);
                prev = world;
            }
        }
    }
}
