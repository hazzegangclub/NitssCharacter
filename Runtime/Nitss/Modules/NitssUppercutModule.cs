using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Trata o comando especial de uppercut (cima + ataque) separadamente do combo terrestre padrão.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssUppercutModule : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssAnimatorController animatorController;
        [SerializeField] private NitssMovementController movementController;
        [SerializeField] private NitssCombatController combatController;
        [SerializeField] private NitssInputReader inputReader;

        [Header("Entrada")]
        [SerializeField, Tooltip("Intensidade mínima no eixo vertical (Y) para considerar comando para cima.")]
        private float minVerticalInput = 0.5f;
        [SerializeField, Tooltip("Janela (s) para manter o ataque em buffer aguardando o direcional para cima.")]
        private float attackBufferWindow = 0.3f;
        [SerializeField, Tooltip("Janela (s) para manter o direcional para cima em buffer aguardando o ataque.")]
        private float upInputBufferWindow = 0.3f;
        [SerializeField, Tooltip("Tempo (s) de coyote-time após sair do chão para ainda permitir uppercut.")]
        private float groundGraceSeconds = 0.12f;

        [Header("Execução")]
        [SerializeField, Tooltip("Nome do trigger de animação usado para o uppercut.")]
        private string uppercutTrigger = "Uppercut";
        [SerializeField, Tooltip("Stage lógico usado pelo NitssCombatController para o uppercut.")]
        private int uppercutStage = 4;
        [SerializeField, Tooltip("Delay (s) entre o início do estágio e o disparo do trigger de animação.")]
        private float activationDelay = 0.05f;
        [SerializeField, Tooltip("Cooldown mínimo (s) entre execuções consecutivas do uppercut.")]
        private float cooldownSeconds = 0.2f;
        [SerializeField, Tooltip("Exige estar no chão (considerando coyote) para iniciar o uppercut.")]
        private bool requireGrounded = true;

        private float attackBufferTimer;
        private float upBufferTimer;
        private float groundGraceTimer;
        private float cooldownTimer;
        private float triggerDelayTimer;
        private bool triggerArmed;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
            movementController = GetComponent<NitssMovementController>();
            combatController = GetComponent<NitssCombatController>();
            inputReader = GetComponent<NitssInputReader>();
        }

        private void Awake()
        {
            EnsureReferences();
            if (combatController != null)
            {
                combatController.AttackStageStarted += OnAttackStageStarted;
                combatController.AttackStageEnded += OnAttackStageEnded;
            }
        }

        private void OnDestroy()
        {
            if (combatController != null)
            {
                combatController.AttackStageStarted -= OnAttackStageStarted;
                combatController.AttackStageEnded -= OnAttackStageEnded;
            }
        }

        private void OnDisable()
        {
            attackBufferTimer = 0f;
            upBufferTimer = 0f;
            groundGraceTimer = 0f;
            cooldownTimer = 0f;
            triggerDelayTimer = 0f;
            triggerArmed = false;
        }

        public void Tick(float dt, NitssInputReader externalReader)
        {
            if (!EnsureReferences())
            {
                return;
            }

            inputReader = externalReader ?? inputReader;
            UpdateTimers(dt);

            if (inputReader == null)
            {
                return;
            }

            if (inputReader.Current.Move.y >= minVerticalInput)
            {
                upBufferTimer = Mathf.Max(upBufferTimer, upInputBufferWindow);
            }

            if (inputReader.AttackPressed)
            {
                if (inputReader.Current.Move.y >= minVerticalInput || upBufferTimer > 0f)
                {
                    attackBufferTimer = Mathf.Max(attackBufferTimer, attackBufferWindow);
                }
                else
                {
                    attackBufferTimer = 0f;
                }
            }

            if (attackBufferTimer > 0f && upBufferTimer > 0f)
            {
                TryStartUppercut();
            }
        }

        private void UpdateTimers(float dt)
        {
            if (attackBufferTimer > 0f)
            {
                attackBufferTimer = Mathf.Max(0f, attackBufferTimer - dt);
            }

            if (upBufferTimer > 0f)
            {
                upBufferTimer = Mathf.Max(0f, upBufferTimer - dt);
            }

            if (cooldownTimer > 0f)
            {
                cooldownTimer = Mathf.Max(0f, cooldownTimer - dt);
            }

            if (movementController != null)
            {
                if (movementController.IsGrounded)
                {
                    groundGraceTimer = groundGraceSeconds;
                }
                else if (groundGraceTimer > 0f)
                {
                    groundGraceTimer = Mathf.Max(0f, groundGraceTimer - dt);
                }
            }

            if (triggerArmed)
            {
                triggerDelayTimer -= dt;
                if (triggerDelayTimer <= 0f)
                {
                    triggerArmed = false;
                    FireAnimatorTrigger();
                }
            }
        }

        private void TryStartUppercut()
        {
            if (cooldownTimer > 0f)
            {
                return;
            }

            if (!CanExecuteUppercut())
            {
                return;
            }

            attackBufferTimer = 0f;
            upBufferTimer = 0f;

            if (combatController.IsAttacking)
            {
                combatController.CancelActiveAttackStage(false);
            }

            combatController.OverrideNextStage(uppercutStage, false);
            if (!combatController.TryRequestAttackStage(out var stage, out _))
            {
                attackBufferTimer = 0.05f;
                upBufferTimer = Mathf.Max(upBufferTimer, 0.05f);
                return;
            }

            if (stage != uppercutStage)
            {
                combatController.CancelActiveAttackStage();
            }
        }

        private bool CanExecuteUppercut()
        {
            if (combatController == null)
            {
                return false;
            }

            if (combatController.IsKnockedDown || combatController.IsStaggered)
            {
                return false;
            }

            if (requireGrounded && movementController != null)
            {
                if (!movementController.IsGrounded && groundGraceTimer <= 0f)
                {
                    return false;
                }
            }

            return true;
        }

        private void OnAttackStageStarted(int stage, bool isAir)
        {
            if (stage != uppercutStage)
            {
                attackBufferTimer = 0f;
                upBufferTimer = 0f;
                groundGraceTimer = 0f;
                return;
            }

            cooldownTimer = Mathf.Max(cooldownTimer, cooldownSeconds);

            if (activationDelay > 0f)
            {
                triggerArmed = true;
                triggerDelayTimer = activationDelay;
            }
            else
            {
                triggerArmed = false;
                FireAnimatorTrigger();
            }
        }

        private void OnAttackStageEnded(int stage, bool isAir)
        {
            if (stage != uppercutStage)
            {
                return;
            }

            triggerArmed = false;
            triggerDelayTimer = 0f;
        }

        private void FireAnimatorTrigger()
        {
            if (animatorController == null || string.IsNullOrEmpty(uppercutTrigger))
            {
                return;
            }

            if (!animatorController.HasTrigger(uppercutTrigger))
            {
                return;
            }

            animatorController.ResetTrigger(uppercutTrigger);
            animatorController.SetTrigger(uppercutTrigger);
        }

        private bool EnsureReferences()
        {
            if (!context)
            {
                context = GetComponent<NitssCharacterContext>();
            }

            if (!animatorController && context)
            {
                animatorController = context.AnimatorController;
            }

            if (!movementController)
            {
                movementController = GetComponent<NitssMovementController>();
            }

            if (!combatController)
            {
                combatController = GetComponent<NitssCombatController>();
            }

            if (!inputReader && context)
            {
                inputReader = context.InputReader;
            }

            return combatController != null;
        }
    }
}
