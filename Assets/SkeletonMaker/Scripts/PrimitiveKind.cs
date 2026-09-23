namespace SkeletonMaker
{
    /// <summary>
    /// The shape a placeable element represents. Kept separate from its visual
    /// mesh so a future raymarch/SDF material can key off the same enum -
    /// every kind here maps to a well-known SDF primitive.
    /// </summary>
    public enum PrimitiveKind
    {
        Sphere,
        Box,
        Capsule,
        Pyramid,
        Torus,
        RoundBox,
        Cone,
        Octahedron,
        HexagonalPrism,
        Cylinder,
        TriangularPrism,
        Link,
    }
}
