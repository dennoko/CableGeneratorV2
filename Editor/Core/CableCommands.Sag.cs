using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal static partial class CableCommands
    {
        // ================================================================
        //  たるみ — 区間ごとの sag（V2）と、V1 互換の「たわみ制御点」
        // ================================================================

        /// <summary>全区間に同じたるみを入れる（形タブ「全体」・シーンの ▼ と同じ値）。</summary>
        public static void SetAllSag(CableGenerator gen, float sag)
        {
            if (gen == null || gen.Spline == null) return;
            var spline = gen.Spline;
            Undo.RecordObject(gen, Tr("undo.sag"));
            for (int i = 0; i < spline.SegmentCount; i++)
                spline.SetSegmentSag(i, Mathf.Max(0f, sag), false);
            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(gen);
        }

        public static void SetSegmentSag(CableGenerator gen, int segment, float sag)
        {
            if (gen == null || segment < 0 || segment >= gen.Spline.SegmentCount) return;
            Undo.RecordObject(gen, Tr("undo.sag"));
            gen.Spline.SetSegmentSag(segment, Mathf.Max(0f, sag));
            EditorUtility.SetDirty(gen);
        }

        /// <summary>全区間が同じたるみなら true（全体スライダーに値を出せる）。</summary>
        public static bool TryGetUniformSag(CableGenerator gen, out float sag)
        {
            sag = 0f;
            var spline = gen != null ? gen.Spline : null;
            if (spline == null || spline.SegmentCount == 0) return true;
            sag = spline.GetSegmentSag(0);
            for (int i = 1; i < spline.SegmentCount; i++)
                if (Mathf.Abs(spline.GetSegmentSag(i) - sag) > 1e-5f) return false;
            return true;
        }

        // ─── V1 互換: たわみ制御点（区間の中点に制御点を足して下げる） ──────

        static CableGenerator s_sagTarget;
        static Vector3[] s_sagBasePositions;
        static int s_sagOriginalCount;
        static bool s_sagWasClosed;

        public static float SagDropDistance = 0.5f;
        public static float SagHandleLength = 0.5f;
        public static bool  SagUseMirrored;

        /// <summary>たわみ制御点を挿入した直後で、まだ降下距離を調整できる状態か。</summary>
        public static bool IsAdjustingSagKnots(CableGenerator gen)
        {
            if (s_sagTarget == null || s_sagTarget != gen || s_sagBasePositions == null) return false;
            int expected = s_sagWasClosed ? s_sagOriginalCount * 2 : s_sagOriginalCount * 2 - 1;
            return gen.Spline.Count == expected;
        }

        public static CableResult InsertSagKnots(CableGenerator cableGen)
        {
            s_sagTarget = null;
            if (cableGen == null || cableGen.Spline == null) return CableResult.Fail(string.Empty);

            var spline = cableGen.Spline;
            int knotCount = spline.Count;
            if (knotCount < 2) return CableResult.Fail(Tr("msg.sagNeedsTwo"));

            bool closed = spline.Closed;
            int curveCount = closed ? knotCount : knotCount - 1;

            var knots = new CableKnot[knotCount];
            for (int i = 0; i < knotCount; i++) knots[i] = spline[i];

            var basePositions = new Vector3[curveCount];
            var inserted = new CableKnot[curveCount];
            CableTangentMode sagMode = SagUseMirrored ? CableTangentMode.Mirrored : CableTangentMode.AutoSmooth;

            for (int i = 0; i < curveCount; i++)
            {
                Vector3 localA = knots[i].position;
                Vector3 localB = knots[(i + 1) % knotCount].position;
                basePositions[i] = (localA + localB) * 0.5f;

                Vector3 segDir = localB - localA;
                segDir = segDir.sqrMagnitude < kVectorEpsilonSqr ? Vector3.forward : segDir.normalized;

                inserted[i] = new CableKnot(basePositions[i], new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f),
                    CableSpline.SafeLookRotation(segDir)) { mode = sagMode };
            }

            Undo.RecordObject(cableGen, Tr("undo.sagKnots"));
            SplineKnotOperations.ReplaceWithInterleavedKnots(spline, knots, inserted);

            s_sagTarget = cableGen;
            s_sagBasePositions = basePositions;
            s_sagOriginalCount = knotCount;
            s_sagWasClosed = closed;

            EditorUtility.SetDirty(cableGen);
            UpdateSagKnots(cableGen);
            return CableResult.Success(Tr("msg.sagKnotsInserted", curveCount, knotCount, spline.Count));
        }

        /// <summary>挿入したたわみ制御点を SagDropDistance だけ下げる（SagUseMirrored ならハンドル長も）。</summary>
        public static void UpdateSagKnots(CableGenerator cableGen)
        {
            if (!IsAdjustingSagKnots(cableGen)) return;

            var spline = cableGen.Spline;
            Vector3 localOffset = cableGen.transform.InverseTransformVector(Vector3.down * SagDropDistance);
            CableTangentMode targetMode = SagUseMirrored ? CableTangentMode.Mirrored : CableTangentMode.AutoSmooth;

            Undo.RecordObject(cableGen, Tr("undo.sagKnots"));
            for (int i = 0; i < s_sagBasePositions.Length; i++)
            {
                int splineIdx = 2 * i + 1;
                var k = spline[splineIdx];
                k.position = s_sagBasePositions[i] + localOffset;
                if (SagUseMirrored)
                {
                    k.tangentIn  = new Vector3(0f, 0f, -SagHandleLength);
                    k.tangentOut = new Vector3(0f, 0f,  SagHandleLength);
                }
                k.mode = targetMode;
                spline.SetKnot(splineIdx, k, false);
            }
            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();
        }
    }
}
