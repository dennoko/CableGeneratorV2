using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Knot Selection — シーン上で選んでいる制御点
        //
        //  s_selectedKnotIndices : 選択中の制御点（移動ハンドルを出す対象）。空なら何も選んでいない
        //  s_snapKnotIndex       : 最後に選んだ制御点（Inspector の「選択ノットの詳細」が見る）。
        //                          選択が空でないときは必ず s_selectedKnotIndices に含まれる
        // ================================================================

        static CableGenerator s_selectionOwner;

        static readonly int s_rectSelectHash = "CableGeneratorV2.KnotRectSelect".GetHashCode();
        static Vector2 s_rectStart;
        static Vector2 s_rectEnd;
        static bool    s_rectDragging;
        const float    kRectDragThreshold = 4f;

        internal static bool HasKnotSelection => s_selectedKnotIndices.Count > 0;

        internal static void SelectSingleKnot(int index)
        {
            s_selectedKnotIndices.Clear();
            s_selectedKnotIndices.Add(index);
            s_snapKnotIndex = index;
            OnKnotSelectionChanged();
        }

        internal static void ToggleKnotSelection(int index)
        {
            if (s_selectedKnotIndices.Remove(index))
            {
                if (s_snapKnotIndex == index && s_selectedKnotIndices.Count > 0)
                    s_snapKnotIndex = MinIndex(s_selectedKnotIndices);
            }
            else
            {
                s_selectedKnotIndices.Add(index);
                s_snapKnotIndex = index;
            }
            OnKnotSelectionChanged();
        }

        internal static void ClearKnotSelection()
        {
            if (s_selectedKnotIndices.Count == 0) return;
            s_selectedKnotIndices.Clear();
            OnKnotSelectionChanged();
        }

        /// <summary>選択中の制御点を昇順で返す（範囲外は除く）。</summary>
        internal static List<int> GetSelectedKnots(int knotCount)
        {
            var list = new List<int>(s_selectedKnotIndices.Count);
            foreach (int i in s_selectedKnotIndices)
                if (i >= 0 && i < knotCount) list.Add(i);
            list.Sort();
            return list;
        }

        static void OnKnotSelectionChanged()
        {
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();   // Inspector の制御点リストも追従させる
            CableSceneToolState.Raise();
        }

        static int MinIndex(HashSet<int> set)
        {
            int min = int.MaxValue;
            foreach (int i in set) if (i < min) min = i;
            return min;
        }

        /// <summary>別のケーブルに切り替わったら選択を引き継がない。Undo などで減った制御点は外す。</summary>
        static void ValidateKnotSelection(CableGenerator gen, int knotCount)
        {
            if (s_selectionOwner != gen)
            {
                s_selectionOwner = gen;
                s_selectedKnotIndices.Clear();
                s_snapKnotIndex = Mathf.Clamp(s_snapKnotIndex, 0, Mathf.Max(0, knotCount - 1));
                CableSceneToolState.Raise();
                return;
            }

            if (s_selectedKnotIndices.RemoveWhere(i => i < 0 || i >= knotCount) > 0)
            {
                if (s_selectedKnotIndices.Count > 0 && !s_selectedKnotIndices.Contains(s_snapKnotIndex))
                    s_snapKnotIndex = MinIndex(s_selectedKnotIndices);
                CableSceneToolState.Raise();
            }
        }

        // ================================================================
        //  Move Handle — 選択中の制御点を 1 つの矢印でまとめて動かす
        // ================================================================

        void DrawSelectionMoveHandle(CableGenerator gen, CableSpline spline, Transform tf)
        {
            var selected = GetSelectedKnots(spline.Count);
            if (selected.Count == 0) return;

            Vector3 center = Vector3.zero;
            foreach (int i in selected) center += tf.TransformPoint(spline[i].position);
            center /= selected.Count;

            Quaternion rotation = Tools.pivotRotation == PivotRotation.Local ? tf.rotation : Quaternion.identity;

            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, rotation);
            if (!EditorGUI.EndChangeCheck()) return;

            Vector3 delta = newCenter - center;
            if (delta.sqrMagnitude < kVectorEpsilonSqr) return;

            Undo.RecordObject(gen, selected.Count > 1 ? "Move Cable Control Points" : "Move Cable Control Point");
            for (int n = 0; n < selected.Count; n++)
            {
                int i = selected[n];
                var knot = spline[i];
                knot.position = tf.InverseTransformPoint(tf.TransformPoint(knot.position) + delta);
                spline.SetKnot(i, knot, n == selected.Count - 1);   // 再構築の通知は最後の 1 回だけ
            }
            EditorUtility.SetDirty(gen);
        }

        // ================================================================
        //  Click / Rect Selection — 何も無いところのクリックとドラッグ
        //    ドラッグ : 矩形内の制御点を選ぶ（Shift = 足す / Ctrl = 外す）
        //    クリック : 選択を外す。制御点を選んでいなければ、クリックした物を通常どおり選ぶ
        // ================================================================

        static bool IsKnotRemoveModifier(Event e) => e.control || e.command;
        static bool IsKnotAddModifier(Event e)    => e.shift;

        /// <summary>矩形ドラッグ中で、i 番目の制御点が矩形に入っているか（描画の予告用）。</summary>
        static bool IsKnotInDragRect(Vector3 worldPos)
        {
            if (!s_rectDragging) return false;
            return IsInsideRect(worldPos, DragRect());
        }

        static Rect DragRect() => Rect.MinMaxRect(
            Mathf.Min(s_rectStart.x, s_rectEnd.x), Mathf.Min(s_rectStart.y, s_rectEnd.y),
            Mathf.Max(s_rectStart.x, s_rectEnd.x), Mathf.Max(s_rectStart.y, s_rectEnd.y));

        static bool IsInsideRect(Vector3 worldPos, Rect rect)
        {
            var cam = Camera.current;
            if (cam != null && cam.WorldToViewportPoint(worldPos).z <= 0f) return false;   // カメラの後ろは選ばない
            return rect.Contains(HandleUtility.WorldToGUIPoint(worldPos));
        }

        void HandleKnotRectSelection(CableGenerator gen, CableSpline spline, Transform tf, int controlId)
        {
            Event e = Event.current;

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    // 手のひらツール (Q) の左ドラッグはカメラ操作なので奪わない
                    if (Tools.current != Tool.View) HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.MouseDown:
                    if (e.button != 0 || e.alt || HandleUtility.nearestControl != controlId) break;
                    GUIUtility.hotControl = controlId;
                    s_rectStart    = s_rectEnd = e.mousePosition;
                    s_rectDragging = false;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) break;
                    s_rectEnd = e.mousePosition;
                    if (!s_rectDragging && (s_rectEnd - s_rectStart).magnitude >= kRectDragThreshold)
                        s_rectDragging = true;
                    e.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId || e.button != 0) break;
                    GUIUtility.hotControl = 0;
                    if (s_rectDragging) ApplyRectSelection(spline, tf, e);
                    else                ClickEmptySpace(gen, e);
                    s_rectDragging = false;
                    e.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.KeyDown:
                    if (e.keyCode != KeyCode.Escape) break;
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        s_rectDragging = false;
                        e.Use();
                    }
                    else if (HasKnotSelection)
                    {
                        ClearKnotSelection();
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    if (GUIUtility.hotControl == controlId && s_rectDragging)
                        DrawDragRect(IsKnotRemoveModifier(e));
                    break;
            }
        }

        static void ApplyRectSelection(CableSpline spline, Transform tf, Event e)
        {
            Rect rect   = DragRect();
            bool add    = IsKnotAddModifier(e);
            bool remove = IsKnotRemoveModifier(e);

            if (!add && !remove) s_selectedKnotIndices.Clear();

            int lastHit = -1;
            for (int i = 0; i < spline.Count; i++)
            {
                if (!IsInsideRect(tf.TransformPoint(spline[i].position), rect)) continue;
                if (remove) s_selectedKnotIndices.Remove(i);
                else      { s_selectedKnotIndices.Add(i); if (lastHit < 0) lastHit = i; }
            }

            if (lastHit >= 0 && !s_selectedKnotIndices.Contains(s_snapKnotIndex))
                s_snapKnotIndex = lastHit;
            else if (s_selectedKnotIndices.Count > 0 && !s_selectedKnotIndices.Contains(s_snapKnotIndex))
                s_snapKnotIndex = MinIndex(s_selectedKnotIndices);

            OnKnotSelectionChanged();
        }

        static void ClickEmptySpace(CableGenerator gen, Event e)
        {
            GameObject picked = HandleUtility.PickGameObject(e.mousePosition, true);
            bool pickedOther = picked != null && picked != gen.gameObject;

            if (pickedOther)
            {
                // 別のオブジェクトはいつもの Scene ビューのクリックと同じように選ぶ
                if (e.shift || e.control || e.command)
                {
                    var objs = new List<Object>(Selection.objects);
                    if (!objs.Remove(picked)) objs.Add(picked);
                    Selection.objects = objs.ToArray();
                }
                else
                {
                    Selection.activeGameObject = picked;
                }
                return;
            }

            if (HasKnotSelection) ClearKnotSelection();   // まず制御点の選択を外す
            else if (picked == null) Selection.activeObject = null;
        }

        static void DrawDragRect(bool removing)
        {
            Color line = removing ? DennokoSceneColors.HoverRemove : DennokoSceneColors.SelectedLine;
            Color fill = line;
            fill.a = 0.12f;

            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(DragRect(), fill, line);
            Handles.EndGUI();
        }
    }
}
