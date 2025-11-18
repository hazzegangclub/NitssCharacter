using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Implementação simplificada de movimento para o Nitss. Controla aceleração, pulo e dash.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssMovementController : MonoBehaviour
    {
        [Header("Configuração")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float acceleration = 18f;
        [SerializeField] private float deceleration = 24f;
        [SerializeField] private float groundCheckDistance = 0.25f;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private bool clampToPlaneZ = true;

        [Header("Animação")]
        [SerializeField, Tooltip("Velocidade de referência para a transição Idle→Walk (m/s).")]
        private float walkAnimationSpeed = 2.5f;
        [SerializeField, Tooltip("Velocidade máxima esperada para corrida (m/s).")]
        private float runAnimationSpeed = 6f;
        [SerializeField, Tooltip("Velocidade de resposta do parâmetro Speed (quanto maior, mais rápido acompanha).")]
        private float animatorResponse = 10f;

        private NitssCharacterContext context;
        private NitssInputReader input;
        private NitssAnimatorController animatorController;
        private Rigidbody body;
        private Transform visual;
        private float smoothedYaw;

        private bool isGrounded;
        private Vector3 planarPush;
        private Vector3 facingDirection = Vector3.right;
        private float animatorSpeed01;
        private float desiredSpeedX;

        // Expostos para outros sistemas
        public bool IsGrounded => isGrounded;
        public Vector3 FacingDirection => facingDirection;
        public bool IsAirForAttacks => !isGrounded;
        public bool IsInDoubleJumpWindow => false;
        public bool DidDoubleJumpThisAirborne => false;

        private void Awake()
        {
            context = GetComponent<NitssCharacterContext>();
            input = context ? context.InputReader : null;
            animatorController = context ? context.AnimatorController : null;
            body = context ? context.Body : null;
            visual = context ? context.VisualRoot : transform;

            if (!body)
            {
                Debug.LogWarning("NitssMovementController exige um Rigidbody referenciado.", this);
            }
            else if (clampToPlaneZ)
            {
                body.constraints |= RigidbodyConstraints.FreezePositionZ;
            }

            if (body)
            {
                body.constraints |= RigidbodyConstraints.FreezeRotation;
            }

            if (visual)
            {
                smoothedYaw = visual.localEulerAngles.y;
            }
        }

        private void Update()
        {
            UpdateGroundState();
            UpdateAnimator(Time.deltaTime);
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            input = reader ?? input;
            if (!body) return;
            if (input != null)
            {
                desiredSpeedX = Mathf.Clamp(input.Current.Move.x, -1f, 1f) * moveSpeed;
            }
            else
            {
                desiredSpeedX = 0f;
            }
        }

        private void UpdateGroundState()
        {
            if (!body) return;
            Vector3 origin = body.position + Vector3.up * 0.05f;
            isGrounded = Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore);
        }
        private void FixedUpdate()
        {
            if (!body) return;

            float dt = Time.fixedDeltaTime;
            float targetSpeed = desiredSpeedX;

            Vector3 velocity = body.linearVelocity;
            float current = velocity.x;
            float accel = Mathf.Abs(targetSpeed) > Mathf.Abs(current) ? acceleration : deceleration;
            float next = Mathf.MoveTowards(current, targetSpeed, accel * dt);
            velocity.x = next;

            if (planarPush.sqrMagnitude > 0f)
            {
                velocity += planarPush;
                planarPush = Vector3.Lerp(planarPush, Vector3.zero, dt * 5f);
            }

            velocity.z = 0f;
            body.linearVelocity = velocity;

            if (Mathf.Abs(next) > 0.05f)
            {
                float facingX = Mathf.Sign(next);
                if (!Mathf.Approximately(facingX, facingDirection.x))
                {
                    facingDirection = new Vector3(facingX, 0f, 0f);
                }
                RotateVisual(Time.fixedDeltaTime, false);
            }
            else
            {
                RotateVisual(Time.fixedDeltaTime, true);
            }
        }

        private void RotateVisual(float dt, bool stationary)
        {
            if (!visual) return;
            float targetYaw;
            if (stationary)
            {
                targetYaw = facingDirection.x >= 0f ? 165f : 230f;
            }
            else
            {
                targetYaw = facingDirection.x >= 0f ? 110f : 250f;
            }
            smoothedYaw = Mathf.MoveTowardsAngle(smoothedYaw, targetYaw, dt * 720f);
            visual.localEulerAngles = new Vector3(0f, smoothedYaw, 0f);
        }

        private void UpdateAnimator(float dt)
        {
            if (animatorController == null && context != null)
            {
                animatorController = context.AnimatorController;
            }
            if (animatorController == null)
            {
                return;
            }

            float planarSpeed = 0f;
            if (body)
            {
                var vel = body.linearVelocity;
                planarSpeed = Mathf.Abs(vel.x);
            }

            float targetSpeed = NormalizeSpeed(planarSpeed);
            animatorSpeed01 = SmoothTowards(animatorSpeed01, targetSpeed, animatorResponse * dt);

            animatorController.SetSpeed(animatorSpeed01);
        }

        private float NormalizeSpeed(float speed)
        {
            float safeWalk = Mathf.Max(0.0001f, walkAnimationSpeed);
            float safeRun = Mathf.Max(safeWalk + 0.0001f, runAnimationSpeed);

            if (speed <= 0f)
            {
                return 0f;
            }

            if (speed <= safeWalk)
            {
                float t = Mathf.InverseLerp(0f, safeWalk, speed);
                return Mathf.Lerp(0f, 0.5f, t); // Idle (0) -> Walk (~0.5)
            }

            float tRun = Mathf.InverseLerp(safeWalk, safeRun, speed);
            return Mathf.Lerp(0.5f, 1f, tRun); // Walk (~0.5) -> Run (1)
        }

        private static float SmoothTowards(float current, float target, float rate)
        {
            if (rate <= 0f)
            {
                return target;
            }

            float t = 1f - Mathf.Exp(-rate);
            return Mathf.Lerp(current, target, Mathf.Clamp01(t));
        }

        public void AddExternalPush(Vector3 planar)
        {
            planar.y = 0f;
            planarPush += planar;
        }

        public void ForceAirborneStateForCombo()
        {
            isGrounded = false;
        }

        public void RegisterKnockdownEntryImpulse(Vector3 planarImpulse)
        {
            AddExternalPush(planarImpulse);
        }

        public void BumpVertical(float minVelocityY)
        {
            if (!body) return;
            Vector3 v = body.linearVelocity;
            if (v.y < minVelocityY)
            {
                v.y = minVelocityY;
                body.linearVelocity = v;
            }
        }

        public void NotifyAirComboHit(float upImpulse, float extendHoverSeconds)
        {
            if (upImpulse > 0f)
            {
                BumpVertical(upImpulse);
            }
        }

        public void ClampRiseImmediate(float cap)
        {
            if (!body || cap <= 0f) return;
            Vector3 v = body.linearVelocity;
            if (v.y > cap)
            {
                v.y = cap;
                body.linearVelocity = v;
            }
        }
    }
}
