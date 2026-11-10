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
                    Quaternion snappedRot = SplinePlaneMath.AlignForwardToPlaneNoNormalTwist(oldKnot.rotation, localHitNormal, localDir);
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
    }
}

