using UnityEngine;

public static class EncounterManager
{
    public static EncounterZone currentZone;
    private static float distanceWalked;

    public static void ResetSteps() { distanceWalked = 0f; }

    public static void RegisterMovement(float distance)
    {
        if (currentZone == null || currentZone.table == null) return;
        if (GameManager.Instance == null || GameManager.Instance.inBattle) return;
        if (OverworldMenu.IsOpen) return;

        distanceWalked += distance;
        if (distanceWalked < currentZone.minMetersBeforeEncounter) return;

        if (Random.value < currentZone.encounterChancePerMeter * distance)
        {
            distanceWalked = 0f;
            StartBattle(currentZone);
        }
    }

    public static void StartBattle(EncounterZone zone)
    {
        FormationEntry formation = zone.table.RollFormation();
        if (formation == null || formation.slots == null || formation.slots.Length == 0)
        {
            Debug.LogWarning("Encounter table '" + zone.table.name + "' has no valid formations.");
            return;
        }
        BattleLauncher.Launch(formation, zone.table);
    }
}