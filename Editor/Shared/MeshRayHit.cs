using UnityEngine;

namespace CableGeneratorV2Editor
{
    public struct MeshRayHit
    {
        public Vector3    point;
        public Vector3    normal;
        public float      distance;
        public Renderer   renderer;
        public MeshFilter meshFilter;
        public int        triangleIndex;
    }
}

