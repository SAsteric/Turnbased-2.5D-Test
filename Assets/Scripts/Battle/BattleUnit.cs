using System.Collections.Generic;
using UnityEngine;

public class BattleUnit : MonoBehaviour
{
    [Header("World size")]
    public float desiredWorldHeight = 2.3f;

    [Header("Battle Animator")]
    public float battleAnimationSpeed = 1f;
    public float attackLungeDistance = 0.55f;

    [Header("Sliding (world units)")]
    public float stepForwardDistance = 0.55f;
    public float stepForwardDuration = 0.22f;

    [Header("Idles")]
    [Tooltip("HP fraction at or below which IdleStatus plays")]
    public float lowHealthThreshold = 0.25f;

    [Tooltip("Speed (m/s) of the big slide to/from the front-of-formation action spot.")]
    public float actionSlideSpeed = 6f;

    [Header("Impact sprite detection")]
    [Tooltip("A sprite whose NAME contains this marker triggers a hit the first time it appears (e.g. 'Pitaya_09_hit').")]
    public string impactSpriteMarker = "_hit";
    [Header("Highlight outline (turn / hover)")]
    public Color outlineColor = new Color(0.2f, 0.9f, 1f, 1f);   // cyan
    [Tooltip("Outline thickness as a fraction of sprite size (0.07 = 7%)")]
    public float outlineThickness = 0.07f;

    public UnitStats Stats { get; private set; }
    public bool IsPlayerSide { get; private set; }
    public bool IsDefending { get; set; }
    public PartyMember SourceMember { get; private set; }

    public int StepDirection { get; private set; }
    /// Queue-jump bonus for the NEXT round (priority skills/items & Defend).
    public int PriorityBoost { get; set; }
    public Vector3 HomePosition { get; private set; }
    private bool steppedForward;   // forward-spot tracker — makes Step* calls idempotent
    public Vector3 FocusPoint { get { return transform.position + Vector3.up * (currentHeight * 0.55f); } }

    private Transform spriteTransform;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private BoxCollider unitCollider;

    private Transform overhead;
    private TextMesh nameMesh;
    private SpriteRenderer hpBg, hpFill;
    private GameObject hpBarRoot;
    private const float HpBarWidth = 1.5f;
    private GameObject weaknessBadge;

    private Sprite originalSprite;
    private Vector3 baseSpriteScale = Vector3.one;
    private bool flipSprite;
    private bool hasBattleAnimator;
    private float currentHeight = 2.3f;

    private float currentAlpha = 1f;
    private bool targetMarked, hoverMarked;
    private GameObject outlineGo;
    private Transform outlineTransform;
    private SpriteRenderer outlineRenderer;

    // ---- pose / idle state ----
    private bool isActiveTurn;      // IdleActive while choosing/acting
    private bool statusIdle;        // paralysis/poison hook — forces IdleStatus
    private string holdState;       // "Defend"/"Death"/"Victory" — freeze pose until broken

    private Camera billboardCam;

    // ---------------- setup ----------------

    public void Setup(UnitStats stats, bool playerSide, PartyMember member = null, float displayScale = 1f)
    {
        Stats = stats;
        IsPlayerSide = playerSide;
        StepDirection = playerSide ? -1 : 1;
        SourceMember = member;
        IsDefending = false;

        currentHeight = desiredWorldHeight * Mathf.Max(0.05f, displayScale);

        GameObject spriteGo = new GameObject("Sprite");
        spriteGo.transform.SetParent(transform, false);
        spriteRenderer = spriteGo.AddComponent<SpriteRenderer>();
        spriteTransform = spriteGo.transform;

        Sprite resting = stats.battleSprite != null ? stats.battleSprite : stats.portrait;
        originalSprite = resting;
        spriteRenderer.sprite = resting;
        spriteRenderer.sortingOrder = 0;

        float srcH = resting != null ? resting.bounds.size.y : 0f;
        float s = srcH > 0.001f ? currentHeight / srcH : 1f;
        flipSprite = stats.flipBattleSprite;
        baseSpriteScale = new Vector3(flipSprite ? -s : s, s, s);
        spriteTransform.localScale = baseSpriteScale;
        spriteTransform.localPosition = new Vector3(0f, currentHeight * 0.5f, 0f);

        // ---- highlight outline: cyan silhouette behind the sprite ----
        outlineGo = new GameObject("Outline");
        outlineGo.transform.SetParent(spriteTransform, false);
        outlineTransform = outlineGo.transform;
        outlineRenderer = outlineGo.AddComponent<SpriteRenderer>();
        outlineRenderer.sortingOrder = -1;   // renders behind the main sprite
        outlineTransform.localScale = new Vector3(1f + outlineThickness, 1f + outlineThickness, 1f);
        outlineTransform.localPosition = Vector3.zero;
        outlineGo.SetActive(false);

        if (resting == null && stats.battleAnimatorController == null)
            Debug.LogWarning("BattleUnit: no sprite and no battle Animator Controller for '" + stats.unitName + "'.");

        hasBattleAnimator = false;
        if (stats.battleAnimatorController != null)
        {
            animator = spriteGo.AddComponent<Animator>();
            animator.runtimeAnimatorController = stats.battleAnimatorController;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = battleAnimationSpeed;
            hasBattleAnimator = true;
            if (HasState("Idle")) animator.Play("Idle", 0, 0f);
        }

        float width = resting != null ? resting.bounds.size.x * s : currentHeight * 0.5f;
        unitCollider = gameObject.AddComponent<BoxCollider>();
        unitCollider.size = new Vector3(Mathf.Max(0.4f, width), currentHeight, 0.35f);
        unitCollider.center = new Vector3(0f, currentHeight * 0.5f, 0f);

        if (!playerSide)
        {
            GameObject oh = new GameObject("Overhead");
            overhead = oh.transform;
            overhead.SetParent(transform, false);
            overhead.localPosition = new Vector3(0f, currentHeight + 0.12f, 0f);
            CreateName(stats.unitName);
            CreateHpBar();
        }

        HomePosition = transform.position;
        RefreshVisual();
        UpdateHUD();
    }

    private void CreateName(string text)
    {
        Font font = GetLegacyFont();
        if (font == null) return;
        GameObject go = new GameObject("Name");
        go.transform.SetParent(overhead, false);
        go.transform.localPosition = new Vector3(0f, 0.34f, -0.05f);
        nameMesh = go.AddComponent<TextMesh>();
        nameMesh.font = font;
        nameMesh.text = text;
        nameMesh.fontSize = 44;
        nameMesh.characterSize = 0.09f;
        nameMesh.anchor = TextAnchor.MiddleCenter;
        nameMesh.alignment = TextAlignment.Center;
        nameMesh.color = Color.white;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = font.material;
        go.SetActive(false); // like the HP bar — only visible while hovered
    }

    private void CreateHpBar()
    {
        GameObject barGo = new GameObject("HPBar");
        barGo.transform.SetParent(overhead, false);
        barGo.transform.localPosition = new Vector3(0f, 0.06f, -0.04f);
        barGo.transform.localScale = Vector3.one;
        hpBarRoot = barGo;
        hpBarRoot.SetActive(false); // visible only on hover

        GameObject bgGo = new GameObject("Bg");
        bgGo.transform.SetParent(barGo.transform, false);
        bgGo.transform.localPosition = Vector3.zero;
        hpBg = bgGo.AddComponent<SpriteRenderer>();
        hpBg.sprite = WhiteCenter;
        hpBg.color = new Color(0f, 0f, 0f, 0.75f);
        hpBg.sortingOrder = 10;
        bgGo.transform.localScale = new Vector3(HpBarWidth, 0.18f, 1f);

        GameObject fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(barGo.transform, false);
        fillGo.transform.localPosition = new Vector3(-HpBarWidth * 0.5f, 0f, -0.01f);
        hpFill = fillGo.AddComponent<SpriteRenderer>();
        hpFill.sprite = WhiteLeft;
        hpFill.color = new Color(0.2f, 0.85f, 0.35f);
        hpFill.sortingOrder = 11;
        fillGo.transform.localScale = new Vector3(HpBarWidth, 0.12f, 1f);
    }

    // ---------------- billboarding ----------------

    private void LateUpdate()
    {
        if (billboardCam == null) billboardCam = Camera.main;
        if (billboardCam == null) return;

        Vector3 dir = billboardCam.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion face = Quaternion.LookRotation(-dir);
            if (spriteTransform != null) spriteTransform.rotation = face;
            if (overhead != null) overhead.rotation = face;
        }

        // outline follows the current animation frame, and recenters so the
        // border is even on all sides whatever the sprite's pivot is
        if (outlineGo != null && outlineGo.activeSelf && spriteRenderer != null)
        {
            outlineRenderer.sprite = spriteRenderer.sprite;
            Sprite sp = spriteRenderer.sprite;
            if (sp != null)
            {
                Vector2 c = sp.bounds.center;
                outlineTransform.localPosition = new Vector3(-outlineThickness * c.x, -outlineThickness * c.y, 0f);
            }
        }
    }

    // ---------------- HUD ----------------

    public void UpdateHUD()
    {
        if (Stats == null) return;
        if (hpFill != null)
        {
            float frac = Stats.MaxHP > 0 ? (float)Stats.currentHP / Stats.MaxHP : 0f;
            hpFill.transform.localScale = new Vector3(HpBarWidth * frac, 0.12f, 1f);
        }
        // low-health idle re-check (HP may have crossed the threshold either way)
        if (IsInAnyIdle()) PlayIdle();
    }

    public void TakeDamage(int amount)
    {
        Stats.currentHP = Mathf.Max(0, Stats.currentHP - amount);
        UpdateHUD();
    }

    public void SyncBackToParty()
    {
        if (SourceMember != null) SourceMember.SyncFrom(Stats);
    }

    public void SetHighlight(bool on) { targetMarked = on; RefreshVisual(); }

    public void SetHover(bool on)
    {
        hoverMarked = on;
        if (hpBarRoot != null) hpBarRoot.SetActive(on);
        if (nameMesh != null) nameMesh.gameObject.SetActive(on);
        RefreshVisual();
    }

    /// While choosing/acting — plays IdleActive instead of Idle.
    /// The turn-ON path forces the idle re-pick: right after a slide the
    /// animator's state info lags one frame behind the queued Play("Idle"),
    /// so the old IsInAnyIdle() gate missed here and IdleActive only ever
    /// started AFTER the command panel closed.
    public void SetActiveTurn(bool on)
    {
        isActiveTurn = on;
        if (on) holdState = null;   // a new turn breaks a held pose (Defend)
        if (on) PlayIdle();         // force it the moment the turn starts
        else if (IsInAnyIdle()) PlayIdle();
    }

    /// Future paralysis/poison hook — forces the status idle.
    public void SetStatusIdle(bool on)
    {
        statusIdle = on;
        if (IsInAnyIdle()) PlayIdle();
    }

    public void Revive()
    {
        currentAlpha = 1f;
        holdState = null;
        RefreshVisual();
        PlayIdle();
        UpdateHUD();
    }

    // ---------------- VULNERABLE badge ----------------

    public void ShowWeaknessBadge()
    {
        if (weaknessBadge == null) weaknessBadge = BuildWeaknessBadge();
        if (weaknessBadge != null) weaknessBadge.SetActive(true);
    }

    private GameObject BuildWeaknessBadge()
    {
        if (overhead == null) return null;
        Font font = GetLegacyFont();
        if (font == null) return null;
        GameObject go = new GameObject("WeaknessBadge");
        go.transform.SetParent(overhead, false);
        go.transform.localPosition = new Vector3(0f, 0.74f, -0.06f);
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = "VULNERABLE";
        tm.fontSize = 40;
        tm.characterSize = 0.085f;
        tm.fontStyle = FontStyle.Bold;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.55f, 0.15f);
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = font.material;
        return go;
    }

    /// Slide out to the ACTION SPOT (in front of the whole formation).
    /// Idempotent — if the unit is already out, nothing happens — so the
    /// panel-open step (players) and ExecuteAction's step never double-slide.
    public System.Collections.IEnumerator StepToActionSpot(Vector3 spot)
    {
        if (steppedForward) yield break;
        steppedForward = true;
        float d = Vector3.Distance(transform.position, spot);
        float duration = Mathf.Clamp(d / Mathf.Max(1f, actionSlideSpeed), 0.25f, 0.8f);
        yield return SlideAnimated(spot, duration, 0f, "SlideForward");
    }

    public System.Collections.IEnumerator StepBack()
    {
        if (!steppedForward) yield break;  // already home
        steppedForward = false;
        float d = Vector3.Distance(transform.position, HomePosition);
        float duration = Mathf.Clamp(d / Mathf.Max(1f, actionSlideSpeed), 0.25f, 0.8f);
        yield return SlideAnimated(HomePosition, duration, 0f, "SlideBack");
    }

    // ---------------- animator core ----------------

    private bool HasState(string stateName)
    {
        return animator != null && animator.runtimeAnimatorController != null
            && animator.HasState(0, Animator.StringToHash(stateName));
    }

    private void PlayBattleState(string stateName)
    {
        if (HasState(stateName)) animator.Play(stateName, 0, 0f);
    }

    private float CurrentClipLength()
    {
        float len = animator.GetCurrentAnimatorStateInfo(0).length;
        if (float.IsInfinity(len) || float.IsNaN(len) || len <= 0f) return 0.5f;
        return len;
    }

    private bool IsInAnyIdle()
    {
        if (!hasBattleAnimator) return false;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.IsName("Idle") || info.IsName("IdleActive") || info.IsName("IdleStatus");
    }

    /// Picks the right idle: status > low health > active turn > plain.
    public void PlayIdle()
    {
        if (Stats == null || !Stats.IsAlive) return;
        if ((statusIdle || (Stats.MaxHP > 0 && (float)Stats.currentHP / Stats.MaxHP <= lowHealthThreshold))
            && HasState("IdleStatus")) { PlayBattleState("IdleStatus"); return; }
        if (isActiveTurn && HasState("IdleActive")) { PlayBattleState("IdleActive"); return; }
        PlayBattleState("Idle");
    }

    public bool HasSkillAttack { get { return hasBattleAnimator && HasState("SkillAttack"); } }

    // ---------------- skill animations ----------------

    /// Optional charge-up. Skips instantly if the state doesn't exist.
    public System.Collections.IEnumerator PlaySkillCharge()
    {
        if (!hasBattleAnimator || !HasState("SkillCharge")) yield break;
        animator.Play("SkillCharge", 0, 0f);
        yield return null;
        yield return new WaitForSeconds(Mathf.Min(CurrentClipLength(), 2f));
    }

    /// Healing/buff skill clip. Returns to idle after.
    public System.Collections.IEnumerator PlaySkillHeal()
    {
        if (!hasBattleAnimator || !HasState("SkillHeal")) yield break;
        animator.Play("SkillHeal", 0, 0f);
        yield return null;
        yield return new WaitForSeconds(Mathf.Min(CurrentClipLength(), 2f));
        PlayIdle();
    }

    /// Multi-hit skill attack. Per-hit timing, in order of priority:
    ///   1. skill.hitFrames — exact frame numbers (if you entered any)
    ///   2. tagged sprites — any sprite whose name contains the marker
    ///   3. anything unfired lands at the end of the clip (safety net)
    /// Works at any animation speed. onHit(hitIndex) fires once per hit.
    public System.Collections.IEnumerator PlaySkillAttack(SkillData skill, System.Action<int> onHit)
    {
        animator.Play("SkillAttack", 0, 0f);
        yield return null;

        AnimatorClipInfo[] infos = animator.GetCurrentAnimatorClipInfo(0);
        AnimationClip clip = infos.Length > 0 ? infos[0].clip : null;
        float totalFrames = 38f;
        if (clip != null && clip.frameRate > 0f && clip.length > 0f)
            totalFrames = clip.length * clip.frameRate;

        int totalHits = (skill != null) ? skill.HitCount : 1;
        int[] frames = (skill != null && skill.hitFrames != null) ? skill.hitFrames : new int[0];

        int next = 0;               // hits fired so far
        int frameIdx = 0;           // next authored frame number to consume
        var firedSprites = new HashSet<int>();

        while (true)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.normalizedTime >= 1f || !info.IsName("SkillAttack")) break;

            bool fire = false;

            // trigger 1 — authored numeric frames
            if (frameIdx < frames.Length && info.normalizedTime * totalFrames >= frames[frameIdx])
            { frameIdx++; fire = true; }

            // trigger 2 — a tagged impact sprite is on screen (first appearance only)
            Sprite sp = spriteRenderer.sprite;
            if (sp != null && sp.name.IndexOf(impactSpriteMarker, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int id = sp.GetInstanceID();
                if (!firedSprites.Contains(id)) { firedSprites.Add(id); fire = true; }
            }

            if (fire && next < totalHits)
            {
                if (onHit != null) onHit(next);
                next++;
            }

            yield return null;
        }

        // safety net — fire anything that never triggered
        while (next < totalHits)
        {
            if (onHit != null) onHit(next);
            next++;
        }

        PlayIdle();
    }

    /// Item usage clip. No state = no animation.
    public System.Collections.IEnumerator PlayUseItem()
    {
        if (!hasBattleAnimator || !HasState("UseItem")) yield break;
        animator.Play("UseItem", 0, 0f);
        yield return null;
        yield return new WaitForSeconds(Mathf.Min(CurrentClipLength(), 2f));
        PlayIdle();
    }

    /// Victory pose — plays and HOLDS the last frame (non-looping clip).
    public void PlayVictory()
    {
        if (Stats == null || !Stats.IsAlive) return;
        if (hasBattleAnimator && HasState("Victory"))
        {
            holdState = "Victory";
            animator.Play("Victory", 0, 0f);
        }
    }

    /// Defend: plays the clip and HOLDS the last frame until the unit is hit
    /// (PlayHit breaks the hold) or their next turn starts.
    public System.Collections.IEnumerator PlayDefendHold()
    {
        if (hasBattleAnimator && HasState("Defend"))
        {
            animator.Play("Defend", 0, 0f);
            holdState = "Defend";
            yield return null;
            yield return new WaitForSeconds(Mathf.Min(CurrentClipLength(), 1.5f));
        }
        else
        {
            Vector3 s = transform.localScale;
            transform.localScale = s * 0.92f;
            yield return new WaitForSeconds(0.15f);
            transform.localScale = s;
        }
    }

    // ---------------- sliding ----------------

    public void SetHomePosition(Vector3 home) { HomePosition = home; }

    public System.Collections.IEnumerator SlideIn(Vector3 home, float duration, float delay)
    {
        return SlideAnimated(home, duration, delay, "SlideForward");
    }

    public System.Collections.IEnumerator StepForward()
    {
        yield return StepForward(stepForwardDistance, stepForwardDuration);
    }

    public System.Collections.IEnumerator StepForward(float distance, float duration)
    {
        if (steppedForward) yield break;   // already at the forward spot — no double slide
        steppedForward = true;
        Vector3 target = HomePosition + new Vector3(StepDirection * distance, 0f, 0f);
        yield return SlideAnimated(target, duration, 0f, "SlideForward");
    }

    private System.Collections.IEnumerator SlideAnimated(Vector3 target, float duration, float delay, string animState)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        Vector3 start = transform.position;
        if (duration <= 0f) { transform.position = target; yield break; }

        Sprite[] frames = null;
        if (!hasBattleAnimator && Stats != null)
            frames = (animState == "SlideForward") ? Stats.slideForwardFrames : Stats.slideBackFrames;
        bool useFrames = frames != null && frames.Length > 0 && spriteRenderer != null;

        if (hasBattleAnimator) PlayBattleState(animState);

        float frameDuration = 1f / Mathf.Max(1f, Stats != null ? Stats.slideFrameRate : 12f);
        int lastIndex = -1;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            transform.position = Vector3.Lerp(start, target, k);
            if (useFrames) SetSlideFrame(frames, t, frameDuration, ref lastIndex);
            yield return null;
        }
        transform.position = target;

        // slide clips hold their last frame; after the slide, return to the
        // appropriate idle — unless a pose is being held (Defend/Death/Victory)
        if (hasBattleAnimator)
        {
            if (holdState != null && HasState(holdState)) PlayBattleState(holdState);
            else PlayIdle();
        }
        else if (useFrames) RestoreRestingSprite();
    }

    // ---------------- attacks ----------------

    public System.Collections.IEnumerator PlayAttack(BattleUnit target, System.Action onHit = null)
    {
        if (hasBattleAnimator && HasState("Attack"))
            yield return PlayAttackWithAnimator(onHit);
        else if (Stats != null && Stats.attackFrames != null && Stats.attackFrames.Length > 0)
        {
            yield return PlayAttackWithFrames(target);
            if (onHit != null) onHit(); // sprite-frame path: hit after animation
        }
        else
            yield return PlayTackle(target, onHit);
    }

    private System.Collections.IEnumerator PlayAttackWithAnimator(System.Action onHit)
    {
        Vector3 start = transform.localPosition;
        Vector3 lunge = start + new Vector3(StepDirection * attackLungeDistance, 0f, 0f);

        animator.Play("Attack", 0, 0f);
        yield return null;

        // Clip's total frame count
        AnimatorClipInfo[] infos = animator.GetCurrentAnimatorClipInfo(0);
        AnimationClip clip = infos.Length > 0 ? infos[0].clip : null;
        float totalFrames = 8f;
        if (clip != null && clip.frameRate > 0f && clip.length > 0f)
            totalFrames = clip.length * clip.frameRate;

        // What fraction of the animation is the hit frame?
        float hitFraction = 1f; // default: hit at the very end
        if (Stats != null && Stats.basicAttackHitFrame > 0 && totalFrames > 0)
            hitFraction = Mathf.Clamp01((float)Stats.basicAttackHitFrame / totalFrames);

        bool hitFired = false;

        while (true)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);

            if (info.normalizedTime >= 1f || !info.IsName("Attack"))
            {
                if (!hitFired && onHit != null) onHit(); // fallback: fire at end
                break;
            }

            // Fire the hit when: a tagged impact sprite appears, OR the animator
            // reaches the authored hit frame (if set). End-of-clip is the fallback.
            if (!hitFired)
            {
                Sprite sp = spriteRenderer.sprite;
                bool markerHit = sp != null &&
                    sp.name.IndexOf(impactSpriteMarker, System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (markerHit || info.normalizedTime >= hitFraction)
                {
                    hitFired = true;
                    if (onHit != null) onHit();
                }
            }

            // Lunge follows the ANIMATION's actual progress (not real time)
            float progress = info.normalizedTime;
            float k = progress < 0.5f ? progress * 2f : (1f - progress) * 2f;
            transform.localPosition = Vector3.Lerp(start, lunge, Mathf.Clamp01(k));

            yield return null;
        }

        transform.localPosition = start;
        PlayIdle();
    }

    private System.Collections.IEnumerator PlayAttackWithFrames(BattleUnit target)
    {
        Sprite[] frames = Stats.attackFrames;
        Vector3 start = transform.localPosition;

        Vector3 toTarget = target != null
            ? target.transform.position - transform.position
            : Vector3.right * StepDirection;
        float dirX = Mathf.Abs(toTarget.x) > 0.05f ? Mathf.Sign(toTarget.x) : StepDirection;
        Vector3 lunge = start + new Vector3(dirX * 0.5f, 0f, 0f);

        float frameDuration = 1f / Mathf.Max(1f, Stats.attackFrameRate);
        float total = Mathf.Max(0.35f, frames.Length * frameDuration);

        float t = 0f;
        int lastIndex = -1;
        while (t < total)
        {
            t += Time.deltaTime;
            int idx = Mathf.Clamp(Mathf.FloorToInt(t / frameDuration), 0, frames.Length - 1);
            if (idx != lastIndex && spriteRenderer != null && frames[idx] != null)
            {
                spriteRenderer.sprite = frames[idx];
                lastIndex = idx;
            }
            float half = total * 0.5f;
            float k = t < half ? (t / half) : 1f - (t - half) / half;
            transform.localPosition = Vector3.Lerp(start, lunge, Mathf.Clamp01(k));
            yield return null;
        }
        RestoreRestingSprite();
        transform.localPosition = start;
    }

    private System.Collections.IEnumerator PlayTackle(BattleUnit target, System.Action onHit = null)
    {
        Vector3 start = transform.localPosition;
        Vector3 toTarget = target != null
            ? target.transform.position - transform.position
            : Vector3.right * StepDirection;
        toTarget.y = 0f;
        Vector3 dir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.right * StepDirection;
        float dist = Mathf.Min(Mathf.Abs(toTarget.x) * 0.45f, 0.9f);
        Vector3 lunge = start + new Vector3(dir.x, 0f, 0f) * dist;

        if (!hasBattleAnimator)
        {
            Sprite[] slide = Stats != null ? Stats.slideForwardFrames : null;
            if (slide != null && slide.Length > 0 && spriteRenderer != null && slide[0] != null)
                spriteRenderer.sprite = slide[0];
        }

        yield return MoveLocal(lunge, 0.12f);

        // tackle: the "contact" moment is the lunge peak
        if (onHit != null) onHit();

        RestoreRestingSprite();
        yield return MoveLocal(start, 0.15f);
    }

    // ---------------- reactions ----------------

    /// Hurt → back to idle. Being hit also BREAKS a held Defend pose.
    public System.Collections.IEnumerator PlayHit()
    {
        if (hasBattleAnimator && (HasState("Hurt") || HasState("Hit")))
        {
            PlayBattleState(HasState("Hurt") ? "Hurt" : "Hit");
            yield return null;
            yield return new WaitForSeconds(Mathf.Min(CurrentClipLength(), 1.2f));
        }
        else
        {
            Vector3 start = transform.localPosition;
            Color original = spriteRenderer != null ? spriteRenderer.color : Color.white;
            if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 0.35f, 0.35f, original.a);
            for (int i = 0; i < 6; i++)
            {
                float offset = (i % 2 == 0) ? 0.09f : -0.09f;
                transform.localPosition = start + new Vector3(offset, 0f, 0f);
                yield return new WaitForSeconds(0.035f);
            }
            transform.localPosition = start;
            if (spriteRenderer != null) spriteRenderer.color = original;
        }
        PlayIdle();
    }

    public System.Collections.IEnumerator PlayHealed()
    {
        if (spriteRenderer == null) yield break;
        Color original = spriteRenderer.color;
        spriteRenderer.color = new Color(0.5f, 1f, 0.55f, original.a);
        yield return new WaitForSeconds(0.25f);
        spriteRenderer.color = original;
    }

    /// Death: with a Death clip — plays all frames and FREEZES on the final
    /// frame. Without one (enemies) — old fade-out.
    public System.Collections.IEnumerator PlayDeath()
    {
        if (weaknessBadge != null) weaknessBadge.SetActive(false);

        holdState = null;
        targetMarked = false;
        hoverMarked = false;

        if (hasBattleAnimator && HasState("Death"))
        {
            animator.Play("Death", 0, 0f);
            holdState = "Death";
            RefreshVisual();
            yield return null;
            float len = Mathf.Min(CurrentClipLength(), 2.5f);
            yield return new WaitForSeconds(len);
        }
        else
        {
            float a = currentAlpha;
            while (a > 0f)
            {
                a -= Time.deltaTime * 1.6f;
                SetVisualAlpha(Mathf.Max(0f, a));
                yield return null;
            }
            RefreshVisual();
        }
        SetHighlight(false);
    }

    // ---------------- visual helpers ----------------

    public void SetVisualAlpha(float alpha)
    {
        currentAlpha = Mathf.Clamp01(alpha);
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        // sprite itself: no tint — highlighting is the cyan outline now
        if (spriteRenderer != null)
            spriteRenderer.color = new Color(1f, 1f, 1f, currentAlpha);

        // cyan outline — shown while it's the unit's turn, a valid target, or hovered
        if (outlineGo != null && outlineRenderer != null)
        {
            outlineGo.SetActive(targetMarked || hoverMarked);
            outlineRenderer.color = new Color(outlineColor.r, outlineColor.g, outlineColor.b, currentAlpha);
        }
        if (nameMesh != null)
        {
            nameMesh.color = new Color(Stats != null && Stats.IsAlive ? 1f : 0.55f,
                Stats != null && Stats.IsAlive ? 1f : 0.55f,
                Stats != null && Stats.IsAlive ? 1f : 0.55f, currentAlpha);
        }
        if (hpBg != null) hpBg.color = new Color(0f, 0f, 0f, 0.75f * currentAlpha);
        if (hpFill != null)
        {
            float frac = Stats != null && Stats.MaxHP > 0 ? (float)Stats.currentHP / Stats.MaxHP : 0f;
            hpFill.color = frac <= 0.25f
                ? new Color(1f, 0.35f, 0.3f, currentAlpha)
                : new Color(0.2f, 0.85f, 0.35f, currentAlpha);
        }
    }

    private void SetSlideFrame(Sprite[] frames, float elapsed, float frameDuration, ref int lastIndex)
    {
        int idx = Mathf.Clamp(Mathf.FloorToInt(elapsed / frameDuration), 0, frames.Length - 1);
        if (idx != lastIndex && frames[idx] != null)
        {
            spriteRenderer.sprite = frames[idx];
            lastIndex = idx;
        }
    }

    private void RestoreRestingSprite()
    {
        if (hasBattleAnimator) { PlayIdle(); return; }
        if (spriteRenderer != null && originalSprite != null) spriteRenderer.sprite = originalSprite;
    }

    private System.Collections.IEnumerator MoveLocal(Vector3 target, float duration)
    {
        Vector3 start = transform.localPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        transform.localPosition = target;
    }

    // ---------------- shared helpers (name text + HP bar sprites) ----------------

    private static Font cachedFont;
    private static Font GetLegacyFont()
    {
        if (cachedFont == null)
            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return cachedFont;
    }

    private static Sprite whiteCenter, whiteLeft;
    private static Sprite WhiteCenter
    {
        get
        {
            if (whiteCenter == null) whiteCenter = CreateWhiteSprite(new Vector2(0.5f, 0.5f));
            return whiteCenter;
        }
    }
    private static Sprite WhiteLeft
    {
        get
        {
            if (whiteLeft == null) whiteLeft = CreateWhiteSprite(new Vector2(0f, 0.5f));
            return whiteLeft;
        }
    }

    private static Sprite CreateWhiteSprite(Vector2 pivot)
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), pivot, 1f);
    }
}