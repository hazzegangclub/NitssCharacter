using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Handles jump logic for Nitss independently of the movement controller so we can iterate on
    /// vertical behaviour without touching horizontal movement code. Supports buffered jumps,
    /// coyote time and an optional double jump trigger.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssJumpModule : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssMovementController movement;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssAnimatorController animatorController;
        [SerializeField] private Rigidbody body;
        [SerializeField] private NitssDashModule dashModule;

        [Header("Jump")]
        [SerializeField, Tooltip("Upward velocity applied on the first jump (m/s).")]
        private float jumpVelocity = 7.25f;
        [SerializeField, Tooltip("Allow performing a secondary jump while airborne.")]
        private bool allowDoubleJump = true;
        [SerializeField, Tooltip("Upward velocity used for the double jump (m/s).")]
        private float doubleJumpVelocity = 7f;
        [SerializeField, Tooltip("Animator trigger fired when the double jump happens (optional).")]
        private string doubleJumpTrigger = "DoubleJump";
        [SerializeField, Tooltip("Animator trigger fired once when the character actually lands.")]
        private string landingTrigger = "Landing";
        [SerializeField, Tooltip("In seconds, how long after leaving the ground a jump input is still accepted.")]
        private float coyoteTime = 0.12f;
        [SerializeField, Tooltip("In seconds, how long a jump input is buffered before it gets consumed.")]
        private float jumpBufferTime = 0.1f;
        [SerializeField, Tooltip("If true, resets any downward velocity before applying the jump impulse.")]
        private bool resetVerticalVelocityOnJump = true;
        [SerializeField, Tooltip("Tempo mínimo que o personagem precisa permanecer afastado do chão antes de considerarmos um pouso (evita flicker que reinicia a animação).")]
        private float landingGraceTime = 0.08f;
        [SerializeField, Tooltip("Se a velocidade vertical for maior que este valor ainda consideramos o personagem em subida, mesmo que o raycast detecte o chão.")]
        private float upwardVelocityGroundTolerance = 0.1f;
        [SerializeField, Tooltip("Tempo mínimo no ar antes de disparar o trigger de pouso. Evita loops quando o raycast oscila em superfícies inclinadas.")]
        private float minAirborneTimeForLanding = 0.05f;

        [Header("Gravity Multipliers")]
        [SerializeField, Tooltip("Applied when the character is rising and keeps the jump button held.")]
        private float ascentGravityMultiplier = 1f;
        [SerializeField, Tooltip("Applied when the character is rising but the jump button is released early.")]
        private float jumpCutGravityMultiplier = 1.75f;
        [SerializeField, Tooltip("Applied when the character is falling.")]
        private float fallGravityMultiplier = 2.1f;

        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpPressedTime = float.NegativeInfinity;
        private bool primaryJumpConsumed;
        private bool doubleJumpConsumed;
        private bool cachedJumpHeld;
        private bool lastAnimatorJumpState;
        private bool doubleJumpPerformedThisAirborne;
        private float lastUngroundedTime = float.NegativeInfinity;
        private float airborneDuration;
        private bool landingArmed;

        public bool IsAirborne { get; private set; }
        public bool AllowDoubleJump
        {
            get => allowDoubleJump;
            set
            {
                allowDoubleJump = value;
                if (!allowDoubleJump)
                {
                    doubleJumpConsumed = true;
                    doubleJumpPerformedThisAirborne = false;
                }
            }
        }
        public bool CanPerformDoubleJump => allowDoubleJump && !doubleJumpConsumed && primaryJumpConsumed && IsAirborne;
        public bool HasDoubleJumpedThisAirborne => doubleJumpPerformedThisAirborne;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            movement = GetComponent<NitssMovementController>();
            inputReader = GetComponent<NitssInputReader>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
            body = context ? context.Body : GetComponentInChildren<Rigidbody>();
            dashModule = GetComponent<NitssDashModule>();
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void OnValidate()
        {
            if (jumpVelocity < 0f) jumpVelocity = 0f;
            if (doubleJumpVelocity < 0f) doubleJumpVelocity = 0f;
            if (coyoteTime < 0f) coyoteTime = 0f;
            if (jumpBufferTime < 0f) jumpBufferTime = 0f;
            jumpCutGravityMultiplier = Mathf.Max(0f, jumpCutGravityMultiplier);
            if (landingGraceTime < 0f) landingGraceTime = 0f;
            upwardVelocityGroundTolerance = Mathf.Max(0f, upwardVelocityGroundTolerance);
            minAirborneTimeForLanding = Mathf.Max(0f, minAirborneTimeForLanding);
            if (!allowDoubleJump)
            {
                doubleJumpConsumed = true;
                doubleJumpPerformedThisAirborne = false;
            }
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
            {
                return;
            }

            inputReader = reader ?? inputReader;

            bool groundedRaw = movement != null && movement.IsGrounded;
            float verticalVelocity = body ? body.linearVelocity.y : 0f;

            if (!groundedRaw)
            {
                lastUngroundedTime = Time.time;
            }

            bool grounded = groundedRaw;
            if (grounded)
            {
                if (verticalVelocity > upwardVelocityGroundTolerance)
                {
                    grounded = false;
                    lastUngroundedTime = Time.time;
                }
                else if (Time.time - lastUngroundedTime <= landingGraceTime)
                {
                    grounded = false;
                }
            }

            bool wasAirborne = IsAirborne;

            if (grounded)
            {
                lastGroundedTime = Time.time;
                primaryJumpConsumed = false;
                doubleJumpConsumed = false;
                IsAirborne = false;
                doubleJumpPerformedThisAirborne = false;
                if (wasAirborne && landingArmed && animatorController != null && !string.IsNullOrWhiteSpace(landingTrigger))
                {
                    animatorController.SetTrigger(landingTrigger);
                }
                airborneDuration = 0f;
                landingArmed = false;
            }
            else
            {
                if (!IsAirborne)
                {
                    IsAirborne = true;
                    airborneDuration = 0f;
                    landingArmed = false;
                    if (animatorController != null && !string.IsNullOrWhiteSpace(landingTrigger))
                    {
                        animatorController.ResetTrigger(landingTrigger);
                    }
                }
                else
                {
                    airborneDuration += dt;
                    if (!landingArmed && airborneDuration >= minAirborneTimeForLanding)
                    {
                        landingArmed = true;
                    }
                }
            }

            if (inputReader != null)
            {
                if (inputReader.JumpPressed)
                {
                    lastJumpPressedTime = Time.time;
                }
                cachedJumpHeld = inputReader.Current.JumpHeld;
            }
            else
            {
                cachedJumpHeld = false;
            }

            bool wantsJump = Time.time - lastJumpPressedTime <= jumpBufferTime;
            bool canUseCoyote = Time.time - lastGroundedTime <= coyoteTime;

            if (wantsJump && (!primaryJumpConsumed && canUseCoyote))
            {
                PerformJump(jumpVelocity, false);
                return;
            }

            if (wantsJump && CanPerformDoubleJump)
            {
                PerformJump(doubleJumpVelocity, true);
            }

            UpdateAnimatorState(grounded);
        }

        private void FixedUpdate()
        {
            if (!EnsureReferences())
            {
                return;
            }

            if (!body || movement == null)
            {
                return;
            }

            if (movement.IsGrounded && body.linearVelocity.y <= 0f)
            {
                return;
            }

            Vector3 velocity = body.linearVelocity;
            float gravityScale = 1f;

            if (velocity.y > 0f)
            {
                gravityScale = cachedJumpHeld ? ascentGravityMultiplier : jumpCutGravityMultiplier;
            }
            else if (velocity.y < 0f)
            {
                gravityScale = fallGravityMultiplier;
            }

            gravityScale = Mathf.Max(0f, gravityScale);

            if (!Mathf.Approximately(gravityScale, 1f))
            {
                Vector3 gravity = Physics.gravity * (gravityScale - 1f);
                velocity += gravity * Time.fixedDeltaTime;
                body.linearVelocity = velocity;
            }
        }

        private void PerformJump(float velocityY, bool isDoubleJump)
        {
            if (body == null)
            {
                return;
            }

            Vector3 linearVelocity = body.linearVelocity;
            if (resetVerticalVelocityOnJump && linearVelocity.y < 0f)
            {
                linearVelocity.y = 0f;
            }
            linearVelocity.y = velocityY;
            body.linearVelocity = linearVelocity;

            if (movement != null)
            {
                movement.ForceAirborneStateForCombo();
            }

            IsAirborne = true;
            primaryJumpConsumed = true;
            if (isDoubleJump)
            {
                doubleJumpConsumed = true;
                doubleJumpPerformedThisAirborne = true;
                if (animatorController != null && !string.IsNullOrWhiteSpace(doubleJumpTrigger))
                {
                    animatorController.ResetTrigger(doubleJumpTrigger);
                    animatorController.SetTrigger(doubleJumpTrigger);
                }
            }
            else
            {
                doubleJumpConsumed = allowDoubleJump ? false : true;
                doubleJumpPerformedThisAirborne = false;
                if (animatorController != null && !string.IsNullOrWhiteSpace(doubleJumpTrigger))
                {
                    animatorController.ResetTrigger(doubleJumpTrigger);
                }
            }

            if (animatorController != null && !string.IsNullOrWhiteSpace(landingTrigger))
            {
                animatorController.ResetTrigger(landingTrigger);
            }

            lastJumpPressedTime = float.NegativeInfinity;
            airborneDuration = 0f;
            landingArmed = false;
            UpdateAnimatorState(false);
        }

        private void UpdateAnimatorState(bool grounded)
        {
            if (animatorController == null)
            {
                return;
            }

            if (!dashModule)
            {
                dashModule = GetComponent<NitssDashModule>();
            }

            bool shouldBeJumping = !grounded;
            if (dashModule != null && dashModule.ShouldHoldJumpState)
            {
                shouldBeJumping = false;
            }
            if (shouldBeJumping != lastAnimatorJumpState)
            {
                animatorController.SetJumping(shouldBeJumping);
                lastAnimatorJumpState = shouldBeJumping;
            }
        }

        private bool EnsureReferences()
        {
            if (!context)
            {
                context = GetComponent<NitssCharacterContext>();
            }
            if (!movement)
            {
                movement = GetComponent<NitssMovementController>();
            }
            if (!inputReader && context)
            {
                inputReader = context.InputReader;
            }
            if (!animatorController && context)
            {
                animatorController = context.AnimatorController;
            }
            if (!body && context)
            {
                body = context.Body;
            }
            if (!dashModule)
            {
                dashModule = GetComponent<NitssDashModule>();
            }
            return context && movement && body;
        }
    }
}
