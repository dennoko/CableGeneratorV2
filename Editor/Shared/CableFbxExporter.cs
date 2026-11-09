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
    public static class CableFbxExporter
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

        // ================================================================
        //  FBX Document Construction
        // ================================================================

        public static byte[] BuildFbx(Mesh mesh, BoneWeight[] weights, Matrix4x4[] boneMatricesUnity,
            string meshName, string materialName)
        {
            bool skinned = weights != null && boneMatricesUnity != null && boneMatricesUnity.Length >= 2;
            int boneCount = skinned ? boneMatricesUnity.Length : 0;

            long nextId = 1_000_000;
            long NewId() => nextId++;

            long geometryId = NewId();
            long meshModelId = NewId();
            long materialId = NewId();
            long bonesRootId = 0, bonesRootAttrId = 0, skinId = 0, poseId = 0;
            var boneIds = new long[boneCount];
            var boneAttrIds = new long[boneCount];
            var clusterIds = new long[boneCount];
            if (skinned)
            {
                bonesRootId = NewId();
                bonesRootAttrId = NewId();
                skinId = NewId();
                poseId = NewId();
                for (int i = 0; i < boneCount; i++)
                {
                    boneIds[i] = NewId();
                    boneAttrIds[i] = NewId();
                    clusterIds[i] = NewId();
                }
            }

            var top = new List<FbxNode>();
            top.Add(BuildHeaderExtension());
            top.Add(new FbxNode("FileId", new byte[] { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 }));
            top.Add(new FbxNode("CreationTime", "1970-01-01 10:00:00:000"));
            top.Add(new FbxNode("Creator", "dennokoworks CableGenerator V2"));
            top.Add(BuildGlobalSettings());

            var documents = new FbxNode("Documents");
            documents.Add("Count", 1);
            var doc = documents.Add("Document", NewId(), "Scene", "Scene");
            var docProps = doc.Add("Properties70");
            docProps.P("SourceObject", "object", "", "");
            docProps.P("ActiveAnimStackName", "KString", "", "", "");
            doc.Add("RootNode", 0L);
            top.Add(documents);

            top.Add(new FbxNode("References"));

            // ---- Definitions ----
            int modelCount = 1 + (skinned ? 1 + boneCount : 0);
            int attrCount = skinned ? 1 + boneCount : 0;
            int deformerCount = skinned ? 1 + boneCount : 0;
            int poseCount = skinned ? 1 : 0;
            var defs = new FbxNode("Definitions");
            defs.Add("Version", 100);
            defs.Add("Count", 1 + modelCount + 1 + 1 + attrCount + deformerCount + poseCount);
            AddObjectType(defs, "GlobalSettings", 1);
            AddObjectType(defs, "Model", modelCount);
            AddObjectType(defs, "Geometry", 1);
            AddObjectType(defs, "Material", 1);
            if (skinned)
            {
                AddObjectType(defs, "NodeAttribute", attrCount);
                AddObjectType(defs, "Deformer", deformerCount);
                AddObjectType(defs, "Pose", poseCount);
            }
            top.Add(defs);

            // ---- Objects ----
            var objects = new FbxNode("Objects");
            objects.Add(BuildGeometry(geometryId, meshName, mesh));
            objects.Add(BuildModel(meshModelId, meshName, "Mesh", Vector3.zero, Vector3.zero));
            objects.Add(BuildMaterial(materialId, materialName));

            Matrix4x4[] boneWorldFbx = null;
            if (skinned)
            {
                boneWorldFbx = new Matrix4x4[boneCount];
                for (int i = 0; i < boneCount; i++)
                    boneWorldFbx[i] = UnityToFbx(boneMatricesUnity[i]);

                var nullAttr = objects.Add("NodeAttribute", bonesRootAttrId, FbxBinaryWriter.ObjectName(kBonesRootName, "NodeAttribute"), "Null");
                nullAttr.Add("TypeFlags", "Null");
                objects.Add(BuildModel(bonesRootId, kBonesRootName, "Null", Vector3.zero, Vector3.zero));

                for (int i = 0; i < boneCount; i++)
                {
                    string boneName = "Bone_" + i;
                    Matrix4x4 parentWorld = i == 0 ? Matrix4x4.identity : boneWorldFbx[i - 1];
                    Matrix4x4 local = parentWorld.inverse * boneWorldFbx[i];

                    var attr = objects.Add("NodeAttribute", boneAttrIds[i], FbxBinaryWriter.ObjectName(boneName, "NodeAttribute"), "LimbNode");
                    attr.Add("Properties70").P("Size", "double", "Number", "", 1.0);
                    attr.Add("TypeFlags", "Skeleton");

                    objects.Add(BuildModel(boneIds[i], boneName, "LimbNode",
                        new Vector3(local.m03, local.m13, local.m23), MatrixToFbxEulerXYZ(local)));
                }

                var skin = objects.Add("Deformer", skinId, FbxBinaryWriter.ObjectName("Skin", "Deformer"), "Skin");
                skin.Add("Version", 101);
                skin.Add("Link_DeformAcuracy", 50.0);

                BuildClusterData(weights, boneCount, out var clusterIndices, out var clusterWeights);
                for (int i = 0; i < boneCount; i++)
                {
                    var cluster = objects.Add("Deformer", clusterIds[i], FbxBinaryWriter.ObjectName("Bone_" + i, "SubDeformer"), "Cluster");
                    cluster.Add("Version", 100);
                    cluster.Add("UserData", "", "");
                    if (clusterIndices[i].Count > 0)
                    {
                        cluster.Add("Indexes", clusterIndices[i].ToArray());
                        cluster.Add("Weights", clusterWeights[i].ToArray());
                    }
                    // Transform はボーン空間で格納する（= inverse(TransformLink) * メッシュのグローバル行列）
                    cluster.Add("Transform", ToFbxMatrixArray(boneWorldFbx[i].inverse));
                    cluster.Add("TransformLink", ToFbxMatrixArray(boneWorldFbx[i]));
                }

                var pose = objects.Add("Pose", poseId, FbxBinaryWriter.ObjectName("BindPose", "Pose"), "BindPose");
                pose.Add("Type", "BindPose");
                pose.Add("Version", 100);
                pose.Add("NbPoseNodes", 2 + boneCount);
                AddPoseNode(pose, meshModelId, Matrix4x4.identity);
                AddPoseNode(pose, bonesRootId, Matrix4x4.identity);
                for (int i = 0; i < boneCount; i++)
                    AddPoseNode(pose, boneIds[i], boneWorldFbx[i]);
            }
            top.Add(objects);

            // ---- Connections ----
            var conns = new FbxNode("Connections");
            Connect(conns, meshModelId, 0L);
            Connect(conns, geometryId, meshModelId);
            Connect(conns, materialId, meshModelId);
            if (skinned)
            {
                Connect(conns, bonesRootId, 0L);
                Connect(conns, bonesRootAttrId, bonesRootId);
                for (int i = 0; i < boneCount; i++)
                {
                    Connect(conns, boneIds[i], i == 0 ? bonesRootId : boneIds[i - 1]);
                    Connect(conns, boneAttrIds[i], boneIds[i]);
                }
                Connect(conns, skinId, geometryId);
                for (int i = 0; i < boneCount; i++)
                {
                    Connect(conns, clusterIds[i], skinId);
                    Connect(conns, boneIds[i], clusterIds[i]);
                }
            }
            top.Add(conns);

            var takes = new FbxNode("Takes");
            takes.Add("Current", "");
            top.Add(takes);

            return FbxBinaryWriter.Write(top);
        }

        static FbxNode BuildHeaderExtension()
        {
            var ext = new FbxNode("FBXHeaderExtension");
            ext.Add("FBXHeaderVersion", 1003);
            ext.Add("FBXVersion", FbxBinaryWriter.Version);
            ext.Add("EncryptionType", 0);

            var now = DateTime.Now;
            var ts = ext.Add("CreationTimeStamp");
            ts.Add("Version", 1000);
            ts.Add("Year", now.Year);
            ts.Add("Month", now.Month);
            ts.Add("Day", now.Day);
            ts.Add("Hour", now.Hour);
            ts.Add("Minute", now.Minute);
            ts.Add("Second", now.Second);
            ts.Add("Millisecond", now.Millisecond);

            ext.Add("Creator", "dennokoworks CableGenerator V2");

            var sceneInfo = ext.Add("SceneInfo", FbxBinaryWriter.ObjectName("GlobalInfo", "SceneInfo"), "UserData");
            sceneInfo.Add("Type", "UserData");
            sceneInfo.Add("Version", 100);
            var meta = sceneInfo.Add("MetaData");
            meta.Add("Version", 100);
            meta.Add("Title", "");
            meta.Add("Subject", "");
            meta.Add("Author", "");
            meta.Add("Keywords", "");
            meta.Add("Revision", "");
            meta.Add("Comment", "");
            var props = sceneInfo.Add("Properties70");
            props.P("DocumentUrl", "KString", "Url", "", "");
            props.P("SrcDocumentUrl", "KString", "Url", "", "");
            props.P("Original", "Compound", "", "");
            props.P("Original|ApplicationVendor", "KString", "", "", "dennokoworks");
            props.P("Original|ApplicationName", "KString", "", "", "CableGenerator V2");
            props.P("Original|ApplicationVersion", "KString", "", "", "2.0");
            props.P("LastSaved", "Compound", "", "");
            props.P("LastSaved|ApplicationVendor", "KString", "", "", "dennokoworks");
            props.P("LastSaved|ApplicationName", "KString", "", "", "CableGenerator V2");
            props.P("LastSaved|ApplicationVersion", "KString", "", "", "2.0");
            return ext;
        }

        static FbxNode BuildGlobalSettings()
        {
            var gs = new FbxNode("GlobalSettings");
            gs.Add("Version", 1000);
            var p = gs.Add("Properties70");
            p.P("UpAxis", "int", "Integer", "", 1);
            p.P("UpAxisSign", "int", "Integer", "", 1);
            p.P("FrontAxis", "int", "Integer", "", 2);
            p.P("FrontAxisSign", "int", "Integer", "", 1);
            p.P("CoordAxis", "int", "Integer", "", 0);
            p.P("CoordAxisSign", "int", "Integer", "", 1);
            p.P("OriginalUpAxis", "int", "Integer", "", 1);
            p.P("OriginalUpAxisSign", "int", "Integer", "", 1);
            p.P("UnitScaleFactor", "double", "Number", "", 100.0);          // 1 unit = 100cm = 1m
            p.P("OriginalUnitScaleFactor", "double", "Number", "", 100.0);
            p.P("AmbientColor", "ColorRGB", "Color", "", 0.0, 0.0, 0.0);
            p.P("DefaultCamera", "KString", "", "", "Producer Perspective");
            p.P("TimeMode", "enum", "", "", 11);
            p.P("TimeSpanStart", "KTime", "Time", "", 0L);
            p.P("TimeSpanStop", "KTime", "Time", "", 46186158000L);
            p.P("CustomFrameRate", "double", "Number", "", 24.0);
            return gs;
        }

        static void AddObjectType(FbxNode defs, string type, int count)
        {
            var ot = defs.Add("ObjectType", type);
            ot.Add("Count", count);
        }

        static FbxNode BuildModel(long id, string name, string type, Vector3 translation, Vector3 eulerDeg)
        {
            var model = new FbxNode("Model", id, FbxBinaryWriter.ObjectName(name, "Model"), type);
            model.Add("Version", 232);
            var p = model.Add("Properties70");
            p.P("Lcl Translation", "Lcl Translation", "", "A", (double)translation.x, (double)translation.y, (double)translation.z);
            p.P("Lcl Rotation", "Lcl Rotation", "", "A", (double)eulerDeg.x, (double)eulerDeg.y, (double)eulerDeg.z);
            p.P("Lcl Scaling", "Lcl Scaling", "", "A", 1.0, 1.0, 1.0);
            p.P("DefaultAttributeIndex", "int", "Integer", "", 0);
            p.P("InheritType", "enum", "", "", 1);
            model.Add("MultiLayer", 0);
            model.Add("MultiTake", 0);
            model.Add("Shading", true);
            model.Add("Culling", "CullingOff");
            return model;
        }

        static FbxNode BuildMaterial(long id, string name)
        {
            var mat = new FbxNode("Material", id, FbxBinaryWriter.ObjectName(name, "Material"), "");
            mat.Add("Version", 102);
            mat.Add("ShadingModel", "lambert");
            mat.Add("MultiLayer", 0);
            var p = mat.Add("Properties70");
            p.P("DiffuseColor", "Color", "", "A", 0.8, 0.8, 0.8);
            p.P("DiffuseFactor", "Number", "", "A", 1.0);
            return mat;
        }

        static FbxNode BuildGeometry(long id, string name, Mesh mesh)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] tris = mesh.triangles;
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            mesh.GetUVs(0, uv0);
            mesh.GetUVs(1, uv1);

            var vertArray = new double[verts.Length * 3];
            for (int i = 0; i < verts.Length; i++)
            {
                vertArray[i * 3]     = -verts[i].x; // X 反転
                vertArray[i * 3 + 1] =  verts[i].y;
                vertArray[i * 3 + 2] =  verts[i].z;
            }

            // X 反転で表裏が入れ替わるため巻き順を逆にする（a,b,c → a,c,b）
            int triCount = tris.Length / 3;
            var polyIndex = new int[tris.Length];
            var uvIndex   = new int[tris.Length];
            var polyNormals = new double[tris.Length * 3];
            for (int t = 0; t < triCount; t++)
            {
                int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                int[] order = { a, c, b };
                for (int k = 0; k < 3; k++)
                {
                    int pv = t * 3 + k;
                    int vi = order[k];
                    polyIndex[pv] = k == 2 ? ~vi : vi; // 多角形の最後の頂点は -(index+1)
                    uvIndex[pv]   = vi;
                    Vector3 n = vi < normals.Length ? normals[vi] : Vector3.up;
                    polyNormals[pv * 3]     = -n.x;
                    polyNormals[pv * 3 + 1] =  n.y;
                    polyNormals[pv * 3 + 2] =  n.z;
                }
            }

            var geom = new FbxNode("Geometry", id, FbxBinaryWriter.ObjectName(name, "Geometry"), "Mesh");
            geom.Add("GeometryVersion", 124);
            geom.Add("Vertices", vertArray);
            geom.Add("PolygonVertexIndex", polyIndex);

            var normalLayer = geom.Add("LayerElementNormal", 0);
            normalLayer.Add("Version", 101);
            normalLayer.Add("Name", "");
            normalLayer.Add("MappingInformationType", "ByPolygonVertex");
            normalLayer.Add("ReferenceInformationType", "Direct");
            normalLayer.Add("Normals", polyNormals);

            AddUvLayer(geom, 0, "UVMap", uv0, verts.Length, uvIndex);
            bool hasUv1 = uv1.Count == verts.Length;
            if (hasUv1) AddUvLayer(geom, 1, "UV1", uv1, verts.Length, uvIndex);

            var matLayer = geom.Add("LayerElementMaterial", 0);
            matLayer.Add("Version", 101);
            matLayer.Add("Name", "");
            matLayer.Add("MappingInformationType", "AllSame");
            matLayer.Add("ReferenceInformationType", "IndexToDirect");
            matLayer.Add("Materials", new[] { 0 });

            var layer0 = geom.Add("Layer", 0);
            layer0.Add("Version", 100);
            AddLayerElement(layer0, "LayerElementNormal", 0);
            AddLayerElement(layer0, "LayerElementMaterial", 0);
            AddLayerElement(layer0, "LayerElementUV", 0);

            if (hasUv1)
            {
                var layer1 = geom.Add("Layer", 1);
                layer1.Add("Version", 100);
                AddLayerElement(layer1, "LayerElementUV", 1);
            }
            return geom;
        }

        static void AddUvLayer(FbxNode geom, int index, string name, List<Vector2> uvs, int vertCount, int[] uvIndex)
        {
            var uvArray = new double[vertCount * 2];
            for (int i = 0; i < vertCount && i < uvs.Count; i++)
            {
                uvArray[i * 2]     = uvs[i].x;
                uvArray[i * 2 + 1] = uvs[i].y;
            }

            var layer = geom.Add("LayerElementUV", index);
            layer.Add("Version", 101);
            layer.Add("Name", name);
            layer.Add("MappingInformationType", "ByPolygonVertex");
            layer.Add("ReferenceInformationType", "IndexToDirect");
            layer.Add("UV", uvArray);
            layer.Add("UVIndex", uvIndex);
        }

        static void AddLayerElement(FbxNode layer, string type, int typedIndex)
        {
            var le = layer.Add("LayerElement");
            le.Add("Type", type);
            le.Add("TypedIndex", typedIndex);
        }

        static void BuildClusterData(BoneWeight[] weights, int boneCount,
            out List<int>[] indices, out List<double>[] values)
        {
            var idx = new List<int>[boneCount];
            var val = new List<double>[boneCount];
            for (int b = 0; b < boneCount; b++)
            {
                idx[b] = new List<int>();
                val[b] = new List<double>();
            }
            indices = idx;
            values  = val;

            void Put(int vertex, int bone, float w)
            {
                if (w <= 0f || bone < 0 || bone >= boneCount) return;
                idx[bone].Add(vertex);
                val[bone].Add(w);
            }

            for (int v = 0; v < weights.Length; v++)
            {
                var bw = weights[v];
                Put(v, bw.boneIndex0, bw.weight0);
                Put(v, bw.boneIndex1, bw.weight1);
                Put(v, bw.boneIndex2, bw.weight2);
                Put(v, bw.boneIndex3, bw.weight3);
            }
        }

        static void AddPoseNode(FbxNode pose, long nodeId, Matrix4x4 globalFbx)
        {
            var pn = pose.Add("PoseNode");
            pn.Add("Node", nodeId);
            pn.Add("Matrix", ToFbxMatrixArray(globalFbx));
        }

        static void Connect(FbxNode conns, long child, long parent) => conns.Add("C", "OO", child, parent);

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Cable";
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c < 0x20 || c == ':' || c == '|' || c == '/' || c == '\\') chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
