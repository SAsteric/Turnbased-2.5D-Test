using UnityEngine;

public class EncounterZone : MonoBehaviour
{
    [Header("Which encounter set this area uses")]
    public EncounterTable table;

    [Header("Customizable encounter feel for this area")]
    [Range(0f, 1f)] public float encounterChancePerMeter = 0.05f;
    public int minMetersBeforeEncounter = 10;

    private void OnTriggerEnter(Collider other)
    {
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc != null && pc.enabled) EncounterManager.currentZone = this;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc != null && pc.enabled && EncounterManager.currentZone == this)
            EncounterManager.currentZone = null;
    }
}