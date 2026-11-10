using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal static partial class CableCommands
    {
        // ================================================================
        //  Bake Export (.asset メッシュ + LODGroup + Attachments)
        // ================================================================

        /// <summary>直前に書き出したファイルのパス（ステータスバーの「保存: …」用）。</summary>
        public static string LastExportPath { get; private set; }

        /// <summary>出力タブ / フッターの「出力」。ツール設定（形式・LOD・ボーン・配置・出力先）で書き出す。</summary>
        public static CableResult Export(CableGenerator generator)
        {
            if (!HasMesh(generator)) return CableResult.Fail(Tr("msg.noMesh"));
            var s = CableToolSettings.instance;
            LastExportPath = null;
            if (s.exportFormat == 0)
                BakeCableToSceneAndAssets(generator, s.outputFolder);   // メッシュ (.asset) はベイクしたオブジェクトをシーンに置く
            else
                ExportCableFbx(generator, s.outputFolder, s.fbxLodTier, s.fbxIncludeBones, s.placeInScene);

            return LastExportPath != null
                ? CableResult.Success(Tr("msg.saved", System.IO.Path.GetFileName(LastExportPath)))
                : CableResult.Fail(Tr("msg.exportFailed"));
        }

        public static GameObject BakeCableToSceneAndAssets(CableGenerator generator, string folderPath)
        {
            if (generator == null) return null;
            Mesh mesh0 = generator.GeneratedMesh;
            if (mesh0 == null || mesh0.vertexCount == 0) return null;

            string baseMeshName = generator.gameObject.name + "_cable";
            string mesh0Path = CableMeshExporter.SaveMeshAsset(mesh0, baseMeshName, folderPath, true);
            if (string.IsNullOrEmpty(mesh0Path))
                return null;
            LastExportPath = mesh0Path;

            string mesh1Path = null;
            string mesh2Path = null;
            if (generator.enableLod)
            {
                Mesh m1 = generator.GeneratedLod1Mesh;
                Mesh m2 = generator.GeneratedLod2Mesh;
                if (m1 != null && m1.vertexCount > 0)
                    mesh1Path = CableMeshExporter.SaveMeshAsset(m1, baseMeshName + "_LOD1", folderPath, false);
                if (m2 != null && m2.vertexCount > 0)
                    mesh2Path = CableMeshExporter.SaveMeshAsset(m2, baseMeshName + "_LOD2", folderPath, false);
            }

            return SetupBakedMeshObject(generator, mesh0Path, mesh1Path, mesh2Path);
        }

        public static GameObject SetupBakedMeshObject(
            CableGenerator generator,
            string mesh0AssetPath,
            string mesh1AssetPath = null,
            string mesh2AssetPath = null)
        {
            Mesh bakedMesh0 = AssetDatabase.LoadAssetAtPath<Mesh>(mesh0AssetPath);
            if (bakedMesh0 == null)
            {
                EditorUtility.DisplayDialog(Tr("dialog.error"), Tr("msg.bakeLoadFailed"), "OK");
                return null;
            }

            Mesh bakedMesh1 = !string.IsNullOrEmpty(mesh1AssetPath) ? AssetDatabase.LoadAssetAtPath<Mesh>(mesh1AssetPath) : null;
            Mesh bakedMesh2 = !string.IsNullOrEmpty(mesh2AssetPath) ? AssetDatabase.LoadAssetAtPath<Mesh>(mesh2AssetPath) : null;

            GameObject sourceObject = generator.gameObject;
            var bakedObject = new GameObject(GenerateUniqueGameObjectName(sourceObject, sourceObject.name + "_cable_baked"));
            Undo.RegisterCreatedObjectUndo(bakedObject, Tr("undo.bake"));
            PlaceNextToSource(bakedObject.transform, sourceObject.transform);

            var sourceRenderer = sourceObject.GetComponent<MeshRenderer>();
            Material[] sharedMats = sourceRenderer != null ? sourceRenderer.sharedMaterials : new Material[0];

            // LOD0 はルート自身、LOD1/LOD2 は子に置く（生成時と同じ構成）
            var meshFilter        = bakedObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = bakedMesh0;
            var bakedRenderer     = bakedObject.AddComponent<MeshRenderer>();
            bakedRenderer.sharedMaterials = sharedMats;

            if (generator.enableLod && bakedMesh1 != null && bakedMesh2 != null)
            {
                Renderer r1 = CreateBakedTierRenderer(bakedObject.transform, "LOD1", bakedMesh1, sharedMats);
                Renderer r2 = CreateBakedTierRenderer(bakedObject.transform, "LOD2", bakedMesh2, sharedMats);

                generator.GetLodScreenHeights(out float h1, out float h2, out float hc);
                var lodGroup = bakedObject.AddComponent<LODGroup>();
                lodGroup.SetLODs(new[]
                {
                    new LOD(h1, new Renderer[] { bakedRenderer }),
                    new LOD(h2, new[] { r1 }),
                    new LOD(hc, new[] { r2 }),
                });
                lodGroup.RecalculateBounds();
            }

            CopyAttachments(generator, bakedObject.transform, null);
            DisableSourceAfterBake(sourceObject);

            Selection.activeGameObject = bakedObject;
            return bakedObject;
        }

        static Renderer CreateBakedTierRenderer(Transform parent, string name, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            return mr;
        }

        // ================================================================
        //  FBX Export (形状固定 + ボーン + スムーズウェイト)
        // ================================================================

        /// <summary>
        /// 現在の形状を固定した FBX を書き出す。placeInScene のときは元ケーブルの位置に配置し、
        /// アタッチメントを最寄りのボーンへ子付けしたうえで元ケーブルを無効化する。
        /// </summary>
        public static GameObject ExportCableFbx(CableGenerator generator, string folderPath,
            int lodTier, bool includeBones, bool placeInScene)
        {
            if (generator == null) return null;
            if (generator.profile == null || generator.Spline == null || generator.Spline.Count < 2)
            {
                EditorUtility.DisplayDialog(Tr("dialog.error"), Tr("msg.fbxNeedsProfile"), "OK");
                return null;
            }

            string folder = CableMeshExporter.ResolveOutputFolder(folderPath);
            if (folder == null) return null;
            CableMeshExporter.EnsureFolderExists(folder);

            string assetPath = CableMeshExporter.GenerateUniqueAssetPath(folder, generator.gameObject.name + "_cable", ".fbx");
            var sourceRenderer = generator.GetComponent<MeshRenderer>();

            var options = new CableFbxExporter.Options
            {
                lodTier      = lodTier,
                includeBones = includeBones,
                boneCount    = generator.boneCount,
                meshName     = generator.gameObject.name,
                material     = sourceRenderer != null ? sourceRenderer.sharedMaterial : null,
            };

            CableFbxExporter.Result result;
            try
            {
                result = CableFbxExporter.Export(generator, assetPath, options);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog(Tr("dialog.error"), Tr("msg.fbxFailed") + "\n" + e.Message, "OK");
                return null;
            }

            if (result == null || result.modelAsset == null)
            {
                EditorUtility.DisplayDialog(Tr("dialog.error"), Tr("msg.fbxEmpty"), "OK");
                return null;
            }

            Debug.Log($"[CableGenerator V2] FBX: {assetPath} (bones {result.boneCount})");
            LastExportPath = assetPath;
            EditorGUIUtility.PingObject(result.modelAsset);
            if (!placeInScene) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(result.modelAsset);
            instance.name = GenerateUniqueGameObjectName(generator.gameObject, generator.gameObject.name + "_cable_fbx");
            Undo.RegisterCreatedObjectUndo(instance, Tr("undo.placeFbx"));
            PlaceNextToSource(instance.transform, generator.transform);

            Transform[] bones = null;
            if (result.boneCount >= 2)
            {
                bones = new Transform[result.boneCount];
                Transform root = instance.transform.Find(CableFbxExporter.kBonesRootName);
                Transform curr = root;
                for (int i = 0; i < bones.Length && curr != null; i++)
                {
                    curr = curr.Find("Bone_" + i);
                    bones[i] = curr;
                }
            }

            CopyAttachments(generator, instance.transform,
                bones != null ? (System.Func<int, Transform>)(knot => NearestBoneForKnot(generator, result, bones, knot)) : null);
            DisableSourceAfterBake(generator.gameObject);

            Selection.activeGameObject = instance;
            return instance;
        }

        static Transform NearestBoneForKnot(CableGenerator generator, CableFbxExporter.Result result, Transform[] bones, int knotIndex)
        {
            int segCount = Mathf.Max(1, generator.Spline.SegmentCount);
            float splineT = Mathf.Clamp01((float)Mathf.Clamp(knotIndex, 0, generator.Spline.Count - 1) / segCount);
            float nd = CableFbxExporter.NormalizedDistanceAtSplineT(result.centerline, splineT);
            int idx = Mathf.Clamp(Mathf.RoundToInt(nd * (bones.Length - 1)), 0, bones.Length - 1);
            return bones[idx];
        }
    }
}
