using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ===========================================================================
// Slash-split battle transition (no black screen):
//   1. Time freezes (timeScale = 0) - the encounter stops the world.
//   2. The camera pans OUT a little (FOV pull-back on the frozen world).
//   3. The screen is captured - the world becomes a still image.
//   4. A tapered blade sweeps across it at a randomized diagonal angle.
//   5. A brief hold + shake sells the impact, then a flash covers the swap
//      to the (already loading) battle scene.
//   6. The still RIPS in two along the cut - a tiny pull-together first,
//      then the halves fly apart with a bit of tumble - while the battle
//      scene is already live behind them (its own camera intro pans out
//      as the halves clear away).
// Public API unchanged: LoadBattleScene(name) + IsActive.
// ===========================================================================
public static class MirrorBreakTransition
{
    // ---------- feel (tweak freely) ----------
    public static float panOutSeconds = 0.35f;   // camera pull-back before the slash
    public static float panOutFovDelta = 7f;     // how far the camera zooms out (FOV degrees)

    public static float minSlashAngle = 25f;     // degrees off horizontal
    public static float maxSlashAngle = 65f;     // randomized each battle, always a diagonal slash
    public static float slashSeconds = 0.13f;    // slash sweep duration
    public static float slashWidth = 16f;        // blade thickness at its widest (px)
    public static float slashGlowWidth = 48f;    // soft halo thickness at its widest (px)
    public static Color slashColor = new Color(0.85f, 0.95f, 1f, 1f);
    public static Color slashGlowColor = new Color(0.55f, 0.8f, 1f, 0.35f);

    public static float impactHoldSeconds = 0.06f; // brief freeze on the cut before it rips
    public static float shakeMagnitude = 6f;        // px, decays to 0 across the hold

    public static float flashAlpha = 0.5f;       // impact flash - also masks the scene swap-in

    public static float anticipationSeconds = 0.05f; // tiny pull-together right before the rip
    public static float anticipationDistance = 16f;

    public static float splitSeconds = 0.5f;     // halves ripping away
    public static float splitDistance = 2600f;   // how far each half travels
    public static float splitSpinDegrees = 10f;  // tumble while flying (randomized a bit per half)

    // If the frozen image appears UPSIDE-DOWN on your machine, set this true.
    public static bool flipCaptureVertically = false;

    public static AudioClip freezeSound;   // optional: plays when the world freezes
    public static AudioClip shatterSound;  // optional: plays at the slash

    public static bool IsActive { get; private set; }

    private static Runner runner;

    public static void LoadBattleScene(string sceneName)
    {
        if (runner != null) return; // a transition is already playing
        GameObject go = new GameObject("MirrorBreakTransition");
        runner = go.AddComponent<Runner>();
        UnityEngine.Object.DontDestroyOnLoad(go);
        runner.Begin(sceneName);
    }

    // Editor safety net: if "Reload Domain" is turned off in Enter Play Mode
    // Settings, static fields like IsActive survive between separate Play
    // sessions. Stopping mid-transition once could leave IsActive stuck true
    // forever, hanging BattleManager's "while (IsActive)" wait the next time
    // you press Play - e.g. if you open the Battle scene directly to test
    // it in isolation. This resets both fields at the start of every
    // session (harmless no-op when Reload Domain is on, which is the
    // Unity default).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticStateForNewSession()
    {
        runner = null;
        IsActive = false;
    }

    // ------------------------------------------------------------------ //

    private class Runner : MonoBehaviour
    {
        private Image flash;
        private ScreenHalfGraphic slash, slashGlow;
        private RectTransform stage;
        private RectTransform halfArt, halfBrt;
        private Texture2D capture;

        private float hw, hh;       // half extents of the stage
        private Vector2 sepDir;     // direction half A flies (B flies the opposite way)
        private Vector3 a0, b0;     // half start positions (their centroids)

        private Camera worldCam;
        private float baseFov;
        private bool fovChanged;

        public void Begin(string sceneName) { StartCoroutine(Run(sceneName)); }

        private IEnumerator Run(string sceneName)
        {
            IsActive = true;
            Time.timeScale = 0f; // the world stops; the effect runs on unscaled time
            PlaySound(freezeSound);

            // ---- Start loading the battle scene IMMEDIATELY (hidden) ----
            AsyncOperation op = null;
            if (!string.IsNullOrEmpty(sceneName))
                op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
                Debug.LogError("MirrorBreakTransition: could not start loading scene '" + sceneName +
                               "'. Is it in Build Profiles with a matching name?");
            else op.allowSceneActivation = false;

            // ---- Phase 1: camera pans out a little, world frozen behind it ----
            yield return PanOut();

            // ---- capture the frozen frame (once this frame has rendered) ----
            yield return new WaitForEndOfFrame();
            capture = ScreenCapture.CaptureScreenshotAsTexture();
            BuildCanvas();

            if (capture != null)
            {
                // ---- Phase 2: the slash ----
                PlaySound(shatterSound);
                yield return SlashSweep();
                yield return ImpactHold();

                // ---- impact flash + release the battle scene behind it ----
                SetAlpha(flash, flashAlpha);
                if (op != null) op.allowSceneActivation = true;

                while (op != null && !op.isDone)
                {
                    // if the scene is slow, the flash settles to a light veil while we hold
                    SetAlpha(flash, Mathf.MoveTowards(flash.color.a, 0.3f, Time.unscaledDeltaTime * 2.5f));
                    yield return null;
                }

                // ---- the battle's own intro begins NOW, behind the halves ----
                Time.timeScale = 1f;
                IsActive = false;
                runner = null;

                // ---- Phase 3: a tiny pull-together, then the halves rip away ----
                yield return Anticipation();

                Vector3 move = sepDir * splitDistance;
                float spinA = splitSpinDegrees * Random.Range(0.7f, 1.3f);
                float spinB = splitSpinDegrees * Random.Range(0.7f, 1.3f);
                float flashStart = flash != null ? flash.color.a : 0f;
                float t = 0f;
                while (t < splitSeconds)
                {
                    t += Dt();
                    float k = Mathf.Clamp01(t / splitSeconds);
                    float e = k * k; // ease-in: accelerating rip

                    halfArt.localPosition = a0 + move * e;
                    halfBrt.localPosition = b0 - move * e;
                    halfArt.localRotation = Quaternion.Euler(0f, 0f, spinA * e);
                    halfBrt.localRotation = Quaternion.Euler(0f, 0f, -spinB * e);

                    SetAlpha(slash, slashColor.a * (1f - k));
                    SetAlpha(slashGlow, slashGlowColor.a * (1f - k));
                    SetAlpha(flash, Mathf.Lerp(flashStart, 0f, Mathf.Clamp01(k / 0.4f)));

                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning("MirrorBreakTransition: screen capture failed - cutting straight to the scene.");
                if (op != null) op.allowSceneActivation = true;
                while (op != null && !op.isDone) yield return null;
                Time.timeScale = 1f;
                IsActive = false;
                runner = null;
            }

            if (op == null)
            {
                // scene failed to load - undo the freeze and drop the battle
                // flag so the overworld keeps working
                GameManager gm = GameManager.Instance;
                if (gm != null) gm.inBattle = false;
            }

            // always give the camera its FOV back, whether this run succeeded or not
            // (previously this only happened on the failure path below, so a
            // persistent overworld camera would end up a little wider after
            // every single battle - the most likely reason it felt "off")
            if (fovChanged && worldCam != null) worldCam.fieldOfView = baseFov;

            if (capture != null) Destroy(capture); // free the screenshot's memory
            Destroy(gameObject);
        }

        // ---------------- construction ----------------

        private void BuildCanvas()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100; // above the battle UI and the DevConsole (1050)

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // stage - everything lives here; its rect is the real screen area
            GameObject stageGo = new GameObject("Stage");
            stageGo.transform.SetParent(transform, false);
            stage = stageGo.AddComponent<RectTransform>();
            Stretch(stage);
            Canvas.ForceUpdateCanvases();
            Rect r = stage.rect;
            hw = r.width * 0.5f;
            hh = r.height * 0.5f;

            if (capture == null) return;

            Vector2 tl = new Vector2(-hw, hh), tr = new Vector2(hw, hh);
            Vector2 br = new Vector2(hw, -hh), bl = new Vector2(-hw, -hh);

            // random diagonal angle each battle, mirrored for either direction -
            // clipped through the exact center so both pieces are always equal area
            float angle = Random.Range(minSlashAngle, maxSlashAngle);
            if (Random.value < 0.5f) angle = 180f - angle;
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));

            SplitRectByLine(tl, tr, br, bl, dir, out Vector2[] polyA, out Vector2[] polyB,
                             out Vector2 cross1, out Vector2 cross2);

            Vector2 cutDir = (cross2 - cross1).normalized;
            Vector2 perp = new Vector2(-cutDir.y, cutDir.x);
            // make sure sepDir actually points toward polyA's own side - the
            // crossing order isn't fixed once the angle is random, so this
            // can't be hard-coded the way it could when the cut was always
            // exactly corner-to-corner
            sepDir = Vector2.Dot(perp, Centroid(polyA)) >= 0f ? perp : -perp;

            // created in render order: halves, glow, slash, flash
            halfArt = CreateHalf("HalfA", polyA);
            halfBrt = CreateHalf("HalfB", polyB);
            a0 = halfArt.localPosition;
            b0 = halfBrt.localPosition;

            float len = Vector2.Distance(cross1, cross2);
            float lineAngle = Mathf.Atan2(cross2.y - cross1.y, cross2.x - cross1.x) * Mathf.Rad2Deg;
            slashGlow = CreateBlade("SlashGlow", cross1, lineAngle, len, slashGlowWidth, slashGlowColor);
            slash = CreateBlade("Slash", cross1, lineAngle, len, slashWidth, slashColor);

            flash = CreateStretchedImage(stage, "Flash", new Color(1f, 1f, 1f, 0f));
        }

        private RectTransform CreateHalf(string name, Vector2[] poly)
        {
            Vector2 centroid = Centroid(poly);

            GameObject go = new GameObject(name);
            go.transform.SetParent(stage, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(10f, 10f);           // the mesh defines its own shape
            rt.localPosition = centroid;

            ScreenHalfGraphic g = go.AddComponent<ScreenHalfGraphic>();
            g.tex = capture;
            g.color = Color.white;
            g.raycastTarget = false;

            var verts = new Vector2[poly.Length];
            var uvs = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++)
            {
                verts[i] = poly[i] - centroid;
                float u = (poly[i].x + hw) / (2f * hw);
                float v = (poly[i].y + hh) / (2f * hh);
                if (flipCaptureVertically) v = 1f - v;
                uvs[i] = new Vector2(u, v);
            }
            g.SetPolygon(verts, uvs);
            g.SetMaterialDirty();

            return rt;
        }

        // A tapered lens shape (pointed at both ends, widest in the middle)
        // instead of a flat bar - reads as a drawn blade rather than a plain
        // rectangle. Reuses ScreenHalfGraphic with tex = null, which falls
        // back to a solid color fill.
        private ScreenHalfGraphic CreateBlade(string name, Vector2 start, float angleDeg, float length, float width, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(stage, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(10f, 10f);
            rt.localPosition = start;
            rt.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            rt.localScale = new Vector3(0f, 1f, 1f);       // hidden until the sweep

            ScreenHalfGraphic g = go.AddComponent<ScreenHalfGraphic>();
            g.tex = null;
            g.color = color;
            g.raycastTarget = false;

            float hwid = width * 0.5f;
            Vector2[] verts = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(length * 0.25f,  hwid * 0.8f),
                new Vector2(length * 0.5f,   hwid),
                new Vector2(length * 0.75f,  hwid * 0.8f),
                new Vector2(length, 0f),
                new Vector2(length * 0.75f, -hwid * 0.8f),
                new Vector2(length * 0.5f,  -hwid),
                new Vector2(length * 0.25f, -hwid * 0.8f),
            };
            g.SetPolygon(verts, null);
            g.SetMaterialDirty();
            return g;
        }

        private Image CreateStretchedImage(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;   // nothing blocks the battle UI below
            Stretch((RectTransform)go.transform);
            return img;
        }

        // ---------------- the camera pull-back ----------------

        private IEnumerator PanOut()
        {
            worldCam = Camera.main;
            if (worldCam == null) yield break;
            baseFov = worldCam.fieldOfView;
            fovChanged = true;

            float t = 0f;
            while (t < panOutSeconds)
            {
                t += Dt();
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / panOutSeconds));
                worldCam.fieldOfView = Mathf.Lerp(baseFov, baseFov + panOutFovDelta, k);
                yield return null;
            }
            worldCam.fieldOfView = baseFov + panOutFovDelta;
        }

        // ---------------- the slash sweep ----------------

        private IEnumerator SlashSweep()
        {
            float t = 0f;
            while (t < slashSeconds)
            {
                t += Dt();
                float k = Mathf.Clamp01(t / slashSeconds);
                float e = 1f - (1f - k) * (1f - k); // ease-out - a fast, snappy cut
                slash.rectTransform.localScale = new Vector3(e, 1f, 1f);
                slashGlow.rectTransform.localScale = new Vector3(e, 1f, 1f);
                yield return null;
            }
            slash.rectTransform.localScale = Vector3.one;
            slashGlow.rectTransform.localScale = Vector3.one;
        }

        // ---------------- impact beat: a short hold + shake ----------------

        private IEnumerator ImpactHold()
        {
            float t = 0f;
            while (t < impactHoldSeconds)
            {
                t += Dt();
                float k = 1f - Mathf.Clamp01(t / impactHoldSeconds);
                stage.anchoredPosition = Random.insideUnitCircle * shakeMagnitude * k;
                yield return null;
            }
            stage.anchoredPosition = Vector2.zero;
        }

        // ---------------- tiny pull-together right before the rip ----------------

        private IEnumerator Anticipation()
        {
            Vector3 inward = sepDir * anticipationDistance;
            float t = 0f;
            while (t < anticipationSeconds)
            {
                t += Dt();
                float k = Mathf.Sin(Mathf.Clamp01(t / anticipationSeconds) * Mathf.PI); // out and back
                halfArt.localPosition = a0 - inward * k;
                halfBrt.localPosition = b0 + inward * k;
                yield return null;
            }
            halfArt.localPosition = a0;
            halfBrt.localPosition = b0;
        }

        // ---------------- geometry helpers ----------------

        // Splits a rectangle by an (unbounded) line through its own center,
        // running in direction 'dir'. Because the line passes through the
        // center, both resulting polygons always have equal area - a literal
        // 50/50 split regardless of the angle.
        private static void SplitRectByLine(Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl, Vector2 dir,
            out Vector2[] polyA, out Vector2[] polyB, out Vector2 cross1, out Vector2 cross2)
        {
            Vector2[] corners = { tl, tr, br, bl };
            var sideList = new List<Vector2>();
            var otherList = new List<Vector2>();
            var crossings = new List<Vector2>();

            for (int i = 0; i < 4; i++)
            {
                Vector2 cur = corners[i];
                Vector2 next = corners[(i + 1) % 4];
                float curSide = Cross(dir, cur);
                float nextSide = Cross(dir, next);
                bool curIn = curSide >= 0f;

                if (curIn) sideList.Add(cur); else otherList.Add(cur);

                if (curIn != (nextSide >= 0f))
                {
                    float denom = curSide - nextSide;
                    float lerpT = Mathf.Abs(denom) > 0.0001f ? curSide / denom : 0.5f;
                    Vector2 pt = Vector2.Lerp(cur, next, lerpT);
                    crossings.Add(pt);
                    sideList.Add(pt);
                    otherList.Add(pt);
                }
            }

            polyA = sideList.ToArray();
            polyB = otherList.ToArray();
            cross1 = crossings.Count > 0 ? crossings[0] : tl;
            cross2 = crossings.Count > 1 ? crossings[1] : br;
        }

        private static float Cross(Vector2 dir, Vector2 p) { return dir.x * p.y - dir.y * p.x; }

        private static Vector2 Centroid(Vector2[] poly)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < poly.Length; i++) c += poly[i];
            return c / poly.Length;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            Color c = g.color; c.a = a; g.color = c;
        }

        // Screenshot capture and scene activation can both cause a one-frame
        // hitch. Right after either one, Time.unscaledDeltaTime can come back
        // large enough to jump a whole phase timer past its target in a single
        // frame - the phase never gets a chance to actually play, it just
        // snaps straight to its end state. Clamping the step size fixes that.
        private static float Dt() { return Mathf.Min(Time.unscaledDeltaTime, 0.05f); }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null) return;
            AudioSource src = GetComponent<AudioSource>();
            if (src == null) { src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; }
            src.PlayOneShot(clip);
        }
    }
}

// A UI Graphic that draws ONE arbitrary polygon: textured (tex != null) for
// the two screen halves, or a flat color fill (tex == null) for the blade
// and its glow.
public class ScreenHalfGraphic : Graphic
{
    [System.NonSerialized] public Texture tex;
    private Vector2[] verts;
    private Vector2[] uvs;

    public override Texture mainTexture { get { return tex != null ? tex : base.mainTexture; } }

    public void SetPolygon(Vector2[] localVerts, Vector2[] uv)
    {
        verts = localVerts;
        uvs = uv;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (verts == null || verts.Length < 3) return;

        UIVertex v = UIVertex.simpleVert;
        for (int i = 0; i < verts.Length; i++)
        {
            v.position = verts[i];
            v.color = color;
            v.uv0 = (uvs != null && uvs.Length == verts.Length) ? uvs[i] : Vector2.zero;
            vh.AddVert(v);
        }
        for (int i = 1; i < verts.Length - 1; i++)
            vh.AddTriangle(0, i, i + 1);
    }
}