using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal static partial class CableSceneHandles
    {
        // ================================================================
        //  2点配線 — 表面を始点・終点の順にクリック（コライダー不要 BVH + ゴーストプレビュー）
        // ================================================================

        static CableGenerator s_pickingTarget;
        static int s_pickCount;
        static readonly Vector3[] s_pickedPoints = new Vector3[2];
        static readonly Vector3[] s_pickedNormals = new Vector3[2];
        static bool s_hasPickHover;
        static Vector3 s_pickHoverPoint;
        static Vector3 s_pickHoverNormal;
        static MeshRaycastSession s_pickSession;

        public static void StartPicking(CableGenerator target)
        {
            if (target == null) return;
            CancelDrawing();
            s_pickSession?.Dispose();
            s_pickingTarget = target;
            s_pickCount = 0;
            s_hasPickHover = false;
            s_pickSession = new MeshRaycastSession(CableToolSettings.instance.hitLayerMask, target.gameObject);
            RaiseWorkChanged();
        }

        /// <summary>始点からやり直す（作業は続ける）。</summary>
        public static void RestartPicking()
        {
            s_pickCount = 0;
            RaiseWorkChanged();
        }

        static void CancelPicking()
        {
            s_pickSession?.Dispose();
            s_pickSession = null;
            s_pickingTarget = null;
            s_pickCount = 0;
            s_hasPickHover = false;
            CableSceneGuide.Hide();
        }

        static void HandlePicking(CableGenerator gen)
        {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            DrawPickedPoints();
            CableSceneGuide.Show(GuideText(gen));

            Event e = Event.current;
            s_pickSession ??= new MeshRaycastSession(CableToolSettings.instance.hitLayerMask, gen.gameObject);

            if (e.type == EventType.MouseMove)
            {
                s_hasPickHover = s_pickSession.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out MeshRayHit hoverHit);
                if (s_hasPickHover)
                {
                    s_pickHoverPoint = hoverHit.point;
                    s_pickHoverNormal = hoverHit.normal;
                }
                SceneView.RepaintAll();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (s_pickSession.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out MeshRayHit hit))
                {
                    float offset = gen.profile != null ? gen.profile.GetMaxRadius() * gen.radiusScale : 0.003f;
                    s_pickedPoints[s_pickCount] = hit.point + hit.normal * offset;
                    s_pickedNormals[s_pickCount] = hit.normal;
                    s_pickCount++;

                    if (s_pickCount >= 2)
                    {
                        ApplySplineFromPoints(gen, s_pickedPoints[0], s_pickedNormals[0], s_pickedPoints[1], s_pickedNormals[1]);
                        EndWork();
                        CableSceneGuide.Toast(Tr("msg.wired"), true);
                    }
                    else
                    {
                        RaiseWorkChanged();   // バナーの文言を「終点を…」にする
                    }
                }
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                EndWork();
                e.Use();
            }
        }

        static void DrawPickedPoints()
        {
            if (Event.current.type != EventType.Repaint) return;

            for (int i = 0; i < s_pickCount; i++)
            {
                Handles.color = i == 0 ? DennokoSceneColors.HoverAdd : DennokoSceneColors.SelectedLine;
                float size = HandleUtility.GetHandleSize(s_pickedPoints[i]) * 0.07f;
                Handles.SphereHandleCap(0, s_pickedPoints[i], Quaternion.identity, size, EventType.Repaint);
                Handles.DrawLine(s_pickedPoints[i], s_pickedPoints[i] + s_pickedNormals[i] * size * 4f, 2f);
                Handles.ArrowHandleCap(0, s_pickedPoints[i], CableSpline.SafeLookRotation(s_pickedNormals[i]), size * 2.5f, EventType.Repaint);
            }

            if (!s_hasPickHover) return;

            // ホバー地点と、1 点目からのゴーストケーブル
            Handles.color = s_pickCount == 0 ? DennokoSceneColors.HoverAdd : DennokoSceneColors.SelectedLine;
            float hoverSize = HandleUtility.GetHandleSize(s_pickHoverPoint) * 0.06f;
            Handles.SphereHandleCap(0, s_pickHoverPoint, Quaternion.identity, hoverSize, EventType.Repaint);

            if (s_pickCount == 1)
            {
                Vector3 pA = s_pickedPoints[0];
                Vector3 pB = s_pickHoverPoint;
                float tanLen = Vector3.Distance(pA, pB) / 3f * CableToolSettings.instance.pickTangentScale;
                Handles.DrawBezier(pA, pB, pA + s_pickedNormals[0] * tanLen, pB + s_pickHoverNormal * tanLen,
                    DennokoSceneColors.SelectedLine, null, 3f);
            }
        }

        /// <summary>2 点（ワールド座標 + 面の法線）から、両端が面から立ち上がるケーブルにする。</summary>
        public static void ApplySplineFromPoints(CableGenerator cableGen, Vector3 worldA, Vector3 normalA, Vector3 worldB, Vector3 normalB)
        {
            if (cableGen == null || cableGen.Spline == null) return;
            Transform tf = cableGen.transform;

            Vector3 localA = tf.InverseTransformPoint(worldA);
            Vector3 localB = tf.InverseTransformPoint(worldB);
            Vector3 localNormalA = tf.InverseTransformDirection(normalA).normalized;
            Vector3 localNormalB = tf.InverseTransformDirection(normalB).normalized;
            float tangentLen = Vector3.Distance(localA, localB) / 3f * CableToolSettings.instance.pickTangentScale;

            Quaternion rotA = CableSpline.SafeLookRotation(localNormalA, localNormalA);
            Quaternion rotB = CableSpline.SafeLookRotation(-localNormalB, localNormalB);

            Undo.RecordObject(cableGen, Tr("undo.wire"));
            // 一括差し替え（アタッチメントは始点→始点・終点→終点に追従する）
            cableGen.Spline.ReplaceKnots(new[]
            {
                new CableKnot(localA, new Vector3(0f, 0f, -tangentLen), new Vector3(0f, 0f, tangentLen), rotA, CableTangentMode.Mirrored),
                new CableKnot(localB, new Vector3(0f, 0f, -tangentLen), new Vector3(0f, 0f, tangentLen), rotB, CableTangentMode.Mirrored),
            }, false);
            EditorUtility.SetDirty(cableGen);
            CableKnotSelection.Clear();
        }
    }
}
