using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3.2f;
    public float runSpeed = 6.0f;
    public float jumpHeight = 1.4f;
    public float gravityMultiplier = 2.0f;

    [Header("Animation")]
    public Animator animator;
    public SpriteRenderer spriteRenderer;
    public bool flipSpriteX = true;
    [Tooltip("1.0 = run clips play at their native speed. Only raise (~1.6-1.8) for walk-clip fallback.")]
    public float runAnimationSpeed = 1.0f;

    [Header("Ground")]
    public LayerMask groundMask = ~0;
    public float groundCheckExtra = 0.15f;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private Vector2 moveInput;
    private bool isRunning;
    private bool isGrounded;

    // --- animation state ---
    private Vector2 animDirection = Vector2.down; // last faced direction (persists while idle)
    private Vector2 blendPosition = new Vector2(0f, -DirectionalAnimation.IdleBlendRadius);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Frictionless skin — the #1 fix for "randomly getting stuck on walls/edges".
        capsule.sharedMaterial = new UnityEngine.PhysicsMaterial("FrictionlessPlayer")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = UnityEngine.PhysicsMaterialCombine.Minimum,
            bounceCombine = UnityEngine.PhysicsMaterialCombine.Minimum
        };

        groundMask &= ~(1 << gameObject.layer); // never ground-check our own layer

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void Update()
    {
        ReadInput();
        UpdateAnimator();
        TrackEncounterDistance();
    }

    private void ReadInput()
    {
        moveInput = Vector2.zero;
        isRunning = false;

        if (OverworldMenu.IsOpen || RecruitableNPC.DialogueOpen) return;
        if (GameManager.Instance != null && GameManager.Instance.inBattle) return;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            float x = 0f, y = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;

            // Clamp to length 1 — diagonal speed identical to straight speed.
            moveInput = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

            isRunning = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (isGrounded && kb.spaceKey.wasPressedThisFrame) Jump();
        }

        Gamepad pad = Gamepad.current;
        if (pad != null && moveInput.sqrMagnitude < 0.01f)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.15f) moveInput = Vector2.ClampMagnitude(stick, 1f);
            isRunning = pad.leftStickButton.isPressed;
            if (isGrounded && pad.buttonSouth.wasPressedThisFrame) Jump();
        }
    }

    private void Jump()
    {
        if (!JumpAllowedHere()) return;   // ledge-gated: no zone, no jump
        float g = Mathf.Abs(Physics.gravity.y) * gravityMultiplier;
        float v = Mathf.Sqrt(2f * g * jumpHeight);
        Vector3 vel = rb.linearVelocity;
        rb.linearVelocity = new Vector3(vel.x, v, vel.z);
    }

    // True while the player's feet are inside a JumpZone's trigger volume.
    private bool JumpAllowedHere()
    {
        Vector3 bottom = transform.position + capsule.center
                       - Vector3.up * (capsule.height * 0.5f - capsule.radius);
        Collider[] hits = Physics.OverlapSphere(bottom, 0.75f, ~0, QueryTriggerInteraction.Collide);
        foreach (Collider c in hits)
            if (c.GetComponentInParent<JumpZone>() != null) return true;
        return false;
    }

    private void FixedUpdate()
    {
        isGrounded = CheckGrounded();

        Vector3 vel = rb.linearVelocity;
        Vector3 target = new Vector3(moveInput.x, 0f, moveInput.y) * (isRunning ? runSpeed : walkSpeed);

        vel.x = target.x;
        vel.z = target.z;
        if (!isGrounded) vel.y += Physics.gravity.y * (gravityMultiplier - 1f) * Time.fixedDeltaTime;

        rb.linearVelocity = vel;
    }

    private bool CheckGrounded()
    {
        Vector3 bottom = transform.position + capsule.center
                       - Vector3.up * (capsule.height * 0.5f - capsule.radius);
        float castDist = capsule.radius + groundCheckExtra;
        return Physics.SphereCast(bottom, capsule.radius * 0.85f, Vector3.down,
            out _, castDist, groundMask, QueryTriggerInteraction.Ignore);
    }

    private void UpdateAnimator()
    {
        bool moving = moveInput.sqrMagnitude > 0.01f;
        if (moving)
        {
            animDirection = moveInput; // remember facing for when we stop
            if (flipSpriteX && spriteRenderer != null && Mathf.Abs(moveInput.x) > 0.05f)
                spriteRenderer.flipX = moveInput.x < 0f;
        }

        // Stopped -> inner ring (idle). Moving -> walk ring, or run ring while Shift is held.
        Vector2 target = DirectionalAnimation.GetBlendTarget(moving, animDirection, isRunning);
        blendPosition = DirectionalAnimation.Smooth(blendPosition, target, Time.deltaTime);

        // Unit-length facing for the Jump tree (magnitude stripped).
        Vector2 facing = DirectionalAnimation.GetFacing(blendPosition, animDirection);

        if (animator != null)
        {
            animator.SetFloat("MoveX", blendPosition.x);
            animator.SetFloat("MoveY", blendPosition.y);
            animator.SetFloat("FaceX", facing.x);
            animator.SetFloat("FaceY", facing.y);
            animator.SetBool("IsGrounded", isGrounded);
            animator.SetFloat("VerticalVelocity", rb.linearVelocity.y);
            // 1.0 with real run clips. Resets to 1 in the air so jump clips play at normal rate.
            animator.speed = (isRunning && moving && isGrounded) ? runAnimationSpeed : 1f;
        }
    }

    private void TrackEncounterDistance()
    {
        Vector3 v = rb.linearVelocity;
        float dist = new Vector2(v.x, v.z).magnitude * Time.deltaTime;
        if (dist > 0f) EncounterManager.RegisterMovement(dist);
    }
}