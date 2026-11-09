using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CableGeneratorV2Runtime;

namespace CableGeneratorV2Editor
{
    /// <summary>
    /// 現在のケーブル形状を固定し、ボーンチェーンとスムーズウェイトを付けた FBX として書き出す。
    ///
    /// ・ボーンは「メッシュ生成に使った中心線（接地補正込み）」上に等弧長で配置するため、ウェイトの分布と完全に一致する。
    /// ・座標系は Unity（左手系 Y-up, m）→ FBX（右手系 Y-up, UnitScaleFactor=100 で m）へ X 反転で変換する。
    ///   Unity / Blender のどちらで読み込んでも元の見た目に戻る。
    /// </summary>
    public static partial class CableFbxExporter
    {
        public const string kBonesRootName = "Bones_Root";

        public sealed class Options
        {
            public int    lodTier      = 0;
            public bool   includeBones = true;
            public int    boneCount    = 8;
            public string meshName     = "Cable";
            public Material material;
        }

        public sealed class Result
        {
            public string      assetPath;
            public GameObject  modelAsset;
            public int         boneCount;
            public List<CableSamplePoint> centerline;
            public Matrix4x4[] boneMatrices; // ケーブルローカル空間
        }

        // ================================================================
        //  Entry Points
        // ================================================================

        /// <summary>
        /// FBX を書き出し、インポート設定（マテリアル割り当て等）まで行う。
        /// </summary>
        public static Result Export(CableGenerator generator, string assetPath, Options options)
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            if (string.IsNullOrEmpty(assetPath)) throw new ArgumentException("assetPath is empty");

            var centerline = new List<CableSamplePoint>();
            var weights    = new List<BoneWeight>();
            int bones      = options.includeBones ? Mathf.Clamp(options.boneCount, CableGenerator.kMinBones, CableGenerator.kMaxBones) : 0;

            var tmpMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!generator.BuildExportMesh(tmpMesh, options.lodTier, bones, centerline, bones > 0 ? weights : null)
                    || tmpMesh.vertexCount == 0)
                    return null;

                Matrix4x4[] boneMatrices = bones > 0 ? ComputeBoneMatrices(centerline, bones) : null;

                string materialName = SanitizeName(options.material != null ? options.material.name : "CableMaterial");
                byte[] bytes = BuildFbx(tmpMesh, bones > 0 ? weights.ToArray() : null, boneMatrices,
                    SanitizeName(options.meshName), materialName);

                string fullPath = Path.GetFullPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, bytes);

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureImporter(assetPath, materialName, options.material);

                return new Result
                {
                    assetPath    = assetPath,
                    modelAsset   = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath),
                    boneCount    = bones,
                    centerline   = centerline,
                    boneMatrices = boneMatrices,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tmpMesh);
            }
        }

        static void ConfigureImporter(string assetPath, string materialName, Material material)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null) return;

            importer.globalScale         = 1f;
            importer.useFileScale        = true;
            importer.importBlendShapes   = false;
            importer.importVisibility    = false;
            importer.importCameras       = false;
            importer.importLights        = false;
            importer.importAnimation     = false;
            // None にするとスキンが取り込まれないため Generic（Avatar 生成なし）にする
            importer.animationType       = ModelImporterAnimationType.Generic;
            importer.avatarSetup         = ModelImporterAvatarSetup.NoAvatar;
            importer.importNormals       = ModelImporterNormals.Import;
            importer.importTangents      = ModelImporterTangents.CalculateMikk;
            importer.isReadable          = false;

            if (material != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material)))
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), materialName), material);
            }

            importer.SaveAndReimport();
        }

        // ================================================================
        //  Bone Placement (中心線上の等弧長配置)
        // ================================================================

        /// <summary>
        /// 中心線サンプルから、正規化弧長 i/(K-1) の位置にボーンを配置したケーブルローカル行列を返す。
        /// ウェイト（<see cref="CableGenerator.EvaluateAnalyticalBoneWeight"/>）と同じ弧長パラメータを使う。
        /// </summary>
        public static Matrix4x4[] ComputeBoneMatrices(List<CableSamplePoint> centerline, int boneCount)
        {
            var result = new Matrix4x4[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                float target = boneCount > 1 ? (float)i / (boneCount - 1) : 0f;
                SampleAtNormalizedDistance(centerline, target, out Vector3 pos, out Vector3 tangent, out Vector3 up);
                result[i] = Matrix4x4.TRS(pos, CableSpline.SafeLookRotation(tangent, up), Vector3.one);
            }
            return result;
        }

        static void SampleAtNormalizedDistance(List<CableSamplePoint> samples, float nd,
            out Vector3 pos, out Vector3 tangent, out Vector3 up)
        {
            int n = samples.Count;
            int hi = 1;
            while (hi < n - 1 && samples[hi].normalizedDistance < nd) hi++;
            var a = samples[hi - 1];
            var b = samples[hi];
            float span = b.normalizedDistance - a.normalizedDistance;
            float f = span > 1e-8f ? Mathf.Clamp01((nd - a.normalizedDistance) / span) : 0f;

            pos     = Vector3.Lerp(a.localPos, b.localPos, f);
            tangent = Vector3.Lerp(a.localTangent, b.localTangent, f);
            up      = Vector3.Lerp(a.localUp, b.localUp, f);
            if (tangent.sqrMagnitude < 1e-10f) tangent = b.localPos - a.localPos;
        }

        /// <summary>
        /// スプラインパラメータ t（ノット k なら k / SegmentCount）に対応する正規化弧長を返す。
        /// </summary>
        public static float NormalizedDistanceAtSplineT(List<CableSamplePoint> samples, float splineT)
        {
            int n = samples.Count;
            if (n == 0) return 0f;
            if (splineT <= samples[0].splineT) return samples[0].normalizedDistance;
            for (int i = 1; i < n; i++)
            {
                if (samples[i].splineT >= splineT)
                {
                    float span = samples[i].splineT - samples[i - 1].splineT;
                    float f = span > 1e-8f ? (splineT - samples[i - 1].splineT) / span : 0f;
                    return Mathf.Lerp(samples[i - 1].normalizedDistance, samples[i].normalizedDistance, f);
                }
            }
            return samples[n - 1].normalizedDistance;
        }

        // ================================================================
        //  Coordinate Conversion (Unity 左手系 → FBX 右手系: X 反転)
        // ================================================================

        static readonly Matrix4x4 kMirrorX = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

        public static Matrix4x4 UnityToFbx(Matrix4x4 m) => kMirrorX * m * kMirrorX;

        /// <summary>
        /// 回転行列（列ベクトル規約）から FBX の eEulerXYZ（X→Y→Z の順に適用, R = Rz·Ry·Rx）の角度[度]を求める。
        /// </summary>
        public static Vector3 MatrixToFbxEulerXYZ(Matrix4x4 m)
        {
            double r20 = m.m20;
            double alpha, beta, gamma;
            if (Math.Abs(r20) < 0.9999995)
            {
                beta  = Math.Asin(-r20);
                alpha = Math.Atan2(m.m21, m.m22);
                gamma = Math.Atan2(m.m10, m.m00);
            }
            else
            {
                // ジンバルロック: Z 回転を 0 に固定
                beta  = r20 < 0 ? Math.PI * 0.5 : -Math.PI * 0.5;
                gamma = 0.0;
                alpha = Math.Atan2(-m.m12, m.m11);
            }
            const double rad2deg = 180.0 / Math.PI;
            return new Vector3((float)(alpha * rad2deg), (float)(beta * rad2deg), (float)(gamma * rad2deg));
        }

        /// <summary>MatrixToFbxEulerXYZ の逆変換（検証用）。</summary>
        public static Matrix4x4 FbxEulerXYZToMatrix(Vector3 eulerDeg)
        {
            return Matrix4x4.Rotate(Quaternion.AngleAxis(eulerDeg.z, Vector3.forward))
                 * Matrix4x4.Rotate(Quaternion.AngleAxis(eulerDeg.y, Vector3.up))
                 * Matrix4x4.Rotate(Quaternion.AngleAxis(eulerDeg.x, Vector3.right));
        }

        static double[] ToFbxMatrixArray(Matrix4x4 m)
        {
            // FBX の行列配列は平行移動が 12〜14 番目に入る並び（= Unity 行列の列優先）
            var a = new double[16];
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    a[c * 4 + r] = m[r, c];
            return a;
        }
    }
}
