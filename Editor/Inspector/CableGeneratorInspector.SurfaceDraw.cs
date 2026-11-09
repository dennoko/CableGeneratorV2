using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Surface Draw Mode — メッシュ表面をなぞってスプライン配置
        // ================================================================

        internal static CableGenerator s_drawTarget = null;

        static readonly List<Vector3> s_strokePoints  = new List<Vector3>();
        static readonly List<Vector3> s_strokeNormals = new List<Vector3>();

        static bool    s_isStroking = false;
        static Vector2 s_lastSampleGuiPos;
        static bool    s_hasHoverHit;
        static Vector3 s_hoverPoint;

        static LayerMask s_drawLayerMask       = ~0;
        static float     s_drawSurfaceOffset   = 0.003f;
        static float     s_drawSimplifyTol     = 0.01f;
        static float     s_drawMinPixelSpacing = 4f;
        static int       s_drawKnotModeIndex   = 0; // 0=自動(AutoSmooth), 1=スムーズ(Mirrored)
        static string    s_drawLastResult      = string.Empty;

        static MeshRaycastSession s_drawSession;

        public static void StartDrawMode(CableGenerator target)
        {
            CancelPickingMode();
            s_drawSession?.Dispose();

            s_drawTarget = target;
            s_strokePoints.Clear();
            s_strokeNormals.Clear();
            s_isStroking     = false;
            s_hasHoverHit    = false;
            s_drawLastResult = string.Empty;

            s_drawSession = new MeshRaycastSession(s_drawLayerMask, target.gameObject);
            SceneView.RepaintAll();
        }

        public static void CancelDrawMode()
        {
            s_drawSession?.Dispose();
            s_drawSession = null;
            s_drawTarget = null;
            s_strokePoints.Clear();
            s_strokeNormals.Clear();
            s_isStroking  = false;
            s_hasHoverHit = false;
            SceneView.RepaintAll();
        }

        void HandleSurfaceDrawSceneGUI(CableGenerator gen)
        {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            Event e = Event.current;
            if (s_drawSession == null)
                s_drawSession = new MeshRaycastSession(s_drawLayerMask, gen.gameObject);

            if (e.type == EventType.Repaint)
            {
                DrawStrokePreview();
                DrawSceneHintText(s_isStroking
                    ? "[ サーフェス描画 ]  マウスを離すと確定   Esc: やり直し"
                    : "[ サーフェス描画 ]  ドラッグで表面をなぞって配線   Esc: キャンセル");
                return; // 描画イベントからは再描画要求を出さない（常時再描画ループ防止）
            }

            bool needsRepaint = false;

            switch (e.type)
            {
                case EventType.MouseMove:
                    s_hasHoverHit = s_drawSession.Raycast(
                        HandleUtility.GUIPointToWorldRay(e.mousePosition), out var hoverHit);
                    if (s_hasHoverHit) s_hoverPoint = hoverHit.point;
                    needsRepaint = true;
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt:
                    GUIUtility.hotControl = controlID;

                    if (s_drawSession.Raycast(
                            HandleUtility.GUIPointToWorldRay(e.mousePosition), out var downHit))
                    {
                        s_isStroking = true;
                        s_strokePoints.Clear();
                        s_strokeNormals.Clear();
                        AppendStrokeSample(downHit, e.mousePosition);
                    }
                    needsRepaint = true;
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlID && s_isStroking:
                    if ((e.mousePosition - s_lastSampleGuiPos).magnitude >= s_drawMinPixelSpacing
                        && s_drawSession.Raycast(
                            HandleUtility.GUIPointToWorldRay(e.mousePosition), out var dragHit))
                    {
                        AppendStrokeSample(dragHit, e.mousePosition);
                        needsRepaint = true;
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlID && e.button == 0:
                    GUIUtility.hotControl = 0;

                    if (s_isStroking)
                    {
                        s_isStroking = false;
                        if (s_strokePoints.Count >= 2)
                        {
                            ApplySplineFromStroke(gen);
                            CancelDrawMode();
                        }
                        else
                        {
                            s_drawLastResult = "サンプルが不足しています。もう一度ドラッグしてください。";
                            s_strokePoints.Clear();
                            s_strokeNormals.Clear();
                        }
                        Repaint();
                    }
                    needsRepaint = true;
                    e.Use();
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    if (s_isStroking)
                    {
                        s_isStroking = false;
                        GUIUtility.hotControl = 0;
                        s_strokePoints.Clear();
                        s_strokeNormals.Clear();
                    }
                    else
                    {
                        CancelDrawMode();
                    }
                    needsRepaint = true;
                    e.Use();
                    break;
            }

            if (needsRepaint)
                SceneView.RepaintAll();
        }

        static void AppendStrokeSample(in MeshRayHit hit, Vector2 guiPos)
        {
            if (s_strokePoints.Count > 0
                && (hit.point - s_strokePoints[s_strokePoints.Count - 1]).sqrMagnitude < kVectorEpsilonSqr)
                return;

            s_strokePoints.Add(hit.point);
            s_strokeNormals.Add(hit.normal);
            s_lastSampleGuiPos = guiPos;
        }

        static void DrawStrokePreview()
        {
            if (s_hasHoverHit && !s_isStroking)
            {
                Handles.color = new Color(0.2f, 1f, 0.3f);
                float size = HandleUtility.GetHandleSize(s_hoverPoint) * 0.05f;
                Handles.SphereHandleCap(0, s_hoverPoint, Quaternion.identity, size, EventType.Repaint);
            }

            if (s_strokePoints.Count >= 2)
            {
                Handles.color = new Color(0.2f, 1f, 0.3f);
                Handles.DrawAAPolyLine(3f, s_strokePoints.ToArray());
            }
        }

        static void ApplySplineFromStroke(CableGenerator cableGen)
        {
            if (cableGen == null || cableGen.Spline == null) return;

            List<int> kept = SimplifyStroke(s_strokePoints, s_drawSimplifyTol);
            if (kept.Count < 2)
            {
                s_drawLastResult = "有効なノットが作れませんでした。";
                return;
            }

            Transform tf = cableGen.transform;

            int n = kept.Count;
            var localPositions = new Vector3[n];
            var localNormals   = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 world = s_strokePoints[kept[i]] + s_strokeNormals[kept[i]] * s_drawSurfaceOffset;
                localPositions[i] = tf.InverseTransformPoint(world);
                localNormals[i]   = tf.InverseTransformDirection(s_strokeNormals[kept[i]]).normalized;
            }

            bool useMirrored = s_drawKnotModeIndex == 1;

            Undo.RecordObject(cableGen, "Draw Spline on Surface");

            var newKnots = new List<CableKnot>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 forward = i < n - 1
                    ? localPositions[i + 1] - localPositions[i]
                    : localPositions[i] - localPositions[i - 1];
                Quaternion rot = CableSpline.SafeLookRotation(forward, localNormals[i]);

                if (useMirrored)
                {
                    float distPrev = i > 0     ? Vector3.Distance(localPositions[i - 1], localPositions[i]) : 0f;
                    float distNext = i < n - 1 ? Vector3.Distance(localPositions[i], localPositions[i + 1]) : 0f;
                    float avgDist  = (i > 0 && i < n - 1) ? (distPrev + distNext) * 0.5f : distPrev + distNext;
                    float len      = avgDist / kLinearTangentDivisor;

                    newKnots.Add(new CableKnot(localPositions[i],
                        new Vector3(0f, 0f, -len), new Vector3(0f, 0f, len), rot,
                        CableTangentMode.Mirrored));
                }
                else
                {
                    newKnots.Add(new CableKnot(localPositions[i],
                        Vector3.zero, Vector3.zero, rot,
                        CableTangentMode.AutoSmooth));
                }
            }

            // 一括差し替え（通知 1 回・アタッチメントは比例対応で追従）
            cableGen.Spline.ReplaceKnots(newKnots, false);
            EditorUtility.SetDirty(cableGen);
            s_drawLastResult = $"{n} ノットで配置しました。";
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 3D Ramer–Douglas–Peucker。
        /// </summary>
        public static List<int> SimplifyStroke(List<Vector3> points, float tolerance)
        {
            var kept = new List<int>();
            if (points == null || points.Count == 0) return kept;

            float minSpacing = Mathf.Max(tolerance * 0.25f, 0.001f);
            float minSpacingSqr = minSpacing * minSpacing;

            var thinned = new List<int> { 0 };
            for (int i = 1; i < points.Count; i++)
                if ((points[i] - points[thinned[thinned.Count - 1]]).sqrMagnitude >= minSpacingSqr)
                    thinned.Add(i);

            int lastIndex = points.Count - 1;
            if (thinned[thinned.Count - 1] != lastIndex)
            {
                bool lastTooClose =
                    (points[lastIndex] - points[thinned[thinned.Count - 1]]).sqrMagnitude < minSpacingSqr;
                if (lastTooClose && thinned.Count > 1)
                    thinned[thinned.Count - 1] = lastIndex;
                else
                    thinned.Add(lastIndex);
            }

            if (thinned.Count <= 2)
                return thinned;

            var keep = new bool[thinned.Count];
            keep[0] = keep[thinned.Count - 1] = true;

            float tolSqr = tolerance * tolerance;
            var ranges = new Stack<(int first, int last)>();
            ranges.Push((0, thinned.Count - 1));

            while (ranges.Count > 0)
            {
                var (first, last) = ranges.Pop();
                if (last - first < 2) continue;

                Vector3 a = points[thinned[first]];
                Vector3 b = points[thinned[last]];

                float maxDistSqr = -1f;
                int   maxIndex   = -1;
                for (int i = first + 1; i < last; i++)
                {
                    float d = DistanceToSegmentSqr(points[thinned[i]], a, b);
                    if (d > maxDistSqr)
                    {
                        maxDistSqr = d;
                        maxIndex   = i;
                    }
                }

                if (maxDistSqr > tolSqr)
                {
                    keep[maxIndex] = true;
                    ranges.Push((first, maxIndex));
                    ranges.Push((maxIndex, last));
                }
            }

            for (int i = 0; i < thinned.Count; i++)
                if (keep[i])
                    kept.Add(thinned[i]);
            return kept;
        }

        static float DistanceToSegmentSqr(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSqr = ab.sqrMagnitude;
            if (lenSqr < kVectorEpsilonSqr) return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSqr);
            return (p - (a + ab * t)).sqrMagnitude;
        }

        void DrawSurfaceDrawSection(CableGenerator generator)
        {
            bool isMyTarget = s_drawTarget == generator;

            if (!isMyTarget)
            {
                s_drawLayerMask       = DrawLayerMaskField("LayerMask", s_drawLayerMask);
                s_drawSurfaceOffset   = Mathf.Max(0f, EditorGUILayout.FloatField("面オフセット", s_drawSurfaceOffset));
                s_drawSimplifyTol     = EditorGUILayout.Slider("簡略化許容誤差", s_drawSimplifyTol, 0.001f, 0.1f);
                s_drawMinPixelSpacing = EditorGUILayout.Slider("サンプル間隔(px)", s_drawMinPixelSpacing, 2f, 20f);
                s_drawKnotModeIndex   = EditorGUILayout.Popup("ノットモード", s_drawKnotModeIndex, new[] { "自動", "スムーズ" });

                GUILayout.Space(6);

                EditorGUI.BeginDisabledGroup(s_drawTarget != null || s_pickingTarget != null);
                if (GUILayout.Button("シーンにドラッグで描画開始", CableGeneratorTheme.SecondaryButtonStyle))
                    StartDrawMode(generator);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUILayout.HelpBox("シーンビューでメッシュ表面をドラッグして描画してください", MessageType.Info);

                if (s_strokePoints.Count > 0)
                    GUILayout.Label($"サンプル数: {s_strokePoints.Count}", CableGeneratorTheme.CaptionStyle);

                GUILayout.Space(4);

                if (GUILayout.Button("キャンセル", CableGeneratorTheme.SecondaryButtonStyle))
                    CancelDrawMode();
            }

            if (!string.IsNullOrEmpty(s_drawLastResult))
            {
                GUILayout.Space(4);
                GUILayout.Label(s_drawLastResult, CableGeneratorTheme.CaptionStyle);
            }
        }
    }
}

