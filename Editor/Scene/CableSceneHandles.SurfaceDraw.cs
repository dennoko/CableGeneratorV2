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
        //  なぞり描画 — メッシュ表面をドラッグしてケーブルを引く
        // ================================================================

        static CableGenerator s_drawTarget;
        static readonly List<Vector3> s_strokePoints = new List<Vector3>();
        static readonly List<Vector3> s_strokeNormals = new List<Vector3>();
        static bool s_isStroking;
        static Vector2 s_lastSampleGuiPos;
        static bool s_hasHoverHit;
        static Vector3 s_hoverPoint;
        static MeshRaycastSession s_drawSession;

        public static void StartDrawing(CableGenerator target)
        {
            if (target == null) return;
            CancelPicking();
            s_drawSession?.Dispose();
            s_drawTarget = target;
            s_strokePoints.Clear();
            s_strokeNormals.Clear();
            s_isStroking = false;
            s_hasHoverHit = false;
            s_drawSession = new MeshRaycastSession(CableToolSettings.instance.hitLayerMask, target.gameObject);
            RaiseWorkChanged();
        }

        static void CancelDrawing()
        {
            s_drawSession?.Dispose();
            s_drawSession = null;
            s_drawTarget = null;
            s_strokePoints.Clear();
            s_strokeNormals.Clear();
            s_isStroking = false;
            s_hasHoverHit = false;
            CableSceneGuide.Hide();
        }

        static void HandleSurfaceDraw(CableGenerator gen)
        {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            Event e = Event.current;
            var settings = CableToolSettings.instance;
            s_drawSession ??= new MeshRaycastSession(settings.hitLayerMask, gen.gameObject);

            // カーソルヒントはイベントを消費する前に呼んでカーソルに追従させる
            CableSceneGuide.Show(GuideText(gen));

            if (e.type == EventType.Repaint)
            {
                DrawStrokePreview();
                return;   // 描画イベントからは再描画要求を出さない（常時再描画ループ防止）
            }

            bool needsRepaint = false;
            switch (e.type)
            {
                case EventType.MouseMove:
                    s_hasHoverHit = s_drawSession.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var hoverHit);
                    if (s_hasHoverHit) s_hoverPoint = hoverHit.point;
                    needsRepaint = true;
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt:
                    GUIUtility.hotControl = controlID;
                    if (s_drawSession.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var downHit))
                    {
                        s_isStroking = true;
                        s_strokePoints.Clear();
                        s_strokeNormals.Clear();
                        AppendStrokeSample(downHit, e.mousePosition);
                        RaiseWorkChanged();   // バナーの文言を「離すと確定」にする
                    }
                    needsRepaint = true;
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlID && s_isStroking:
                    if ((e.mousePosition - s_lastSampleGuiPos).magnitude >= settings.drawMinPixelSpacing
                        && s_drawSession.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var dragHit))
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
                            var result = ApplySplineFromStroke(gen);
                            EndWork();
                            CableSceneGuide.Toast(result.Message, result.Ok);
                        }
                        else
                        {
                            s_strokePoints.Clear();
                            s_strokeNormals.Clear();
                            CableSceneGuide.Toast(Tr("msg.strokeTooShort"), false);
                            RaiseWorkChanged();
                        }
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
                        RaiseWorkChanged();
                    }
                    else
                    {
                        EndWork();
                    }
                    needsRepaint = true;
                    e.Use();
                    break;
            }

            if (needsRepaint) SceneView.RepaintAll();
        }

        static void AppendStrokeSample(in MeshRayHit hit, Vector2 guiPos)
        {
            if (s_strokePoints.Count > 0 &&
                (hit.point - s_strokePoints[s_strokePoints.Count - 1]).sqrMagnitude < CableCommands.kVectorEpsilonSqr)
                return;
            s_strokePoints.Add(hit.point);
            s_strokeNormals.Add(hit.normal);
            s_lastSampleGuiPos = guiPos;
        }

        static void DrawStrokePreview()
        {
            Handles.color = DennokoSceneColors.SelectedLine;
            if (s_hasHoverHit && !s_isStroking)
            {
                float size = HandleUtility.GetHandleSize(s_hoverPoint) * 0.05f;
                Handles.SphereHandleCap(0, s_hoverPoint, Quaternion.identity, size, EventType.Repaint);
            }
            if (s_strokePoints.Count >= 2)
                DennokoSceneColors.Polyline(s_strokePoints.ToArray(), DennokoSceneColors.SelectedLine);
        }

        static CableResult ApplySplineFromStroke(CableGenerator cableGen)
        {
            if (cableGen == null || cableGen.Spline == null) return CableResult.Fail(string.Empty);
            var settings = CableToolSettings.instance;

            List<int> kept = StrokeSimplifier.SimplifyStroke(s_strokePoints, settings.drawSimplifyTol);
            if (kept.Count < 2) return CableResult.Fail(Tr("msg.strokeNoKnots"));

            Transform tf = cableGen.transform;
            int n = kept.Count;
            var localPositions = new Vector3[n];
            var localNormals = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 world = s_strokePoints[kept[i]] + s_strokeNormals[kept[i]] * settings.drawSurfaceOffset;
                localPositions[i] = tf.InverseTransformPoint(world);
                localNormals[i] = tf.InverseTransformDirection(s_strokeNormals[kept[i]]).normalized;
            }

            bool useMirrored = settings.drawKnotMode == 1;
            var newKnots = new List<CableKnot>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 forward = i < n - 1 ? localPositions[i + 1] - localPositions[i] : localPositions[i] - localPositions[i - 1];
                Quaternion rot = CableSpline.SafeLookRotation(forward, localNormals[i]);
                if (useMirrored)
                {
                    float distPrev = i > 0 ? Vector3.Distance(localPositions[i - 1], localPositions[i]) : 0f;
                    float distNext = i < n - 1 ? Vector3.Distance(localPositions[i], localPositions[i + 1]) : 0f;
                    float avgDist = (i > 0 && i < n - 1) ? (distPrev + distNext) * 0.5f : distPrev + distNext;
                    float len = avgDist / CableCommands.kLinearTangentDivisor;
                    newKnots.Add(new CableKnot(localPositions[i], new Vector3(0f, 0f, -len), new Vector3(0f, 0f, len), rot, CableTangentMode.Mirrored));
                }
                else
                {
                    newKnots.Add(new CableKnot(localPositions[i], Vector3.zero, Vector3.zero, rot, CableTangentMode.AutoSmooth));
                }
            }

            Undo.RecordObject(cableGen, Tr("undo.draw"));
            cableGen.Spline.ReplaceKnots(newKnots, false);   // 一括差し替え（通知 1 回・アタッチメントは比例対応で追従）
            EditorUtility.SetDirty(cableGen);
            CableKnotSelection.Clear();
            return CableResult.Success(Tr("msg.drawn", n));
        }
    }
}
