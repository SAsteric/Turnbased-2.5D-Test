using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ---------------------------------------------------------------------------
// HIT FX — impact effects whenever damage lands (both sides, automatically).
// Two flavours, picked per damage type on the BattleManager:
//   2D: a sprite strip (like your attack sheets) — played once, billboards.
//   3D: a prefab with a ParticleSystem — instantiated and auto-destroyed.
// If an entry has BOTH, the particle prefab wins.
// ---------------------------------------------------------------------------

[System.Serializable]
public class HitFX
{
    [Tooltip("3D: prefab with a ParticleSystem (or anything self-playing).")]
    public GameObject particlePrefab;

    [Tooltip("2D: impact frames, played once in array order.")]
    public Sprite[] frames;
    [Tooltip("2D playback speed (frames per second).")]
    public float frameRate = 15f;
    [Tooltip("World height of the 2D effect (world units).")]
    public float worldHeight = 1.6f;
    [Tooltip("Lift above the impact point.")]
    public float yOffset = 0.2f;
    [Tooltip("Random tilt (degrees) so repeated hits don't look identical.")]
    public float randomTilt = 12f;
}

[System.Serializable]
public class DamageTypeFX
{
    public DamageType type = DamageType.Slash;
    public HitFX fx = new HitFX();
}

public static class BattleFX
{
    /// Assigned by BattleManager at battle start.
    public static List<DamageTypeFX> Library;

    public static void Spawn(Transform parent, Vector3 position, DamageType type)
    {
        if (Library == null) return;
        HitFX fx = null;
        foreach (DamageTypeFX entry in Library)
            if (entry != null && entry.type == type) { fx = entry.fx; break; }
        if (fx == null) return; // no effect configured for this type

        position += Vector3.up * fx.yOffset;

        // ---- 3D prefab path ----
        if (fx.particlePrefab != null)
        {
            GameObject go = UnityEngine.Object.Instantiate(fx.particlePrefab, position, Quaternion.identity);
            if (parent != null) go.transform.SetParent(parent, true);

            float life = 2f;
            ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>();
            foreach (ParticleSystem ps in systems)
            {
                ps.Play();
                float l = ps.main.duration + ps.main.startLifetimeMultiplier + 0.25f;
                if (l > life) life = l;
            }
            UnityEngine.Object.Destroy(go, Mathf.Min(life, 5f));
            return;
        }

        // ---- 2D sprite strip path ----
        if (fx.frames == null || fx.frames.Length == 0) return;
        Sprite first = fx.frames[0];
        if (first == null) return;

        GameObject go2 = new GameObject("HitFX_" + type);
        go2.transform.position = position;
        if (parent != null) go2.transform.SetParent(parent, true);

        SpriteRenderer sr = go2.AddComponent<SpriteRenderer>();
        sr.sprite = first;
        sr.sortingOrder = 20; // above the units and HP bars

        float s = fx.worldHeight / Mathf.Max(0.001f, first.bounds.size.y);
        go2.transform.localScale = new Vector3(s, s, 1f);

        BattleFXSprite player = go2.AddComponent<BattleFXSprite>();
        player.Play(fx, sr, Random.Range(-fx.randomTilt, fx.randomTilt));
    }
}

// Plays a one-shot sprite strip, billboarded to the camera like the units.
public class BattleFXSprite : MonoBehaviour
{
    private Sprite[] frames;
    private float frameRate;
    private SpriteRenderer sr;
    private Camera cam;
    private float tilt;

    public void Play(HitFX fx, SpriteRenderer renderer, float tiltDegrees)
    {
        frames = fx.frames;
        frameRate = Mathf.Max(1f, fx.frameRate);
        sr = renderer;
        cam = Camera.main;
        tilt = tiltDegrees;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        float duration = frames.Length / frameRate;
        float t = 0f;
        int last = -1;
        while (t < duration)
        {
            t += Time.deltaTime;
            int idx = Mathf.Clamp(Mathf.FloorToInt(t * frameRate), 0, frames.Length - 1);
            if (idx != last && frames[idx] != null) { sr.sprite = frames[idx]; last = idx; }
            yield return null;
        }
        UnityEngine.Object.Destroy(gameObject);
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector3 dir = cam.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, 0f, tilt);
    }
}