using UnityEditor;
using UnityEngine;

namespace CableGeneratorV2Editor
{
    public static class CableMeshExporter
    {
        public const string DefaultOutputFolder = "Assets/dennokoworks/CableGeneratorV2/BakedMesh";

        /// <summary>
        /// 保存先フォルダを正規化する。Assets 配下でなければダイアログを出して null を返す。
        /// </summary>
        public static string ResolveOutputFolder(string outputFolder)
        {
            string folder = string.IsNullOrEmpty(outputFolder) ? DefaultOutputFolder : outputFolder;
            folder = folder.Replace("\\", "/").TrimEnd('/');

            if (folder != "Assets" && !folder.StartsWith("Assets/"))
            {
                EditorUtility.DisplayDialog("エラー",
                    $"保存先フォルダは Assets 配下のパスを指定してください。\n指定値: {folder}", "OK");
                return null;
            }
            return folder;
        }

        /// <summary>
        /// メッシュを .asset ファイルとして保存する。
        /// </summary>
        public static string SaveMeshAsset(Mesh sourceMesh, string defaultName, string outputFolder, bool ping = true)
        {
            if (sourceMesh == null || sourceMesh.vertexCount == 0)
            {
                EditorUtility.DisplayDialog("エラー", "保存するメッシュがありません。先にメッシュを生成してください。", "OK");
                return null;
            }

            string folder = ResolveOutputFolder(outputFolder);
            if (folder == null) return null;

            EnsureFolderExists(folder);

            string path = GenerateUniqueAssetPath(folder, defaultName);
            string actualName = System.IO.Path.GetFileNameWithoutExtension(path);

            Mesh copy = Object.Instantiate(sourceMesh);
            copy.name = actualName;
            // 旧バージョンの生成メッシュは「シーンに保存しない」フラグ付きのことがあるので、アセット化する前に必ず外す
            copy.hideFlags = HideFlags.None;

            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();

            if (ping)
                EditorGUIUtility.PingObject(copy);
            Debug.Log($"[CableGenerator V2] メッシュを保存しました: {path}");

            return path;
        }

        public static string GenerateUniqueAssetPath(string folder, string baseName, string extension = ".asset")
        {
            string path = $"{folder}/{baseName}{extension}";
            if (!AssetExists(path))
                return path;

            int counter = 1;
            do
            {
                path = $"{folder}/{baseName} {counter}{extension}";
                counter++;
            }
            while (AssetExists(path));

            return path;
        }

        static bool AssetExists(string path)
            => AssetDatabase.LoadAssetAtPath<Object>(path) != null || System.IO.File.Exists(path);

        public static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Replace("\\", "/").Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
