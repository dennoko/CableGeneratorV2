using System;
using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Editor
{
    public static partial class CableFbxExporter
    {
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
    }
}

