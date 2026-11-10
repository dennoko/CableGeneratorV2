using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal static partial class CableCommands
    {
        // ================================================================
        //  面に吸着 — 選択中の制御点を指定方向のメッシュ表面へ移す（コライダー不要）
        // ================================================================

        public static CableResult SnapKnots(CableGenerator cableGen, IList<int> targetIndices)
        {
            if (cableGen == null || cableGen.Spline == null) return CableResult.Fail(string.Empty);
            var spline = cableGen.Spline;
            if (targetIndices == null || targetIndices.Count == 0) return CableResult.Fail(Tr("msg.selectKnotsFirst"));

            var s = CableToolSettings.instance;
            Vector3 dir = s.SnapDirection(cableGen.transform);
            if (dir.sqrMagnitude < 0.000001f) return CableResult.Fail(Tr("msg.zeroDirection"));
            dir.Normalize();

            int knotCount = spline.Count;
            var knots = new CableKnot[knotCount];
            for (int i = 0; i < knotCount; i++) knots[i] = spline[i];

            Transform tf = cableGen.transform;
            Vector3 localDir = tf.InverseTransformDirection(dir).normalized;
            int successCount = 0;
            var failed = new List<int>();

            // 2点配線・なぞり描画のセッションとは独立した一時セッション
            using (var session = new MeshRaycastSession(s.snapLayerMask, cableGen.gameObject))
            {
                foreach (int index in targetIndices)
                {
                    if (index < 0 || index >= knotCount) continue;
                    var ray = new Ray(tf.TransformPoint(knots[index].position), dir);
                    if (!session.Raycast(ray, out MeshRayHit hit, s.snapMaxDistance))
                    {
                        failed.Add(index);
                        continue;
                    }

                    Vector3 localHitNormal = tf.InverseTransformDirection(hit.normal).normalized;
                    var knot = knots[index];
                    knot.position = tf.InverseTransformPoint(hit.point + hit.normal * s.snapSurfaceOffset);
                    knot.rotation = SplinePlaneMath.AlignForwardToPlaneNoNormalTwist(knot.rotation, localHitNormal, localDir);
                    knots[index] = knot;
                    successCount++;
                }
            }

            if (successCount == 0) return CableResult.Fail(Tr("msg.snapAllFailed"));

            Undo.RecordObject(cableGen, Tr("undo.snap"));
            for (int i = 0; i < knotCount; i++) spline.SetKnot(i, knots[i], false);
            spline.NotifyChanged(-1, CableSplineModification.KnotModified);
            EditorUtility.SetDirty(cableGen);
            SceneView.RepaintAll();

            return failed.Count > 0
                ? CableResult.Success(Tr("msg.snapPartial", successCount, string.Join(", ", failed)))
                : CableResult.Success(Tr("msg.snapped", successCount));
        }
    }
}
