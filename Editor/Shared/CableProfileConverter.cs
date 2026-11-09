using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    public static class CableProfileConverter
    {
        [MenuItem("Assets/Convert V1 Profile to CableGenerator V2 (Copy)", false, 2000)]
        public static void ConvertSelectedV1Profile()
        {
            var selected = Selection.activeObject as ScriptableObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("V1 Profile Converter", "Please select a V1 CableProfile ScriptableObject asset in Project view.", "OK");
                return;
            }

            string typeName = selected.GetType().Name;
            Type targetType = null;
            switch (typeName)
            {
                case "HeavyDutyCableProfile": targetType = typeof(HeavyDutyCableProfile); break;
                case "FlatCableProfile": targetType = typeof(FlatCableProfile); break;
                case "ParallelWireProfile": targetType = typeof(ParallelWireProfile); break;
                case "BundledCableProfile": targetType = typeof(BundledCableProfile); break;
                case "ClusteredCableProfile": targetType = typeof(ClusteredCableProfile); break;
                case "SpiralTubeProfile": targetType = typeof(SpiralTubeProfile); break;
                case "TwistedCableProfile": targetType = typeof(TwistedCableProfile); break;
                default:
                    EditorUtility.DisplayDialog("V1 Profile Converter", $"Unsupported profile type: {typeName}", "OK");
                    return;
            }

            var newProfile = ScriptableObject.CreateInstance(targetType) as CableProfile;
            if (newProfile == null) return;

            // Copy matching serialized fields
            var srcFields = selected.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var dstFields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var sf in srcFields)
            {
                foreach (var df in dstFields)
                {
                    if (df.Name == sf.Name && df.FieldType == sf.FieldType)
                    {
                        df.SetValue(newProfile, sf.GetValue(selected));
                        break;
                    }
                }
            }

            string srcPath = AssetDatabase.GetAssetPath(selected);
            string dir = Path.GetDirectoryName(srcPath);
            string baseName = Path.GetFileNameWithoutExtension(srcPath);
            string dstPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(dir, $"{baseName}_V2.asset").Replace("\\", "/"));

            AssetDatabase.CreateAsset(newProfile, dstPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = newProfile;
            EditorGUIUtility.PingObject(newProfile);
            Debug.Log($"[CableGenerator V2] Converted V1 profile '{selected.name}' -> '{dstPath}'");
        }

        [MenuItem("Assets/Convert V1 Profile to CableGenerator V2 (Copy)", true)]
        public static bool ValidateConvertSelectedV1Profile()
        {
            if (Selection.activeObject is ScriptableObject so)
            {
                string t = so.GetType().Name;
                return t == "HeavyDutyCableProfile" || t == "FlatCableProfile" ||
                       t == "ParallelWireProfile" || t == "BundledCableProfile" ||
                       t == "ClusteredCableProfile" || t == "SpiralTubeProfile" ||
                       t == "TwistedCableProfile";
            }
            return false;
        }
    }
}
