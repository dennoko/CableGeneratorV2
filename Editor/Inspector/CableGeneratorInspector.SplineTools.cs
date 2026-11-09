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
        //  Knot Projection (EditorMeshRaycaster BVH + Physics 併用でコライダー不要)
        // ================================================================

        static LayerMask DrawLayerMaskField(string label, LayerMask selected)
        {
            string[] layerNames   = InternalEditorUtility.layers;
            int[]    layerNumbers = new int[layerNames.Length];

            for (int i = 0; i < layerNames.Length; i++)
                layerNumbers[i] = LayerMask.NameToLayer(layerNames[i]);

            int compressedMask = 0;
            for (int i = 0; i < layerNumbers.Length; i++)
                if ((selected.value & (1 << layerNumbers[i])) != 0)
                    compressedMask |= 1 << i;

            compressedMask = EditorGUILayout.MaskField(label, compressedMask, layerNames);

            int finalMask = 0;
            for (int i = 0; i < layerNumbers.Length; i++)
                if ((compressedMask & (1 << i)) != 0)
                    finalMask |= 1 << layerNumbers[i];

            selected.value = finalMask;
            return selected;
        }

        public static bool SnapKnotInDirection(CableGenerator cableGen)
        {
            s_snapLastResult = string.Empty;

            if (cableGen == null || cableGen.Spline == null)
            {
                s_snapLastResult = "CableGenerator が見つかりません。";
                return false;
            }

            var spline = cableGen.Spline;
            int count  = spline.Count;

            if (count == 0)
            {
                s_snapLastResult = "ノットが存在しません。";
                return false;
            }

            List<int> targetIndices;
            if (s_selectedKnotIndices.Count > 0)
            {
                targetIndices = new List<int>(s_selectedKnotIndices);
                targetIndices.Sort();
            }
            else
            {
                targetIndices = new List<int> { s_snapKnotIndex };
            }

            foreach (int idx in targetIndices)
            {
                if (idx < 0 || idx >= count)
                {
                    s_snapLastResult = $"Index {idx} は範囲外です（0..{count - 1}）。";
                    return false;
                }
            }

            Vector3 dir = s_snapDirectionIsLocal
                ? cableGen.transform.TransformDirection(s_snapDirection)
                : s_snapDirection;

            if (dir.sqrMagnitude < 0.000001f)
            {
                s_snapLastResult = "方向ベクトルがゼロです。";
                return false;
            }
            dir.Normalize();

            int        knotCount = spline.Count;
            CableKnot[] knots    = new CableKnot[knotCount];
            for (int i = 0; i < knotCount; i++)
                knots[i] = spline[i];

            Vector3 localDir = cableGen.transform.InverseTransformDirection(dir).normalized;

            int successCount = 0;
            var failedIndices = new List<int>();

            // 2点配線・なぞり描画のセッションとは独立した一時セッション
            using (var session = new MeshRaycastSession(s_snapLayerMask, cableGen.gameObject))
            {
                foreach (int targetIndex in targetIndices)
                {
                    Vector3 worldOrigin = cableGen.transform.TransformPoint(knots[targetIndex].position);
                    Ray worldRay = new Ray(worldOrigin, dir);

                    if (!session.Raycast(worldRay, out MeshRayHit hit, s_snapMaxDistance))
                    {
                        failedIndices.Add(targetIndex);
                        continue;
                    }

                    Vector3    worldTargetPos = hit.point + hit.normal * s_snapSurfaceOffset;
                    Vector3    localTargetPos = cableGen.transform.InverseTransformPoint(worldTargetPos);
                    Vector3    localHitNormal = cableGen.transform.InverseTransformDirection(hit.normal).normalized;

                    var        oldKnot    = knots[targetIndex];
                    Quaternion snappedRot = AlignForwardToPlaneNoNormalTwist(oldKnot.rotation, localHitNormal, localDir);
                    oldKnot.position      = localTargetPos;
                    oldKnot.rotation      = snappedRot;
                    knots[targetIndex]    = oldKnot;

                    successCount++;
                }
            }

            if (successCount == 0)
            {
                s_snapLastResult = "すべてのノットで投影に失敗しました。";
                return false;
            }

            Undo.RecordObject(cableGen, "Snap Knots To Surface");
            for (int i = 0; i < knotCount; i++)
                spline.SetKnot(i, knots[i], false);

            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();

            if (failedIndices.Count > 0)
            {
                string failedStr = string.Join(", ", failedIndices);
                s_snapLastResult = $"{successCount} ノットを投影。ヒットなし: [{failedStr}]";
            }
            else if (targetIndices.Count == 1)
            {
                s_snapLastResult = $"Knot {targetIndices[0]} をヒット位置へ移動しました。";
            }
            else
            {
                string indexStr = string.Join(", ", targetIndices);
                s_snapLastResult = $"Knot [{indexStr}] をヒット位置へ移動しました。";
            }
            return true;
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

            ReplaceWithInterleavedKnots(spline, knots, inserted);

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
        //  Knot Operations (Insert / Add / Flip / Redistribute / Subdivide)
        // ================================================================

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

            Undo.RecordObject(cableGen, "制御点を挿入");

            // 挿入元の区間 sag は半分ずつに引き継ぐかリセット
            var kA = spline[index];
            kA.sag = 0f;
            spline.SetKnot(index, kA, false);

            spline.Insert(index + 1, newKnot, CableTangentMode.AutoSmooth); // 変更通知でメッシュは再構築される

            s_snapKnotIndex = index + 1;
            s_selectedKnotIndices.Clear();
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

            int index = s_snapKnotIndex;
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

            Undo.RecordObject(cableGen, "制御点を末尾に追加");
            spline.Add(new CableKnot(newPos, new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), Quaternion.identity, CableTangentMode.AutoSmooth));
            s_snapKnotIndex = spline.Count - 1;
            s_selectedKnotIndices.Clear();
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

            Undo.RecordObject(cableGen, "スプラインの方向を反転");
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
            s_snapKnotIndex = Mathf.Clamp(count - 1 - s_snapKnotIndex, 0, count - 1);
            s_selectedKnotIndices.Clear();
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

            Undo.RecordObject(cableGen, "制御点を均等再配置");
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
        static void ReplaceWithInterleavedKnots(CableSpline spline, CableKnot[] original, CableKnot[] inserted)
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

        public static bool SubdivideSplineKnots(CableGenerator cableGen, CableTangentMode addedKnotMode)
        {
            s_knotInitLastResult = string.Empty;
            if (cableGen == null || cableGen.Spline == null) return false;

            var spline    = cableGen.Spline;
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                s_knotInitLastResult = "細分化には最低2つのノットが必要です。";
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

            Undo.RecordObject(cableGen, "Subdivide Cable Knots");
            ReplaceWithInterleavedKnots(spline, originals, inserted);

            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();

            s_knotInitLastResult = $"ノットを細分化しました（{knotCount} → {spline.Count}）。";
            return true;
        }

        public static bool RedistributeKnotsBetweenEndpoints(CableGenerator cableGen, int divisionCount, CableTangentMode addedKnotMode)
        {
            s_knotInitLastResult = string.Empty;
            if (cableGen == null || cableGen.Spline == null) return false;

            var spline    = cableGen.Spline;
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                s_knotInitLastResult = "ノットが不足しています。";
                return false;
            }
            if (spline.Closed)
            {
                s_knotInitLastResult = "閉じたSplineでは始点-終点の等分は使用できません。";
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

            Undo.RecordObject(cableGen, "Redistribute Cable Knots");
            // 中間ノットのアタッチメントは位置比率で新しいノットに割り当てる（削除はしない）
            spline.ReplaceKnots(newKnots, false);

            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();

            s_knotInitLastResult = $"始点-終点を{divisionCount}分割で再配置しました（{targetKnotCount}ノット）。";
            return true;
        }

        // ================================================================
        //  Math Helpers
        // ================================================================

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

        static Vector3 SurfaceParallelDirection(Vector3 preferredDirection, Vector3 normal)
        {
            Vector3 n      = normal.sqrMagnitude < 0.0001f ? Vector3.up : normal.normalized;
            Vector3 planar = Vector3.ProjectOnPlane(preferredDirection, n);

            if (planar.sqrMagnitude < 0.000001f)
            {
                planar = Vector3.Cross(n, Vector3.up);
                if (planar.sqrMagnitude < 0.000001f)
                    planar = Vector3.Cross(n, Vector3.right);
            }
            return planar.normalized;
        }

        static Quaternion AlignForwardToPlaneNoNormalTwist(
            Quaternion oldRotation, Vector3 planeNormal, Vector3 fallbackDirection)
        {
            Vector3 n             = planeNormal.sqrMagnitude < 0.0001f ? Vector3.up : planeNormal.normalized;
            Vector3 oldForward    = oldRotation * Vector3.forward;
            Vector3 planarForward = Vector3.ProjectOnPlane(oldForward, n);

            if (planarForward.sqrMagnitude < 0.000001f)
                planarForward = SurfaceParallelDirection(fallbackDirection, n);
            if (planarForward.sqrMagnitude < 0.000001f)
                return oldRotation;

            Quaternion delta = Quaternion.FromToRotation(oldForward.normalized, planarForward.normalized);
            return delta * oldRotation;
        }
    }
}

