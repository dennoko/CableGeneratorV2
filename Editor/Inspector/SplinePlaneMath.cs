using UnityEngine;

namespace CableGeneratorV2Editor
{
    public static class SplinePlaneMath
    {
        public static Vector3 SurfaceParallelDirection(Vector3 preferredDirection, Vector3 normal)
        {
            Vector3 n      = normal.sqrMagnitude < 0.0001f ? Vector3.up : normal.normalized;
            Vector3 planar = Vector3.ProjectOnPlane(preferredDirection, n);

            if (planar.sqrMagnitude < 0.000001f)
            {
                planar = Vector3.Cross(n, Vector3.up);
                if (planar.sqrMagnitude < 0.000001f)
                    planar = Vector3.Cross(n, Vector3.right);
            }
            return planar.normalized;
        }

        public static Quaternion AlignForwardToPlaneNoNormalTwist(
            Quaternion oldRotation, Vector3 planeNormal, Vector3 fallbackDirection)
        {
            Vector3 n             = planeNormal.sqrMagnitude < 0.0001f ? Vector3.up : planeNormal.normalized;
            Vector3 oldForward    = oldRotation * Vector3.forward;
            Vector3 planarForward = Vector3.ProjectOnPlane(oldForward, n);

            if (planarForward.sqrMagnitude < 0.000001f)
                planarForward = SurfaceParallelDirection(fallbackDirection, n);
            if (planarForward.sqrMagnitude < 0.000001f)
                return oldRotation;

            Quaternion delta = Quaternion.FromToRotation(oldForward.normalized, planarForward.normalized);
            return delta * oldRotation;
        }
    }
}
