using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using CableGeneratorV2Editor;

namespace CableGeneratorV2Tests
{
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

