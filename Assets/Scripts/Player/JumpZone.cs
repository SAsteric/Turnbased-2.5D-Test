using UnityEngine;

// MARKER COMPONENT — put this on an empty GameObject that has a trigger
// collider (e.g. Box Collider with "Is Trigger" ON) covering the ledge area
// where jumping is allowed. Space / gamepad-A does nothing anywhere else.
[RequireComponent(typeof(Collider))]
public class JumpZone : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.35f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(b.center, b.size);
    }
}