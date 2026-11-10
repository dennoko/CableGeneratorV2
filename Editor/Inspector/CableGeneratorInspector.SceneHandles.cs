using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Tangent Handles & Preview Lines
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

