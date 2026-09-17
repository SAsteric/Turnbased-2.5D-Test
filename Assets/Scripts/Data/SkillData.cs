using UnityEngine;

[CreateAssetMenu(fileName = "Skill_", menuName = "RPG/Skill")]
public class SkillData : ScriptableObject
{
    [Header("Info")]
    public string skillName = "New Skill";
    [TextArea(2, 4)] public string description = "";
    public Sprite icon;

    [Header("Cost")]
    public int mpCost = 0;

    [Header("Turn Priority: 0 = normal, 1-3 = acts first (beats speed)")]
    [Range(0, 3)] public int priority = 0;

    [Header("Damage / Healing")]
    public bool isHealing = false;
    public bool isPhysical = true;
    [Tooltip("% of Attack or Magic")] public int power = 100;
    public DamageType damageType = DamageType.Slash;

    [Header("Targeting")]
    public TargetSide side = TargetSide.Enemies;
    public TargetScope scope = TargetScope.Single;

    [Header("Multi-Hit")]
    [Min(1)] public int hits = 1;

    [Header("Hit timing (optional)")]
    [Tooltip("Exact frame numbers in the SkillAttack clip. Leave EMPTY to fire on tagged sprites (name contains '_hit').")]
    public int[] hitFrames = new int[0];

    public int HitCount { get { return Mathf.Max(1, hits); } }
}