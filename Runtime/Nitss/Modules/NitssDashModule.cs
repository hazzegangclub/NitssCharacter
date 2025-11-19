using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssDashModule : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssMovementController movement;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssAnimatorController animatorController;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Animator animator;

        [Header("Dash")]
        [SerializeField, Tooltip("Horizontal speed applied when the dash starts (m/s).")]
        private float dashSpeed = 12f;
        [SerializeField, Tooltip("Dash duration in seconds.")]
        private float dashDuration = 0.2f;
        [SerializeField, Tooltip("Cooldown between dashes in seconds.")]
        private float dashCooldown = 0.35f;
        [SerializeField, Tooltip("Buffer window to consume a dash input (seconds).")]
        private float dashBufferTime = 0.12f;
        [SerializeField, Tooltip("Allow air dashes.")]
        private bool allowAirDash = false;
        [SerializeField, Tooltip("Speed multiplier applied to air dashes.")]
        private float airDashSpeedMultiplier = 0.85f;
        [SerializeField, Tooltip("Minimum vertical velocity while dashing on ground.")]
        private float groundedVerticalClamp = -4f;
        [SerializeField, Tooltip("Se verdadeiro, força o parâmetro IsJumping para falso durante o dash para manter o Dodge visível.")]
        private bool overrideJumpBoolWhileDashing = true;
        [SerializeField, Tooltip("Tempo após um dash aéreo antes de restaurar o JumpFall (segundos).")]
        private float airDashJumpResumeDelay = 0.08f;
        [SerializeField, Tooltip("Nome do estado de queda usado quando o dash aéreo termina.")]
        private string jumpFallStateName = "Sword&Shield_JumpFall";
        [SerializeField, Tooltip("Duração do crossfade para o estado de queda após o dash aéreo.")]
        private float jumpFallTransition = 0.08f;
        [SerializeField, Tooltip("Layer do Animator utilizado para tocar o estado de queda.")]
        private int animatorLayerIndex = 0;

        [Header("Animator")]
        [SerializeField, Tooltip("Animator trigger fired when the dash begins.")]
        private string dashTrigger = "Dodge";

        private float cooldownTimer;
        private float activeTimer;
        private float lastDashPressedTime = float.NegativeInfinity;
        private Vector3 dashDirection = Vector3.right;
        private bool applyJumpAfterDash;
        private float pendingJumpResumeDelay;
        private int jumpFallStateHash;

        public bool IsDashing => activeTimer > 0f;
        public bool IsReady => cooldownTimer <= 0f;
        public bool ShouldHoldJumpState => overrideJumpBoolWhileDashing && (IsDashing || (applyJumpAfterDash && pendingJumpResumeDelay > 0f));

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            movement = GetComponent<NitssMovementController>();
            inputReader = GetComponent<NitssInputReader>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
            body = context ? context.Body : GetComponentInChildren<Rigidbody>();
            animator = context && context.Animator ? context.Animator : GetComponentInChildren<Animator>();
            jumpFallStateHash = string.IsNullOrWhiteSpace(jumpFallStateName) ? 0 : Animator.StringToHash(jumpFallStateName);
        }

        private void Awake()
        {
            EnsureReferences();
            RebuildAnimatorCaches();
        }

        private void OnValidate()
        {
            jumpFallTransition = Mathf.Max(0f, jumpFallTransition);
            animatorLayerIndex = Mathf.Max(0, animatorLayerIndex);
            RebuildAnimatorCaches();
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
            {
                return;
            }

            inputReader = reader ?? inputReader;

            cooldownTimer = Mathf.Max(0f, cooldownTimer - dt);
            if (activeTimer > 0f)
            {
                activeTimer = Mathf.Max(0f, activeTimer - dt);
                MaintainDashVelocity();
                if (activeTimer <= 0f)
                {
                    FinishDash();
                }
            }

            if (pendingJumpResumeDelay > 0f)
            {
                pendingJumpResumeDelay = Mathf.Max(0f, pendingJumpResumeDelay - dt);
                if (pendingJumpResumeDelay <= 0f && applyJumpAfterDash)
                {
                    CompleteAirDashRecovery();
                }
            }

            if (inputReader != null && inputReader.DashPressed)
            {
                lastDashPressedTime = Time.time;
            }

            if (IsDashing)
            {
                return;
            }

            if (cooldownTimer > 0f)
            {
                return;
            }

            if (Time.time - lastDashPressedTime > dashBufferTime)
            {
                return;
            }

            if (!allowAirDash && movement != null && !movement.IsGrounded)
            {
                return;
            }

            Vector3 direction = ResolveDashDirection();
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            StartDash(direction);
        }

        private void StartDash(Vector3 direction)
        {
            dashDirection = direction;
            lastDashPressedTime = float.NegativeInfinity;
            pendingJumpResumeDelay = 0f;

            bool grounded = movement != null && movement.IsGrounded;
            float speed = dashSpeed;
            if (!grounded && allowAirDash)
            {
                speed *= Mathf.Max(0f, airDashSpeedMultiplier);
            }

            float velocityX = direction.x * speed;
            movement?.ForceHorizontalVelocity(velocityX, dashDuration);
            if (body != null)
            {
                Vector3 velocity = body.linearVelocity;
                velocity.x = velocityX;
                if (grounded && groundedVerticalClamp < 0f)
                {
                    velocity.y = Mathf.Max(velocity.y, groundedVerticalClamp);
                }
                body.linearVelocity = velocity;
            }

            applyJumpAfterDash = !grounded;
            if (animatorController != null && !string.IsNullOrWhiteSpace(dashTrigger))
            {
                animatorController.SetTrigger(dashTrigger);
            }

            if (!grounded && animatorController != null)
            {
                if (overrideJumpBoolWhileDashing)
                {
                    animatorController.SetJumping(false);
                }
                applyJumpAfterDash = true;
            }

            activeTimer = dashDuration;
            cooldownTimer = dashCooldown;
        }

        private void MaintainDashVelocity()
        {
            if (movement == null)
            {
                return;
            }

            bool grounded = movement.IsGrounded;
            float speed = dashSpeed;
            if (!grounded && allowAirDash)
            {
                speed *= Mathf.Max(0f, airDashSpeedMultiplier);
            }

            float velocityX = dashDirection.x * speed;
            movement.ForceHorizontalVelocity(velocityX, Mathf.Max(activeTimer, Time.fixedDeltaTime));
            if (body != null)
            {
                Vector3 velocity = body.linearVelocity;
                velocity.x = velocityX;
                if (grounded && groundedVerticalClamp < 0f)
                {
                    velocity.y = Mathf.Max(velocity.y, groundedVerticalClamp);
                }
                body.linearVelocity = velocity;
            }
        }

        private Vector3 ResolveDashDirection()
        {
            float dirX = 0f;
            if (inputReader != null)
            {
                Vector2 move = inputReader.Current.Move;
                if (Mathf.Abs(move.x) > 0.1f)
                {
                    dirX = Mathf.Sign(move.x);
                }
            }

            if (Mathf.Abs(dirX) < 0.1f && movement != null)
            {
                Vector3 facing = movement.FacingDirection;
                if (Mathf.Abs(facing.x) > 0.1f)
                {
                    dirX = Mathf.Sign(facing.x);
                }
            }

            if (Mathf.Abs(dirX) < 0.1f)
            {
                dirX = 1f;
            }

            return new Vector3(dirX, 0f, 0f);
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
            if (!animator && context)
            {
                animator = context.Animator;
            }
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
            if (jumpFallStateHash == 0)
            {
                jumpFallStateHash = string.IsNullOrWhiteSpace(jumpFallStateName)
                    ? 0
                    : Animator.StringToHash(jumpFallStateName);
            }
            return movement && body;
        }

        private void FinishDash()
        {
            if (!animatorController)
            {
                applyJumpAfterDash = false;
                pendingJumpResumeDelay = 0f;
                return;
            }

            if (applyJumpAfterDash)
            {
                if (airDashJumpResumeDelay > 0f)
                {
                    pendingJumpResumeDelay = Mathf.Max(pendingJumpResumeDelay, airDashJumpResumeDelay);
                }
                else
                {
                    CompleteAirDashRecovery();
                }
            }
        }

        private void CompleteAirDashRecovery()
        {
            if (!applyJumpAfterDash)
            {
                return;
            }

            bool grounded = movement != null && movement.IsGrounded;
            if (grounded)
            {
                applyJumpAfterDash = false;
                pendingJumpResumeDelay = 0f;
                return;
            }

            if (animatorController != null)
            {
                animatorController.SetJumping(true);
            }

            if (animator == null && context)
            {
                animator = context.Animator;
            }

            if (animator != null && jumpFallStateHash != 0)
            {
                animator.CrossFadeInFixedTime(jumpFallStateHash, jumpFallTransition, animatorLayerIndex, 0f);
            }

            applyJumpAfterDash = false;
            pendingJumpResumeDelay = 0f;
        }

        private void RebuildAnimatorCaches()
        {
            jumpFallStateHash = string.IsNullOrWhiteSpace(jumpFallStateName)
                ? 0
                : Animator.StringToHash(jumpFallStateName);
        }
    }
}
