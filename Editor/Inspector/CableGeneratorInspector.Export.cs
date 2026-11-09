using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Bake Export (.asset メッシュ + LODGroup + Attachments)
        // ================================================================

        public static GameObject BakeCableToSceneAndAssets(CableGenerator generator, string folderPath)
        {
            if (generator == null) return null;
            Mesh mesh0 = generator.GeneratedMesh;
            if (mesh0 == null || mesh0.vertexCount == 0) return null;

            string baseMeshName = generator.gameObject.name + "_cable";
            string mesh0Path = CableMeshExporter.SaveMeshAsset(mesh0, baseMeshName, folderPath, true);
            if (string.IsNullOrEmpty(mesh0Path))
                return null;

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
                EditorUtility.DisplayDialog("エラー", "保存したメッシュの読み込みに失敗しました。", "OK");
                return null;
            }

            Mesh bakedMesh1 = !string.IsNullOrEmpty(mesh1AssetPath) ? AssetDatabase.LoadAssetAtPath<Mesh>(mesh1AssetPath) : null;
            Mesh bakedMesh2 = !string.IsNullOrEmpty(mesh2AssetPath) ? AssetDatabase.LoadAssetAtPath<Mesh>(mesh2AssetPath) : null;

            GameObject sourceObject = generator.gameObject;
            var bakedObject = new GameObject(GenerateUniqueGameObjectName(sourceObject, sourceObject.name + "_cable_baked"));
            Undo.RegisterCreatedObjectUndo(bakedObject, "Create Baked Cable Object");
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
                EditorUtility.DisplayDialog("エラー", "断面プロファイルと 2 つ以上の制御点が必要です。", "OK");
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
                EditorUtility.DisplayDialog("エラー", "FBX の書き出しに失敗しました。\n" + e.Message, "OK");
                return null;
            }

            if (result == null || result.modelAsset == null)
            {
                EditorUtility.DisplayDialog("エラー", "FBX の書き出しに失敗しました（メッシュが空です）。", "OK");
                return null;
            }

            Debug.Log($"[CableGenerator V2] FBX を書き出しました: {assetPath}（ボーン {result.boneCount} 本）");
            EditorGUIUtility.PingObject(result.modelAsset);
            if (!placeInScene) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(result.modelAsset);
            instance.name = GenerateUniqueGameObjectName(generator.gameObject, generator.gameObject.name + "_cable_fbx");
            Undo.RegisterCreatedObjectUndo(instance, "Place Exported Cable FBX");
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

        // ================================================================
        //  Shared Helpers
        // ================================================================

        static void PlaceNextToSource(Transform target, Transform source)
        {
            Transform parent = source.parent;
            if (parent != null)
            {
                target.SetParent(parent, false);
                target.SetSiblingIndex(source.GetSiblingIndex() + 1);
            }
            else if (target.gameObject.scene != source.gameObject.scene)
            {
                SceneManager.MoveGameObjectToScene(target.gameObject, source.gameObject.scene);
            }

            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale    = source.localScale;
            target.gameObject.layer = source.gameObject.layer;
        }

        /// <summary>
        /// アタッチされたモデルをワールド座標・スケールを維持して複製配置する。
        /// parentForKnot が指定されていればそのノットに対応する Transform（最寄りボーン等）の子にする。
        /// </summary>
        static void CopyAttachments(CableGenerator generator, Transform defaultParent, System.Func<int, Transform> parentForKnot)
        {
            var attachments = generator.GetComponentsInChildren<CableKnotAttachment>(true);
            foreach (var attachment in attachments)
            {
                GameObject spawned = attachment.SpawnedInstance;
                if (spawned == null || attachment.Generator != generator) continue;

                Transform spawnedTransform = spawned.transform;
                GameObject copy;

                // Prefab インスタンスは Prefab の接続を保ったまま複製する
                var source = PrefabUtility.IsAnyPrefabInstanceRoot(spawned)
                    ? PrefabUtility.GetCorrespondingObjectFromSource(spawned)
                    : null;
                if (source != null)
                    copy = (GameObject)PrefabUtility.InstantiatePrefab(source, defaultParent.gameObject.scene);
                else
                    copy = Object.Instantiate(spawned);

                copy.transform.SetPositionAndRotation(spawnedTransform.position, spawnedTransform.rotation);
                copy.transform.localScale = spawnedTransform.lossyScale;
                copy.name = spawned.name;
                Undo.RegisterCreatedObjectUndo(copy, "Copy Attachment to Baked Cable");

                Transform parent = parentForKnot?.Invoke(attachment.knotIndex) ?? defaultParent;
                copy.transform.SetParent(parent != null ? parent : defaultParent, true);
            }
        }

        static void DisableSourceAfterBake(GameObject sourceObject)
        {
            Undo.RecordObject(sourceObject, "Disable Original Cable Object");
            bool editorOnlyTagExists = System.Array.IndexOf(InternalEditorUtility.tags, "EditorOnly") >= 0;
            if (editorOnlyTagExists)
                sourceObject.tag = "EditorOnly";
            else
                Debug.LogWarning("[CableGenerator V2] EditorOnly タグが見つからないため、タグ設定をスキップしました。");
            sourceObject.SetActive(false);
            EditorUtility.SetDirty(sourceObject);
        }

        static string GenerateUniqueGameObjectName(GameObject source, string baseName)
        {
            var usedNames = new HashSet<string>();
            Transform parent = source.transform.parent;

            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                    usedNames.Add(parent.GetChild(i).name);
            }
            else if (source.scene.IsValid())
            {
                foreach (var root in source.scene.GetRootGameObjects())
                    usedNames.Add(root.name);
            }

            if (!usedNames.Contains(baseName))
                return baseName;

            int counter = 1;
            string candidate;
            do
            {
                candidate = $"{baseName} {counter}";
                counter++;
            }
            while (usedNames.Contains(candidate));

            return candidate;
        }

        // ================================================================
        //  Menu & Default Asset Resolution
        // ================================================================

        [MenuItem("GameObject/Cable Generator V2/Create Cable", false, 10)]
        public static GameObject CreateCableGeneratorMenu(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Cable");
            if (menuCommand != null && menuCommand.context is GameObject contextGo)
                GameObjectUtility.SetParentAndAlign(go, contextGo);

            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();

            Material defaultMat = FindOrCreateDefaultMaterial();
            if (defaultMat != null)
                renderer.sharedMaterial = defaultMat;

            var gen = go.AddComponent<CableGenerator>();
            gen.profile = FindOrCreateDefaultProfile();
            gen.RebuildMesh();

            Undo.RegisterCreatedObjectUndo(go, "Create Cable Generator V2");
            Selection.activeGameObject = go;
            return go;
        }

        public static Material FindOrCreateDefaultMaterial()
        {
            string[] matGuids = AssetDatabase.FindAssets("dennokoworks_UCG_default t:Material");
            if (matGuids.Length > 0)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuids[0]));
                if (m != null) return m;
            }
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
        }

        public static CableProfile FindOrCreateDefaultProfile()
        {
            string[] guids = AssetDatabase.FindAssets("t:CableGeneratorV2Runtime.CableProfile");
            if (guids.Length > 0)
            {
                var p = AssetDatabase.LoadAssetAtPath<CableProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (p != null) return p;
            }

            // V2 用のデフォルトプロファイルアセットがまだ存在しない場合は Profile_sample に自動生成
            const string folder = "Assets/dennokoworks/CableGeneratorV2/Profile_sample";
            CableMeshExporter.EnsureFolderExists(folder);
            string assetPath = folder + "/HeavyDutyCableProfile.asset";

            var existing = AssetDatabase.LoadAssetAtPath<CableProfile>(assetPath);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<HeavyDutyCableProfile>();
            created.radius   = 0.015f;
            created.segments = 16;
            created.ribCount = 0;
            AssetDatabase.CreateAsset(created, assetPath);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
