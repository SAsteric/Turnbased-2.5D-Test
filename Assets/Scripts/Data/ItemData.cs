using UnityEngine;

public enum ItemCategory { Item, Consumable, Equipment, KeyItem }

[CreateAssetMenu(fileName = "Item_", menuName = "RPG/Item")]
public class ItemData : ScriptableObject
{
    [Header("Info")]
    public string itemName = "New Item";
    [TextArea(2, 4)] public string description = "";
    public Sprite icon;

    [Header("Category (which inventory pocket)")]
    public ItemCategory category = ItemCategory.Consumable;

    [Header("Consumable Effects")]
    public int healHP = 0;
    public int healMP = 0;
    public bool revive = false;

    [Header("Battle Item Damage (leave 0 for none)")]
    public int damagePower = 0;
    public DamageType damageType = DamageType.True;
    public TargetSide side = TargetSide.Enemies;
    public TargetScope scope = TargetScope.Single;

    [Header("Turn Priority (0 = normal)")]
    [Range(0, 3)] public int priority = 0;
}