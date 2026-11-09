using System;
using System.Collections.Generic;
using UnityEngine;

namespace CableGeneratorV2Editor
{
    public static partial class CableFbxExporter
    {
        // ================================================================
        //  Geometry & Skinning Nodes
        // ================================================================

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
