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
        //  Scene View Top Capsule Overlay Toolbar
        // ================================================================

        void DrawSceneOverlayToolbar(CableGenerator gen)
        {
            Handles.BeginGUI();

            var sceneView = SceneView.currentDrawingSceneView;
            float viewWidth = sceneView != null
                ? sceneView.position.width
                : Screen.width / EditorGUIUtility.pixelsPerPoint;

            float barWidth = Mathf.Min(viewWidth - 24f, 760f);
            float barX = (viewWidth - barWidth) * 0.5f;

            GUILayout.BeginArea(new Rect(barX, 8f, barWidth, 36f), CableGeneratorTheme.OverlayBarStyle);
            GUILayout.BeginHorizontal();

            // 1. 2点配線ボタン
            bool isPicking = s_pickingTarget == gen;
            if (GUILayout.Button(isPicking ? "🎯 2点配線中 (Esc解除)" : "🎯 2点配線",
                isPicking ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                if (isPicking) CancelPickingMode();
                else           StartPickingMode(gen);
            }

            // 2. なぞり描画ボタン
            bool isDrawing = s_drawTarget == gen;
            if (GUILayout.Button(isDrawing ? "✏️ 描画中 (Esc解除)" : "✏️ なぞり描画",
                isDrawing ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                if (isDrawing) CancelDrawMode();
                else           StartDrawMode(gen);
            }

            // 3. 面投影ショートカット
            if (GUILayout.Button(new GUIContent("🧲 面投影", "選択ノットを下方向のメッシュ表面へスナップ投影します"),
                CableGeneratorTheme.SecondaryButtonStyle))
            {
                SnapKnotInDirection(gen);
                Repaint();
            }

            // 4. 床判定 (Auto-Draping) トグル
            bool colOn = gen.enableSurfaceCollision;
            if (GUILayout.Button(colOn ? "🧱 床判定:ON" : "🧱 床判定:OFF",
                colOn ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                Undo.RecordObject(gen, "Toggle Cable Surface Collision");
                gen.enableSurfaceCollision = !colOn;
                EditorUtility.SetDirty(gen);
                gen.RequestRefresh(); // 構造変更を伴い得るので OnGUI 外で実行
                Repaint();
            }

            // 5. ワンクリック LOD トグル
            bool lodOn = gen.enableLod;
            if (GUILayout.Button(lodOn ? "⚡ LOD:ON" : "⚡ LOD:OFF",
                lodOn ? CableGeneratorTheme.ActiveToggleStyle : CableGeneratorTheme.SecondaryButtonStyle))
            {
                Undo.RecordObject(gen, "Toggle Cable LOD");
                gen.enableLod = !lodOn;
                EditorUtility.SetDirty(gen);
                gen.RequestRefresh();
                Repaint();
            }

            // 6. メッシュ保存 (Bake)
            Mesh mesh0 = gen.GeneratedMesh;
            bool hasMesh = mesh0 != null && mesh0.vertexCount > 0;
            EditorGUI.BeginDisabledGroup(!hasMesh);
            if (GUILayout.Button("💾 保存(Bake)", CableGeneratorTheme.SecondaryButtonStyle))
            {
                BakeCableToSceneAndAssets(gen, bakeFolderPath);
                GUIUtility.ExitGUI();
            }

            // 7. ボーン付き FBX 書き出し（形状固定）
            if (GUILayout.Button(new GUIContent($"🦴 FBX({gen.boneCount}本)", "現在の形状を固定し、ボーン＆ウェイト付き FBX を書き出してシーンに配置します"),
                CableGeneratorTheme.SecondaryButtonStyle))
            {
                ExportCableFbx(gen, bakeFolderPath, s_fbxLodTier, true, true);
                GUIUtility.ExitGUI();
            }
            EditorGUI.EndDisabledGroup();

            // 8. ポリゴン数バッジ
            int triCount = TriangleCount(mesh0);
            Mesh lod2 = lodOn ? gen.GeneratedLod2Mesh : null;
            if (lod2 != null)
            {
                GUILayout.Label($"{triCount}→{TriangleCount(lod2)}△", CableGeneratorTheme.CaptionStyle, GUILayout.Width(68));
            }
            else
            {
                GUILayout.Label($"{triCount}△", CableGeneratorTheme.CaptionStyle, GUILayout.Width(48));
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            Handles.EndGUI();
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

        // ================================================================
        //  Handle Drawing
        // ================================================================

        void DrawTangentHandle(CableSpline spline, Transform tf,
            int knotIndex, CableKnot knot, CableGenerator gen, bool isIn)
        {
            Vector3 tangent        = isIn ? knot.tangentIn : knot.tangentOut;
            Vector3 knotWorld      = tf.TransformPoint(knot.position);
            Vector3 rotatedTangent = knot.rotation * tangent;
            Vector3 tangentWorld   = knotWorld + tf.TransformDirection(rotatedTangent);

            float handleSize = HandleUtility.GetHandleSize(tangentWorld) * 0.06f;

            Handles.color = isIn ? new Color(0.2f, 0.6f, 1f, 0.8f) : new Color(1f, 0.6f, 0.2f, 0.8f);
            Handles.DrawLine(knotWorld, tangentWorld);

            EditorGUI.BeginChangeCheck();
            var fmhRot = Quaternion.identity;
            Vector3 newTangentWorld = Handles.FreeMoveHandle(
                tangentWorld, handleSize, Vector3.zero, Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(gen, "Move Cable Tangent");

                Vector3 newLocalDir = tf.InverseTransformDirection(newTangentWorld - knotWorld);
                Vector3 newTangent  = Quaternion.Inverse(knot.rotation) * newLocalDir;

                if (isIn)
                {
                    knot.tangentIn = newTangent;
                    if (knot.mode == CableTangentMode.Mirrored)
                        knot.tangentOut = -newTangent;
                }
                else
                {
                    knot.tangentOut = newTangent;
                    if (knot.mode == CableTangentMode.Mirrored)
                        knot.tangentIn = -newTangent;
                }

                spline.SetKnot(knotIndex, knot);
                EditorUtility.SetDirty(gen);
            }
        }

        void DrawSplinePreview(CableSpline spline, Transform tf, Vector3 localGravity)
        {
            Handles.color = new Color(1f, 1f, 0f, 0.5f);
            int     previewSteps = 64;
            Vector3 prevPoint    = Vector3.zero;

            for (int i = 0; i <= previewSteps; i++)
            {
                float t = (float)i / previewSteps;
                spline.Evaluate(t, localGravity, out Vector3 pos, out _, out _);
                Vector3 worldPos = tf.TransformPoint(pos);

                if (i > 0) Handles.DrawLine(prevPoint, worldPos);
                prevPoint = worldPos;
            }
        }

        static Color GetKnotColor(CableTangentMode mode)
        {
            switch (mode)
            {
                case CableTangentMode.Mirrored:   return Color.green;
                case CableTangentMode.Continuous: return Color.yellow;
                case CableTangentMode.Broken:     return Color.red;
                case CableTangentMode.AutoSmooth: return Color.cyan;
                default:                          return Color.white;
            }
        }

        public static int ToSimpleIndex(CableTangentMode mode) => mode switch
        {
            CableTangentMode.AutoSmooth => 0,
            CableTangentMode.Mirrored   => 1,
            CableTangentMode.Continuous => 1,
            CableTangentMode.Broken     => 2,
            _                           => 0,
        };

        public static CableTangentMode FromSimpleIndex(int idx) => idx switch
        {
            0 => CableTangentMode.AutoSmooth,
            1 => CableTangentMode.Mirrored,
            2 => CableTangentMode.Broken,
            _ => CableTangentMode.AutoSmooth,
        };
    }
}

