using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Coordena o combo básico de ataques terrestres (Attack1/2/3). Lê o input, respeita restrições
    /// de salto e dispara os triggers corretos no Animator enquanto sincroniza com o NitssCombatController.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssGroundAttackModule : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssAnimatorController animatorController;
        [SerializeField] private NitssMovementController movementController;
        [SerializeField] private NitssCombatController combatController;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssJumpModule jumpModule;
        [SerializeField] private NitssDashModule dashModule;

        [Header("Triggers de Animação")]
        [SerializeField] private string attack1Trigger = "Attack1";
        [SerializeField] private string attack2Trigger = "Attack2";
        [SerializeField] private string attack3Trigger = "Attack3";

        [Header("Tempos de Combo")]
        [SerializeField, Tooltip("Delay entre o input e o disparo da animação do primeiro ataque (s).")]
        private float initialAttackDelay = 0.06f;
        [SerializeField, Tooltip("Delay aplicado antes dos ataques subsequentes quando o combo prossegue (s).")]
        private float comboChainDelay = 0.05f;
        [SerializeField, Tooltip("Janela para aceitar o próximo input do combo após cada estágio (s).")]
        private float comboInputWindow = 0.4f;

        [Header("Regras")]
        [SerializeField, Tooltip("Permite iniciar Attack1 no ar contanto que o double jump ainda não tenha sido usado.")]
        private bool allowAirStart = true;

        private bool queuedRequest;
        private bool queuedFromCombo;
        private float queuedTimer;
        private float comboWindowTimer;
        private int trackedStage;
        private bool comboActive;
        private bool dashCancelHandled;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
            movementController = GetComponent<NitssMovementController>();
            combatController = GetComponent<NitssCombatController>();
            inputReader = GetComponent<NitssInputReader>();
            jumpModule = GetComponent<NitssJumpModule>();
            dashModule = GetComponent<NitssDashModule>();
        }

        private void Awake()
        {
            EnsureReferences();
            if (combatController != null)
            {
                combatController.AttackStageStarted += HandleAttackStageStarted;
                combatController.AttackStageEnded += HandleAttackStageEnded;
            }
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
            queuedRequest = false;
            queuedFromCombo = false;
            queuedTimer = 0f;
            comboWindowTimer = 0f;
            trackedStage = 0;
            comboActive = false;
            dashCancelHandled = false;
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
            {
                return;
            }

            inputReader = reader ?? inputReader;

            UpdateDashCancelState();

            if (comboWindowTimer > 0f)
            {
                comboWindowTimer = Mathf.Max(0f, comboWindowTimer - dt);
            }

            if (queuedRequest)
            {
                queuedTimer -= dt;
                if (queuedTimer <= 0f)
                {
                    queuedRequest = false;
                    TryExecuteStageRequest();
                }
            }

            if (inputReader != null && inputReader.AttackPressed)
            {
                HandleAttackPressed();
            }
        }

        private void HandleAttackPressed()
        {
            if (!comboActive)
            {
                if (!CanStartInitialAttack())
                {
                    return;
                }
                QueueStage(initialAttackDelay, false);
                return;
            }

            if (trackedStage >= 3)
            {
                return;
            }

            if (comboWindowTimer <= 0f)
            {
                return;
            }

            QueueStage(comboChainDelay, true);
        }

        private void QueueStage(float delay, bool fromCombo)
        {
            queuedRequest = true;
            queuedFromCombo = fromCombo;
            queuedTimer = Mathf.Max(0f, delay);
        }

        private bool CanStartInitialAttack()
        {
            if (combatController == null)
            {
                return false;
            }

            if (combatController.IsKnockedDown || combatController.IsStaggered)
            {
                return false;
            }

            if (!allowAirStart)
            {
                return movementController == null || movementController.IsGrounded;
            }

            if (movementController != null && !movementController.IsGrounded)
            {
                bool hasDoubleJumped = false;
                if (jumpModule != null)
                {
                    hasDoubleJumped = jumpModule.HasDoubleJumpedThisAirborne;
                }
                else
                {
                    hasDoubleJumped = movementController.DidDoubleJumpThisAirborne;
                }

                if (hasDoubleJumped)
                {
                    return false;
                }
            }

            return true;
        }

        private void TryExecuteStageRequest()
        {
            if (combatController == null)
            {
                queuedFromCombo = false;
                return;
            }

            if (!queuedFromCombo && !CanStartInitialAttack())
            {
                queuedFromCombo = false;
                return;
            }

            if (!combatController.TryRequestAttackStage(out var stage, out _))
            {
                queuedFromCombo = false;
                return;
            }

            FireAnimatorTrigger(stage);
            comboActive = true;
            trackedStage = stage;
            comboWindowTimer = stage < 3 ? comboInputWindow : 0f;
            queuedFromCombo = false;
        }

        private void FireAnimatorTrigger(int stage)
        {
            if (animatorController == null)
            {
                return;
            }

            string triggerName = stage switch
            {
                1 => attack1Trigger,
                2 => attack2Trigger,
                3 => attack3Trigger,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(triggerName))
            {
                return;
            }

            animatorController.ResetTrigger(triggerName);
            animatorController.SetTrigger(triggerName);
        }

        private void HandleAttackStageStarted(int stage, bool isAir)
        {
            comboActive = true;
            trackedStage = stage;
            comboWindowTimer = stage < 3 ? comboInputWindow : 0f;
        }

        private void HandleAttackStageEnded(int stage, bool isAir)
        {
            if (stage != trackedStage)
            {
                return;
            }

            comboActive = false;
            trackedStage = 0;
            comboWindowTimer = 0f;
            queuedRequest = false;
            queuedFromCombo = false;
            queuedTimer = 0f;
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
            if (!jumpModule)
            {
                jumpModule = GetComponent<NitssJumpModule>();
            }
            if (!dashModule)
            {
                dashModule = GetComponent<NitssDashModule>();
            }
            return combatController != null;
        }

        private void UpdateDashCancelState()
        {
            if (!dashModule)
            {
                dashCancelHandled = false;
                return;
            }

            if (dashModule.IsDashing)
            {
                if (!dashCancelHandled)
                {
                    ForceCancelCombo();
                    dashCancelHandled = true;
                }
            }
            else
            {
                dashCancelHandled = false;
            }
        }

        public void ForceCancelCombo()
        {
            if (combatController != null)
            {
                combatController.CancelActiveAttackStage();
            }

            comboActive = false;
            trackedStage = 0;
            comboWindowTimer = 0f;
            queuedRequest = false;
            queuedFromCombo = false;
            queuedTimer = 0f;
            dashCancelHandled = dashModule != null && dashModule.IsDashing;
        }
    }
}
