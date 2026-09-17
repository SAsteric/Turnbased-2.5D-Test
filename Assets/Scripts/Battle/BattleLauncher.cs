using System.Collections.Generic;

public class EnemySpawnRequest
{
    public EnemyData enemy;
    public int level;
}

public static class BattleLauncher
{
    public static readonly List<EnemySpawnRequest> PendingEnemies = new List<EnemySpawnRequest>();

    public static void Launch(FormationEntry formation, EncounterTable table)
    {
        PendingEnemies.Clear();
        if (formation != null && formation.slots != null)
        {
            foreach (EnemySlot slot in formation.slots)
            {
                if (slot == null || slot.enemy == null) continue;
                PendingEnemies.Add(new EnemySpawnRequest
                {
                    enemy = slot.enemy,
                    level = table != null ? table.RollLevel(slot.levelOffset) : 1
                });
            }
        }
        if (PendingEnemies.Count == 0) return;

        GameManager gm = GameManager.Instance;
        gm.inBattle = true; // locks player input BEFORE the freeze starts
        if (PlayerParty.Instance != null)
        {
            gm.returnPosition = PlayerParty.Instance.GetLeaderPosition();
            gm.hasReturnPosition = true;
        }
        gm.returnScene = gm.overworldSceneName;

        // Slash-split transition: freezes the world, slashes the frozen
        // frame in half, and loads the battle scene itself (behind the
        // flying halves).
        MirrorBreakTransition.LoadBattleScene(gm.battleSceneName);
    }
}