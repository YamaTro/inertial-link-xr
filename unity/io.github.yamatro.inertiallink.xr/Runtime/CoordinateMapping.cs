using UnityEngine;
using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    public static class CoordinateMapping
    {
        // Wire is OpenXR right-handed (+X right, +Y up, -Z forward).
        // Unity is left-handed (+X right, +Y up, +Z forward).
        public static Vector3 PolarVectorToUnity(Float3 value)
        {
            return new Vector3(value.X, value.Y, -value.Z);
        }

        // Angular velocity is an axial vector, so handedness reflection also flips X/Y.
        public static Vector3 AngularVelocityToUnity(Float3 value)
        {
            return new Vector3(-value.X, -value.Y, value.Z);
        }

        public static Quaternion RotationToUnity(Float4 value)
        {
            return new Quaternion(-value.X, -value.Y, value.Z, value.W);
        }
    }
}
