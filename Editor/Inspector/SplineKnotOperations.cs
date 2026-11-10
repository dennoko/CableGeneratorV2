using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public static partial class SplineKnotOperations
    {
        internal const float kVectorEpsilon = 0.0001f;
        internal const float kVectorEpsilonSqr = 1e-10f;
        internal const float kLinearTangentDivisor = 3f;
        internal const float kMidpointTangentDivisor = 3f;

        /// <summary>直前の細分化・等分の結果（画面に出す文言）。</summary>
        internal static string LastMessage = string.Empty;

        public static void InsertKnotAfter(CableGenerator cableGen, int index)
        {
            if (cableGen == null || cableGen.Spline == null) return;
            var spline = cableGen.Spline;
            if (spline.Count < 2) return;

            int nextIndex = (index + 1) % spline.Count;
            EvaluateSegmentArcLengthMidpoint(spline, index, cableGen.transform.InverseTransformVector(Vector3.down),
                out Vector3 midPos, out Vector3 tan, out Vector3 up);

            Vector3 dir = spline[nextIndex].position - spline[index].position;
            float tanLen = dir.magnitude > 1e-6f ? dir.magnitude / 6f : 0.5f;
            Quaternion rot = CableSpline.SafeLookRotation(tan, up);

            var newKnot = new CableKnot(midPos,
                new Vector3(0f, 0f, -tanLen), new Vector3(0f, 0f, tanLen), rot, CableTangentMode.AutoSmooth);

            Undo.RecordObject(cableGen, CableL10n.Tr("undo.insertKnot"));

            // 挿入元の区間 sag は半分ずつに引き継ぐかリセット
            var kA = spline[index];
            kA.sag = 0f;
            spline.SetKnot(index, kA, false);

            spline.Insert(index + 1, newKnot, CableTangentMode.AutoSmooth); // 変更通知でメッシュは再構築される

            CableKnotSelection.SelectSingle(cableGen, index + 1);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();
        }

        public static void AddKnotAfterSelected(CableGenerator cableGen)
        {
            if (cableGen == null || cableGen.Spline == null) return;
            var spline = cableGen.Spline;
            if (spline.Count == 0)
            {
                AddKnotAtEnd(cableGen);
                return;
            }

            int index = CableKnotSelection.Active;
            if (index < 0 || index >= spline.Count) index = spline.Count - 1;

            if (!spline.Closed && index == spline.Count - 1)
                AddKnotAtEnd(cableGen);
            else
                InsertKnotAfter(cableGen, index);
        }

        public static void AddKnotAtEnd(CableGenerator cableGen)
        {
            if (cableGen == null || cableGen.Spline == null) return;
            var spline = cableGen.Spline;

            Vector3 newPos;
            if (spline.Count >= 1)
            {
                var last = spline[spline.Count - 1];
                Vector3 dir;
                if (last.mode == CableTangentMode.AutoSmooth && spline.Count >= 2)
                {
                    spline.ComputeAutoWorldTangents(spline.Count - 1, out _, out dir);
                }
                else
                {
                    dir = last.rotation * last.tangentOut;
                }
                Vector3 off = dir.sqrMagnitude > 1e-6f ? dir.normalized * 2f : new Vector3(0f, 0f, 2f);
                newPos = last.position + off;
            }
            else newPos = Vector3.zero;

            Undo.RecordObject(cableGen, CableL10n.Tr("undo.addKnot"));
            spline.Add(new CableKnot(newPos, new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), Quaternion.identity, CableTangentMode.AutoSmooth));
            CableKnotSelection.SelectSingle(cableGen, spline.Count - 1);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();
        }

        public static void FlipSplineDirection(CableGenerator cableGen)
        {
            if (cableGen == null || cableGen.Spline == null) return;
            var spline = cableGen.Spline;
            int count = spline.Count;
            if (count < 2) return;

            var knots = new CableKnot[count];
            for (int i = 0; i < count; i++) knots[i] = spline[i];

            Undo.RecordObject(cableGen, CableL10n.Tr("undo.flip"));
            var flipped = new CableKnot[count];
            var remap   = new int[count];
            for (int i = 0; i < count; i++)
            {
                int src = count - 1 - i;
                var k   = knots[src];
                (k.tangentIn, k.tangentOut) = (-k.tangentOut, -k.tangentIn);
                // 区間 sag は逆順の区間へ移す
                int sagSrc = (src - 1 + count) % count;
                k.sag = (!spline.Closed && i == count - 1) ? 0f : knots[sagSrc].sag;
                flipped[i] = k;
                remap[src] = i; // アタッチメントは同じ物理位置のノットに付いたまま
            }

            spline.ReplaceKnots(flipped, spline.Closed, remap);
            // 選択は同じ物理位置の制御点に付いたまま（番号だけ逆順にする）
            var flippedSelection = new List<int>();
            foreach (int i in CableKnotSelection.Sorted(count)) flippedSelection.Add(count - 1 - i);
            CableKnotSelection.Set(cableGen, flippedSelection, count - 1 - CableKnotSelection.Active);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();
        }

        public static void RedistributeKnotsEvenly(CableGenerator cableGen)
        {
            if (cableGen == null || cableGen.Spline == null) return;
            var spline = cableGen.Spline;
            int count = spline.Count;
            if (count < 2) return;

            Vector3 localGravity = cableGen.transform.InverseTransformVector(Vector3.down);
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                spline.Evaluate(t, localGravity, out positions[i], out _, out _);
            }

            Undo.RecordObject(cableGen, CableL10n.Tr("undo.redistribute"));
            for (int i = 0; i < count; i++)
            {
                var k = spline[i];
                k.position = positions[i];
                spline.SetKnot(i, k, false);
            }

            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 元のノット i の直後に inserted[i] を挟んだ列へ一括差し替えする（元ノット j → 2j に追従）。
        /// 各区間の sag はノットベースのたわみ／細分化に置き換わるためリセットする。
        /// </summary>
        public static void ReplaceWithInterleavedKnots(CableSpline spline, CableKnot[] original, CableKnot[] inserted)
        {
            var result = new List<CableKnot>(original.Length + inserted.Length);
            var remap  = new int[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                var k = original[i];
                k.sag = 0f;
                remap[i] = result.Count;
                result.Add(k);
                if (i < inserted.Length)
                    result.Add(inserted[i]);
            }
            spline.ReplaceKnots(result, spline.Closed, remap);
        }
    }
}
