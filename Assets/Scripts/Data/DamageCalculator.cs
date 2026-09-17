using System.Collections.Generic;
using UnityEngine;

// IMPORTANT: this struct lives OUTSIDE the DamageCalculator class —
// top-level, exactly like this. Do NOT nest it inside the class.
public struct DamageResult
{
    public int amount;
    public float typeMultiplier;
    public bool isWeakness;
    public bool isResistance;
    public DamageType type;   // NEW — drives the hit FX
}

public static class DamageCalculator
{
    public const float WeaknessMultiplier = 2.0f;
    public const float ResistanceMultiplier = 0.5f;
    public const float DefendMultiplier = 0.5f;   // guarding halves damage
    public const float VarianceMin = 0.9f;
    public const float VarianceMax = 1.1f;

    public static DamageResult Calculate(UnitStats attacker, UnitStats defender,
        DamageType type, bool isPhysical, int power, bool defenderIsDefending)
    {
        float atk = isPhysical ? attacker.Attack : attacker.Magic;
        float def = isPhysical ? defender.Defense : defender.Resistance;

        float raw = atk * (power / 100f) - def * 0.5f;
        raw = Mathf.Max(atk * 0.15f, raw); // damage floor

        float mult = 1f;
        bool weak = false, res = false;

        // TRUE damage ignores the entire weakness/resistance system.
        if (type != DamageType.True)
        {
            if (defender.weaknesses.Contains(type)) { mult *= WeaknessMultiplier; weak = true; }
            if (defender.resistances.Contains(type)) { mult *= ResistanceMultiplier; res = true; }
        }

        if (defenderIsDefending) mult *= DefendMultiplier;

        int amount = Mathf.Max(1, Mathf.RoundToInt(raw * mult * Random.Range(VarianceMin, VarianceMax)));
        return new DamageResult { amount = amount, typeMultiplier = mult, isWeakness = weak, isResistance = res, type = type };
    }

    // Deterministic (no randomness) — used by enemy AI to plan.
    public static float PredictDamage(UnitStats attacker, UnitStats defender,
        DamageType type, bool isPhysical, int power, bool defenderIsDefending)
    {
        float atk = isPhysical ? attacker.Attack : attacker.Magic;
        float def = isPhysical ? defender.Defense : defender.Resistance;
        float raw = atk * (power / 100f) - def * 0.5f;
        raw = Mathf.Max(atk * 0.15f, raw);

        float mult = 1f;
        if (type != DamageType.True)
        {
            if (defender.weaknesses.Contains(type)) mult *= WeaknessMultiplier;
            if (defender.resistances.Contains(type)) mult *= ResistanceMultiplier;
        }
        if (defenderIsDefending) mult *= DefendMultiplier;
        return raw * mult;
    }

    public static int CalculateHeal(UnitStats caster, int power)
    {
        return Mathf.Max(1, Mathf.RoundToInt(caster.Magic * (power / 100f) * Random.Range(VarianceMin, VarianceMax)));
    }

    public static DamageResult CalculateItem(ItemData item, UnitStats defender, bool defending)
    {
        float mult = 1f;
        bool weak = false, res = false;
        if (item.damageType != DamageType.True)
        {
            if (defender.weaknesses.Contains(item.damageType)) { mult *= WeaknessMultiplier; weak = true; }
            if (defender.resistances.Contains(item.damageType)) { mult *= ResistanceMultiplier; res = true; }
        }
        if (defending) mult *= DefendMultiplier;

        int amount = Mathf.Max(1, Mathf.RoundToInt(item.damagePower * mult * Random.Range(VarianceMin, VarianceMax)));
        return new DamageResult { amount = amount, typeMultiplier = mult, isWeakness = weak, isResistance = res, type = item.damageType };
    }
}