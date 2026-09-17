using UnityEngine;

public class PartyFollower : MonoBehaviour
{
    public int followIndex;
    public float moveSpeed = 4.4f;
    [Tooltip("Sprint speed when far behind. Must comfortably beat the player's RUN speed (6.0).")]
    public float catchUpSpeed = 9f;
    public bool flipSpriteX = true;

    [Header("Speed feel")]
    [Tooltip("Extra speed over the leader's CURRENT speed, so followers keep pace instead of rubber-banding.")]
    public float overtakeMargin = 1.5f;
    [Tooltip("1.0 = real run clips. 1.4-1.8 = walk clips sped up to look like running.")]
    public float runAnimationSpeed = 1.4f;

    [Header("Ground glue (slopes & area terrain)")]
    public float climbSpeed = 12f;
    public float descendSpeed = 9f;

    [Header("Ledge hops (followers DO jump these)")]
    [Tooltip("Upward height change that counts as a ledge (hop) instead of a step (glue).")]
    public float ledgeUpThreshold = 0.55f;
    [Tooltip("Downward drop that triggers the gap/descent check before following it down.")]
    public float ledgeDownThreshold = 0.9f;
    public float hopDuration = 0.45f;
    public float hopUpArc = 0.55f;    // extra height added mid-hop (upward ledges)
    public float hopDownArc = 0.12f;  // extra height added mid-hop (downward ledges)

    public PlayerParty Party { get; set; }

    private Animator animator;
    private SpriteRenderer spriteRenderer;

    // --- animation state ---
    private Vector2 lastDirection = Vector2.down;
    private Vector2 blendPosition = new Vector2(0f, -DirectionalAnimation.IdleBlendRadius);

    private bool maskReady;
    private int groundMask;

    // --- hop / gap state ---
    private bool hopping;      // mid ledge-hop
    private bool gliding;      // crossing a gap at constant height
    private float hopT;        // 0..1 through the hop
    private float hopStartY, hopEndY, hopArc;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        Party = GetComponentInParent<PlayerParty>();
    }

    private void Update()
    {
        if (Party == null) return;
        Vector3 target = Party.GetTrailPoint(followIndex);

        // ---- XZ: steer toward the trail point ----
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        float dist = flat.magnitude;

        float leaderSpeed = Party.LeaderSpeed;
        float speed = Mathf.Clamp(leaderSpeed + overtakeMargin + dist, moveSpeed, catchUpSpeed);

        Vector2 dirInput = Vector2.zero;
        if (dist > 0.05f)
        {
            Vector3 dir = flat / dist;
            float step = Mathf.Min(speed * Time.deltaTime, dist);
            transform.position += dir * step;
            dirInput = new Vector2(dir.x, dir.z);
            lastDirection = dirInput;
            if (flipSpriteX && spriteRenderer != null && Mathf.Abs(dir.x) > 0.05f)
                spriteRenderer.flipX = dir.x < 0f;
        }

        // ---- Y: hop / gap-glide / ground-glue ----
        if (hopping)
        {
            hopT += Time.deltaTime / hopDuration;
            if (hopT >= 1f) { hopping = false; SetY(hopEndY); }
            else SetY(Mathf.Lerp(hopStartY, hopEndY, hopT) + hopArc * Mathf.Sin(Mathf.PI * hopT));
        }
        else
        {
            float ownGround = GroundYUnderMe();
            float curY = transform.position.y;
            float diff = ownGround - curY;   // + = ground above us, - = ground below us

            if (diff > ledgeUpThreshold)
            {
                StartHop(ownGround, hopUpArc);          // ledge UP → jump up onto it
            }
            else if (diff < -ledgeDownThreshold)
            {
                if (target.y > curY - ledgeDownThreshold)
                    gliding = true;                     // GAP: the path continues at our height — cross it
                else
                    StartHop(ownGround, hopDownArc);    // intended descent → jump down
            }
            else
            {
                gliding = false;
                float rate = diff >= 0f ? climbSpeed : descendSpeed;
                SetY(Mathf.MoveTowards(curY, ownGround, rate * Time.deltaTime));
            }
        }

        // ---- animation ----
        bool moving = dirInput.sqrMagnitude > 0.01f;
        bool hurrying = speed > moveSpeed + 0.5f;
        Vector2 blendTarget = DirectionalAnimation.GetBlendTarget(moving, lastDirection, hurrying);
        blendPosition = DirectionalAnimation.Smooth(blendPosition, blendTarget, Time.deltaTime);
        Vector2 facing = DirectionalAnimation.GetFacing(blendPosition, lastDirection);

        if (animator != null)
        {
            animator.SetFloat("MoveX", blendPosition.x);
            animator.SetFloat("MoveY", blendPosition.y);
            animator.SetFloat("FaceX", facing.x);
            animator.SetFloat("FaceY", facing.y);
            // airborne during a hop; leap pose while crossing a gap
            animator.SetBool("IsGrounded", !(hopping || gliding));
            animator.SetFloat("VerticalVelocity", hopping ? (hopT < 0.5f ? 4f : -6f) : 0f);
            animator.speed = (moving && hurrying) ? runAnimationSpeed : 1f;
        }
    }

    // ---------------- helpers ----------------

    private void SetY(float y)
    {
        Vector3 p = transform.position;
        p.y = y;
        transform.position = p;
    }

    private void StartHop(float endY, float arc)
    {
        hopping = true;
        gliding = false;
        hopT = 0f;
        hopStartY = transform.position.y;
        hopEndY = endY;
        hopArc = arc;
    }

    // Ground height under THIS follower — never under the leader.
    private float GroundYUnderMe()
    {
        if (!maskReady)
        {
            int p = LayerMask.NameToLayer("Player");
            groundMask = p >= 0 ? ~(1 << p) : ~0;
            maskReady = true;
        }
        Vector3 origin = transform.position + Vector3.up * 2f; // cast from above our own head
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return transform.position.y; // no ground found — hold height
    }
}