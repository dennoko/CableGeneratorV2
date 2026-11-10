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
            switch (s.exportFormat)
            {
                case 0:
                    BakeCableToSceneAndAssets(generator, s.outputFolder);   // メッシュ (.asset) はベイクしたオブジェクトをシーンに置く
                    break;
                case 2:
                    ExportCablePrefab(generator, s.outputFolder, s.placeInScene);
                    break;
                default:
                    ExportCableFbx(generator, s.outputFolder, s.fbxLodTier, s.fbxIncludeBones, s.placeInScene);
                    break;
            }

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
        //  Prefab Export (LOD0〜2 のメッシュ + LODGroup を設定した Prefab)
        // ================================================================

        /// <summary>
        /// LOD0〜LOD2 のメッシュを 1 つの .asset に保存し、LODGroup を設定した Prefab を書き出す。
        /// ケーブルの「軽量化」が OFF でも LOD は必ず作る（切り替えの画面占有率はケーブルの値）。
        /// アタッチメントは Prefab の中に含める。placeInScene のときは Prefab のインスタンスを元ケーブルの位置に置き、
        /// 元ケーブルを無効化する。
        /// </summary>
        public static GameObject ExportCablePrefab(CableGenerator generator, string folderPath, bool placeInScene)
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

            string prefabPath = CableMeshExporter.GenerateUniqueAssetPath(folder, generator.gameObject.name + "_cable", ".prefab");
            string prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
            string meshPath   = CableMeshExporter.GenerateUniqueAssetPath(folder, prefabName + "_LODs");

            // LOD0 を主アセット、LOD1・LOD2 をサブアセットにした 1 ファイル
            var meshes = new Mesh[3];
            for (int tier = 0; tier < meshes.Length; tier++)
            {
                var m = new Mesh { name = prefabName + "_LOD" + tier };
                meshes[tier] = m;
                if (!generator.BuildExportMesh(m, tier, 0, null, null) || m.vertexCount == 0)
                {
                    foreach (var built in meshes)
                        if (built != null) Object.DestroyImmediate(built);
                    EditorUtility.DisplayDialog(Tr("dialog.error"), Tr("msg.noMesh"), "OK");
                    return null;
                }
            }
            AssetDatabase.CreateAsset(meshes[0], meshPath);
            AssetDatabase.AddObjectToAsset(meshes[1], meshes[0]);
            AssetDatabase.AddObjectToAsset(meshes[2], meshes[0]);
            AssetDatabase.SaveAssets();

            GameObject root = BuildLodObject(generator, prefabName, meshes, recordUndo: placeInScene);

            // Prefab のルート位置は原点にしておく（シーンに置くときはインスタンスの位置で上書き）
            Vector3 placedPosition = root.transform.localPosition;
            root.transform.localPosition = Vector3.zero;

            bool saved;
            GameObject prefabAsset = placeInScene
                ? PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction, out saved)
                : PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out saved);

            if (!saved || prefabAsset == null)
            {
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(meshPath);
                EditorUtility.DisplayDialog(Tr("dialog.error"), Tr("msg.exportFailed"), "OK");
                return null;
            }

            Debug.Log($"[CableGenerator V2] Prefab: {prefabPath} (meshes {meshPath})");
            LastExportPath = prefabPath;
            EditorGUIUtility.PingObject(prefabAsset);

            if (!placeInScene)
            {
                Object.DestroyImmediate(root);
                return null;
            }

            root.transform.localPosition = placedPosition;
            DisableSourceAfterBake(generator.gameObject);
            Selection.activeGameObject = root;
            return root;
        }

        /// <summary>LOD0 をルート、LOD1・LOD2 を子に置き、LODGroup を設定したオブジェクトを元ケーブルの隣に作る。</summary>
        static GameObject BuildLodObject(CableGenerator generator, string name, Mesh[] meshes, bool recordUndo)
        {
            GameObject sourceObject = generator.gameObject;
            var root = new GameObject(name);
            if (recordUndo) Undo.RegisterCreatedObjectUndo(root, Tr("undo.placePrefab"));
            PlaceNextToSource(root.transform, sourceObject.transform);

            var sourceRenderer = sourceObject.GetComponent<MeshRenderer>();
            Material[] mats = sourceRenderer != null ? sourceRenderer.sharedMaterials : new Material[0];

            root.AddComponent<MeshFilter>().sharedMesh = meshes[0];
            var r0 = root.AddComponent<MeshRenderer>();
            r0.sharedMaterials = mats;
            Renderer r1 = CreateBakedTierRenderer(root.transform, "LOD1", meshes[1], mats);
            Renderer r2 = CreateBakedTierRenderer(root.transform, "LOD2", meshes[2], mats);
            if (sourceRenderer != null)
            {
                foreach (var r in new[] { r0, r1, r2 })
                {
                    r.shadowCastingMode = sourceRenderer.shadowCastingMode;
                    r.receiveShadows    = sourceRenderer.receiveShadows;
                }
            }

            generator.GetLodScreenHeights(out float h1, out float h2, out float hc);
            var lodGroup = root.AddComponent<LODGroup>();
            lodGroup.SetLODs(new[]
            {
                new LOD(h1, new Renderer[] { r0 }),
                new LOD(h2, new[] { r1 }),
                new LOD(hc, new[] { r2 }),
            });
            lodGroup.RecalculateBounds();

            CopyAttachments(generator, root.transform, null, recordUndo);
            return root;
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
            PlaceModelOverSource(instance.transform, generator.transform);   // モデルのルートの変換（軸変換など）を消さない

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
