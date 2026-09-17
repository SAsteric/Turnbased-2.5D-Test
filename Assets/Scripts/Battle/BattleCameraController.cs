using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ===========================================================================
// Battle camera with a SOFT ZONE (your sketch):
//   - Focus point (red dot): the camera is always aimed at currentLook.
//   - Drift circle (adjustable radius): the camera's position sways inside
//     it via smooth Perlin noise — subtle, "invisible" life.
//   - The drift composes with every move, punch, and shake.
//
//   PlayIntro / ReturnToOverview / FocusUnit — as before.
//   FocusGroup(centroid) — NEW: frames a whole side during target selection.
//   showDriftDebug — NEW: draws the dot + circle live on screen.
// ===========================================================================
public class BattleCameraController : MonoBehaviour
{
    public static BattleCameraController Instance { get; private set; }

    [Header("Framing")]
    public Vector3 overviewPosition = new Vector3(0f, 4.6f, -11.5f);
    public Vector3 overviewLookAt = new Vector3(0f, 1.8f, 0.8f);
    public Vector3 introStartPosition = new Vector3(0f, 1.7f, -3.0f);
    public Vector3 introStartLookAt = new Vector3(0f, 0.2f, 1.8f);
    public float fieldOfView = 50f;

    [Header("Focus (whose turn it is)")]
    public Vector3 focusOffset = new Vector3(0.6f, 2.4f, -8.5f);
    public float focusDuration = 0.45f;

    [Header("Group framing (target selection)")]
    [Tooltip("Camera offset from a group's centroid when framing the enemy side or the party.")]
    public Vector3 groupFocusOffset = new Vector3(0f, 2.6f, -10.0f);
    public float groupFocusDuration = 0.5f;

    [Header("Idle drift — the soft zone")]
    [Tooltip("Radius of the drift circle: how far the camera's position sways. 0 = off.")]
    public float driftRadius = 0.6f;
    [Tooltip("Sway speed. 0.3–0.5 = slow cinematic drift.")]
    public float driftSpeed = 0.35f;
    [Range(0f, 1f)] public float driftVerticalFraction = 0.35f;
    [Tooltip("Draw the focus dot + drift circle on screen — your sketch, live.")]
    public bool showDriftDebug = false;

    [Header("Attack punch (FOV kick)")]
    public float punchFovDelta = 7f;
    public float punchDuration = 0.28f;

    [Header("Hit shake")]
    public float shakeStrength = 0.12f;
    public float shakeDuration = 0.22f;

    [Header("End-of-battle party shot")]
    [Tooltip("Camera offset from the party's centroid for the win/lose shot.")]
    public Vector3 partyEndOffset = new Vector3(0.6f, 2.0f, -7.0f);
    public float partyEndDuration = 1.1f;

    /// Win or lose: frame the whole party (victory poses read great in this).
    public Coroutine FocusPartyEnding(List<BattleUnit> units)
    {
        Vector3 centroid = Vector3.zero;
        int count = 0;
        foreach (BattleUnit u in units)
        {
            if (u == null) continue;
            centroid += u.FocusPoint;
            count++;
        }
        if (count == 0) return null;
        centroid /= count;
        return MoveTo(centroid + partyEndOffset, centroid, partyEndDuration);
    }

    [Header("Impact pan (when a hit connects)")]
    [Tooltip("How fast the whip-pan to the action is.")]
    public float impactPanDuration = 0.22f;
    [Tooltip("Camera height above the action midpoint.")]
    public float impactHeight = 1.9f;
    [Tooltip("Minimum distance the impact camera keeps from the action.")]
    public float impactMinDistance = 6.0f;
    [Tooltip("Pull-back per unit of attacker-victim distance. Lower = tighter framing.")]
    public float impactDistanceScale = 0.6f;
    [Tooltip("Extra distance padding so both units always have margin.")]
    public float impactDistancePad = 2.5f;

    private Camera cam;
    private float baseFov;
    private Vector3 currentPos, currentLook;
    private Vector3 shakeOffset;
    private Vector3 driftOffset;
    private float shakeRemaining, shakeTotal, shakeAmp;
    private Coroutine punchRoutine;
    private Coroutine moveRoutine;

    public Camera Cam { get { return cam; } }

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        if (cam == null) { Debug.LogError("BattleCameraController: no Camera on this object."); return; }
        baseFov = fieldOfView;
        cam.fieldOfView = fieldOfView;

        currentPos = introStartPosition;
        currentLook = introStartLookAt;
        transform.position = currentPos;
        transform.LookAt(currentLook);
    }

    private void LateUpdate()
    {
        UpdateDrift();

        if (shakeRemaining > 0f)
        {
            shakeRemaining -= Time.deltaTime;
            float k = Mathf.Clamp01(shakeRemaining / Mathf.Max(0.001f, shakeTotal));
            shakeOffset = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * (shakeAmp * k);
        }
        else shakeOffset = Vector3.zero;

        // position = logical spot + idle drift + hit shake.
        // The LOOK target (the red dot) is untouched by drift and shake —
        // that's what keeps the framing pinned to the field.
        transform.position = currentPos + driftOffset + shakeOffset;
        transform.LookAt(currentLook);
    }

    // ---------------- drift (the circle) ----------------

    private void UpdateDrift()
    {
        if (driftRadius <= 0f) { driftOffset = Vector3.zero; return; }
        float t = Time.time * driftSpeed;
        float x = (Mathf.PerlinNoise(t, 13.37f) * 2f - 1f) * driftRadius;
        float y = (Mathf.PerlinNoise(71.3f, t) * 2f - 1f) * (driftRadius * driftVerticalFraction);
        driftOffset = new Vector3(x, y, 0f);
    }

    // ---------------- public API ----------------

    public void PlayIntro(float duration) { MoveTo(overviewPosition, overviewLookAt, duration); }

    public void ReturnToOverview(float duration = 0.6f) { MoveTo(overviewPosition, overviewLookAt, duration); }

    public void FocusUnit(BattleUnit unit, float duration = -1f)
    {
        if (unit == null) { ReturnToOverview(); return; }
        if (duration < 0f) duration = focusDuration;

        Vector3 center = unit.FocusPoint;
        Vector3 look = center + new Vector3(unit.StepDirection * 2.2f, -0.2f, 0f);
        MoveTo(look + focusOffset, look, duration);
    }

    /// NEW — frame a whole group (the enemy side, or the party) during
    /// target selection. Pass the group's centroid.
    public void FocusGroup(Vector3 centroid, float duration = -1f)
    {
        if (duration < 0f) duration = groupFocusDuration;
        MoveTo(centroid + groupFocusOffset, centroid, duration);
    }

    /// Whip-pan onto the ACTION the moment a hit connects — frames the
    /// attacker AND the victim (biased toward the victim), pulled back far
    /// enough that the attacker's animation stays fully visible.
    public void PanToImpact(BattleUnit attacker, BattleUnit target)
    {
        if (target == null) return;
        Vector3 a = (attacker != null) ? attacker.FocusPoint : target.FocusPoint;
        Vector3 b = target.FocusPoint;

        Vector3 mid = Vector3.Lerp(a, b, 0.55f);   // 55% toward the victim = victim-favored
        float spread = Vector3.Distance(a, b);
        float back = Mathf.Max(impactMinDistance, spread * impactDistanceScale + impactDistancePad);

        MoveTo(mid + new Vector3(0f, impactHeight, -back), mid, impactPanDuration);
    }

    public void Punch()
    {
        if (cam == null) return;
        if (punchRoutine != null) StopCoroutine(punchRoutine);
        punchRoutine = StartCoroutine(PunchRoutine());
    }

    public void Shake(float strength = -1f, float duration = -1f)
    {
        if (strength >= 0f) shakeAmp = strength; else shakeAmp = shakeStrength;
        shakeTotal = duration >= 0f ? duration : shakeDuration;
        shakeRemaining = shakeTotal;
    }

    public Coroutine MoveTo(Vector3 position, Vector3 lookAt, float duration)
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine); // never let two moves fight
        moveRoutine = StartCoroutine(MoveRoutine(position, lookAt, duration));
        return moveRoutine;
    }

    // ---------------- debug overlay (the sketch, live) ----------------

    private void OnGUI()
    {
        if (!showDriftDebug || cam == null) return;

        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        // world units per screen pixel at the focus distance
        float dist = Mathf.Max(0.01f, Vector3.Distance(currentPos, currentLook));
        float worldPerPixel = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
        float radiusPx = Mathf.Max(6f, driftRadius / worldPerPixel);

        // the circle — how far the camera may drift
        int segments = 48;
        Vector2 prev = center + new Vector2(Mathf.Cos(0f), Mathf.Sin(0f)) * radiusPx;
        for (int i = 1; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radiusPx;
            DrawGUILine(prev, p, new Color(1f, 1f, 1f, 0.6f));
            prev = p;
        }

        // where the camera currently sits inside the circle
        Vector2 driftPoint = center + new Vector2(driftOffset.x, -driftOffset.y) / worldPerPixel;
        DrawGUIDot(driftPoint, 6f, new Color(1f, 1f, 1f, 0.9f));

        // the red dot — always dead center, because the camera always aims at it
        DrawGUIDot(center, 8f, new Color(1f, 0.25f, 0.25f, 0.95f));
    }

    private static void DrawGUILine(Vector2 a, Vector2 b, Color color)
    {
        Vector2 dir = b - a;
        float length = dir.magnitude;
        if (length < 0.5f) return;
        Color saved = GUI.color;
        GUI.color = color;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y, length, 1.5f), Texture2D.whiteTexture);
        GUI.matrix = Matrix4x4.identity;
        GUI.color = saved;
    }

    private static void DrawGUIDot(Vector2 point, float size, Color color)
    {
        Color saved = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), Texture2D.whiteTexture);
        GUI.color = saved;
    }

    // ---------------- internals ----------------

    private IEnumerator PunchRoutine()
    {
        float half = punchDuration * 0.5f;
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            cam.fieldOfView = Mathf.Lerp(baseFov, baseFov - punchFovDelta, t / half);
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            cam.fieldOfView = Mathf.Lerp(baseFov - punchFovDelta, baseFov, t / half);
            yield return null;
        }
        cam.fieldOfView = baseFov;
    }

    private IEnumerator MoveRoutine(Vector3 position, Vector3 lookAt, float duration)
    {
        Vector3 startPos = currentPos;
        Vector3 startLook = currentLook;
        if (duration <= 0f)
        {
            currentPos = position; currentLook = lookAt;
            yield break;
        }
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            currentPos = Vector3.Lerp(startPos, position, k);
            currentLook = Vector3.Lerp(startLook, lookAt, k);
            yield return null;
        }
        currentPos = position;
        currentLook = lookAt;
    }
}