using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CableGeneratorV2Editor;

namespace CableGeneratorV2Tests
{
    /// <summary>
    /// コライダー無しメッシュへの MeshRaycastSession のヒット判定テスト。
    /// </summary>
    public class CableGeneratorV2RaycastTests
    {
        // シーン内の他オブジェクトと干渉しないよう遠方に置く
        static readonly Vector3    kOrigin   = new Vector3(1000f, 1000f, 1000f);
        static readonly Quaternion kRotation = Quaternion.Euler(0f, 30f, 0f);
        static readonly Vector3    kScale    = new Vector3(2f, 1f, 2f);

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        /// <summary>XZ 平面上の 1x1 の四角形（Collider なし）</summary>
        Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "TestQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f,  0.5f), new Vector3(-0.5f, 0f,  0.5f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            created.Add(mesh);
            return mesh;
        }

        GameObject CreateTransformedObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            go.transform.SetPositionAndRotation(kOrigin, kRotation);
            go.transform.localScale = kScale;
            return go;
        }

        /// <summary>四角形ローカル座標 (x, 0, z) の真上から下向きのレイ</summary>
        static Ray DownRayAtLocal(float x, float z)
        {
            Vector3 world = kOrigin + kRotation * Vector3.Scale(kScale, new Vector3(x, 0f, z));
            return new Ray(world + Vector3.up * 5f, Vector3.down);
        }

        void AssertHitsQuad(Renderer expectedRenderer)
        {
            using (var session = new MeshRaycastSession(~0, null))
            {
                // スケール 2 倍なのでローカル ±0.5 の外 (0.4*2=0.8 相当) にも当たる・±1.0 の外には当たらない
                Ray inside = DownRayAtLocal(0.4f, 0.3f);
                Assert.IsTrue(session.Raycast(inside, out MeshRayHit hit, 10f), "コライダー無しでも表面にヒットすること");
                Assert.AreSame(expectedRenderer, hit.renderer);
                Assert.AreEqual(kOrigin.y, hit.point.y, 1e-4f, "ヒット高さ");
                Assert.AreEqual(inside.origin.x, hit.point.x, 1e-4f, "ヒット X");
                Assert.AreEqual(inside.origin.z, hit.point.z, 1e-4f, "ヒット Z");
                Assert.Greater(hit.normal.y, 0.99f, "法線がレイ側（上）を向くこと");

                Assert.IsFalse(session.Raycast(DownRayAtLocal(0.6f, 0f), out _, 10f),
                    "スケールが二重に掛からないこと（範囲外にはヒットしない）");
            }
        }

        [Test]
        public void Session_HitsMeshRendererWithoutCollider()
        {
            var go = CreateTransformedObject("PlainQuad");
            go.AddComponent<MeshFilter>().sharedMesh = CreateQuadMesh();
            var mr = go.AddComponent<MeshRenderer>();

            AssertHitsQuad(mr);
        }

        [Test]
        public void Session_HitsSkinnedMeshRendererWithoutCollider()
        {
            var go = CreateTransformedObject("SkinnedQuad");
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = CreateQuadMesh();
            smr.localBounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));

            AssertHitsQuad(smr);
        }

        [Test]
        public void Session_HitsBonedSkinnedMeshRendererWithLargeRendererScale()
        {
            // FBX インポート時のように Renderer の localScale が 100 倍、rootBone (Hips) が大きくオフセットしている構造を再現
            var root = new GameObject("AvatarRoot");
            created.Add(root);
            root.transform.SetPositionAndRotation(kOrigin, kRotation);

            var bone = new GameObject("Hips").transform;
            bone.SetParent(root.transform, false);
            bone.localPosition = new Vector3(0f, 0.9f, 0f);

            var smrGo = new GameObject("SkinnedBody");
            smrGo.transform.SetParent(root.transform, false);
            smrGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            smrGo.transform.localScale = new Vector3(100f, 100f, 100f);

            var mesh = CreateQuadMesh();
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            };
            // bone.localToWorldMatrix * bindpose によって root 空間で kScale 倍された XZ 四角形になるよう設定
            mesh.bindposes = new[]
            {
                bone.worldToLocalMatrix * root.transform.localToWorldMatrix * Matrix4x4.Scale(kScale)
            };

            var smr = smrGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = new[] { bone };
            smr.rootBone = bone;
            smr.localBounds = new Bounds(new Vector3(0f, -0.9f, 0f), new Vector3(2f, 0.2f, 2f));

            AssertHitsQuad(smr);
        }

        [Test]
        public void Session_IgnoresInactiveRenderersInFrontOfTarget()
        {
            // カメラ（レイ始点）側にある非アクティブなオブジェクトに吸われず、奥の有効な SkinnedMeshRenderer に正しくヒットすること
            var blocker = CreateTransformedObject("InactiveBlocker");
            blocker.transform.position = kOrigin + Vector3.up * 2.5f;
            blocker.AddComponent<MeshFilter>().sharedMesh = CreateQuadMesh();
            blocker.AddComponent<MeshRenderer>();
            blocker.SetActive(false);

            var skinnedBlocker = CreateTransformedObject("InactiveSkinnedBlocker");
            skinnedBlocker.transform.position = kOrigin + Vector3.up * 2.0f;
            var blockerSmr = skinnedBlocker.AddComponent<SkinnedMeshRenderer>();
            blockerSmr.sharedMesh = CreateQuadMesh();
            blockerSmr.localBounds = new Bounds(Vector3.zero, new Vector3(2f, 0.2f, 2f));
            skinnedBlocker.SetActive(false);

            var target = CreateTransformedObject("ActiveTarget");
            var targetSmr = target.AddComponent<SkinnedMeshRenderer>();
            targetSmr.sharedMesh = CreateQuadMesh();
            targetSmr.localBounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));

            AssertHitsQuad(targetSmr);
        }

        [Test]
        public void Session_ExcludesRootAndItsChildren()
        {
            var go = CreateTransformedObject("ExcludedQuad");
            go.AddComponent<MeshFilter>().sharedMesh = CreateQuadMesh();
            go.AddComponent<MeshRenderer>();

            using (var session = new MeshRaycastSession(~0, go))
                Assert.IsFalse(session.Raycast(DownRayAtLocal(0.4f, 0.3f), out _, 10f));
        }
    }
}
