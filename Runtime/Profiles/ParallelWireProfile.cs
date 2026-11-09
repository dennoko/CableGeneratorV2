using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 2本の円を並べたメガネ型断面プロファイル。
    /// </summary>
    [CreateAssetMenu(fileName = "ParallelWireProfile", menuName = "Cable Generator V2/Parallel Wire Profile")]
    public class ParallelWireProfile : CableProfile
    {
        [Tooltip("各ワイヤーの半径")]
        public float wireRadius = 0.02f;

        [Tooltip("2本のワイヤー中心間の距離")]
        public float spacing = 0.06f;

        [Tooltip("各円の分割数")]
        [Range(4, 32)]
        public int segments = 8;

        public override Vector2[] GetVertices()
        {
            int seg = Mathf.Max(3, segments);
            int count = (seg + 1) * 2;
            var verts = new Vector2[count];

            float halfSpacing = spacing * 0.5f;

            for (int i = 0; i <= seg; i++)
            {
                float angle = (float)i / seg * Mathf.PI * 2f;
                verts[i] = new Vector2(
                    -halfSpacing + Mathf.Cos(angle) * wireRadius,
                    Mathf.Sin(angle) * wireRadius
                );
            }

            for (int i = 0; i <= seg; i++)
            {
                float angle = (float)i / seg * Mathf.PI * 2f;
                verts[seg + 1 + i] = new Vector2(
                    halfSpacing + Mathf.Cos(angle) * wireRadius,
                    Mathf.Sin(angle) * wireRadius
                );
            }

            return verts;
        }

        public override Vector2[] GetNormals()
        {
            int seg = Mathf.Max(3, segments);
            int count = (seg + 1) * 2;
            var norms = new Vector2[count];

            for (int i = 0; i <= seg; i++)
            {
                float angle = (float)i / seg * Mathf.PI * 2f;
                var n = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                norms[i] = n;
                norms[seg + 1 + i] = n;
            }

            return norms;
        }

        public override int GetVerticesPerLoop() => Mathf.Max(3, segments) + 1;

        public override float[] GetUCoords()
        {
            int seg = Mathf.Max(3, segments);
            int count = (seg + 1) * 2;
            var us = new float[count];

            for (int i = 0; i <= seg; i++)
            {
                us[i] = (float)i / seg * 0.5f;
                us[seg + 1 + i] = 0.5f + (float)i / seg * 0.5f;
            }

            return us;
        }
    }
}

