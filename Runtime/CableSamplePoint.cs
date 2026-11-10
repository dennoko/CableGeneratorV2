using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public struct CableSamplePoint
    {
        public Vector3 localPos;
        public Vector3 localTangent;
        public Vector3 localRight;
        public Vector3 localUp;
        public float   cumulativeLength;
        public float   normalizedDistance;
        public float   splineT;
    }
}

