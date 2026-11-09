using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public struct CableSamplePoint
    {
        public Vector3 localPos;
        public Vector3 localTangent;
        public Vector3 localRight;
        public Vector3 localUp;
        public float   cumulativeLength;
        public float   normalizedDistance;
        public float   splineT;
    }

    /// <summary>
    /// CableSpline からメッシュ押出用のサンプル点列を生成するサンプラー。
    /// ・曲率適応型（Adaptive）サンプリングによる直線部の頂点削減（50〜80%削減）
    /// ・V1 互換の均等（Uniform）サンプリング
    /// ・Double Reflection RMF（Rotation Minimizing Frames）によるねじれ・裏返り防止
    /// ・コライダーへのリアルタイム接地クランプ（Auto-Draping）
    /// </summary>
    public static class CableCurveSampler
    {
        const float kEpsilonSqr = 1e-10f;
        const int   kMaxAdaptiveDepth = 5; // 1初期区間あたり最大32分割

        /// <summary>
        /// スプラインをサンプリングし、RMF 座標系と累積弧長を計算したリストを出力する。
        /// </summary>
        public static float SampleSpline(
            CableSpline spline,
            Transform cableTransform,
            bool adaptive,
            int uniformResolution,
            float adaptiveAngleDegrees,
            float maxStepLength,
            bool enableSurfaceCollision,
            float collisionClearance,
            LayerMask collisionMask,
            List<CableSamplePoint> outSamples)
        {
            outSamples.Clear();
            if (spline == null || spline.Count < 2) return 0f;

            Matrix4x4 localToWorld = cableTransform != null ? cableTransform.localToWorldMatrix : Matrix4x4.identity;
            Matrix4x4 worldToLocal = cableTransform != null ? cableTransform.worldToLocalMatrix : Matrix4x4.identity;
            Vector3 localGravity = worldToLocal.MultiplyVector(Vector3.down);
            if (localGravity.sqrMagnitude < kEpsilonSqr)
                localGravity = Vector3.down;
            else
                localGravity.Normalize();

            if (adaptive)
            {
                BuildAdaptiveRawSamples(
                    spline, localToWorld, localGravity,
                    Mathf.Clamp(adaptiveAngleDegrees, 1f, 45f),
                    maxStepLength,
                    outSamples);
            }
            else
            {
                BuildUniformRawSamples(
                    spline, localGravity,
                    Mathf.Clamp(uniformResolution, 2, 512),
                    outSamples);
            }

            if (outSamples.Count < 2) return 0f;

            // コライダー接地（めり込み防止）パス
            if (enableSurfaceCollision && cableTransform != null)
            {
                ApplyColliderDraping(
                    spline, cableTransform, localToWorld, worldToLocal,
                    Mathf.Max(0.001f, collisionClearance), collisionMask,
                    outSamples);
            }

            // 累積ワールド弧長と正規化距離の計算
            float totalLength = ComputeArcLengths(localToWorld, outSamples);

            // Double Reflection RMF によるねじれ防止フレーム構築
            BuildRotationMinimizingFrames(spline, localToWorld, worldToLocal, localGravity, outSamples);

            return totalLength;
        }

        // ================================================================
        //  Uniform Sampling (V1 互換)
        // ================================================================

        static void BuildUniformRawSamples(
            CableSpline spline,
            Vector3 localGravity,
            int resolution,
            List<CableSamplePoint> outSamples)
        {
            for (int i = 0; i <= resolution; i++)
            {
                float t = (float)i / resolution;
                spline.Evaluate(t, localGravity, out Vector3 pos, out Vector3 tan, out Vector3 up);
                outSamples.Add(new CableSamplePoint
                {
                    localPos     = pos,
                    localTangent = tan.sqrMagnitude > kEpsilonSqr ? tan.normalized : Vector3.forward,
                    localUp      = up.sqrMagnitude  > kEpsilonSqr ? up.normalized  : Vector3.up,
                    splineT      = t,
                });
            }
        }

        // ================================================================
        //  Curvature-Adaptive Sampling (V2 軽量化コア)
        // ================================================================

        static void BuildAdaptiveRawSamples(
            CableSpline spline,
            Matrix4x4 localToWorld,
            Vector3 localGravity,
            float maxAngleDeg,
            float maxStepLength,
            List<CableSamplePoint> outSamples)
        {
            int segCount = spline.SegmentCount;
            float cosThreshold = Mathf.Cos(maxAngleDeg * Mathf.Deg2Rad);

            for (int seg = 0; seg < segCount; seg++)
            {
                spline.GetSegmentControlPoints(seg, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float sag);

                // 直線判定: 制御点がほぼ一直線上かつ sag がゼロなら最小分割（端点のみ）
                bool isStraight = sag <= 1e-5f && IsSegmentCollinear(p0, p1, p2, p3, 1e-4f);

                if (seg == 0)
                {
                    AddEvaluatedPoint(spline, seg, 0f, segCount, localGravity, outSamples);
                }

                if (isStraight && maxStepLength <= 0f)
                {
                    AddEvaluatedPoint(spline, seg, 1f, segCount, localGravity, outSamples);
                    continue;
                }

                // S字変曲点やたわみ頂点を跨いでスキップしないよう、初期分割を2区間（u=0, 0.5, 1.0）から開始
                SubdivideSegmentRecursive(
                    spline, seg, segCount, 0f, 0.5f,
                    localToWorld, localGravity, cosThreshold, maxStepLength, 0, outSamples);
                SubdivideSegmentRecursive(
                    spline, seg, segCount, 0.5f, 1f,
                    localToWorld, localGravity, cosThreshold, maxStepLength, 0, outSamples);
            }
        }

        static void SubdivideSegmentRecursive(
            CableSpline spline,
            int seg, int segCount,
            float uA, float uB,
            Matrix4x4 localToWorld,
            Vector3 localGravity,
            float cosThreshold,
            float maxStepLength,
            int depth,
            List<CableSamplePoint> outSamples)
        {
            float uMid = 0.5f * (uA + uB);

            spline.EvaluateSegment(seg, uA,   localGravity, out Vector3 posA, out Vector3 tanA, out _);
            spline.EvaluateSegment(seg, uMid, localGravity, out Vector3 posM, out Vector3 tanM, out _);
            spline.EvaluateSegment(seg, uB,   localGravity, out Vector3 posB, out Vector3 tanB, out _);

            Vector3 dirA = tanA.sqrMagnitude > kEpsilonSqr ? tanA.normalized : Vector3.forward;
            Vector3 dirM = tanM.sqrMagnitude > kEpsilonSqr ? tanM.normalized : dirA;
            Vector3 dirB = tanB.sqrMagnitude > kEpsilonSqr ? tanB.normalized : dirM;

            bool needSplit = false;
            if (depth < kMaxAdaptiveDepth)
            {
                // 1. 接線角度差チェック
                if (Vector3.Dot(dirA, dirB) < cosThreshold ||
                    Vector3.Dot(dirA, dirM) < cosThreshold ||
                    Vector3.Dot(dirM, dirB) < cosThreshold)
                {
                    needSplit = true;
                }

                // 2. 弦誤差（Chord Error）チェック
                if (!needSplit)
                {
                    Vector3 chordMid = 0.5f * (posA + posB);
                    float chordLenSq = (posB - posA).sqrMagnitude;
                    float midErrSq   = (posM - chordMid).sqrMagnitude;
                    if (chordLenSq > 1e-6f && midErrSq > chordLenSq * 0.0004f)
                        needSplit = true;
                }

                // 3. ツイストプロファイル等の最大ステップ長チェック
                if (!needSplit && maxStepLength > 0.001f)
                {
                    float worldLen = Vector3.Distance(
                        localToWorld.MultiplyPoint3x4(posA),
                        localToWorld.MultiplyPoint3x4(posB));
                    if (worldLen > maxStepLength)
                        needSplit = true;
                }
            }

            if (needSplit)
            {
                SubdivideSegmentRecursive(spline, seg, segCount, uA, uMid, localToWorld, localGravity, cosThreshold, maxStepLength, depth + 1, outSamples);
                SubdivideSegmentRecursive(spline, seg, segCount, uMid, uB, localToWorld, localGravity, cosThreshold, maxStepLength, depth + 1, outSamples);
            }
            else
            {
                AddEvaluatedPoint(spline, seg, uB, segCount, localGravity, outSamples);
            }
        }

        static void AddEvaluatedPoint(
            CableSpline spline, int seg, float u, int segCount,
            Vector3 localGravity, List<CableSamplePoint> outSamples)
        {
            spline.EvaluateSegment(seg, u, localGravity, out Vector3 pos, out Vector3 tan, out Vector3 up);
            float globalT = (seg + u) / Mathf.Max(1, segCount);
            outSamples.Add(new CableSamplePoint
            {
                localPos     = pos,
                localTangent = tan.sqrMagnitude > kEpsilonSqr ? tan.normalized : Vector3.forward,
                localUp      = up.sqrMagnitude  > kEpsilonSqr ? up.normalized  : Vector3.up,
                splineT      = globalT,
            });
        }

        static bool IsSegmentCollinear(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float tol)
        {
            Vector3 chord = p3 - p0;
            float lenSq = chord.sqrMagnitude;
            if (lenSq < 1e-8f) return true;

            Vector3 dir = chord / Mathf.Sqrt(lenSq);
            Vector3 d1 = (p1 - p0) - Vector3.Dot(p1 - p0, dir) * dir;
            Vector3 d2 = (p2 - p0) - Vector3.Dot(p2 - p0, dir) * dir;
            float tolSq = tol * tol * lenSq;
            return d1.sqrMagnitude <= tolSq && d2.sqrMagnitude <= tolSq;
        }

        // ================================================================
        //  Collider Draping (床・障害物への自動接地)
        // ================================================================

        static void ApplyColliderDraping(
            CableSpline spline,
            Transform cableTransform,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToLocal,
            float clearance,
            LayerMask mask,
            List<CableSamplePoint> samples)
        {
            int count = samples.Count;
            if (count < 3) return;

            bool anyHit = false;

            for (int i = 1; i < count - 1; i++)
            {
                var s = samples[i];
                Vector3 worldPos = localToWorld.MultiplyPoint3x4(s.localPos);

                // スプラインの無重力基準高さ（または上空 2m）から下向きにレイキャストし、床や障害物の上に乗せる
                float probeHeight = 1.5f;
                Vector3 rayOrigin = worldPos + Vector3.up * probeHeight;
                float rayDist = probeHeight + clearance;

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDist, mask.value, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(cableTransform))
                        continue;

                    float minAllowedY = hit.point.y + clearance;
                    if (worldPos.y < minAllowedY)
                    {
                        worldPos.y = minAllowedY;
                        s.localPos = worldToLocal.MultiplyPoint3x4(worldPos);
                        samples[i] = s;
                        anyHit = true;
                    }
                }
            }

            if (!anyHit) return;

            // 接地境界のカクつきを和らげる軽量ラプラシアン平滑化（1パス）と接線再計算
            for (int i = 1; i < count - 1; i++)
            {
                var s = samples[i];
                s.localPos = 0.2f * samples[i - 1].localPos + 0.6f * s.localPos + 0.2f * samples[i + 1].localPos;
                samples[i] = s;
            }

            for (int i = 0; i < count; i++)
            {
                var s = samples[i];
                Vector3 tan = i == 0 ? (samples[1].localPos - samples[0].localPos)
                            : i == count - 1 ? (samples[count - 1].localPos - samples[count - 2].localPos)
                            : (samples[i + 1].localPos - samples[i - 1].localPos);
                if (tan.sqrMagnitude > kEpsilonSqr)
                    s.localTangent = tan.normalized;
                samples[i] = s;
            }
        }

        // ================================================================
        //  Arc Length & Double Reflection RMF
        // ================================================================

        static float ComputeArcLengths(Matrix4x4 localToWorld, List<CableSamplePoint> samples)
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
        static void BuildRotationMinimizingFrames(
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

