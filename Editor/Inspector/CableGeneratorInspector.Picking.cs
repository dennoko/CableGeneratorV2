using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Picking Mode — Scene ビューからの 2点選択（コライダー不要 BVH 対応 + ゴーストプレビュー）
        // ================================================================

        static bool    s_hasPickHover;
        static Vector3 s_pickHoverPoint;
        static Vector3 s_pickHoverNormal;

        void DrawPickedPoints()
        {
            for (int i = 0; i < s_pickCount; i++)
            {
                Handles.color = i == 0 ? new Color(0.2f, 1f, 0.3f) : new Color(0.3f, 0.6f, 1f);

                float   size      = HandleUtility.GetHandleSize(s_pickedPoints[i]) * 0.07f;
                Vector3 normalEnd = s_pickedPoints[i] + s_pickedNormals[i] * size * 4f;

                Handles.SphereHandleCap(0, s_pickedPoints[i], Quaternion.identity, size, EventType.Repaint);
                Handles.DrawLine(s_pickedPoints[i], normalEnd, 2f);
                Handles.ArrowHandleCap(0, s_pickedPoints[i],
                    CableSpline.SafeLookRotation(s_pickedNormals[i]),
                    size * 2.5f, EventType.Repaint);
            }

            // ホバー地点のプレビューと1点目からのゴーストケーブル描画
            if (s_hasPickHover)
            {
                Handles.color = s_pickCount == 0
                    ? new Color(0.2f, 1f, 0.3f, 0.85f)
                    : new Color(0.3f, 0.7f, 1f, 0.85f);

                float size = HandleUtility.GetHandleSize(s_pickHoverPoint) * 0.06f;
                Handles.SphereHandleCap(0, s_pickHoverPoint, Quaternion.identity, size, EventType.Repaint);

                if (s_pickCount == 1)
                {
                    Handles.color = new Color(0.3f, 0.9f, 1f, 0.75f);
                    Vector3 pA = s_pickedPoints[0];
                    Vector3 pB = s_pickHoverPoint;
                    float dist = Vector3.Distance(pA, pB);
                    float tanLen = dist / 3f * s_tangentScale;
                    Vector3 p1 = pA + s_pickedNormals[0] * tanLen;
                    Vector3 p2 = pB + s_pickHoverNormal  * tanLen;
                    Handles.DrawBezier(pA, pB, p1, p2, new Color(0.3f, 0.9f, 1f, 0.8f), null, 3f);
                }
            }
        }

        static GUIStyle s_sceneHintStyle;

        static void DrawSceneHintLabel()
        {
            DrawSceneHintText(s_pickCount == 0
                ? "[ 2点配線モード ]  1点目（始点）の表面をクリック   Esc: キャンセル"
                : "[ 2点配線モード ]  2点目（終点）の表面をクリック   Esc: キャンセル");
        }

        static void DrawSceneHintText(string msg)
        {
            Handles.BeginGUI();

            if (s_sceneHintStyle == null)
                s_sceneHintStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize  = 12,
                    normal    = { textColor = Color.white },
                };

            var   sceneView = SceneView.currentDrawingSceneView;
            float viewWidth = sceneView != null
                ? sceneView.position.width
                : Screen.width / EditorGUIUtility.pixelsPerPoint;

            Vector2 size = s_sceneHintStyle.CalcSize(new GUIContent(msg));
            float   x    = (viewWidth - size.x) * 0.5f;
            GUI.Box(new Rect(x, 42, size.x + 20, size.y + 8), msg, s_sceneHintStyle);
            Handles.EndGUI();
        }

        static MeshRaycastSession s_pickSession;

        public static void StartPickingMode(CableGenerator target)
        {
            CancelDrawMode();
            s_pickSession?.Dispose();
            s_pickingTarget = target;
            s_pickCount     = 0;
            s_hasPickHover  = false;
            s_pickSession   = new MeshRaycastSession(~0, target.gameObject);
            SceneView.RepaintAll();
        }

        public static void CancelPickingMode()
        {
            s_pickSession?.Dispose();
            s_pickSession   = null;
            s_pickingTarget = null;
            s_pickCount     = 0;
            s_hasPickHover  = false;
            SceneView.RepaintAll();
        }

        void HandlePickingSceneGUI(CableGenerator gen)
        {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            DrawPickedPoints();
            DrawSceneHintLabel();

            Event e = Event.current;
            if (s_pickSession == null)
                s_pickSession = new MeshRaycastSession(~0, gen.gameObject);

            if (e.type == EventType.MouseMove)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                s_hasPickHover = s_pickSession.Raycast(ray, out MeshRayHit hoverHit);
                if (s_hasPickHover)
                {
                    s_pickHoverPoint  = hoverHit.point;
                    s_pickHoverNormal = hoverHit.normal;
                }
                SceneView.RepaintAll();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (s_pickSession.Raycast(ray, out MeshRayHit hit))
                {
                    float offset = gen.profile != null ? gen.profile.GetMaxRadius() * gen.radiusScale : 0.003f;
                    s_pickedPoints[s_pickCount]  = hit.point + hit.normal * offset;
                    s_pickedNormals[s_pickCount] = hit.normal;
                    s_pickCount++;

                    if (s_pickCount >= 2)
                    {
                        ApplySplineFromPoints(gen,
                            s_pickedPoints[0], s_pickedNormals[0],
                            s_pickedPoints[1], s_pickedNormals[1]);
                        CancelPickingMode();
                    }

                    Repaint();
                    SceneView.RepaintAll();
                }
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelPickingMode();
                e.Use();
            }
        }

        // ================================================================
        //  Spline from Surface Points
        // ================================================================

        public static void ApplySplineFromPoints(
            CableGenerator cableGen,
            Vector3 worldA, Vector3 normalA,
            Vector3 worldB, Vector3 normalB)
        {
            if (cableGen == null || cableGen.Spline == null) return;

            Transform tf = cableGen.transform;

            Vector3 localA       = tf.InverseTransformPoint(worldA);
            Vector3 localB       = tf.InverseTransformPoint(worldB);
            Vector3 localNormalA = tf.InverseTransformDirection(normalA).normalized;
            Vector3 localNormalB = tf.InverseTransformDirection(normalB).normalized;

            float tangentLen = Vector3.Distance(localA, localB) / 3f * s_tangentScale;

            Quaternion rotA = CableSpline.SafeLookRotation(localNormalA,  localNormalA);
            Quaternion rotB = CableSpline.SafeLookRotation(-localNormalB, localNormalB);

            Undo.RecordObject(cableGen, "Set Spline from Surface Points");

            // 一括差し替え（アタッチメントは始点→始点・終点→終点に追従する）
            cableGen.Spline.ReplaceKnots(new[]
            {
                new CableKnot(localA, new Vector3(0f, 0f, -tangentLen), new Vector3(0f, 0f, tangentLen), rotA, CableTangentMode.Mirrored),
                new CableKnot(localB, new Vector3(0f, 0f, -tangentLen), new Vector3(0f, 0f, tangentLen), rotB, CableTangentMode.Mirrored),
            }, false);

            EditorUtility.SetDirty(cableGen);
        }
    }
}

