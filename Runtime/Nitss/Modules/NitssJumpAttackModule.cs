using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssJumpAttackModule : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssAnimatorController animatorController;
        [SerializeField] private NitssMovementController movementController;
        [SerializeField] private NitssCombatController combatController;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssJumpModule jumpModule;
        [SerializeField] private Rigidbody body;

        [Header("Fallback")]
        [SerializeField] private float idleToJumpFallSeconds = 0.45f;
        [SerializeField] private string jumpFallStateName = "Sword&Shield_JumpFall";
        [SerializeField] private float jumpFallCrossfade = 0.08f;
        [SerializeField] private int jumpFallLayerIndex = 0;

        [Header("Triggers de Animacao")]
        [SerializeField] private string jumpAttack1Trigger = "JumpAttack1";
        [SerializeField] private string jumpAttack2Trigger = "JumpAttack2";
        [SerializeField] private string jumpAttack3Trigger = "JumpAttack3";

        [Header("Combo Timing")]
        [SerializeField] private float attack2MinDelaySeconds = 0.08f;
        [SerializeField] private float attack2WindowSeconds = 0.35f;
        [SerializeField] private float attack3MinDelaySeconds = 0.08f;
        [SerializeField] private float attack3WindowSeconds = 0.45f;

        [Header("Impulsos")]
        [SerializeField] private float verticalImpulsePerHit = 2.25f;
        [SerializeField] private float forwardImpulsePerHit = 1.6f;

        [Header("Debug")]
        [SerializeField] private bool debugLogging;

        private bool doubleJumpArmed;
        private bool firstAttackAvailable;
        private bool stageInProgress;
        private bool holdAcquired;
        private bool wasGroundedLastFrame;
        private bool airComboUsedThisAirborne;
        private int activeStage;
        private int pendingStage;
        private float idleTimer;
        private Animator animator;
        private int jumpFallStateHash;

        private bool nextStageArmed;
        private int nextStageCandidate;
        private float comboDelayTimer;
        private float comboWindowTimer;

        private void Awake()
        {
            EnsureReferences();
            if (combatController != null)
            {
                combatController.AttackStageStarted += HandleAttackStageStarted;
                combatController.AttackStageEnded += HandleAttackStageEnded;
            }
            RebuildAnimatorCaches();
        }

        private void OnDestroy()
        {
            if (combatController != null)
            {
                combatController.AttackStageStarted -= HandleAttackStageStarted;
                combatController.AttackStageEnded -= HandleAttackStageEnded;
            }
        }

        private void OnDisable()
        {
            ReleaseComboHold(true);
            ResetState();
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences()) return;
            inputReader = reader ?? inputReader;
            bool attackPressed = inputReader != null && inputReader.AttackPressed;
            bool grounded = movementController != null && movementController.IsGrounded;
            if (grounded)
            {
                if (!wasGroundedLastFrame)
                {
                    ReleaseComboHold(true);
                    ResetState();
                }
                wasGroundedLastFrame = true;
                idleTimer = 0f;
            }
            else
            {
                wasGroundedLastFrame = false;
            }

            bool hasDoubleJumped = jumpModule != null && jumpModule.HasDoubleJumpedThisAirborne;
            if (hasDoubleJumped && !airComboUsedThisAirborne && !doubleJumpArmed)
            {
                ArmFirstStage();
            }

            if (!doubleJumpArmed)
            {
                idleTimer = 0f;
                return;
            }

            UpdateComboTimers(dt);

            if (attackPressed && !stageInProgress)
            {
                if (nextStageArmed && comboDelayTimer <= 0f)
                {
                    TryStartStage(nextStageCandidate);
                }
                else if (firstAttackAvailable)
                {
                    TryStartStage(1);
                }
            }

            UpdateIdleTimer(dt);
        }

        private void UpdateComboTimers(float dt)
        {
            if (!nextStageArmed) return;
            if (comboDelayTimer > 0f) comboDelayTimer = Mathf.Max(0f, comboDelayTimer - dt);
            if (!stageInProgress && comboWindowTimer > 0f)
            {
                comboWindowTimer -= dt;
                if (comboWindowTimer <= 0f)
                {
                    Log($"Janela do JumpAttack{nextStageCandidate} expirou.");
                    FinishCombo();
                }
            }
        }

        private void ArmFirstStage()
        {
            doubleJumpArmed = true;
            firstAttackAvailable = true;
            stageInProgress = false;
            pendingStage = 0;
            activeStage = 0;
            idleTimer = 0f;
            nextStageArmed = false;
            nextStageCandidate = 0;
            comboWindowTimer = 0f;
            comboDelayTimer = 0f;
            Log("Combo aereo armado (JumpAttack1 disponivel).");
        }

        private bool TryStartStage(int stage)
        {
            if (combatController == null)
            {
                Log($"Sem combatController, cancelando JumpAttack{stage}.");
                FinishCombo(false);
                return false;
            }
            pendingStage = stage;
            combatController.OverrideNextStage(stage, true);
            if (!combatController.TryRequestAttackStage(out int resolvedStage, out bool isAir))
            {
                Log($"TryRequestAttackStage falhou para JumpAttack{stage}.");
                pendingStage = 0;
                return false;
            }
            Log($"TryStartStage({stage}) -> resolved={resolvedStage}, isAir={isAir}");
            if (!isAir) movementController?.ForceAirborneStateForCombo();
            if (resolvedStage != stage)
            {
                Log($"Estagio inesperado ao iniciar JumpAttack{stage}, cancelando.");
                combatController.CancelActiveAttackStage();
                FinishCombo();
                return false;
            }
            if (stage == 1)
            {
                firstAttackAvailable = false;
            }
            else
            {
                nextStageArmed = false;
                nextStageCandidate = 0;
                comboWindowTimer = 0f;
                comboDelayTimer = 0f;
            }
            return true;
        }

        private void HandleAttackStageStarted(int stage, bool isAir)
        {
            if (!doubleJumpArmed) return;
            if (!isAir || stage < 1 || stage > 3) return;
            stageInProgress = true;
            airComboUsedThisAirborne = true;
            activeStage = stage;
            pendingStage = 0;
            idleTimer = 0f;
            comboWindowTimer = 0f;
            comboDelayTimer = 0f;
            if (!holdAcquired)
            {
                combatController?.HoldComboReset();
                holdAcquired = combatController != null;
            }
            FireAnimatorTrigger(stage);
            ApplyImpulse();
            movementController?.ForceAirborneStateForCombo();
            Log($"JumpAttack{stage} iniciado.");
        }

        private void HandleAttackStageEnded(int stage, bool isAir)
        {
            if (!isAir || stage != activeStage) return;
            stageInProgress = false;
            activeStage = 0;
            idleTimer = 0f;
            Log($"JumpAttack{stage} finalizado.");
            switch (stage)
            {
                case 1:
                    PrepareNextStage(2, attack2MinDelaySeconds, attack2WindowSeconds);
                    TryPlayJumpFallVisual();
                    break;
                case 2:
                    PrepareNextStage(3, attack3MinDelaySeconds, attack3WindowSeconds);
                    TryPlayJumpFallVisual();
                    break;
                default:
                    FinishCombo();
                    break;
            }
        }

        private void PrepareNextStage(int stage, float delaySeconds, float windowSeconds)
        {
            if (delaySeconds < 0f) delaySeconds = 0f;
            if (windowSeconds <= 0f)
            {
                FinishCombo();
                return;
            }
            nextStageCandidate = stage;
            nextStageArmed = true;
            comboDelayTimer = delaySeconds;
            comboWindowTimer = windowSeconds;
            Log($"JumpAttack{stage} liberado apos delay {delaySeconds:F2}s por {windowSeconds:F2}s.");
        }

        private void FireAnimatorTrigger(int stage)
        {
            if (animatorController == null) return;
            string trigger = stage switch
            {
                1 => jumpAttack1Trigger,
                2 => jumpAttack2Trigger,
                3 => jumpAttack3Trigger,
                _ => string.Empty
            };
            if (string.IsNullOrWhiteSpace(trigger))
            {
                Log($"Trigger vazio para JumpAttack{stage}.");
                return;
            }
            if (!animatorController.HasTrigger(trigger))
            {
                Log($"Trigger {trigger} nao existe no Animator.");
            }
            animatorController.ResetTrigger(trigger);
            animatorController.SetTrigger(trigger);
        }

        private void ApplyImpulse()
        {
            if (movementController != null)
            {
                Vector3 facing = movementController.FacingDirection;
                if (forwardImpulsePerHit > 0f && facing.sqrMagnitude > 0.0001f)
                {
                    Vector3 planar = facing.normalized; planar.y = 0f;
                    if (planar.sqrMagnitude < 0.0001f) planar = Vector3.right;
                    movementController.AddExternalPush(planar * forwardImpulsePerHit);
                }
                if (verticalImpulsePerHit > 0f)
                {
                    float target = GetCurrentVerticalVelocity() + verticalImpulsePerHit;
                    movementController.BumpVertical(target);
                }
            }
            else if (body != null)
            {
                Vector3 velocity = body.linearVelocity;
                velocity.y += verticalImpulsePerHit;
                velocity.x += Mathf.Sign(transform.right.x) * forwardImpulsePerHit;
                body.linearVelocity = velocity;
            }
        }

        private float GetCurrentVerticalVelocity()
        {
            return body != null ? body.linearVelocity.y : 0f;
        }

        private void FinishCombo(bool playJumpFallVisual = true)
        {
            doubleJumpArmed = false;
            firstAttackAvailable = false;
            stageInProgress = false;
            pendingStage = 0;
            activeStage = 0;
            nextStageArmed = false;
            nextStageCandidate = 0;
            ReleaseComboHold(false);
            idleTimer = 0f;
            airComboUsedThisAirborne = true;
            comboWindowTimer = 0f;
            comboDelayTimer = 0f;
            if (playJumpFallVisual) TryPlayJumpFallVisual();
        }

        private void ReleaseComboHold(bool resetTimer)
        {
            if (!holdAcquired) return;
            combatController?.ReleaseComboReset(resetTimer);
            holdAcquired = false;
        }

        private void ResetState()
        {
            doubleJumpArmed = false;
            firstAttackAvailable = false;
            stageInProgress = false;
            pendingStage = 0;
            activeStage = 0;
            holdAcquired = false;
            idleTimer = 0f;
            airComboUsedThisAirborne = false;
            nextStageArmed = false;
            nextStageCandidate = 0;
            comboWindowTimer = 0f;
            comboDelayTimer = 0f;
        }

        private void UpdateIdleTimer(float dt)
        {
            if (!doubleJumpArmed) { idleTimer = 0f; return; }
            if (stageInProgress) { idleTimer = 0f; return; }
            if (idleToJumpFallSeconds <= 0f) return;
            idleTimer += dt;
            if (idleTimer >= idleToJumpFallSeconds) ForceJumpFall();
        }

        private void ForceJumpFall()
        {
            Log("ForceJumpFall");
            idleTimer = 0f;
            FinishCombo();
        }

        private void RebuildAnimatorCaches()
        {
            if (!string.IsNullOrWhiteSpace(jumpFallStateName)) jumpFallStateHash = Animator.StringToHash(jumpFallStateName);
        }

        private bool EnsureReferences()
        {
            if (!context) context = GetComponent<NitssCharacterContext>();
            if (!animatorController && context) animatorController = context.AnimatorController;
            if (!movementController) movementController = GetComponent<NitssMovementController>();
            if (!combatController) combatController = GetComponent<NitssCombatController>();
            if (!inputReader && context) inputReader = context.InputReader;
            if (!jumpModule) jumpModule = GetComponent<NitssJumpModule>();
            if (!body && context) body = context.Body;
            if (!animator && context) animator = context.Animator;
            if (jumpFallStateHash == 0 && !string.IsNullOrWhiteSpace(jumpFallStateName)) jumpFallStateHash = Animator.StringToHash(jumpFallStateName);
            return combatController != null && movementController != null && jumpModule != null;
        }

        private void TryPlayJumpFallVisual()
        {
            if (movementController != null && movementController.IsGrounded) return;
            if (!animator) animator = context ? context.Animator : GetComponentInChildren<Animator>();
            if (animator != null && jumpFallStateHash == 0 && !string.IsNullOrWhiteSpace(jumpFallStateName)) jumpFallStateHash = Animator.StringToHash(jumpFallStateName);
            if (animator != null && jumpFallStateHash != 0) animator.CrossFadeInFixedTime(jumpFallStateHash, Mathf.Max(0f, jumpFallCrossfade), Mathf.Max(0, jumpFallLayerIndex), 0f);
            animatorController?.SetJumping(true);
        }

        private void Log(string message)
        {
            if (!debugLogging) return;
            Debug.Log($"[NitssJumpAttackModule] {message}", this);
        }
    }
}
