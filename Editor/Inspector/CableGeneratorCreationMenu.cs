using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public static class CableGeneratorCreationMenu
    {
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
