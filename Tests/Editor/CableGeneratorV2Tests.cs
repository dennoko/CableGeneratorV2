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

            List<int> kept = CableGeneratorInspector.SimplifyStroke(stroke, 0.01f);
            // 始点・角・終点の3点に簡略化されること
            Assert.AreEqual(3, kept.Count);
            Assert.AreEqual(0, kept[0]);
            Assert.AreEqual(20, kept[1]);
            Assert.AreEqual(40, kept[2]);
        }
    }

    /// <summary>
    /// シーン上の GameObject を使うライフサイクル・書き出しのテスト。
    /// </summary>
    public class CableGeneratorV2SceneTests
    {
        const string kTempFolder = "Assets/dennokoworks/CableGeneratorV2/Tests/__TempFbx";

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();

            if (AssetDatabase.IsValidFolder(kTempFolder))
                AssetDatabase.DeleteAsset(kTempFolder);
        }

        CableGenerator CreateCable(int knotCount, CableProfile profile = null)
        {
            var go = new GameObject("TestCable");
            created.Add(go);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var gen = go.AddComponent<CableGenerator>();

            if (profile == null)
            {
                var p = ScriptableObject.CreateInstance<HeavyDutyCableProfile>();
                p.radius = 0.02f;
                p.segments = 12;
                profile = p;
            }
            created.Add(profile);
            gen.profile = profile;

            var knots = new List<CableKnot>();
            for (int i = 0; i < knotCount; i++)
                knots.Add(new CableKnot(new Vector3(i * 0.5f, Mathf.Sin(i) * 0.2f, i * 0.3f)));
            gen.Spline.ReplaceKnots(knots, false);
            gen.RebuildMesh();
            return gen;
        }

        CableKnotAttachment AddAttachment(CableGenerator gen, int knotIndex)
        {
            var child = new GameObject("Att" + knotIndex);
            child.transform.SetParent(gen.transform, false);
            var att = child.AddComponent<CableKnotAttachment>();
            att.knotIndex = knotIndex;
            return att;
        }

        [Test]
        public void ReplaceKnots_KeepsStartAndEndAttachments()
        {
            var gen = CreateCable(5);
            var start = AddAttachment(gen, 0);
            var end   = AddAttachment(gen, 4);

            // 2点配線と同じ一括差し替え（旧実装の Clear+Add では両方が末尾へずれていた）
            gen.Spline.ReplaceKnots(new[] { new CableKnot(Vector3.zero), new CableKnot(Vector3.forward) }, false);

            Assert.AreEqual(0, start.knotIndex);
            Assert.AreEqual(1, end.knotIndex);
        }

        [Test]
        public void Subdivide_And_Flip_KeepAttachmentsOnSamePhysicalKnot()
        {
            var gen = CreateCable(3);
            var mid = AddAttachment(gen, 1);
            Vector3 midPos = gen.Spline[1].position;

            CableGeneratorInspector.SubdivideSplineKnots(gen, CableTangentMode.AutoSmooth);
            Assert.AreEqual(5, gen.Spline.Count);
            Assert.AreEqual(2, mid.knotIndex);
            Assert.Less(Vector3.Distance(midPos, gen.Spline[mid.knotIndex].position), 1e-5f);

            CableGeneratorInspector.FlipSplineDirection(gen);
            Assert.AreEqual(2, mid.knotIndex);
            Assert.Less(Vector3.Distance(midPos, gen.Spline[mid.knotIndex].position), 1e-5f);

            var end = AddAttachment(gen, 4);
            Vector3 endPos = gen.Spline[4].position;
            CableGeneratorInspector.FlipSplineDirection(gen);
            Assert.AreEqual(0, end.knotIndex);
            Assert.Less(Vector3.Distance(endPos, gen.Spline[end.knotIndex].position), 1e-5f);
        }

        [Test]
        public void GeneratedMesh_IsTransient_AndNotSharedWithDuplicate()
        {
            var gen = CreateCable(3);
            Mesh mesh = gen.GeneratedMesh;
            Assert.IsNotNull(mesh);
            Assert.IsTrue((mesh.hideFlags & HideFlags.DontSaveInEditor) != 0, "生成メッシュはシーンに保存されないこと");

            // 複製直後は同じメッシュを参照するが、複製側は所有者でないため自分用のメッシュを確保すること
            var cloneGo = Object.Instantiate(gen.gameObject);
            created.Add(cloneGo);
            var clone = cloneGo.GetComponent<CableGenerator>();
            Assert.IsNull(clone.GeneratedMesh);
            clone.RebuildMesh();
            Assert.IsNotNull(clone.GeneratedMesh);
            Assert.AreNotSame(mesh, clone.GeneratedMesh);
            Assert.AreSame(mesh, gen.GeneratedMesh);

            // 再構築しても同じメッシュを再利用する（リークしない）
            gen.RebuildMesh();
            Assert.AreSame(mesh, gen.GeneratedMesh);
        }

        [Test]
        public void Lod_IsBuiltAsTransientHierarchy_AndRemovedWhenDisabled()
        {
            var gen = CreateCable(4);
            gen.enableLod = true;
            gen.RebuildMesh();

            Transform lodRoot = gen.transform.Find(CableGenerator.kLodRootName);
            Assert.IsNotNull(lodRoot);
            Assert.IsTrue((lodRoot.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0);
            Assert.IsNotNull(gen.GeneratedLod1Mesh);
            Assert.IsNotNull(gen.GeneratedLod2Mesh);
            Assert.Greater(gen.GeneratedLod1Mesh.vertexCount, 0);
            Assert.IsTrue(gen.GetComponent<MeshRenderer>().enabled, "LOD0 はルート自身の Renderer");
            Assert.IsNotNull(gen.GetComponent<LODGroup>());

            gen.enableLod = false;
            gen.RebuildMesh();
            Assert.IsNull(gen.transform.Find(CableGenerator.kLodRootName));
            Assert.IsNull(gen.GetComponent<LODGroup>());
        }

        [Test]
        public void LargeProfile_Uses32BitIndices()
        {
            var profile = ScriptableObject.CreateInstance<ClusteredCableProfile>();
            profile.wireCount = 37;
            profile.segments = 32;
            var gen = CreateCable(4, profile);
            gen.useAdaptiveSampling = false;
            gen.resolution = 256;

            var mesh = new Mesh();
            created.Add(mesh);
            Assert.IsTrue(gen.BuildExportMesh(mesh, 0, 0, null, null));
            Assert.Greater(mesh.vertexCount, 65535);
            Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt32, mesh.indexFormat);

            var tris = mesh.triangles;
            int max = 0;
            foreach (int t in tris) max = Mathf.Max(max, t);
            Assert.Less(max, mesh.vertexCount);
            Assert.Greater(max, 65535);
        }

        [Test]
        public void FbxEulerXYZ_RoundTrips()
        {
            var rng = new System.Random(1234);
            for (int i = 0; i < 200; i++)
            {
                var q = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
                Matrix4x4 m = Matrix4x4.Rotate(q);
                Vector3 e = CableFbxExporter.MatrixToFbxEulerXYZ(m);
                Matrix4x4 back = CableFbxExporter.FbxEulerXYZToMatrix(e);
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        Assert.AreEqual(m[r, c], back[r, c], 1e-4f, $"sample {i} m[{r},{c}]");
            }

            // ジンバルロック付近
            foreach (float pitch in new[] { 90f, -90f })
            {
                Matrix4x4 m = Matrix4x4.Rotate(Quaternion.AngleAxis(pitch, Vector3.up) * Quaternion.AngleAxis(30f, Vector3.right));
                Matrix4x4 back = CableFbxExporter.FbxEulerXYZToMatrix(CableFbxExporter.MatrixToFbxEulerXYZ(m));
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        Assert.AreEqual(m[r, c], back[r, c], 1e-4f);
            }
        }

        [Test]
        public void FbxExport_ImportsAsSkinnedMesh_WithMatchingGeometryAndBones()
        {
            var gen = CreateCable(4);
            gen.Spline.SetSegmentSag(1, 0.3f);
            gen.transform.position = new Vector3(3f, 1f, -2f);   // 書き出しはローカル空間で行われること
            gen.transform.rotation = Quaternion.Euler(10f, 40f, 0f);
            gen.RebuildMesh();

            const int boneCount = 6;
            CableMeshExporter.EnsureFolderExists(kTempFolder);
            string path = kTempFolder + "/test_cable.fbx";
            var result = CableFbxExporter.Export(gen, path, new CableFbxExporter.Options
            {
                lodTier = 0, includeBones = true, boneCount = boneCount, meshName = "TestCable",
            });

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.modelAsset, "FBX がインポートできること");

            string dump = System.Environment.GetEnvironmentVariable("CABLEGEN_FBX_DUMP");
            if (!string.IsNullOrEmpty(dump))
                System.IO.File.Copy(System.IO.Path.GetFullPath(path), dump, true);

            var smr = result.modelAsset.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(smr, "SkinnedMeshRenderer が生成されること");
            Assert.AreEqual(boneCount, smr.bones.Length);

            // ---- 形状の一致（X 反転・巻き順・単位系が往復で元に戻ること）----
            var original = new Mesh();
            created.Add(original);
            var weights = new List<BoneWeight>();
            gen.BuildExportMesh(original, 0, boneCount, null, weights);

            Mesh imported = smr.sharedMesh;
            Assert.AreEqual(original.vertexCount, imported.vertexCount);
            AssertVector(original.bounds.center, imported.bounds.center, 1e-3f, "bounds.center");
            AssertVector(original.bounds.size,   imported.bounds.size,   1e-3f, "bounds.size");
            Assert.AreEqual(FrontFacingRatio(original) > 0.5f, FrontFacingRatio(imported) > 0.5f, "表裏が反転していないこと");

            // ---- ボーンの位置・向き ----
            for (int i = 0; i < boneCount; i++)
            {
                Transform bone = FindDeep(result.modelAsset.transform, "Bone_" + i);
                Assert.IsNotNull(bone, "Bone_" + i);
                Matrix4x4 expected = result.boneMatrices[i];
                AssertVector(expected.GetColumn(3), bone.position, 1e-3f, "Bone_" + i + " position");
                Assert.Less(Quaternion.Angle(expected.rotation, bone.rotation), 0.1f, "Bone_" + i + " rotation");
                if (i > 0) Assert.AreEqual("Bone_" + (i - 1), bone.parent.name, "ボーンはチェーン構造");
            }

            // ---- ウェイト ----
            var importedWeights = imported.boneWeights;
            Assert.AreEqual(imported.vertexCount, importedWeights.Length);
            foreach (var bw in importedWeights)
                Assert.AreEqual(1f, bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3, 1e-3f);
        }

        static float FrontFacingRatio(Mesh mesh)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var t = mesh.triangles;
            int front = 0, total = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (face.sqrMagnitude < 1e-14f) continue;
                total++;
                if (Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) > 0f) front++;
            }
            return total > 0 ? (float)front / total : 0f;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static void AssertVector(Vector3 expected, Vector3 actual, float tol, string label)
        {
            Assert.AreEqual(expected.x, actual.x, tol, label + ".x");
            Assert.AreEqual(expected.y, actual.y, tol, label + ".y");
            Assert.AreEqual(expected.z, actual.z, tol, label + ".z");
        }
    }
}

