using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private BattleUIController ui;

    [Header("World spawn anchors")]
    [SerializeField] private Transform partyAnchor;
    [SerializeField] private Transform enemyAnchor;

    [Header("Hit FX (per damage type — 2D sprite strip or 3D particle prefab)")]
    [SerializeField] private List<DamageTypeFX> hitFX = new List<DamageTypeFX>();

    [Header("Formations")]
    [SerializeField] private float enemySpacingX = 1.15f;
    [SerializeField] private float enemySpacingZ = 0.85f;

    [Header("Party formation — rhombus (max 4)")]
    [Tooltip("Diamond slots — slot 0 is the leader at the front. Tweak the numbers to reshape the diamond.")]
    [SerializeField] private Vector3[] partySlots = new Vector3[]
    {
        new Vector3(-0.9f, 0f,  0.0f),   // front   — leader
        new Vector3( 0.0f, 0f,  0.85f),  // mid     — up-screen
        new Vector3( 0.0f, 0f, -0.85f),  // mid     — down-screen
        new Vector3( 0.9f, 0f,  0.0f),   // back    — center
    };

    [Header("Entrance Sliding (world units)")]
    [SerializeField] private float slideInDuration = 0.7f;
    [SerializeField] private float slideInStagger = 0.07f;
    [SerializeField] private float offscreenX = 16f;

    [Header("Tilted stage")]
    [Tooltip("Must MATCH the Ground's X rotation: Ground rotation (-10,0,0) = 10 here.")]
    [SerializeField] private float groundTiltDegrees = 10f;

    [Header("Action spot (the 'step out front')")]
    [Tooltip("How far beyond the front-most member of the side the actor slides on their turn.")]
    [SerializeField] private float actionSpotClearance = 1.2f;

    [Header("Timing")]
    [SerializeField] private float timeBetweenActions = 0.45f;

    public List<BattleUnit> Players { get; } = new List<BattleUnit>();
    public List<BattleUnit> Enemies { get; } = new List<BattleUnit>();
    public BattleUIController UI { get { return ui; } }

    private bool battleOver;
    private bool victory;
    private int pendingParallel;
    private BattleCameraController cameraController;
    private Transform unitsRoot;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (ui == null) ui = FindFirstObjectByType<BattleUIController>();
        BattleFX.Library = hitFX;
    }

    private IEnumerator Start()
    {
        GameManager.EnsureExists();
        GameManager gm = GameManager.Instance;

        while (MirrorBreakTransition.IsActive) yield return null;

        if (ui == null) { Debug.LogError("BattleManager: BattleUIController not assigned."); yield break; }
        if (partyAnchor == null || enemyAnchor == null)
        { Debug.LogError("BattleManager: spawn anchors not assigned."); yield break; }

        cameraController = FindFirstObjectByType<BattleCameraController>();
        ui.HideAllPanels();

        unitsRoot = new GameObject("BattleUnits").transform;

        var entrances = new List<IEnumerator>();

        foreach (PartyMember pm in gm.ActiveParty())
        {
            if (Players.Count >= 4) break;   // hard cap — a battle team is 4, ever
            BattleUnit unit = SpawnPlayer(pm);
            Vector3 home = unit.HomePosition;
            unit.transform.position = home + Vector3.right * offscreenX;
            entrances.Add(unit.SlideIn(home, slideInDuration, slideInStagger * entrances.Count));
        }

        foreach (EnemySpawnRequest req in BattleLauncher.PendingEnemies)
        {
            BattleUnit unit = SpawnEnemy(req.enemy, req.level);
            Vector3 home = unit.HomePosition;
            unit.transform.position = home - Vector3.right * offscreenX;
            entrances.Add(unit.SlideIn(home, slideInDuration, slideInStagger * entrances.Count));
        }
        BattleLauncher.PendingEnemies.Clear();

        if (Enemies.Count == 0) { Debug.LogError("Battle: no enemies to spawn."); yield break; }
        if (Players.Count == 0) { Debug.LogError("Battle: no active party members."); yield break; }

        // Parent the HUD to THIS battle scene's canvas — never a persistent one.
        // (FindFirstObjectByType could grab the SceneLoader's or MirrorBreak's
        // DontDestroyOnLoad canvas, and the HUD would survive into the overworld.)
        Canvas battleCanvas = ui.GetComponentInParent<Canvas>();
        BattlePartyHUD.EnsureBuilt(battleCanvas).Bind(Players);

        if (cameraController != null)
            cameraController.PlayIntro(slideInDuration + slideInStagger * Mathf.Max(0, entrances.Count - 1));

        yield return RunParallel(entrances);

        var names = new System.Text.StringBuilder();
        foreach (BattleUnit e in Enemies) { if (names.Length > 0) names.Append(", "); names.Append(e.Stats.unitName); }
        ui.Log(names.ToString() + " attack!");
        yield return new WaitForSeconds(0.8f);

        if (AllDead(Players)) { battleOver = true; victory = false; }
        else
        {
            int round = 0;
            while (!battleOver)
            {
                round++;
                yield return RunRound(round);
            }
        }

        if (cameraController != null) cameraController.FocusPartyEnding(Players);
        yield return EndBattle();
    }

    private float GroundHeightAt(float z)
    {
        return z * Mathf.Tan(groundTiltDegrees * Mathf.Deg2Rad);
    }

    private BattleUnit SpawnPlayer(PartyMember pm)
    {
        UnitStats stats = pm.CreateStats();
        stats.currentHP = Mathf.Clamp(pm.currentHP, 0, stats.MaxHP);
        stats.currentMP = Mathf.Clamp(pm.currentMP, 0, stats.MaxMP);

        GameObject go = new GameObject("P_" + pm.data.characterName);
        go.transform.SetParent(unitsRoot, false);
        BattleUnit unit = go.AddComponent<BattleUnit>();

        int i = Mathf.Clamp(Players.Count, 0, partySlots.Length - 1);
        Vector3 home = partyAnchor.position + partySlots[i];
        home.y = GroundHeightAt(home.z);
        go.transform.position = home;

        unit.Setup(stats, true, pm);
        Players.Add(unit);
        return unit;
    }

    private BattleUnit SpawnEnemy(EnemyData data, int level)
    {
        UnitStats stats = UnitStats.FromEnemy(data, level);
        GameObject go = new GameObject("E_" + data.enemyName);
        go.transform.SetParent(unitsRoot, false);
        BattleUnit unit = go.AddComponent<BattleUnit>();

        int i = Enemies.Count;
        Vector3 home = enemyAnchor.position + new Vector3(enemySpacingX * i, 0f, enemySpacingZ * i);
        home.y = GroundHeightAt(home.z);
        go.transform.position = home;

        unit.Setup(stats, false, null, data.displayScale);
        Enemies.Add(unit);
        return unit;
    }

    /// Where an actor slides on their turn: clearly in FRONT of their whole
    /// formation — not a small nudge from home. Depth (Z) keeps the actor's
    /// own lane, so no two units ever overlap at the front.
    public Vector3 GetActionSpot(BattleUnit unit)
    {
        List<BattleUnit> side = unit.IsPlayerSide ? Players : Enemies;

        float frontX = unit.HomePosition.x;
        if (unit.IsPlayerSide)
        {
            foreach (BattleUnit u in side)
                if (u != null && u.HomePosition.x < frontX) frontX = u.HomePosition.x;
            frontX -= actionSpotClearance;    // players act toward −X (the enemy side)
        }
        else
        {
            foreach (BattleUnit u in side)
                if (u != null && u.HomePosition.x > frontX) frontX = u.HomePosition.x;
            frontX += actionSpotClearance;    // enemies act toward +X (the party)
        }

        float z = unit.HomePosition.z;        // keep own depth lane
        return new Vector3(frontX, GroundHeightAt(z), z);
    }

    private IEnumerator RunParallel(List<IEnumerator> routines)
    {
        pendingParallel = routines.Count;
        for (int i = 0; i < routines.Count; i++)
            StartCoroutine(ParallelRoutine(routines[i]));
        while (pendingParallel > 0) yield return null;
    }

    private IEnumerator ParallelRoutine(IEnumerator routine)
    {
        yield return StartCoroutine(routine);
        pendingParallel--;
    }

    private IEnumerator RunRound(int round)
    {
        // ---- build this round's turn queue: every living unit, sorted by ----
        // priority boost (from priority skills/items & Defend last round),
        // then Speed, then random ties.
        List<BattleUnit> queue = BuildTurnQueue();
        ui.ShowTurnQueue(queue, round);
        yield return new WaitForSeconds(0.4f);
        foreach (BattleUnit u in queue) u.PriorityBoost = 0; // boosts consumed by this queue

        for (int i = 0; i < queue.Count; i++)
        {
            if (battleOver) yield break;
            BattleUnit unit = queue[i];
            if (unit == null) continue;
            if (!unit.Stats.IsAlive)
            {
                yield return unit.StepBack(); // safety — no-op unless stranded forward
                continue;
            }

            ui.SetCurrentTurn(unit);

            BattleAction action;
            if (unit.IsPlayerSide)
            {
                // PLAYER turn: pick a command, it executes IMMEDIATELY.
                if (cameraController != null) cameraController.FocusUnit(unit);
                yield return ui.WaitForPlayerCommand(unit, Players, Enemies);
                action = ui.LastAction;
                if (action == null)
                    action = new BattleAction { actor = unit, type = ActionType.Defend, targets = new List<BattleUnit>() };
            }
            else
            {
                // ENEMY turn: AI decides at its ACTUAL turn, executes immediately.
                action = EnemyAI.Decide(unit, Enemies, Players);
                if (action == null)
                    action = new BattleAction { actor = unit, type = ActionType.Defend, targets = new List<BattleUnit>() };
            }

            yield return ExecuteAction(action);

            if (AllDead(Enemies)) { battleOver = true; victory = true; yield break; }
            if (AllDead(Players)) { battleOver = true; victory = false; yield break; }
            yield return new WaitForSeconds(timeBetweenActions);
        }

        if (cameraController != null) cameraController.ReturnToOverview(0.6f);
    }

    // Turn queue: priority boost descending, then Speed descending, then random.
    private List<BattleUnit> BuildTurnQueue()
    {
        var queue = new List<BattleUnit>();
        foreach (BattleUnit u in Players) if (u != null && u.Stats.IsAlive) queue.Add(u);
        foreach (BattleUnit e in Enemies) if (e != null && e.Stats.IsAlive) queue.Add(e);

        queue.Sort((a, b) =>
        {
            int pb = b.PriorityBoost.CompareTo(a.PriorityBoost);
            if (pb != 0) return pb;
            int s = b.Stats.Speed.CompareTo(a.Stats.Speed);
            if (s != 0) return s;
            return Random.value < 0.5f ? -1 : 1;
        });
        return queue;
    }

    private class SkillHitPlan
    {
        public BattleUnit target;
        public DamageResult result;
        public int[] shares;
    }

    private static int[] SplitDamage(int total, int parts)
    {
        parts = Mathf.Max(1, parts);
        int[] shares = new int[parts];
        int per = Mathf.Max(1, total / parts);
        for (int i = 0; i < parts - 1; i++) shares[i] = per;
        shares[parts - 1] = Mathf.Max(1, total - per * (parts - 1));
        return shares;
    }

    private IEnumerator ExecuteAction(BattleAction action)
    {
        BattleUnit actor = action.actor;
        actor.IsDefending = false;

        yield return actor.StepToActionSpot(GetActionSpot(actor));
        actor.SetActiveTurn(true);

        if (cameraController != null)
        {
            if (actor.IsPlayerSide)
            {
                cameraController.FocusUnit(actor);
            }
            else
            {
                // ENEMY TURN: brief focus while they step up, then pull back
                // to the overview so the player sees the whole field while
                // the enemy acts.
                cameraController.FocusUnit(actor, 0.3f);
                yield return new WaitForSeconds(0.35f);
                cameraController.ReturnToOverview(0.5f);
            }
        }

        switch (action.type)
        {
            case ActionType.Defend:
            {
                actor.IsDefending = true;
                actor.PriorityBoost = Mathf.Max(actor.PriorityBoost, 1); // defend: act earlier next round
                ui.Log(actor.Stats.unitName + " takes a defensive stance.");
                yield return actor.PlayDefendHold();
                break;
            }

            case ActionType.Attack:
            {
                BattleUnit target = FirstAlive(action.targets);
                if (target == null) break;
                ui.Log(actor.Stats.unitName + " attacks " + target.Stats.unitName + "!");

                DamageResult r = DamageCalculator.Calculate(actor.Stats, target.Stats,
                    actor.Stats.basicAttackType, actor.Stats.basicAttackIsPhysical,
                    actor.Stats.basicAttackPower, target.IsDefending);

                bool hitFired = false;
                yield return actor.PlayAttack(target, () =>
                {
                    if (hitFired) return;
                    hitFired = true;
                    StartCoroutine(ApplyDamage(actor, target, r)); // damage lands AT the hit moment
                });
                if (!hitFired) yield return ApplyDamage(actor, target, r); // safety net
                break;
            }

            case ActionType.Skill:
            {
                SkillData skill = action.skill;
                if (skill == null) break;
                if (actor.Stats.currentMP < skill.mpCost)
                {
                    ui.Log(actor.Stats.unitName + " lacks MP for " + skill.skillName + "!");
                    break;
                }
                actor.Stats.currentMP -= skill.mpCost;
                if (skill.priority > 0) actor.PriorityBoost = skill.priority; // jump the queue next round
                ui.Log(actor.Stats.unitName + " uses " + skill.skillName + "!");

                // optional charge-up clip first
                yield return actor.PlaySkillCharge();

                if (skill.isHealing)
                {
                    yield return actor.PlaySkillHeal();
                    int amount = DamageCalculator.CalculateHeal(actor.Stats, skill.power);
                    foreach (BattleUnit t in action.targets)
                    {
                        if (t == null || !t.Stats.IsAlive) continue;
                        t.Stats.currentHP = Mathf.Min(t.Stats.MaxHP, t.Stats.currentHP + amount);
                        t.UpdateHUD();
                        ui.SpawnDamageText(t, "+" + amount, Color.green);
                        yield return t.PlayHealed();
                    }
                    ui.Log(skill.skillName + " restores " + amount + " HP.");
                }
                else
                {
                    var skillTargets = new List<BattleUnit>();
                    foreach (BattleUnit t in action.targets)
                        if (t != null && t.Stats.IsAlive) skillTargets.Add(t);
                    if (skillTargets.Count == 0) break;

                    if (actor.HasSkillAttack)
                    {
                        // ---- authored multi-hit skill: damage lands at the clip's hit frames ----
                        int hits = skill.HitCount;
                        var plans = new List<SkillHitPlan>();
                        foreach (BattleUnit t in skillTargets)
                        {
                            DamageResult r = DamageCalculator.Calculate(actor.Stats, t.Stats,
                                skill.damageType, skill.isPhysical, skill.power, t.IsDefending);
                            plans.Add(new SkillHitPlan { target = t, result = r, shares = SplitDamage(r.amount, hits) });
                        }

                        yield return actor.PlaySkillAttack(skill, hitIndex =>
                        {
                            bool pannedThisHit = false;
                            foreach (SkillHitPlan plan in plans)
                            {
                                BattleUnit t = plan.target;
                                if (!t.Stats.IsAlive) continue;
                                int dmg = plan.shares[hitIndex];
                                t.TakeDamage(dmg);
                                ui.SpawnDamageText(t,
                                    plan.result.isWeakness ? dmg + "!" : dmg.ToString(),
                                    plan.result.isWeakness ? Color.yellow : plan.result.isResistance ? Color.gray : new Color(1f, 0.35f, 0.35f));
                                BattleFX.Spawn(unitsRoot, t.FocusPoint, skill.damageType);
                                if (plan.result.isWeakness && !t.IsPlayerSide) t.ShowWeaknessBadge();
                                if (cameraController != null)
                                {
                                    if (!pannedThisHit) { cameraController.PanToImpact(actor, t); pannedThisHit = true; }
                                    cameraController.Punch();
                                    cameraController.Shake();
                                }
                                StartCoroutine(t.PlayHit());
                            }
                        });

                        // one log line per target + deaths
                        foreach (SkillHitPlan plan in plans)
                        {
                            string tag = plan.result.isWeakness ? "  — WEAKNESS!" : plan.result.isResistance ? "  (resisted)" : "";
                            ui.Log(plan.target.Stats.unitName + " takes " + plan.result.amount + " damage" + tag);
                            if (!plan.target.Stats.IsAlive)
                            {
                                ui.Log(plan.target.Stats.unitName + " is defeated!");
                                StartCoroutine(plan.target.PlayDeath());
                            }
                        }
                    }
                    else
                    {
                        // ---- fallback: no authored SkillAttack clip (e.g. enemies) ----
                        BattleUnit mainTarget = FirstAlive(action.targets)
                            ?? (action.targets.Count > 0 ? action.targets[0] : null);
                        if (mainTarget != null) yield return actor.PlayAttack(mainTarget);
                        foreach (BattleUnit t in action.targets)
                        {
                            if (t == null || !t.Stats.IsAlive) continue;
                            DamageResult r = DamageCalculator.Calculate(actor.Stats, t.Stats,
                                skill.damageType, skill.isPhysical, skill.power, t.IsDefending);
                            yield return ApplyDamage(actor, t, r);
                        }
                    }
                }
                break;
            }

            case ActionType.Item:
            {
                ItemData item = action.item;
                if (item == null) break;
                if (!GameManager.Instance.ConsumeItem(item, 1))
                {
                    ui.Log(actor.Stats.unitName + " reaches for " + item.itemName + "... but it's gone!");
                    break;
                }
                if (item.priority > 0) actor.PriorityBoost = item.priority; // priority item: jump next round
                ui.Log(actor.Stats.unitName + " uses " + item.itemName + "!");
                yield return actor.PlayUseItem();

                foreach (BattleUnit t in action.targets)
                {
                    if (t == null) continue;

                    if (item.revive && !t.Stats.IsAlive)
                    {
                        t.Stats.currentHP = Mathf.Max(1, Mathf.RoundToInt(t.Stats.MaxHP * 0.5f) + item.healHP);
                        t.Revive();
                        ui.SpawnDamageText(t, "REVIVED", new Color(0.4f, 1f, 0.8f));
                        ui.Log(t.Stats.unitName + " is revived!");
                        continue;
                    }
                    if (!t.Stats.IsAlive) continue;

                    if (item.healHP > 0)
                    {
                        t.Stats.currentHP = Mathf.Min(t.Stats.MaxHP, t.Stats.currentHP + item.healHP);
                        ui.SpawnDamageText(t, "+" + item.healHP, Color.green);
                    }
                    if (item.healMP > 0)
                        t.Stats.currentMP = Mathf.Min(t.Stats.MaxMP, t.Stats.currentMP + item.healMP);

                    if (item.damagePower > 0)
                    {
                        DamageResult r = DamageCalculator.CalculateItem(item, t.Stats, t.IsDefending);
                        yield return ApplyDamage(actor, t, r);
                        continue;
                    }

                    t.UpdateHUD();
                    yield return t.PlayHealed();
                }
                break;
            }
        }

        yield return actor.StepBack();
        actor.SetActiveTurn(false);
        actor.SetHighlight(false);   // turn officially over — outline off
    }

    private IEnumerator ApplyDamage(BattleUnit actor, BattleUnit target, DamageResult r)
    {
        target.TakeDamage(r.amount);
        string tag = r.isWeakness ? "  — WEAKNESS!" : r.isResistance ? "  (resisted)" : "";
        ui.Log(target.Stats.unitName + " takes " + r.amount + " damage" + tag);

        // CAMERA: whip to the victim as the hit connects, punch in + shake,
        // and let the camera ARRIVE before the flash/number land on them.
        if (cameraController != null)
        {
            cameraController.PanToImpact(actor, target);
            cameraController.Punch();
            cameraController.Shake();
            yield return new WaitForSeconds(cameraController.impactPanDuration);
        }

        ui.SpawnDamageText(target,
            r.isWeakness ? r.amount + "!" : r.amount.ToString(),
            r.isWeakness ? Color.yellow : r.isResistance ? Color.gray : new Color(1f, 0.35f, 0.35f));

        BattleFX.Spawn(unitsRoot, target.FocusPoint, r.type);

        if (r.isWeakness && !target.IsPlayerSide) target.ShowWeaknessBadge();

        yield return target.PlayHit();

        if (!target.Stats.IsAlive)
        {
            ui.Log(target.Stats.unitName + " is defeated!");
            yield return target.PlayDeath();
        }
    }

    private IEnumerator EndBattle()
    {
        yield return new WaitForSeconds(0.5f);
        GameManager gm = GameManager.Instance;

        foreach (BattleUnit p in Players) p.SyncBackToParty();

        if (victory)
        {
            // survivors strike their victory poses (held on the final frame)
            foreach (BattleUnit p in Players)
                if (p.Stats.IsAlive) p.PlayVictory();
            yield return new WaitForSeconds(1.0f); // let the poses read before the panel

            int exp = 0, gold = 0;
            foreach (BattleUnit e in Enemies) { exp += e.Stats.expReward; gold += e.Stats.goldReward; }
            gm.gold += gold;

            var levelUps = new List<string>();
            foreach (PartyMember pm in gm.ActiveParty())
            {
                var learned = pm.AddExp(exp);
                foreach (SkillData s in learned)
                    levelUps.Add(pm.data.characterName + " learned " + s.skillName + "!");
            }

            ui.ShowResult(true, "Gained " + exp + " EXP and " + gold + " gold.\n\n" + string.Join("\n", levelUps));
        }
        else
        {
            foreach (PartyMember pm in gm.party)
            {
                UnitStats s = pm.CreateStats();
                pm.currentHP = Mathf.Max(1, Mathf.RoundToInt(s.MaxHP * 0.5f));
                pm.currentMP = s.MaxMP / 2;
            }
            gm.gold = Mathf.RoundToInt(gm.gold * 0.9f);
            ui.ShowResult(false, "The party was defeated...\nYou awaken where you fell, hurting but alive.");
        }
    }

    // ---------------- dev console hooks ----------------

    public void DevForceWin()
    {
        battleOver = true;
        victory = true;
        if (ui != null) ui.DevResolvePendingCommand();
    }

    public void DevForceLose()
    {
        battleOver = true;
        victory = false;
        if (ui != null) ui.DevResolvePendingCommand();
    }

    public void ReturnToOverworld()
    {
        GameManager gm = GameManager.Instance;
        gm.inBattle = false;
        SceneLoader.Load(gm.returnScene);
    }

    private static bool AllDead(List<BattleUnit> list)
    {
        foreach (BattleUnit u in list) if (u.Stats.IsAlive) return false;
        return true;
    }

    private static BattleUnit FirstAlive(List<BattleUnit> targets)
    {
        foreach (BattleUnit t in targets) if (t != null && t.Stats.IsAlive) return t;
        return null;
    }
}