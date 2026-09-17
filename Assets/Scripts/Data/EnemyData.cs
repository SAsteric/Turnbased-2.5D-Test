using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class EnemySkillEntry
{
    public SkillData skill;
    public int weight = 10;
}

[CreateAssetMenu(fileName = "Enemy_", menuName = "RPG/Enemy")]
public class EnemyData : ScriptableObject
{
    [Header("Identity")]
    public string enemyName = "New Enemy";
    public Sprite battleSprite;
    public float displayScale = 1f;

    [Header("Stats (scaled by the area's battle level)")]
    public StatBlock baseStats = new StatBlock();

    [Header("Basic Attack")]
    public int basicAttackPower = 100;
    public DamageType basicAttackType = DamageType.Blunt;
    public bool basicAttackIsPhysical = true;

    [Header("Weaknesses & Resistances — MULTIPLE allowed")]
    public DamageType[] weaknesses = new DamageType[0];
    public DamageType[] resistances = new DamageType[0];

    [Header("AI Brain")]
    public AIDifficulty aiDifficulty = AIDifficulty.Easy;

    [Header("Skills this enemy can use")]
    public List<EnemySkillEntry> skills = new List<EnemySkillEntry>();

    [Header("Battle Animations — THE way for real frame counts")]
    [Tooltip("Animator Controller with states named Idle, Attack, SlideForward, SlideBack (Hit/Death optional).")]
    public RuntimeAnimatorController battleAnimatorController;

    [Header("Fallback: small frame sequences (skip if using the controller above)")]
    public Sprite[] attackSprites;
    public float attackFrameRate = 10f;
    public Sprite[] slideForwardSprites;
    public Sprite[] slideBackSprites;
    public float slideFrameRate = 12f;

    [Header("Battle Sprite Facing")]
    [Tooltip("Enemies stand LEFT, facing RIGHT. Tick if the art faces LEFT.")]
    public bool flipBattleSprite = false;

    public int expReward = 10;
    public int goldReward = 5;
}