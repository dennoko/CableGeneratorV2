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
                    normal    = { textColor = new Color(0.5f, 0.9f, 1f) },
                };
            }

            if (s_indexLabelSelectedStyle == null)
            {
                s_indexLabelSelectedStyle = new GUIStyle(s_indexLabelStyle)
                {
                    fontSize = 13,
                    normal   = { textColor = new Color(1f, 0.9f, 0.2f) },
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

            // Scene ビュー上部カプセルオーバーレイバーを常時描画
            DrawSceneOverlayToolbar(gen);

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

            // プレビュー線は描画イベントでのみ評価する（Layout / マウスイベント毎の曲線評価を省く）
            if (Event.current.type == EventType.Repaint)
                DrawSplinePreview(spline, tf, localGravity);
            DrawSegmentMidpointGizmos(gen, spline, tf, localGravity);

            int count = spline.Count;
            for (int i = 0; i < count; i++)
            {
                var     knot     = spline[i];
                Vector3 worldPos = tf.TransformPoint(knot.position);

                float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.1f;
                bool  isSelected = i == s_snapKnotIndex || s_selectedKnotIndices.Contains(i);

                // インデックスラベル ([0]▶ / ◀[N] / [i])
                string idxLabel = (i == 0) ? $"[{i}]▶"
                                : (!spline.Closed && i == count - 1) ? $"◀[{i}]"
                                : $"[{i}]";
                Handles.Label(worldPos + Vector3.up * (handleSize * 2.2f), idxLabel,
                    isSelected ? s_indexLabelSelectedStyle : s_indexLabelStyle);

                // PositionHandle: ドラッグで移動
                Handles.color = GetKnotColor(knot.mode);
                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPos = Handles.PositionHandle(worldPos, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(gen, "Move Cable Control Point");
                    knot.position = tf.InverseTransformPoint(newWorldPos);
                    spline.SetKnot(i, knot);
                    EditorUtility.SetDirty(gen);
                }

                // 選択スフィア: クリックで単体選択、Shift+クリックで複数選択
                Handles.color = isSelected
                    ? (i == s_snapKnotIndex ? Color.white : new Color(1f, 0.9f, 0.2f))
                    : GetKnotColor(knot.mode);

                bool shiftHeld = Event.current.shift;
                if (Handles.Button(worldPos, Quaternion.identity, handleSize * 1.8f, handleSize * 2.3f, Handles.SphereHandleCap))
                {
                    if (shiftHeld)
                    {
                        if (s_selectedKnotIndices.Contains(i)) s_selectedKnotIndices.Remove(i);
                        else                                   s_selectedKnotIndices.Add(i);
                    }
                    else
                    {
                        s_selectedKnotIndices.Clear();
                        s_snapKnotIndex = i;
                    }
                    Repaint();
                    SceneView.RepaintAll();
                }

                // 手動モード時のみベジェ接線ハンドルを表示（AutoSmooth時はスッキリ隠す）
                if (knot.mode != CableTangentMode.AutoSmooth)
                {
                    DrawTangentHandle(spline, tf, i, knot, gen, true);
                    DrawTangentHandle(spline, tf, i, knot, gen, false);
                }
            }

            // 選択中ノットの直上クイックフローティングバー
            if (s_snapKnotIndex >= 0 && s_snapKnotIndex < count)
            {
                DrawSelectedKnotFloatingBar(gen, spline, tf, s_snapKnotIndex);
            }
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

        // ================================================================
        //  Selected Knot Quick Floating Bar
        // ================================================================

        void DrawSelectedKnotFloatingBar(CableGenerator gen, CableSpline spline, Transform tf, int index)
        {
            Vector3 worldPos = tf.TransformPoint(spline[index].position);
            Vector2 guiPt = HandleUtility.WorldToGUIPoint(worldPos);

            Handles.BeginGUI();
            Rect barRect = new Rect(guiPt.x - 105f, guiPt.y + 18f, 210f, 24f);
            GUILayout.BeginArea(barRect, CableGeneratorTheme.OverlayBarStyle);
            GUILayout.BeginHorizontal();

            int currentSimple = ToSimpleIndex(spline.GetTangentMode(index));
            string[] modeShort = { "自動", "スムーズ", "角" };
            for (int m = 0; m < 3; m++)
            {
                if (GUILayout.Button(modeShort[m],
                    currentSimple == m ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle,
                    GUILayout.Height(16)))
                {
                    Undo.RecordObject(gen, "接線モードを変更");
                    spline.SetTangentMode(index, FromSimpleIndex(m));
                    EditorUtility.SetDirty(gen);
                    Repaint();
                }
            }

            if (GUILayout.Button(new GUIContent("🔌", "この制御点にアタッチメントを追加"),
                CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(24), GUILayout.Height(16)))
            {
                AddAttachment(gen, index);
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                Handles.EndGUI();
                GUIUtility.ExitGUI(); // 選択変更でインスペクタが作り直されるため GUI を打ち切る
            }

            EditorGUI.BeginDisabledGroup(spline.Count <= 2);
            if (GUILayout.Button(new GUIContent("×", "この制御点を削除"),
                CableGeneratorTheme.DangerButtonStyle, GUILayout.Width(20), GUILayout.Height(16)))
            {
                Undo.RecordObject(gen, "制御点を削除");
                spline.RemoveAt(index);
                s_snapKnotIndex = Mathf.Clamp(s_snapKnotIndex, 0, Mathf.Max(0, spline.Count - 1));
                s_selectedKnotIndices.Clear();
                EditorUtility.SetDirty(gen);
                Repaint();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
