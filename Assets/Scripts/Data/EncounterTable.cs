using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class EnemySlot
{
    public EnemyData enemy;
    public int levelOffset = 0;
}

[System.Serializable]
public class FormationEntry
{
    public string formationName = "New Formation";
    public int weight = 10;
    public EnemySlot[] slots = new EnemySlot[0];
}

[CreateAssetMenu(fileName = "Encounters_", menuName = "RPG/Encounter Table")]
public class EncounterTable : ScriptableObject
{
    [Header("Enemy level range for this area")]
    public int minLevel = 1;
    public int maxLevel = 3;

    [Header("Formations (weighted random pick)")]
    public List<FormationEntry> formations = new List<FormationEntry>();

    public FormationEntry RollFormation()
    {
        int total = 0;
        foreach (FormationEntry f in formations) total += Mathf.Max(0, f.weight);
        if (total <= 0 || formations.Count == 0) return null;

        int roll = Random.Range(0, total);
        foreach (FormationEntry f in formations)
        {
            roll -= Mathf.Max(0, f.weight);
            if (roll < 0) return f;
        }
        return formations[formations.Count - 1];
    }

    public int RollLevel(int offset = 0)
    {
        return Mathf.Max(1, Random.Range(minLevel, maxLevel + 1) + offset);
    }
}