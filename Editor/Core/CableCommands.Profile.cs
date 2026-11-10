using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;
using static CableGeneratorV2Editor.CableL10n;

namespace CableGeneratorV2Editor
{
    internal static partial class CableCommands
    {
        // ================================================================
        //  断面プロファイル（見た目タブ。設計書 §5.3 / §5.3.1）
        // ================================================================

        /// <summary>「＋ 新規」で選べる種類（表示名のキー, 型）。</summary>
        public static readonly (string key, Type type)[] ProfileTypes =
        {
            ("profile.type.heavy",    typeof(HeavyDutyCableProfile)),
            ("profile.type.flat",     typeof(FlatCableProfile)),
            ("profile.type.twisted",  typeof(TwistedCableProfile)),
            ("profile.type.bundled",  typeof(BundledCableProfile)),
            ("profile.type.cluster",  typeof(ClusteredCableProfile)),
            ("profile.type.parallel", typeof(ParallelWireProfile)),
            ("profile.type.spiral",   typeof(SpiralTubeProfile)),
        };

        public static string ProfileTypeName(CableProfile profile)
        {
            if (profile == null) return string.Empty;
            foreach (var (key, type) in ProfileTypes)
                if (type == profile.GetType()) return Tr(key);
            return profile.GetType().Name;
        }

        /// <summary>プロジェクト内のプロファイル（名前順）。</summary>
        public static List<CableProfile> FindAllProfiles()
        {
            var list = new List<CableProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(CableProfile).FullName))
            {
                var p = AssetDatabase.LoadAssetAtPath<CableProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && !list.Contains(p)) list.Add(p);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        static CableProfile s_usersProfile;
        static int s_usersCount;
        static double s_usersTime;

        /// <summary>
        /// 開いているシーンでこのプロファイルを使っているケーブルの本数。
        /// 表示の更新（ドラッグ中も呼ばれる）のたびにシーン全体を探さないよう、同じプロファイルは 1 秒だけ覚えておく。
        /// </summary>
        public static int CountProfileUsers(CableProfile profile)
        {
            if (profile == null) return 0;
            double now = EditorApplication.timeSinceStartup;
            if (profile == s_usersProfile && now - s_usersTime < 1.0) return s_usersCount;

            int count = 0;
            foreach (var gen in UnityEngine.Object.FindObjectsByType<CableGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (gen.profile == profile && !EditorUtility.IsPersistent(gen)) count++;
            s_usersProfile = profile;
            s_usersCount = count;
            s_usersTime = now;
            return count;
        }

        public static void AssignProfile(CableGenerator gen, CableProfile profile)
        {
            if (gen == null || gen.profile == profile) return;
            Undo.RecordObject(gen, Tr("undo.assignProfile"));
            gen.profile = profile;
            s_usersProfile = null;   // 使用中の本数を数え直す
            EditorUtility.SetDirty(gen);
            gen.RequestRefresh();
        }

        /// <summary>
        /// 「複製して編集」: 今のプロファイルを複製し、このケーブルに割り当てる（Undo で元に戻る）。
        /// 保存先は元と同じフォルダ。元が書き込めない場所ならツール設定の「プロファイルの保存先」。
        /// </summary>
        public static CableResult DuplicateProfile(CableGenerator gen)
        {
            var source = gen != null ? gen.profile : null;
            if (source == null) return CableResult.Fail(Tr("msg.noProfile"));

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets")) folder = ProfileFolder();
            CableMeshExporter.EnsureFolderExists(folder);

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}_{SafeName(gen.gameObject.name)}.asset");
            CableProfile copy;
            if (!string.IsNullOrEmpty(sourcePath) && AssetDatabase.CopyAsset(sourcePath, path))
            {
                copy = AssetDatabase.LoadAssetAtPath<CableProfile>(path);
            }
            else
            {
                copy = UnityEngine.Object.Instantiate(source);   // パッケージ内などコピーできない場所
                AssetDatabase.CreateAsset(copy, path);
            }
            if (copy == null) return CableResult.Fail(Tr("msg.duplicateFailed"));

            AssignProfile(gen, copy);
            return CableResult.Success(Tr("msg.duplicated", copy.name));
        }

        /// <summary>「＋ 新規」: 種類を選んで初期値のプロファイルを作り、このケーブルに割り当てる。</summary>
        public static CableResult CreateProfile(CableGenerator gen, Type type, string typeName)
        {
            if (gen == null || type == null) return CableResult.Fail(string.Empty);
            string folder = ProfileFolder();
            CableMeshExporter.EnsureFolderExists(folder);

            var created = (CableProfile)ScriptableObject.CreateInstance(type);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(typeName)}.asset");
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            AssignProfile(gen, created);
            return CableResult.Success(Tr("msg.profileCreated", created.name));
        }

        static string ProfileFolder()
        {
            string folder = CableToolSettings.instance.profileFolder;
            return string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets") ? CableToolSettings.DefaultProfileFolder : folder;
        }

        static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
