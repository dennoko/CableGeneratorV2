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
            var go = CreateCable(menuCommand?.context as GameObject);
            // 作ったらウィンドウを開いて「配線」タブから始める（設計書 §3.1）
            CableGeneratorWindow.Open(go.GetComponent<CableGenerator>(), CableGeneratorWindow.TabWire);
            return go;
        }

        /// <summary>
        /// ケーブルを作る（メニュー・ウィンドウの「＋ 新規」）。parent が null ならシーン直下。
        /// Inspector ではコンポーネントを畳んだ状態にする（Inspector は使わない体験。設計書 §7.1）。
        /// </summary>
        public static GameObject CreateCable(GameObject parent)
        {
            GameObject go = new GameObject("Cable");
            if (parent != null) GameObjectUtility.SetParentAndAlign(go, parent);

            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();

            Material defaultMat = FindOrCreateDefaultMaterial();
            if (defaultMat != null)
                renderer.sharedMaterial = defaultMat;

            var gen = go.AddComponent<CableGenerator>();
            gen.profile = FindOrCreateDefaultProfile();
            gen.RebuildMesh();
            UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(gen, false);

            Undo.RegisterCreatedObjectUndo(go, CableL10n.Tr("undo.createCable"));
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

