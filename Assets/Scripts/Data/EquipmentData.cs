using UnityEngine;

[CreateAssetMenu(fileName = "Equip_", menuName = "RPG/Equipment")]
public class EquipmentData : ScriptableObject
{
    public string equipName = "New Equipment";
    [TextArea(2, 4)] public string description = "";
    public Sprite icon;
    public EquipSlot slot = EquipSlot.Armor;

    [Header("Stat Bonuses")]
    public StatBlock statBonus = new StatBlock();

    [Header("Extra Resistances Granted While Equipped")]
    public DamageType[] grantedResistances = new DamageType[0];

    [Header("Weapon Only — defines the character's basic attack")]
    public DamageType weaponDamageType = DamageType.Slash;
    public int weaponPower = 100;
}