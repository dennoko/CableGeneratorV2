using UnityEngine;

namespace CableGeneratorV2Runtime
{
    /// <summary>
    /// 複数のケーブルを同心円状に束ねた断面プロファイル。
    /// </summary>
    [CreateAssetMenu(fileName = "ClusteredCableProfile", menuName = "Cable Generator V2/Clustered Cable Profile")]
    public class ClusteredCableProfile : CableProfile
    {
        [Tooltip("ケーブルの本数")]
        [Range(1, 37)]
        public int wireCount = 7;

        [Tooltip("各ケーブルの半径")]
        public float wireRadius = 0.008f;

        [Tooltip("各ケーブルの円周分割数")]
        [Range(4, 32)]
        public int segments = 6;

        [Tooltip("位置の揺らぎの強さ（0=きっちり配置、1=大きく崩れる）")]
        [Range(0f, 1f)]
        public float jitter = 0.3f;

        [Tooltip("半径の揺らぎの強さ（0=均一、1=最大±50%のばらつき）")]
        [Range(0f, 1f)]
        public float radiusJitter = 0.0f;

        [Tooltip("揺らぎのシード値（同じ値なら同じ配置になる）")]
        public int seed = 42;

        Vector2[] cachedCenters;
        float[] cachedRadii;
        int cachedHash;

        protected override void OnValidate()
        {
            InvalidateCache();
            base.OnValidate();
        }

        void InvalidateCache()
        {
            cachedCenters = null;
            cachedRadii = null;
            cachedHash = 0;
        }

        int ComputeParamHash()
        {
            unchecked
            {
                int h = wireCount * 397;
                h = h * 397 + wireRadius.GetHashCode();
                h = h * 397 + segments;
                h = h * 397 + jitter.GetHashCode();
                h = h * 397 + radiusJitter.GetHashCode();
                h = h * 397 + seed;
                return h;
            }
        }

        Vector2[] GetCachedCenters()
        {
            int h = ComputeParamHash();
            if (cachedCenters == null || cachedHash != h)
            {
                cachedCenters = ComputeWireCenters();
                cachedRadii = ComputeWireRadii(cachedCenters.Length);
                cachedHash = h;
            }
            return cachedCenters;
        }

        float[] GetCachedRadii()
        {
            GetCachedCenters();
            return cachedRadii;
        }

        public override Vector2[] GetVertices()
        {
            int seg = Mathf.Max(3, segments);
            var centers = GetCachedCenters();
            var radii = GetCachedRadii();
            int vertsPerWire = seg + 1;
            int totalVerts = vertsPerWire * centers.Length;
            var verts = new Vector2[totalVerts];

            for (int w = 0; w < centers.Length; w++)
            {
                int baseIdx = w * vertsPerWire;
                float r = radii[w];
                for (int i = 0; i <= seg; i++)
                {
                    float angle = (float)i / seg * Mathf.PI * 2f;
                    verts[baseIdx + i] = new Vector2(
                        centers[w].x + Mathf.Cos(angle) * r,
                        centers[w].y + Mathf.Sin(angle) * r
                    );
                }
            }

            return verts;
        }

        public override Vector2[] GetNormals()
        {
            int seg = Mathf.Max(3, segments);
            var centers = GetCachedCenters();
            int vertsPerWire = seg + 1;
            int totalVerts = vertsPerWire * centers.Length;
            var norms = new Vector2[totalVerts];

            for (int w = 0; w < centers.Length; w++)
            {
                int baseIdx = w * vertsPerWire;
                for (int i = 0; i <= seg; i++)
                {
                    float angle = (float)i / seg * Mathf.PI * 2f;
                    norms[baseIdx + i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
            }

            return norms;
        }

        public override int GetVerticesPerLoop() => Mathf.Max(3, segments) + 1;

        public override float[] GetUCoords()
        {
            int seg = Mathf.Max(3, segments);
            var centers = GetCachedCenters();
            int vertsPerWire = seg + 1;
            int totalVerts = vertsPerWire * centers.Length;
            var us = new float[totalVerts];

            float uPerWire = 1f / centers.Length;

            for (int w = 0; w < centers.Length; w++)
            {
                int baseIdx = w * vertsPerWire;
                float uStart = uPerWire * w;
                for (int i = 0; i <= seg; i++)
                    us[baseIdx + i] = uStart + (float)i / seg * uPerWire;
            }

            return us;
        }

        float[] ComputeWireRadii(int count)
        {
            var radii = new float[count];

            if (radiusJitter <= 0f)
            {
                for (int i = 0; i < count; i++)
                    radii[i] = wireRadius;
                return radii;
            }

            var rng = new System.Random(seed + 7919);
            float maxVariation = wireRadius * radiusJitter * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float offset = ((float)rng.NextDouble() * 2f - 1f) * maxVariation;
                radii[i] = wireRadius + offset;
            }

            return radii;
        }

        Vector2[] ComputeWireCenters()
        {
            int count = Mathf.Max(1, wireCount);
            var centers = new Vector2[count];

            float pitch = wireRadius * 2.1f;
            int placed = 0;

            if (placed < count)
                centers[placed++] = Vector2.zero;

            int ring = 1;
            while (placed < count)
            {
                int spotsInRing = ring * 6;
                float ringRadius = pitch * ring;

                for (int i = 0; i < spotsInRing && placed < count; i++)
                {
                    float angle = (float)i / spotsInRing * Mathf.PI * 2f;
                    centers[placed++] = new Vector2(
                        Mathf.Cos(angle) * ringRadius,
                        Mathf.Sin(angle) * ringRadius
                    );
                }

                ring++;
            }

            if (jitter > 0f)
            {
                float maxOffset = wireRadius * jitter * 0.5f;
                var rng = new System.Random(seed);

                for (int i = 1; i < count; i++)
                {
                    float ox = ((float)rng.NextDouble() * 2f - 1f) * maxOffset;
                    float oy = ((float)rng.NextDouble() * 2f - 1f) * maxOffset;
                    centers[i] += new Vector2(ox, oy);
                }
            }

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < count; i++)
                centroid += centers[i];
            centroid /= count;
            for (int i = 0; i < count; i++)
                centers[i] -= centroid;

            return centers;
        }
    }
}

