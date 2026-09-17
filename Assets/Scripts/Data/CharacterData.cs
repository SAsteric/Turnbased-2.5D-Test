using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class LearnableSkill
{
    public SkillData skill;
    public int learnLevel = 1;
}

[CreateAssetMenu(fileName = "Char_", menuName = "RPG/Character")]
public class CharacterData : ScriptableObject
{
    [Header("Identity")]
    public string characterName = "New Character";
    public Sprite portrait;
    public GameObject overworldPrefab;

    [Header("Stats")]
    public int startingLevel = 1;
    public StatBlock baseStats = new StatBlock();

    [Header("Weaknesses & Resistances")]
    public DamageType[] weaknesses = new DamageType[0];
    public DamageType[] resistances = new DamageType[0];

    [Header("Signature Weapon (locked)")]
    public EquipmentData signatureWeapon;

    [Header("Skills learned by level")]
    public List<LearnableSkill> learnableSkills = new List<LearnableSkill>();

    [Header("Battle Animations")]
    public RuntimeAnimatorController battleAnimatorController;

    [Header("Battle Animations — Fallback (for characters without Animator Controllers)")]
    public Sprite[] battleAttackSprites;
    public float battleAttackFrameRate = 10f;
    public Sprite[] battleSlideForwardSprites;
    public Sprite[] battleSlideBackSprites;
    public float slideFrameRate = 12f;
    
    [Header("Basic Attack Hit Frame")]
    public int basicAttackHitFrame = 0;

    [Header("Battle Sprite Facing")]
    public bool flipBattleSprite = false;
}