using UnityEngine;

// Defaults are intentionally ZERO — designers fill in real values.
[System.Serializable]
public class StatBlock
{
    public int maxHP;
    public int maxMP;
    public int attack;
    public int defense;
    public int magic;
    public int resistance; // magical defense
    public int speed;

    // Returns a new StatBlock scaled to the given level (12% growth/level).
    public StatBlock Scaled(int level)
    {
        return new StatBlock
        {
            maxHP = ScaleStat(maxHP, level),
            maxMP = ScaleStat(maxMP, level),
            attack = ScaleStat(attack, level),
            defense = ScaleStat(defense, level),
            magic = ScaleStat(magic, level),
            resistance = ScaleStat(resistance, level),
            speed = ScaleStat(speed, level)
        };
    }

    public void Add(StatBlock other)
    {
        if (other == null) return;
        maxHP += other.maxHP;   maxMP += other.maxMP;
        attack += other.attack; defense += other.defense;
        magic += other.magic;   resistance += other.resistance;
        speed += other.speed;
    }

    public static int ScaleStat(int baseValue, int level)
    {
        return baseValue + Mathf.RoundToInt((level - 1) * baseValue * 0.12f);
    }
}