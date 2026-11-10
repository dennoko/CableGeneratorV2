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
        /// recordUndo が false なら Undo に記録しない（Prefab に保存してすぐ破棄する一時オブジェクト用）。
        /// </summary>
        static void CopyAttachments(CableGenerator generator, Transform defaultParent, System.Func<int, Transform> parentForKnot,
            bool recordUndo = true)
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
                if (recordUndo) Undo.RegisterCreatedObjectUndo(copy, Tr("undo.copyAttachment"));

                Transform parent = parentForKnot?.Invoke(attachment.knotIndex) ?? defaultParent;
                copy.transform.SetParent(parent != null ? parent : defaultParent, true);
            }
        }

        static void DisableSourceAfterBake(GameObject sourceObject)
        {
            Undo.RecordObject(sourceObject, Tr("undo.disableSource"));
            bool editorOnlyTagExists = System.Array.IndexOf(InternalEditorUtility.tags, "EditorOnly") >= 0;
            if (editorOnlyTagExists)
                sourceObject.tag = "EditorOnly";
            else
                Debug.LogWarning("[CableGenerator V2] EditorOnly tag not found. Skipped tagging the source cable.");
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
    }
}
