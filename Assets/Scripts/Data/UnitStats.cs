using System.Collections.Generic;
using UnityEngine;

// Runtime instance of stats used by the battle system.
public class UnitStats
{
    public string unitName;
    public int level;

    public int MaxHP, MaxMP;
    public int Attack, Defense, Magic, Resistance, Speed;
    public int currentHP, currentMP;

    public List<DamageType> weaknesses = new List<DamageType>();
    public List<DamageType> resistances = new List<DamageType>();
    public List<SkillData> skills = new List<SkillData>();

    public DamageType basicAttackType = DamageType.Blunt;
    public int basicAttackPower = 100;
    public bool basicAttackIsPhysical = true;

    public RuntimeAnimatorController battleAnimatorController;

    // ---- animation frames / timing (copied from CharacterData / EnemyData) ----
    public Sprite[] attackFrames;
    public float attackFrameRate = 10f;
    public int basicAttackHitFrame;          // 0 = hit fires at the end of the clip
    public Sprite[] slideForwardFrames;
    public Sprite[] slideBackFrames;
    public float slideFrameRate = 12f;
    public bool flipBattleSprite;

    public Sprite portrait;
    public Sprite battleSprite;

    public CharacterData sourceCharacter;
    public EnemyData sourceEnemy;
    public AIDifficulty aiDifficulty = AIDifficulty.Easy;
    public int expReward;
    public int goldReward;

    public bool IsAlive { get { return currentHP > 0; } }

    public static UnitStats FromCharacter(CharacterData data, int level,
                                          EquipmentData armor, EquipmentData accessory1, EquipmentData accessory2)
    {
        StatBlock s = data.baseStats.Scaled(level);

        StatBlock equipBonus = new StatBlock();
        var extraResists = new List<DamageType>();

        if (data.signatureWeapon != null)
        {
            equipBonus.Add(data.signatureWeapon.statBonus);
            if (data.signatureWeapon.grantedResistances != null)
                extraResists.AddRange(data.signatureWeapon.grantedResistances);
        }
        // armor + BOTH accessory slots
        EquipmentData[] worn = { armor, accessory1, accessory2 };
        foreach (EquipmentData eq in worn)
        {
            if (eq == null) continue;
            equipBonus.Add(eq.statBonus);
            if (eq.grantedResistances != null) extraResists.AddRange(eq.grantedResistances);
        }
        s.Add(equipBonus);

        UnitStats u = new UnitStats
        {
            unitName = data.characterName,
            level = level,
            MaxHP = Mathf.Max(1, s.maxHP),
            MaxMP = s.maxMP,
            Attack = s.attack,
            Defense = s.defense,
            Magic = s.magic,
            Resistance = s.resistance,
            Speed = s.speed,
            portrait = data.portrait,
            sourceCharacter = data,
            battleAnimatorController = data.battleAnimatorController,
            attackFrames = data.battleAttackSprites,
            attackFrameRate = data.battleAttackFrameRate,
            basicAttackHitFrame = data.basicAttackHitFrame,
            slideForwardFrames = data.battleSlideForwardSprites,
            slideBackFrames = data.battleSlideBackSprites,
            slideFrameRate = data.slideFrameRate,
            flipBattleSprite = data.flipBattleSprite
        };

        foreach (LearnableSkill ls in data.learnableSkills)
            if (ls != null && ls.skill != null && level >= ls.learnLevel)
                u.skills.Add(ls.skill);

        u.weaknesses.AddRange(data.weaknesses);
        u.resistances.AddRange(data.resistances);
        u.resistances.AddRange(extraResists);

        if (data.signatureWeapon != null)
        {
            u.basicAttackType = data.signatureWeapon.weaponDamageType;
            u.basicAttackPower = data.signatureWeapon.weaponPower;
        }
        return u;
    }

    public static UnitStats FromEnemy(EnemyData data, int level)
    {
        StatBlock s = data.baseStats.Scaled(level);
        UnitStats u = new UnitStats
        {
            unitName = data.enemyName,
            level = level,
            MaxHP = Mathf.Max(1, s.maxHP),
            MaxMP = s.maxMP,
            Attack = s.attack,
            Defense = s.defense,
            Magic = s.magic,
            Resistance = s.resistance,
            Speed = s.speed,
            battleSprite = data.battleSprite,
            sourceEnemy = data,
            aiDifficulty = data.aiDifficulty,
            basicAttackType = data.basicAttackType,
            basicAttackPower = data.basicAttackPower,
            basicAttackIsPhysical = data.basicAttackIsPhysical,
            battleAnimatorController = data.battleAnimatorController,
            attackFrames = data.attackSprites,
            attackFrameRate = data.attackFrameRate,
            slideForwardFrames = data.slideForwardSprites,
            slideBackFrames = data.slideBackSprites,
            slideFrameRate = data.slideFrameRate,
            flipBattleSprite = data.flipBattleSprite,
            expReward = Mathf.RoundToInt(data.expReward * (1f + (level - 1) * 0.25f)),
            goldReward = Mathf.RoundToInt(data.goldReward * (1f + (level - 1) * 0.25f))
        };

        u.weaknesses.AddRange(data.weaknesses);
        u.resistances.AddRange(data.resistances);
        foreach (EnemySkillEntry e in data.skills)
            if (e != null && e.skill != null) u.skills.Add(e.skill);

        u.currentHP = u.MaxHP;
        u.currentMP = u.MaxMP;
        return u;
    }
}