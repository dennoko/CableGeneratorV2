using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CableGeneratorV2Runtime
{
    public partial class CableGenerator
    {
        // ================================================================
        //  Core Mesh Extrusion (Tier 0 = Full, Tier 1 = LOD1, Tier 2 = LOD2)
        // ================================================================

        bool BuildTierMesh(Mesh targetMesh, int lodTier, in ProfileSource src, int skinBoneCount, List<BoneWeight> weightsOut)
        {
            targetMesh.Clear();
            s_verts.Clear();
            s_tris.Clear();
            s_normals.Clear();
            s_uvs.Clear();
            s_uv2s.Clear();

            float scale = Mathf.Max(0.01f, radiusScale);
            DecimateProfile(src.verts, src.normals, src.us, src.vertsPerLoop, src.perimeters, lodTier, scale,
                out Vector2[] profileVerts, out Vector2[] profileNormals, out float[] profileUs,
                out int vertsPerLoop, out float[] perimeters);

            int profileVertCount = profileVerts.Length;
            if (profileVertCount == 0 || vertsPerLoop < 2) return false;

            float twistTurnsPerMeter = src.twistTurnsPerMeter;
            float splineLength = SampleCenterline(lodTier, twistTurnsPerMeter, src.maxRadius * scale, s_samples);

            int sampleCount = s_samples.Count;
            if (sampleCount < 2 || splineLength < 1e-5f) return false;

            int loopCount = profileVertCount / vertsPerLoop;
            var uv1Layout = CableUvPacking.CalculateUV1Layout(perimeters, loopCount, splineLength);
            int loopDiv = Mathf.Max(1, vertsPerLoop - 1);

            bool buildWeights = weightsOut != null && skinBoneCount >= kMinBones;

            for (int i = 0; i < sampleCount; i++)
            {
                var sample = s_samples[i];
                Vector3 localPos   = sample.localPos;
                Vector3 localRight = sample.localRight;
                Vector3 localUp    = sample.localUp;

                float dist     = sample.cumulativeLength;
                float normDist = sample.normalizedDistance;
                float v        = dist * uvTiling;

                float twistAngle = twistTurnsPerMeter * (Mathf.PI * 2f) * dist;
                float twistCos = Mathf.Cos(twistAngle);
                float twistSin = Mathf.Sin(twistAngle);

                BoneWeight ringWeight = buildWeights ? EvaluateAnalyticalBoneWeight(normDist, skinBoneCount) : default;

                for (int j = 0; j < profileVertCount; j++)
                {
                    float px = profileVerts[j].x * twistCos - profileVerts[j].y * twistSin;
                    float py = profileVerts[j].x * twistSin + profileVerts[j].y * twistCos;
                    s_verts.Add(localPos + localRight * px + localUp * py);

                    float nx = profileNormals[j].x * twistCos - profileNormals[j].y * twistSin;
                    float ny = profileNormals[j].x * twistSin + profileNormals[j].y * twistCos;
                    s_normals.Add((localRight * nx + localUp * ny).normalized);

                    s_uvs.Add(new Vector2(profileUs[j], v));

                    int loopIdx = j / vertsPerLoop;
                    int localJ  = j % vertsPerLoop;
                    float localU = (localJ == vertsPerLoop - 1)
                        ? (float)(localJ - 1) / loopDiv + 0.5f / loopDiv
                        : (float)localJ / loopDiv;
                    s_uv2s.Add(CableUvPacking.CalculateUV1(uv1Layout, loopIdx, localU, normDist));

                    if (buildWeights)
                        weightsOut.Add(ringWeight);
                }
            }

            int ringSegments = sampleCount - 1;
            for (int i = 0; i < ringSegments; i++)
            {
                int ringStart     = i * profileVertCount;
                int nextRingStart = (i + 1) * profileVertCount;

                for (int j = 0; j < profileVertCount - 1; j++)
                {
                    if ((j + 1) % vertsPerLoop == 0)
                        continue;

                    int a = ringStart + j;
                    int b = ringStart + j + 1;
                    int c = nextRingStart + j;
                    int d = nextRingStart + j + 1;

                    s_tris.Add(a); s_tris.Add(b); s_tris.Add(c);
                    s_tris.Add(b); s_tris.Add(d); s_tris.Add(c);
                }
            }

            // 太い束線プロファイル × 長いケーブルでは 65535 頂点を超えるため 32bit インデックスに切り替える
            targetMesh.indexFormat = s_verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            targetMesh.SetVertices(s_verts);
            targetMesh.SetNormals(s_normals);
            targetMesh.SetUVs(0, s_uvs);
            targetMesh.SetUVs(1, s_uv2s);
            targetMesh.SetTriangles(s_tris, 0, true);
            return true;
        }

        float SampleCenterline(int lodTier, float twistTurnsPerMeter, float clearance, List<CableSamplePoint> outSamples)
        {
            // ツイストがある場合は直線区間でもねじれが表現されるよう最大ステップ長を設定
            float maxStepLength = 0f;
            if (Mathf.Abs(twistTurnsPerMeter) > 0.01f)
            {
                float stepsPerTurn = lodTier == 0 ? 12f : (lodTier == 1 ? 7f : 4f);
                maxStepLength = 1f / (Mathf.Abs(twistTurnsPerMeter) * stepsPerTurn);
            }

            float tierAngleError = lodTier == 0 ? adaptiveAngleError
                                 : lodTier == 1 ? adaptiveAngleError * 2.2f
                                 :                adaptiveAngleError * 4.5f;

            int tierUniformRes = lodTier == 0 ? resolution
                               : lodTier == 1 ? Mathf.Max(2, resolution / 2)
                               :                Mathf.Max(2, resolution / 4);

            return CableCurveSampler.SampleSpline(
                spline,
                transform,
                useAdaptiveSampling,
                tierUniformRes,
                tierAngleError,
                maxStepLength,
                enableSurfaceCollision,
                clearance,
                collisionLayerMask,
                outSamples);
        }

        // ================================================================
        //  Procedural Profile Decimation for LOD1 / LOD2
        // ================================================================

        public static void GetDecimatedProfileData(
            CableProfile srcProfile,
            int lodTier,
            float scale,
            out Vector2[] outVerts,
            out Vector2[] outNormals,
            out float[] outUs,
            out int outVertsPerLoop,
            out float[] outPerimeters)
        {
            DecimateProfile(
                srcProfile.GetVertices(), srcProfile.GetNormals(), srcProfile.GetUCoords(),
                Mathf.Max(1, srcProfile.GetVerticesPerLoop()), srcProfile.GetPerimeters(),
                lodTier, scale,
                out outVerts, out outNormals, out outUs, out outVertsPerLoop, out outPerimeters);
        }

        static void DecimateProfile(
            Vector2[] rawVerts, Vector2[] rawNorms, float[] rawUs, int rawVpl, float[] rawPerims,
            int lodTier, float scale,
            out Vector2[] outVerts, out Vector2[] outNormals, out float[] outUs,
            out int outVertsPerLoop, out float[] outPerimeters)
        {
            int loopCount = rawVerts.Length / rawVpl;
            int rawSegmentsPerLoop = rawVpl - 1;

            // LOD0 または頂点数が少ない断面は間引きせずスケールのみ適用
            if (lodTier <= 0 || rawSegmentsPerLoop <= 4)
            {
                outVerts = new Vector2[rawVerts.Length];
                for (int i = 0; i < rawVerts.Length; i++)
                    outVerts[i] = rawVerts[i] * scale;
                outNormals = rawNorms;
                outUs = rawUs;
                outVertsPerLoop = rawVpl;
                outPerimeters = new float[rawPerims.Length];
                for (int i = 0; i < rawPerims.Length; i++)
                    outPerimeters[i] = rawPerims[i] * scale;
                return;
            }

            // LOD1: 円周分割数を約 50% (最低4角)、LOD2: 約 30% (最低3角) に手続き間引き
            int targetSegments = lodTier == 1
                ? Mathf.Max(4, Mathf.RoundToInt(rawSegmentsPerLoop * 0.5f))
                : Mathf.Max(3, Mathf.RoundToInt(rawSegmentsPerLoop * 0.3f));

            targetSegments = Mathf.Min(targetSegments, rawSegmentsPerLoop);
            outVertsPerLoop = targetSegments + 1;

            int totalOut = loopCount * outVertsPerLoop;
            outVerts = new Vector2[totalOut];
            outNormals = new Vector2[totalOut];
            outUs = new float[totalOut];
            outPerimeters = new float[loopCount];

            for (int loop = 0; loop < loopCount; loop++)
            {
                int srcBase = loop * rawVpl;
                int dstBase = loop * outVertsPerLoop;

                for (int k = 0; k < targetSegments; k++)
                {
                    int srcIdx = Mathf.Clamp(Mathf.RoundToInt((float)k / targetSegments * rawSegmentsPerLoop), 0, rawSegmentsPerLoop - 1);
                    outVerts[dstBase + k]   = rawVerts[srcBase + srcIdx] * scale;
                    outNormals[dstBase + k] = rawNorms[srcBase + srcIdx];
                    outUs[dstBase + k]      = rawUs[srcBase + srcIdx];
                }

                // 閉じ頂点（末尾）
                outVerts[dstBase + targetSegments]   = rawVerts[srcBase + rawSegmentsPerLoop] * scale;
                outNormals[dstBase + targetSegments] = rawNorms[srcBase + rawSegmentsPerLoop];
                outUs[dstBase + targetSegments]      = rawUs[srcBase + rawSegmentsPerLoop];

                float p = 0f;
                for (int k = 1; k < outVertsPerLoop; k++)
                    p += Vector2.Distance(outVerts[dstBase + k], outVerts[dstBase + k - 1]);
                outPerimeters[loop] = p;
            }
        }

        // ================================================================
        //  Analytical Smooth Skinning Weights (FBX 書き出し用)
        // ================================================================

        public static BoneWeight EvaluateAnalyticalBoneWeight(float normalizedDistance, int numBones)
        {
            if (numBones <= 1)
                return new BoneWeight { boneIndex0 = 0, weight0 = 1f };

            float b = Mathf.Clamp01(normalizedDistance) * (numBones - 1);
            int idx0 = Mathf.Clamp(Mathf.FloorToInt(b), 0, numBones - 1);
            int idx1 = Mathf.Min(idx0 + 1, numBones - 1);

            if (idx0 == idx1)
                return new BoneWeight { boneIndex0 = idx0, weight0 = 1f };

            float f = Mathf.Clamp01(b - idx0);
            // Hermite SmoothStep 補間により関節の折れ曲がりを滑らかにする
            float w1 = f * f * (3f - 2f * f);
            float w0 = 1f - w1;

            return new BoneWeight
            {
                boneIndex0 = idx0,
                weight0    = w0,
                boneIndex1 = idx1,
                weight1    = w1,
            };
        }
    }
}

