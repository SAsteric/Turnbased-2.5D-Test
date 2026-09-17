using System.Collections.Generic;
using UnityEngine;

public static class EnemyAI
{
    public static BattleAction Decide(BattleUnit self, List<BattleUnit> allies, List<BattleUnit> opponents)
    {
        List<BattleUnit> foes = Alive(opponents);
        if (foes.Count == 0) return DefendAction(self);

        switch (self.Stats.aiDifficulty)
        {
            case AIDifficulty.Normal: return Normal(self, allies, foes);
            case AIDifficulty.Hard: return Hard(self, allies, foes);
            case AIDifficulty.Hardest: return Hardest(self, allies, foes);
            default: return Easy(self, allies, foes);
        }
    }

    // ---------------- EASY: mostly random attacks ----------------
    private static BattleAction Easy(BattleUnit self, List<BattleUnit> allies, List<BattleUnit> foes)
    {
        if (Random.value < 0.25f)
        {
            SkillData s = RandomUsableSkill(self);
            if (s != null) return SkillOnRandomTargets(self, s, allies, foes);
        }
        return BasicAttackAction(self, foes[Random.Range(0, foes.Count)]);
    }

    // ---------------- NORMAL: heals allies, biases to weak targets ----------------
    private static BattleAction Normal(BattleUnit self, List<BattleUnit> allies, List<BattleUnit> foes)
    {
        BattleAction heal;
        if (TryHeal(self, allies, 0.40f, out heal)) return heal;

        if (Random.value < 0.5f)
        {
            SkillData s = RandomUsableSkill(self);
            if (s != null && !s.isHealing) return SkillOnRandomTargets(self, s, allies, foes);
        }

        BattleUnit target = Random.value < 0.6f ? LowestHP(foes) : foes[Random.Range(0, foes.Count)];
        return BasicAttackAction(self, target);
    }

    // ---------------- HARD: scores every option, exploits weaknesses ----------------
    private static BattleAction Hard(BattleUnit self, List<BattleUnit> allies, List<BattleUnit> foes)
    {
        BattleAction heal;
        if (TryHeal(self, allies, 0.5f, out heal)) return heal;

        List<Scored> options = ScoreOffenses(self, foes);
        if (options.Count == 0) return BasicAttackAction(self, LowestHP(foes));
        options.Sort((a, b) => b.score.CompareTo(a.score));
        return options[0].action;
    }

    // ---------------- HARDEST: optimal play, kill-securing, threat targeting ----------------
    private static BattleAction Hardest(BattleUnit self, List<BattleUnit> allies, List<BattleUnit> foes)
    {
        BattleAction heal;
        if (TryHeal(self, allies, 0.35f, out heal)) return heal;

        List<Scored> options = ScoreOffenses(self, foes);
        if (options.Count > 0)
        {
            options.Sort((a, b) => b.score.CompareTo(a.score));
            return options[0].action;
        }

        // Out of resources: hit the biggest threat, or turtle if nearly dead.
        if ((float)self.Stats.currentHP / self.Stats.MaxHP < 0.2f) return DefendAction(self);
        BattleUnit threat = foes[0];
        foreach (BattleUnit f in foes)
            if (f.Stats.Attack + f.Stats.Magic > threat.Stats.Attack + threat.Stats.Magic) threat = f;
        return BasicAttackAction(self, threat);
    }

    // ---------------- shared helpers ----------------

    private class Scored { public BattleAction action; public float score; }

    // Scores basic attacks + skills against every foe. Because PredictDamage
    // includes weakness/resistance multipliers, Hard/Hardest naturally
    // "know" and exploit your typing system.
    private static List<Scored> ScoreOffenses(BattleUnit self, List<BattleUnit> foes)
    {
        var list = new List<Scored>();

        foreach (BattleUnit foe in foes)
        {
            float dmg = DamageCalculator.PredictDamage(self.Stats, foe.Stats,
                self.Stats.basicAttackType, self.Stats.basicAttackIsPhysical,
                self.Stats.basicAttackPower, foe.IsDefending);
            list.Add(new Scored
            {
                action = BasicAttackAction(self, foe),
                score = dmg + (dmg >= foe.Stats.currentHP ? 150f : 0f) // kill-secure bonus
            });
        }

        foreach (SkillData s in self.Stats.skills)
        {
            if (s.isHealing || s.mpCost > self.Stats.currentMP) continue;

            if (s.scope == TargetScope.AllEnemies)
            {
                float total = 0f;
                var targets = new List<BattleUnit>();
                foreach (BattleUnit foe in foes)
                {
                    float d = DamageCalculator.PredictDamage(self.Stats, foe.Stats,
                        s.damageType, s.isPhysical, s.power, foe.IsDefending);
                    total += d + (d >= foe.Stats.currentHP ? 150f : 0f);
                    targets.Add(foe);
                }
                list.Add(new Scored { action = SkillAction(self, s, targets), score = total * 0.9f });
            }
            else if (s.scope == TargetScope.Single && s.side == TargetSide.Enemies)
            {
                foreach (BattleUnit foe in foes)
                {
                    float d = DamageCalculator.PredictDamage(self.Stats, foe.Stats,
                        s.damageType, s.isPhysical, s.power, foe.IsDefending);
                    list.Add(new Scored
                    {
                        action = SkillAction(self, s, new List<BattleUnit> { foe }),
                        score = d + (d >= foe.Stats.currentHP ? 150f : 0f) - s.mpCost * 0.5f
                    });
                }
            }
        }
        return list;
    }

    private static bool TryHeal(BattleUnit self, List<BattleUnit> allies, float hpFraction, out BattleAction action)
    {
        action = null;
        foreach (SkillData s in self.Stats.skills)
        {
            if (!s.isHealing || s.side != TargetSide.Allies || s.mpCost > self.Stats.currentMP) continue;

            if (s.scope == TargetScope.AllAllies)
            {
                int hurt = 0;
                foreach (BattleUnit a in allies)
                    if (a.Stats.IsAlive && (float)a.Stats.currentHP / a.Stats.MaxHP < hpFraction) hurt++;
                if (hurt >= 2) { action = SkillAction(self, s, Alive(allies)); return true; }
                continue;
            }

            foreach (BattleUnit a in Alive(allies))
            {
                if ((float)a.Stats.currentHP / a.Stats.MaxHP < hpFraction)
                {
                    action = SkillAction(self, s, new List<BattleUnit> { a });
                    return true;
                }
            }
        }
        return false;
    }

    private static SkillData RandomUsableSkill(BattleUnit self)
    {
        var usable = new List<SkillData>();
        foreach (SkillData s in self.Stats.skills)
            if (s.mpCost <= self.Stats.currentMP) usable.Add(s);
        if (usable.Count == 0) return null;
        return usable[Random.Range(0, usable.Count)];
    }

    private static BattleAction SkillOnRandomTargets(BattleUnit self, SkillData s,
        List<BattleUnit> allies, List<BattleUnit> foes)
    {
        if (s.isHealing)
        {
            var alive = Alive(allies);
            if (s.scope == TargetScope.AllAllies) return SkillAction(self, s, alive);
            if (s.scope == TargetScope.Self) return SkillAction(self, s, new List<BattleUnit> { self });
            if (alive.Count == 0) return BasicAttackAction(self, foes[0]);
            return SkillAction(self, s, new List<BattleUnit> { alive[Random.Range(0, alive.Count)] });
        }
        if (s.scope == TargetScope.AllEnemies) return SkillAction(self, s, foes);
        if (s.scope == TargetScope.Self) return SkillAction(self, s, new List<BattleUnit> { self });
        return SkillAction(self, s, new List<BattleUnit> { foes[Random.Range(0, foes.Count)] });
    }

    private static List<BattleUnit> Alive(List<BattleUnit> list)
    {
        var r = new List<BattleUnit>();
        foreach (BattleUnit u in list) if (u.Stats.IsAlive) r.Add(u);
        return r;
    }

    private static BattleUnit LowestHP(List<BattleUnit> list)
    {
        BattleUnit best = list[0];
        foreach (BattleUnit u in list) if (u.Stats.currentHP < best.Stats.currentHP) best = u;
        return best;
    }

    private static BattleAction BasicAttackAction(BattleUnit self, BattleUnit target)
    {
        return new BattleAction { actor = self, type = ActionType.Attack, targets = new List<BattleUnit> { target } };
    }

    private static BattleAction SkillAction(BattleUnit self, SkillData s, List<BattleUnit> targets)
    {
        return new BattleAction { actor = self, type = ActionType.Skill, skill = s, targets = targets };
    }

    private static BattleAction DefendAction(BattleUnit self)
    {
        return new BattleAction { actor = self, type = ActionType.Defend, targets = new List<BattleUnit>() };
    }
}