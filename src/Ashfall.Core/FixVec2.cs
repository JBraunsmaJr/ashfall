namespace Ashfall.Core;

/// <summary>A 2D vector of <see cref="Fix64"/> components, in cell units on the map grid.</summary>
public readonly record struct FixVec2(Fix64 X, Fix64 Y)
{
    public static FixVec2 Zero => default;

    public static FixVec2 FromInts(int x, int y) => new(Fix64.FromInt(x), Fix64.FromInt(y));

    public static FixVec2 operator +(FixVec2 left, FixVec2 right) => new(left.X + right.X, left.Y + right.Y);

    public static FixVec2 operator -(FixVec2 left, FixVec2 right) => new(left.X - right.X, left.Y - right.Y);

    public static FixVec2 operator -(FixVec2 value) => new(-value.X, -value.Y);

    public static FixVec2 operator *(FixVec2 vector, Fix64 scalar) => new(vector.X * scalar, vector.Y * scalar);

    public static FixVec2 operator *(Fix64 scalar, FixVec2 vector) => vector * scalar;

    public static FixVec2 operator /(FixVec2 vector, Fix64 scalar) => new(vector.X / scalar, vector.Y / scalar);

    public Fix64 LengthSquared => (X * X) + (Y * Y);

    public Fix64 Length => FixMath.Sqrt(LengthSquared);

    public static Fix64 Dot(FixVec2 a, FixVec2 b) => (a.X * b.X) + (a.Y * b.Y);

    public static Fix64 DistanceSquared(FixVec2 a, FixVec2 b) => (a - b).LengthSquared;

    /// <summary>Unit vector in the same direction, or <see cref="Zero"/> for a zero vector.</summary>
    public FixVec2 Normalized()
    {
        var length = Length;
        return length == Fix64.Zero ? Zero : this / length;
    }

    public override string ToString() => $"({X}, {Y})";
}
