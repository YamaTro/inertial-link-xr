using System;

namespace YamaTro.InertialLink.Core
{
    public struct Float3 : IEquatable<Float3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Float3 Zero { get { return new Float3(0f, 0f, 0f); } }
        public bool IsFinite { get { return Finite(X) && Finite(Y) && Finite(Z); } }
        public float Magnitude { get { return (float)Math.Sqrt((X * X) + (Y * Y) + (Z * Z)); } }

        public static Float3 operator +(Float3 a, Float3 b) { return new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Float3 operator -(Float3 a, Float3 b) { return new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Float3 operator *(Float3 value, float scale) { return new Float3(value.X * scale, value.Y * scale, value.Z * scale); }
        public static Float3 Lerp(Float3 from, Float3 to, float t)
        {
            var bounded = Math.Max(0f, Math.Min(1f, t));
            return from + ((to - from) * bounded);
        }

        public bool Equals(Float3 other) { return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z); }
        public override bool Equals(object obj) { return obj is Float3 && Equals((Float3)obj); }
        public override int GetHashCode() { return X.GetHashCode() ^ (Y.GetHashCode() << 1) ^ (Z.GetHashCode() << 2); }
        public override string ToString() { return string.Format("({0:F3}, {1:F3}, {2:F3})", X, Y, Z); }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    public struct Float4
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public Float4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public bool IsFinite
        {
            get
            {
                return Finite(X) && Finite(Y) && Finite(Z) && Finite(W);
            }
        }

        public float Norm { get { return (float)Math.Sqrt((X * X) + (Y * Y) + (Z * Z) + (W * W)); } }

        public Float4 Normalized()
        {
            var norm = Norm;
            return norm > 0f ? new Float4(X / norm, Y / norm, Z / norm, W / norm) : this;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
