using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public static partial class SplineKnotOperations
    {
        public static bool SubdivideSplineKnots(CableGenerator cableGen, CableTangentMode addedKnotMode)
        {
            LastMessage = string.Empty;
            if (cableGen == null || cableGen.Spline == null) return false;

            var spline    = cableGen.Spline;
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                LastMessage = CableL10n.Tr("msg.subdivideNeedsTwo");
                return false;
            }

            bool closed     = spline.Closed;
            int  curveCount = closed ? knotCount : knotCount - 1;
            Vector3 localGravity = cableGen.transform.InverseTransformVector(Vector3.down);

            var inserted = new CableKnot[curveCount];
            for (int i = 0; i < curveCount; i++)
            {
                EvaluateSegmentArcLengthMidpoint(spline, i, localGravity,
                    out Vector3 pos, out Vector3 tan, out Vector3 up);

                Vector3 tangent = tan.sqrMagnitude > kVectorEpsilonSqr ? tan.normalized : Vector3.forward;
                Quaternion rot  = CableSpline.SafeLookRotation(tangent, up);
                float      len  = tan.magnitude / kMidpointTangentDivisor;

                inserted[i] = new CableKnot(
                    pos,
                    new Vector3(0f, 0f, -len),
                    new Vector3(0f, 0f,  len),
                    rot,
                    addedKnotMode);
            }

            var originals = new CableKnot[knotCount];
            for (int i = 0; i < knotCount; i++)
                originals[i] = spline[i];

            Undo.RecordObject(cableGen, CableL10n.Tr("undo.subdivide"));
            ReplaceWithInterleavedKnots(spline, originals, inserted);

            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();

            LastMessage = CableL10n.Tr("msg.subdivided", knotCount, spline.Count);
            return true;
        }

        public static bool RedistributeKnotsBetweenEndpoints(CableGenerator cableGen, int divisionCount, CableTangentMode addedKnotMode)
        {
            LastMessage = string.Empty;
            if (cableGen == null || cableGen.Spline == null) return false;

            var spline    = cableGen.Spline;
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                LastMessage = CableL10n.Tr("msg.notEnoughKnots");
                return false;
            }
            if (spline.Closed)
            {
                LastMessage = CableL10n.Tr("msg.divideNotForLoop");
                return false;
            }

            int targetKnotCount = Mathf.Max(2, divisionCount + 1);

            Vector3 start       = spline[0].position;
            Vector3 end         = spline[knotCount - 1].position;
            Vector3 dir         = end - start;
            float   totalLength = dir.magnitude;

            Vector3    forward          = totalLength > kVectorEpsilon ? dir / totalLength : Vector3.forward;
            Quaternion interiorRotation = CableSpline.SafeLookRotation(forward);
            float      tangentLen       = totalLength / Mathf.Max(1, divisionCount) / kLinearTangentDivisor;

            var newKnots = new List<CableKnot>(targetKnotCount) { spline[0] };
            for (int i = 1; i < targetKnotCount - 1; i++)
            {
                float   t   = (float)i / (targetKnotCount - 1);
                Vector3 pos = Vector3.Lerp(start, end, t);

                newKnots.Add(new CableKnot(
                    pos,
                    new Vector3(0f, 0f, -tangentLen),
                    new Vector3(0f, 0f,  tangentLen),
                    interiorRotation,
                    addedKnotMode));
            }
            newKnots.Add(spline[knotCount - 1]);

            Undo.RecordObject(cableGen, CableL10n.Tr("undo.divide"));
            // 中間ノットのアタッチメントは位置比率で新しいノットに割り当てる（削除はしない）
            spline.ReplaceKnots(newKnots, false);

            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();

            LastMessage = CableL10n.Tr("msg.divided", divisionCount, targetKnotCount);
            return true;
        }

        public static void EvaluateSegmentArcLengthMidpoint(
            CableSpline spline, int segmentIndex, Vector3 localGravity,
            out Vector3 pos, out Vector3 tan, out Vector3 up, int samples = 64)
        {
            var cumLen = new float[samples + 1];
            var pts    = new Vector3[samples + 1];
            cumLen[0] = 0f;
            spline.EvaluateSegment(segmentIndex, 0f, localGravity, out pts[0], out _, out _);

            for (int j = 1; j <= samples; j++)
            {
                float s = (float)j / samples;
                spline.EvaluateSegment(segmentIndex, s, localGravity, out pts[j], out _, out _);
                cumLen[j] = cumLen[j - 1] + Vector3.Distance(pts[j], pts[j - 1]);
            }

            float halfLen = cumLen[samples] * 0.5f;
            float tMid = 0.5f;
            for (int j = 1; j <= samples; j++)
            {
                if (cumLen[j] >= halfLen)
                {
                    float span = cumLen[j] - cumLen[j - 1];
                    float frac = span > 1e-9f ? (halfLen - cumLen[j - 1]) / span : 0f;
                    tMid = ((float)(j - 1) + frac) / samples;
                    break;
                }
            }

            spline.EvaluateSegment(segmentIndex, tMid, localGravity, out pos, out tan, out up);
        }
    }
}

