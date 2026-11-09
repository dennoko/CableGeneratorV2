using System;
using UnityEngine;

namespace CableGeneratorV2Runtime
{
    public enum CableTangentMode
    {
        AutoSmooth = 0,
        Mirrored   = 1,
        Continuous = 2,
        Broken     = 3,
    }

    public enum CableSplineModification
    {
        Default        = 0,
        KnotModified   = 1,
        KnotInserted   = 2,
        KnotRemoved    = 3,
        ClosedModified = 4,
    }

    [Serializable]
    public struct CableKnot
    {
        public Vector3          position;
        public Vector3          tangentIn;
        public Vector3          tangentOut;
        public Quaternion       rotation;
        public CableTangentMode mode;
        public float            sag;

        public CableKnot(Vector3 position)
        {
            this.position   = position;
            this.tangentIn  = new Vector3(0f, 0f, -0.5f);
            this.tangentOut = new Vector3(0f, 0f,  0.5f);
            this.rotation   = Quaternion.identity;
            this.mode       = CableTangentMode.AutoSmooth;
            this.sag        = 0f;
        }

        public CableKnot(Vector3 position, Vector3 tangentIn, Vector3 tangentOut, Quaternion rotation, CableTangentMode mode = CableTangentMode.Mirrored, float sag = 0f)
        {
            this.position   = position;
            this.tangentIn  = tangentIn;
            this.tangentOut = tangentOut;
            this.rotation   = NormalizeSafe(rotation);
            this.mode       = mode;
            this.sag        = sag;
        }

        static Quaternion NormalizeSafe(Quaternion q)
        {
            float magSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (magSq < 1e-8f) return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(magSq);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }
    }
}
