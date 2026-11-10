using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    internal static partial class CableSceneHandles
    {
        // ================================================================
        //  何も無いところのクリックとドラッグ
        //    ドラッグ : 矩形内の制御点を選ぶ（Shift = 足す / Ctrl = 外す）
        //    クリック : 制御点の選択を外す。選んでいなければ、クリックした物を通常どおり選ぶ
        // ================================================================

        static readonly int s_rectSelectHash = "CableGeneratorV2.KnotRectSelect".GetHashCode();
        static Vector2 s_rectStart;
        static Vector2 s_rectEnd;
        static bool s_rectDragging;
        const float kRectDragThreshold = 4f;

        static bool IsRemoveModifier(Event e) => e.control || e.command;
        static bool IsAddModifier(Event e) => e.shift;

        static bool IsInDragRect(Vector3 worldPos) => s_rectDragging && IsInsideRect(worldPos, DragRect());

        static Rect DragRect() => Rect.MinMaxRect(
            Mathf.Min(s_rectStart.x, s_rectEnd.x), Mathf.Min(s_rectStart.y, s_rectEnd.y),
            Mathf.Max(s_rectStart.x, s_rectEnd.x), Mathf.Max(s_rectStart.y, s_rectEnd.y));

        static bool IsInsideRect(Vector3 worldPos, Rect rect)
        {
            var cam = Camera.current;
            if (cam != null && cam.WorldToViewportPoint(worldPos).z <= 0f) return false;   // カメラの後ろは選ばない
            return rect.Contains(HandleUtility.WorldToGUIPoint(worldPos));
        }

        static void HandleRectSelection(CableGenerator gen, CableSpline spline, Transform tf, int controlId)
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
                    s_rectStart = s_rectEnd = e.mousePosition;
                    s_rectDragging = false;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) break;
                    s_rectEnd = e.mousePosition;
                    if (!s_rectDragging && (s_rectEnd - s_rectStart).magnitude >= kRectDragThreshold) s_rectDragging = true;
                    e.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId || e.button != 0) break;
                    GUIUtility.hotControl = 0;
                    if (s_rectDragging) ApplyRectSelection(gen, spline, tf, e);
                    else ClickEmptySpace(gen, e);
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
                    else if (CableKnotSelection.HasSelection)
                    {
                        CableKnotSelection.Clear();
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    if (GUIUtility.hotControl == controlId && s_rectDragging) DrawDragRect(IsRemoveModifier(e));
                    break;
            }
        }

        static void ApplyRectSelection(CableGenerator gen, CableSpline spline, Transform tf, Event e)
        {
            Rect rect = DragRect();
            bool add = IsAddModifier(e);
            bool remove = IsRemoveModifier(e);

            var result = new HashSet<int>();
            if (add || remove)
                foreach (int i in CableKnotSelection.Sorted(spline.Count)) result.Add(i);

            int firstHit = -1;
            for (int i = 0; i < spline.Count; i++)
            {
                if (!IsInsideRect(tf.TransformPoint(spline[i].position), rect)) continue;
                if (remove) result.Remove(i);
                else
                {
                    result.Add(i);
                    if (firstHit < 0) firstHit = i;
                }
            }
            CableKnotSelection.Set(gen, result, firstHit);
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

            if (CableKnotSelection.HasSelection) CableKnotSelection.Clear();   // まず制御点の選択を外す
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
