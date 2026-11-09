using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Tangent Scale
        // ================================================================

        public static void ApplyTangentScaleToSpline(CableGenerator cableGen, float scale)
        {
            if (cableGen == null || cableGen.Spline == null) return;

            var spline = cableGen.Spline;
            int count  = spline.Count;
            if (count == 0) return;

            bool      closed    = spline.Closed;
            Vector3[] positions = new Vector3[count];
            for (int i = 0; i < count; i++)
                positions[i] = spline[i].position;

            float[] baseLens = new float[count];
            for (int i = 0; i < count; i++)
            {
                float dPrev = 0f, dNext = 0f;
                if (closed)
                {
                    int prev = (i - 1 + count) % count;
                    int next = (i + 1) % count;
                    dPrev        = Vector3.Distance(positions[prev], positions[i]);
                    dNext        = Vector3.Distance(positions[i], positions[next]);
                    baseLens[i]  = ((dPrev + dNext) * 0.5f) / 3f * scale;
                }
                else
                {
                    if (i > 0)         dPrev = Vector3.Distance(positions[i - 1], positions[i]);
                    if (i < count - 1) dNext = Vector3.Distance(positions[i],     positions[i + 1]);

                    float avg   = (i > 0 && i < count - 1)
                        ? (dPrev + dNext) * 0.5f
                        : (dPrev > 0f ? dPrev : dNext);
                    baseLens[i] = (avg / 3f) * scale;
                }
            }

            Undo.RecordObject(cableGen, "Adjust Spline Tangent Strength");

            for (int i = 0; i < count; i++)
            {
                if (spline.GetTangentMode(i) == CableTangentMode.AutoSmooth) continue;

                var     k      = spline[i];
                float   len    = baseLens[i];
                Vector3 dirIn  = k.tangentIn.sqrMagnitude  > 1e-6f ? k.tangentIn.normalized  : new Vector3(0f, 0f, -1f);
                Vector3 dirOut = k.tangentOut.sqrMagnitude > 1e-6f ? k.tangentOut.normalized : new Vector3(0f, 0f,  1f);
                k.tangentIn  = dirIn  * len;
                k.tangentOut = dirOut * len;
                spline.SetKnot(i, k, false);
            }

            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(cableGen);
        }

        // ================================================================
        //  Cable Sag (V1 たわみノット挿入 ＆ V2 直接 sag 対応)
        // ================================================================

        public static void InsertSagKnots(CableGenerator cableGen)
        {
            s_sagLastResult = string.Empty;
            s_hasSagKnots   = false;

            if (cableGen == null || cableGen.Spline == null)
            {
                s_sagLastResult = "CableGenerator が見つかりません。";
                return;
            }

            var spline    = cableGen.Spline;
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                s_sagLastResult = "たわみ挿入には最低2つのノットが必要です。";
                return;
            }

            bool closed     = spline.Closed;
            int  curveCount = closed ? knotCount : knotCount - 1;

            var knots = new CableKnot[knotCount];
            for (int i = 0; i < knotCount; i++)
                knots[i] = spline[i];

            var basePositions = new Vector3[curveCount];
            var inserted      = new CableKnot[curveCount];

            for (int i = 0; i < curveCount; i++)
            {
                int     nextIdx = (i + 1) % knotCount;
                Vector3 localA  = knots[i].position;
                Vector3 localB  = knots[nextIdx].position;
                basePositions[i] = (localA + localB) * 0.5f;

                Vector3 segDir = localB - localA;
                if (segDir.sqrMagnitude < kVectorEpsilonSqr) segDir = Vector3.forward;
                else segDir.Normalize();

                inserted[i] = new CableKnot(
                    basePositions[i],
                    new Vector3(0f, 0f, -0.5f),
                    new Vector3(0f, 0f,  0.5f),
                    CableSpline.SafeLookRotation(segDir));
            }

            Undo.RecordObject(cableGen, "Insert Cable Sag Knots");
            CableTangentMode sagMode = s_sagUseMirrored ? CableTangentMode.Mirrored : CableTangentMode.AutoSmooth;
            for (int i = 0; i < curveCount; i++)
                inserted[i].mode = sagMode;

            SplineKnotOperations.ReplaceWithInterleavedKnots(spline, knots, inserted);

            s_sagTarget        = cableGen;
            s_sagBasePositions = basePositions;
            s_sagOriginalCount = knotCount;
            s_sagWasClosed     = closed;
            s_hasSagKnots      = true;

            EditorUtility.SetDirty(cableGen);
            UpdateSagKnots(cableGen, s_sagDropDistance, s_sagHandleLength);

            s_sagLastResult = $"区間 {curveCount} にたわみノットを挿入しました（{knotCount} → {spline.Count} ノット）。";
        }

        public static void UpdateSagKnots(CableGenerator cableGen, float dropDistance, float handleLength)
        {
            if (!s_hasSagKnots || s_sagBasePositions == null) return;
            if (cableGen == null || cableGen.Spline == null) return;

            // たわみノットを挿入したケーブル以外には適用しない（別ケーブルを選択した場合の誤適用防止）
            if (s_sagTarget != cableGen)
            {
                s_hasSagKnots   = false;
                s_sagLastResult = string.Empty;
                return;
            }

            var spline        = cableGen.Spline;
            int expectedCount = s_sagWasClosed ? s_sagOriginalCount * 2 : s_sagOriginalCount * 2 - 1;

            if (spline.Count != expectedCount)
            {
                s_hasSagKnots   = false;
                s_sagLastResult = "スプライン構造が変更されたため調整を終了しました。";
                return;
            }

            Transform tf          = cableGen.transform;
            Vector3   localOffset = tf.InverseTransformVector(Vector3.down * dropDistance);
            int       sagCount    = s_sagBasePositions.Length;

            CableTangentMode targetMode = s_sagUseMirrored ? CableTangentMode.Mirrored : CableTangentMode.AutoSmooth;

            Undo.RecordObject(cableGen, "Adjust Cable Sag");

            for (int i = 0; i < sagCount; i++)
            {
                int splineIdx = 2 * i + 1;
                var k         = spline[splineIdx];
                k.position = s_sagBasePositions[i] + localOffset;

                if (s_sagUseMirrored)
                {
                    k.tangentIn  = new Vector3(0f, 0f, -handleLength);
                    k.tangentOut = new Vector3(0f, 0f,  handleLength);
                }

                k.mode = targetMode;
                spline.SetKnot(splineIdx, k, false);
            }

            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();
        }

        // ================================================================
        //  Delegated Knot Operations
        // ================================================================

        public static void InsertKnotAfter(CableGenerator cableGen, int index) =>
            SplineKnotOperations.InsertKnotAfter(cableGen, index);

        public static void AddKnotAfterSelected(CableGenerator cableGen) =>
            SplineKnotOperations.AddKnotAfterSelected(cableGen);

        public static void AddKnotAtEnd(CableGenerator cableGen) =>
            SplineKnotOperations.AddKnotAtEnd(cableGen);

        public static void FlipSplineDirection(CableGenerator cableGen) =>
            SplineKnotOperations.FlipSplineDirection(cableGen);

        public static void RedistributeKnotsEvenly(CableGenerator cableGen) =>
            SplineKnotOperations.RedistributeKnotsEvenly(cableGen);

        public static bool SubdivideSplineKnots(CableGenerator cableGen, CableTangentMode addedKnotMode) =>
            SplineKnotOperations.SubdivideSplineKnots(cableGen, addedKnotMode);

        public static bool RedistributeKnotsBetweenEndpoints(CableGenerator cableGen, int divisionCount, CableTangentMode addedKnotMode) =>
            SplineKnotOperations.RedistributeKnotsBetweenEndpoints(cableGen, divisionCount, addedKnotMode);

        public static void EvaluateSegmentArcLengthMidpoint(
            CableSpline spline, int segmentIndex, Vector3 localGravity,
            out Vector3 pos, out Vector3 tan, out Vector3 up, int samples = 64) =>
            SplineKnotOperations.EvaluateSegmentArcLengthMidpoint(spline, segmentIndex, localGravity, out pos, out tan, out up, samples);
    }
}
