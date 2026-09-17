using System.Collections.Generic;

public class BattleAction
{
    public ActionType type;
    public BattleUnit actor;
    public SkillData skill;
    public ItemData item;
    public List<BattleUnit> targets = new List<BattleUnit>();

    public int Priority
    {
        get
        {
            if (type == ActionType.Skill && skill != null) return skill.priority;
            if (type == ActionType.Item && item != null) return item.priority;
            return 0; // Attack & Defend = normal priority
        }
    }

    public string Describe()
    {
        switch (type)
        {
            case ActionType.Attack: return "Attack";
            case ActionType.Defend: return "Defend";
            case ActionType.Skill:
                return (skill != null ? skill.skillName : "Skill") + (Priority > 0 ? "  [+" + Priority + "]" : "");
            case ActionType.Item:
                return item != null ? item.itemName : "Item";
        }
        return "?";
    }
}