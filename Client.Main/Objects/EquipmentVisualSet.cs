using Client.Main.Helpers;
using Client.Main.Objects.Player;
using Client.Main.Objects.Wings;

namespace Client.Main.Objects;

public sealed class EquipmentVisualSet
{
    public PlayerMaskHelmObject HelmMask { get; }
    public PlayerHelmObject Helm { get; }
    public PlayerArmorObject Armor { get; }
    public PlayerPantObject Pants { get; }
    public PlayerGloveObject Gloves { get; }
    public PlayerBootObject Boots { get; }
    public WeaponObject Weapon1 { get; }
    public WeaponObject Weapon2 { get; }
    public WingObject Wings { get; }

    public EquipmentVisualSet()
    {
        HelmMask = new PlayerMaskHelmObject { LinkParentAnimation = true, Hidden = true };
        Helm = new PlayerHelmObject { LinkParentAnimation = true };
        Armor = new PlayerArmorObject { LinkParentAnimation = true };
        Pants = new PlayerPantObject { LinkParentAnimation = true };
        Gloves = new PlayerGloveObject { LinkParentAnimation = true };
        Boots = new PlayerBootObject { LinkParentAnimation = true };
        Weapon1 = new WeaponObject();
        Weapon2 = new WeaponObject();
        Wings = new WingObject { LinkParentAnimation = false, Hidden = true };
    }

    public void AddTo(ChildrenCollection<WorldObject> children)
    {
        children.Add(HelmMask);
        children.Add(Helm);
        children.Add(Armor);
        children.Add(Pants);
        children.Add(Gloves);
        children.Add(Boots);
        children.Add(Weapon1);
        children.Add(Weapon2);
        children.Add(Wings);
    }
}
