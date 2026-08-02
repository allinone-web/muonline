using Client.Main.Graphics;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects;

internal sealed class DroppedItemVisual
{
    private DroppedItemShineEffect _shineEffect;

    public void Reset()
    {
        _shineEffect = null;
    }

    public bool IsShineEffect(WorldObject child)
    {
        return ReferenceEquals(child, _shineEffect);
    }

    public void AttachShineEffect(DroppedItemObject owner)
    {
        if (_shineEffect != null)
            return;

        _shineEffect = new DroppedItemShineEffect(owner);
        owner.Children.Add(_shineEffect);
        _ = _shineEffect.Load();
    }

    public void DrawShineEffect(DroppedItemObject owner, GameTime gameTime, bool pickedUp, bool renderVisuals)
    {
        if (!owner.Visible || pickedUp)
            return;

        if (Camera.Instance?.Frustum != null && !Camera.Instance.Frustum.Intersects(owner.BoundingBoxWorld))
            return;

        if (!renderVisuals)
            return;

        _shineEffect?.Draw(gameTime);
    }
}
