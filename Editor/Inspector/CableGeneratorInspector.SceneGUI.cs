using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        static GUIStyle s_indexLabelStyle;
        static GUIStyle s_indexLabelSelectedStyle;
        static GUIStyle s_midpointButtonStyle;

        static void EnsureSceneStyles()
        {
            if (s_indexLabelStyle == null)
            {
                s_indexLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize  = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = new Color(1f, 1f, 1f, 0.85f) },
                };
            }

            if (s_indexLabelSelectedStyle == null)
            {
                s_indexLabelSelectedStyle = new GUIStyle(s_indexLabelStyle)
                {
                    fontSize = 13,
                    normal   = { textColor = DennokoSceneColors.SelectedLine },
                };
            }

            if (s_midpointButtonStyle == null)
            {
                s_midpointButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize  = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding   = new RectOffset(2, 2, 1, 1),
                };
            }
        }

        // ================================================================
        //  Main OnSceneGUI
        // ================================================================

        void OnSceneGUI()
        {
            var gen = (CableGenerator)target;
            if (gen == null || gen.Spline == null) return;

            CableGeneratorTheme.Initialize();
            EnsureSceneStyles();

            // シーン上のツールバーは S1 Overlay (CableSceneToolbar) が描く。ここは対象を知らせるだけ
            CableSceneToolState.SetTarget(gen);

            // 1. サーフェス描画モード
            if (s_drawTarget == gen)
            {
                HandleSurfaceDrawSceneGUI(gen);
                return;
            }

            // 2. 2点選択配線モード
            if (s_pickingTarget == gen)
            {
                HandlePickingSceneGUI(gen);
                return;
            }

            // 3. 通常の直接ギズモ編集モード
            var spline = gen.Spline;
            Transform tf = gen.transform;
            Vector3 localGravity = tf.InverseTransformVector(Vector3.down);
            Event e = Event.current;

            int count = spline.Count;
            ValidateKnotSelection(gen, count);

            // 矩形選択の既定コントロール。ID は他のハンドルより先に取り、処理は最後に回す（ハンドルやボタンを優先する）
            int rectSelectId = GUIUtility.GetControlID(s_rectSelectHash, FocusType.Passive);

            // プレビュー線は描画イベントでのみ評価する（Layout / マウスイベント毎の曲線評価を省く）
            if (e.type == EventType.Repaint)
                DrawSplinePreview(spline, tf, localGravity);
            DrawSegmentMidpointGizmos(gen, spline, tf, localGravity);

            bool removePreview = IsKnotRemoveModifier(e);
            for (int i = 0; i < count; i++)
            {
                var     knot     = spline[i];
                Vector3 worldPos = tf.TransformPoint(knot.position);

                float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.1f;
                bool  isSelected = s_selectedKnotIndices.Contains(i);

                // インデックスラベル ([0]▶ / ◀[N] / [i])
                string idxLabel = (i == 0) ? $"[{i}]▶"
                                : (!spline.Closed && i == count - 1) ? $"◀[{i}]"
                                : $"[{i}]";
                Handles.Label(worldPos + Vector3.up * (handleSize * 2.2f), idxLabel,
                    isSelected ? s_indexLabelSelectedStyle : s_indexLabelStyle);

                // 制御点の球: 常に表示。クリックで単体選択、Shift / Ctrl + クリックで選択に足す / 外す
                Handles.color = IsKnotInDragRect(worldPos)
                    ? (removePreview ? DennokoSceneColors.HoverRemove : DennokoSceneColors.HoverAdd)
                    : isSelected ? DennokoSceneColors.SelectedLine
                    : GetKnotColor(knot.mode);

                if (Handles.Button(worldPos, Quaternion.identity, handleSize * 1.8f, handleSize * 2.3f, Handles.SphereHandleCap))
                {
                    if (e.shift || e.control || e.command) ToggleKnotSelection(i);
                    else                                   SelectSingleKnot(i);
                }

                // 手動モード時のみベジェ接線ハンドルを表示（AutoSmooth時はスッキリ隠す）
                if (knot.mode != CableTangentMode.AutoSmooth)
                {
                    DrawTangentHandle(spline, tf, i, knot, gen, true);
                    DrawTangentHandle(spline, tf, i, knot, gen, false);
                }
            }

            // XYZ の移動矢印は選択中の制御点だけ。複数選択時はその中心に 1 つだけ出してまとめて動かす
            DrawSelectionMoveHandle(gen, spline, tf);

            HandleKnotRectSelection(gen, spline, tf, rectSelectId);
        }

        // ================================================================
        //  Midpoint [⊕ Add Knot] & [▼ Direct Sag Drag] Gizmos
        // ================================================================

        void DrawSegmentMidpointGizmos(CableGenerator gen, CableSpline spline, Transform tf, Vector3 localGravity)
        {
            int segCount = spline.SegmentCount;
            if (segCount <= 0) return;

            for (int seg = 0; seg < segCount; seg++)
            {
                spline.EvaluateSegment(seg, 0.5f, localGravity, out Vector3 midLocal, out _, out _);
                Vector3 midWorld = tf.TransformPoint(midLocal);
                float size = HandleUtility.GetHandleSize(midWorld);

                // ▼ 直接たるみドラッグハンドル（下方向スライダー）
                Handles.color = new Color(0.3f, 0.85f, 1f, 0.9f);
                Vector3 sagHandlePos = midWorld + Vector3.down * (size * 0.22f);
                EditorGUI.BeginChangeCheck();
                Vector3 newSagPos = Handles.Slider(sagHandlePos, Vector3.down, size * 0.14f, Handles.ConeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    float deltaDown = Vector3.Dot(newSagPos - sagHandlePos, Vector3.down);
                    float currentSag = spline.GetSegmentSag(seg);
                    Undo.RecordObject(gen, "Adjust Cable Segment Sag");
                    spline.SetSegmentSag(seg, Mathf.Max(0f, currentSag + deltaDown));
                    EditorUtility.SetDirty(gen);
                    Repaint();
                }

                // ⊕ 1クリック中間点追加ボタン（2D GUI ボタンとして中間点の上に小さく表示）
                Vector2 guiPt = HandleUtility.WorldToGUIPoint(midWorld);
                Handles.BeginGUI();
                Rect addRect = new Rect(guiPt.x - 11f, guiPt.y - 24f, 22f, 18f);
                if (GUI.Button(addRect, new GUIContent("＋", "クリックでこの区間の中央に制御点を追加します"), s_midpointButtonStyle))
                {
                    InsertKnotAfter(gen, seg);
                    Handles.EndGUI();
                    GUIUtility.ExitGUI();
                    return;
                }
                Handles.EndGUI();
            }
        }
    }
}
