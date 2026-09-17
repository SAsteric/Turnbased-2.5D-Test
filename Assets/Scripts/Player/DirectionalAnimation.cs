using UnityEngine;

// Drives the "three-ring" locomotion blend tree:
//   inner ring  (radius 0.25) = idle clips
//   middle ring (radius 0.60) = walk clips
//   outer ring  (radius 1.00) = run clips
// and provides a clean unit-length facing vector for the Jump blend tree.
public static class DirectionalAnimation
{
    public const float IdleBlendRadius = 0.25f;
    public const float WalkBlendRadius = 0.6f;
    public const float RunBlendRadius = 1f;
    public const float BlendDamping = 14f; // higher = snappier ring switching

    // Where in blend space we want to be right now.
    public static Vector2 GetBlendTarget(bool isMoving, Vector2 lastDirection, bool isRunning)
    {
        if (!isMoving) return SnapToCardinal(lastDirection) * IdleBlendRadius;
        Vector2 dir = Vector2.ClampMagnitude(lastDirection, 1f);
        return dir * (isRunning ? RunBlendRadius : WalkBlendRadius);
    }

    // Frame-rate independent smoothing (time constant ~ 1/14 sec).
    public static Vector2 Smooth(Vector2 current, Vector2 target, float deltaTime)
    {
        float t = 1f - Mathf.Exp(-BlendDamping * Mathf.Max(0f, deltaTime));
        return Vector2.Lerp(current, target, t);
    }

    // Nearest of the 4 cardinal directions (exact ties favour Up/Down).
    public static Vector2 SnapToCardinal(Vector2 v)
    {
        if (v.sqrMagnitude < 0.0001f) return Vector2.down;
        if (Mathf.Abs(v.x) > Mathf.Abs(v.y)) return new Vector2(Mathf.Sign(v.x), 0f);
        return new Vector2(0f, Mathf.Sign(v.y));
    }

    // Unit-length facing vector for the Jump tree.
    public static Vector2 GetFacing(Vector2 blendPosition, Vector2 fallbackDirection)
    {
        Vector2 v = blendPosition;
        if (v.sqrMagnitude < 0.0001f) v = SnapToCardinal(fallbackDirection);
        return v.normalized; // guaranteed non-zero unit vector
    }
}