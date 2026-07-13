using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Four-vertex template used by hardware-instanced grass rendering.
    /// Corner.X is -1/+1 (left/right), Corner.Y is 0/1 (base/top).
    /// </summary>
    public readonly struct GrassBladeVertex : IVertexType
    {
        public readonly Vector2 Corner;
        public readonly Vector2 TextureCoordinate;

        public GrassBladeVertex(Vector2 corner, Vector2 textureCoordinate)
        {
            Corner = corner;
            TextureCoordinate = textureCoordinate;
        }

        public static readonly VertexDeclaration VertexDeclaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
