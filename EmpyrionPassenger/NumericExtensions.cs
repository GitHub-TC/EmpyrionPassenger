using System.Numerics;

namespace EmpyrionPassenger
{
    public static class NumericExtensions
    {
        public static Matrix4x4 Transpose(this Matrix4x4 m)
        {
            return new Matrix4x4(m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43, m.M14, m.M24, m.M34, m.M44);
        }

    }
}
