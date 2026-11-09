using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public static partial class CableCurveSampler
    {
        const float kEpsilonSqr = 1e-10f;

        // ================================================================
        //  Arc Length & Double Reflection RMF
        // ================================================================

        public static float ComputeArcLengths(Matrix4x4 localToWorld, List<CableSamplePoint> samples)
        {
            int count = samples.Count;
            if (count == 0) return 0f;

            float cumLen = 0f;
            var s0 = samples[0];
            s0.cumulativeLength = 0f;
            s0.normalizedDistance = 0f;
            samples[0] = s0;

            Vector3 prevWorld = localToWorld.MultiplyPoint3x4(s0.localPos);

            for (int i = 1; i < count; i++)
            {
                var s = samples[i];
                Vector3 currWorld = localToWorld.MultiplyPoint3x4(s.localPos);
                cumLen += Vector3.Distance(prevWorld, currWorld);
                s.cumulativeLength = cumLen;
                samples[i] = s;
                prevWorld = currWorld;
            }

            float invTotal = cumLen > 1e-6f ? 1f / cumLen : 0f;
            for (int i = 1; i < count; i++)
            {
                var s = samples[i];
                s.normalizedDistance = s.cumulativeLength * invTotal;
                samples[i] = s;
            }

            return cumLen;
        }

        /// <summary>
        /// Wang et al. (2008) の Double Reflection 法による Rotation Minimizing Frames (RMF) 構築。
        /// 3次元空間で急激に曲がっても法線が反転しない安定した断面基底を生成する。
        /// </summary>
        public static void BuildRotationMinimizingFrames(
            CableSpline spline,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToLocal,
            Vector3 localGravity,
            List<CableSamplePoint> samples)
        {
            int count = samples.Count;
            if (count == 0) return;

            // 初期フレーム (i = 0)
            var first = samples[0];
            Vector3 t0 = first.localTangent.sqrMagnitude > kEpsilonSqr ? first.localTangent.normalized : Vector3.forward;
            Vector3 u0 = first.localUp.sqrMagnitude      > kEpsilonSqr ? first.localUp.normalized      : Vector3.up;

            // t0 と u0 が平行に近い場合のフォールバック
            Vector3 r0 = Vector3.Cross(u0, t0);
            if (r0.sqrMagnitude < 1e-6f)
            {
                Vector3 fallbackUp = Mathf.Abs(Vector3.Dot(t0, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
                r0 = Vector3.Cross(fallbackUp, t0);
            }
            r0.Normalize();
            u0 = Vector3.Cross(t0, r0).normalized;

            first.localTangent = t0;
            first.localRight   = r0;
            first.localUp      = u0;
            samples[0] = first;

            Vector3 prevPos = first.localPos;
            Vector3 prevT   = t0;
            Vector3 prevR   = r0;
            Vector3 prevU   = u0;

            for (int i = 1; i < count; i++)
            {
                var curr = samples[i];
                Vector3 currPos = curr.localPos;
                Vector3 currT   = curr.localTangent.sqrMagnitude > kEpsilonSqr ? curr.localTangent.normalized : prevT;

                // Double Reflection RMF
                Vector3 v1 = currPos - prevPos;
                float c1 = v1.sqrMagnitude;
                Vector3 rL, uL;

                if (c1 > 1e-10f)
                {
                    rL = prevR - (2f / c1) * Vector3.Dot(v1, prevR) * v1;
                    Vector3 tL = prevT - (2f / c1) * Vector3.Dot(v1, prevT) * v1;

                    Vector3 v2 = currT - tL;
                    float c2 = v2.sqrMagnitude;
                    if (c2 > 1e-10f)
                        rL = rL - (2f / c2) * Vector3.Dot(v2, rL) * v2;

                    uL = Vector3.Cross(currT, rL);
                    if (uL.sqrMagnitude < 1e-8f)
                    {
                        uL = prevU;
                        rL = Vector3.Cross(uL, currT);
                    }
                    rL.Normalize();
                    uL.Normalize();
                }
                else
                {
                    rL = prevR;
                    uL = prevU;
                }

                curr.localTangent = currT;
                curr.localRight   = rL;
                curr.localUp      = uL;
                samples[i] = curr;

                prevPos = currPos;
                prevT   = currT;
                prevR   = rL;
                prevU   = uL;
            }
        }
    }
}
