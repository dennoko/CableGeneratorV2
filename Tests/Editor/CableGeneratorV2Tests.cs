using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using CableGeneratorV2Editor;

namespace CableGeneratorV2Tests
{
    public class CableGeneratorV2Tests
    {
        [Test]
        public void AllBuiltInProfiles_ContractAndInvariants_AreValid()
        {
            var profiles = new CableProfile[]
            {
                ScriptableObject.CreateInstance<HeavyDutyCableProfile>(),
                ScriptableObject.CreateInstance<FlatCableProfile>(),
                ScriptableObject.CreateInstance<ParallelWireProfile>(),
                ScriptableObject.CreateInstance<BundledCableProfile>(),
                ScriptableObject.CreateInstance<ClusteredCableProfile>(),
                ScriptableObject.CreateInstance<SpiralTubeProfile>(),
                ScriptableObject.CreateInstance<TwistedCableProfile>(),
            };

            foreach (var p in profiles)
            {
                Vector2[] verts = p.GetVertices();
                Vector2[] norms = p.GetNormals();
                float[]   us    = p.GetUCoords();
                int       vpl   = p.GetVerticesPerLoop();
                float[]   perim = p.GetPerimeters();

                Assert.IsNotNull(verts, p.GetType().Name);
                Assert.Greater(verts.Length, 2, p.GetType().Name);
                Assert.AreEqual(verts.Length, norms.Length, p.GetType().Name);
                Assert.AreEqual(verts.Length, us.Length, p.GetType().Name);
                Assert.AreEqual(0, verts.Length % vpl, p.GetType().Name);
                Assert.AreEqual(verts.Length / vpl, perim.Length, p.GetType().Name);
                Assert.Greater(p.GetMaxRadius(), 0f, p.GetType().Name);

                Object.DestroyImmediate(p);
            }
        }

        [Test]
        public void CableSpline_Evaluation_AutoSmoothAndSag_WorksCorrectly()
        {
            var spline = new CableSpline();
            Assert.AreEqual(2, spline.Count);

            spline.Evaluate(0f, Vector3.down, out Vector3 startPos, out _, out _);
            spline.Evaluate(1f, Vector3.down, out Vector3 endPos, out _, out _);

            Assert.Less(Vector3.Distance(startPos, Vector3.zero), 1e-4f);
            Assert.Less(Vector3.Distance(endPos, new Vector3(0f, 0f, 2f)), 1e-4f);

            // 直接たわみ (sag = 0.5m) を設定すると中点 (u=0.5) が 0.5m 下がる
            spline.SetSegmentSag(0, 0.5f);
            spline.Evaluate(0.5f, Vector3.down, out Vector3 midPos, out Vector3 midTan, out _);
            Assert.AreEqual(-0.5f, midPos.y, 1e-4f);
            Assert.AreEqual(1.0f,  midPos.z, 1e-4f);
            Assert.Greater(midTan.z, 0.1f);
        }

        [Test]
        public void AdaptiveSampling_ReducesPolygonsOnStraightLine_MoreThanCurvedLine()
        {
            var straightSpline = new CableSpline();
            var samplesStraight = new List<CableSamplePoint>();
            CableCurveSampler.SampleSpline(
                straightSpline, null,
                adaptive: true, uniformResolution: 32, adaptiveAngleDegrees: 5f,
                maxStepLength: 0f, enableSurfaceCollision: false, collisionClearance: 0.02f,
                collisionMask: ~0, samplesStraight);

            // 直線はわずか 2 サンプル（始点と終点）に最適化される
            Assert.AreEqual(2, samplesStraight.Count);

            // S字カーブまたはたわみがある場合は自動的に密に分割される
            var curvedSpline = new CableSpline();
            curvedSpline.SetSegmentSag(0, 0.8f);
            var samplesCurved = new List<CableSamplePoint>();
            CableCurveSampler.SampleSpline(
                curvedSpline, null,
                adaptive: true, uniformResolution: 32, adaptiveAngleDegrees: 5f,
                maxStepLength: 0f, enableSurfaceCollision: false, collisionClearance: 0.02f,
                collisionMask: ~0, samplesCurved);

            Assert.Greater(samplesCurved.Count, 6);
        }

        [Test]
        public void DoubleReflectionRMF_PreventsTwistFlip_On3DCurve()
        {
            var spline = new CableSpline();
            spline.Clear();
            spline.Add(new CableKnot(new Vector3(0f, 0f, 0f)), CableTangentMode.AutoSmooth);
            spline.Add(new CableKnot(new Vector3(1f, 1f, 1f)), CableTangentMode.AutoSmooth);
            spline.Add(new CableKnot(new Vector3(0f, 2f, 2f)), CableTangentMode.AutoSmooth);
            spline.Add(new CableKnot(new Vector3(-1f, 1f, 3f)), CableTangentMode.AutoSmooth);

            var samples = new List<CableSamplePoint>();
            CableCurveSampler.SampleSpline(
                spline, null,
                adaptive: true, uniformResolution: 32, adaptiveAngleDegrees: 5f,
                maxStepLength: 0f, enableSurfaceCollision: false, collisionClearance: 0.02f,
                collisionMask: ~0, samples);

            Assert.Greater(samples.Count, 8);
            for (int i = 1; i < samples.Count; i++)
            {
                // 隣接サンプル間で Up や Right が 180度反転しないこと（内積 > 0.5）
                float dotUp = Vector3.Dot(samples[i - 1].localUp, samples[i].localUp);
                float dotRight = Vector3.Dot(samples[i - 1].localRight, samples[i].localRight);
                Assert.Greater(dotUp, 0.5f, $"Flip detected at sample {i}");
                Assert.Greater(dotRight, 0.5f, $"Flip detected at sample {i}");
            }
        }

        [Test]
        public void LodDecimation_ReducesVertexCountMonotonically_WhilePreservingLoopClosure()
        {
            var profile = ScriptableObject.CreateInstance<HeavyDutyCableProfile>();
            profile.segments = 24;

            CableGenerator.GetDecimatedProfileData(profile, 0, 1f, out var v0, out var n0, out var u0, out int vpl0, out _);
            CableGenerator.GetDecimatedProfileData(profile, 1, 1f, out var v1, out var n1, out var u1, out int vpl1, out _);
            CableGenerator.GetDecimatedProfileData(profile, 2, 1f, out var v2, out var n2, out var u2, out int vpl2, out _);

            Assert.Greater(vpl0, vpl1);
            Assert.Greater(vpl1, vpl2);
            Assert.GreaterOrEqual(vpl2, 4); // 最低3角形(3+1頂点)

            // 閉じ頂点が先頭と一致すること
            Assert.Less(Vector2.Distance(v1[0], v1[vpl1 - 1]), 1e-5f);
            Assert.Less(Vector2.Distance(v2[0], v2[vpl2 - 1]), 1e-5f);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void AnalyticalBoneWeights_AreNormalizedAndContinuous()
        {
            int boneCount = 8;
            BoneWeight wStart = CableGenerator.EvaluateAnalyticalBoneWeight(0f, boneCount);
            Assert.AreEqual(0, wStart.boneIndex0);
            Assert.AreEqual(1f, wStart.weight0, 1e-5f);

            BoneWeight wEnd = CableGenerator.EvaluateAnalyticalBoneWeight(1f, boneCount);
            Assert.AreEqual(boneCount - 1, wEnd.boneIndex0);
            Assert.AreEqual(1f, wEnd.weight0, 1e-5f);

            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                BoneWeight bw = CableGenerator.EvaluateAnalyticalBoneWeight(t, boneCount);
                float sum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                Assert.AreEqual(1f, sum, 1e-5f);
                Assert.GreaterOrEqual(bw.weight0, 0f);
                Assert.GreaterOrEqual(bw.weight1, 0f);
            }
        }

        [Test]
        public void SimplifyStroke_RdpAlgorithm_PreservesCornersAndReducesCollinearPoints()
        {
            var stroke = new List<Vector3>();
            // (0,0,0) -> (1,0,0) まで20点、そこから (1,0,1) まで20点のL字ストローク
            for (int i = 0; i <= 20; i++)
                stroke.Add(new Vector3(i / 20f, 0f, 0f));
            for (int i = 1; i <= 20; i++)
                stroke.Add(new Vector3(1f, 0f, i / 20f));

            List<int> kept = StrokeSimplifier.SimplifyStroke(stroke, 0.01f);
            // 始点・角・終点の3点に簡略化されること
            Assert.AreEqual(3, kept.Count);
            Assert.AreEqual(0, kept[0]);
            Assert.AreEqual(20, kept[1]);
            Assert.AreEqual(40, kept[2]);
        }
    }
}

