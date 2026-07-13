using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Per-blade data. A 68-byte instance replaces six 40-byte expanded vertices,
    /// reducing grass geometry memory and upload volume by roughly 70%.
    /// </summary>
    public readonly struct GrassBladeInstanceData : IVertexType
    {
        // x=centerX, y=centerY, z=left base height, w=right base height
        public readonly Vector4 PositionHeights;

        // x=half width, y=height, z=cos(angle), w=sin(angle)
        public readonly Vector4 Shape;

        // x=wind dir X, y=wind dir Y, z=phase, w=sway amplitude
        public readonly Vector4 Wind;

        // x=u0, y=u1, z=lean amount, w=stable density threshold [0..1]
        public readonly Vector4 UvLeanDensity;

        public readonly Color Color;

        public GrassBladeInstanceData(
            Vector4 positionHeights,
            Vector4 shape,
            Vector4 wind,
            Vector4 uvLeanDensity,
            Color color)
        {
            PositionHeights = positionHeights;
            Shape = shape;
            Wind = wind;
            UvLeanDensity = uvLeanDensity;
            Color = color;
        }

        public static readonly VertexDeclaration VertexDeclaration = new(
            new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
            new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
            new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
            new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4),
            new VertexElement(64, VertexElementFormat.Color, VertexElementUsage.Color, 1));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
