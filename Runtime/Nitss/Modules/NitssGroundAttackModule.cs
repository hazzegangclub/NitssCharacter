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
        [SerializeField] private NitssJumpAttackModule jumpAttackModule;
        [SerializeField] private NitssDashModule dashModule;
        [SerializeField] private NitssCrouchModule crouchModule;

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
        private bool wasAirborneLastFrame;
        private int landingCooldownFrames;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
            movementController = GetComponent<NitssMovementController>();
            combatController = GetComponent<NitssCombatController>();
            inputReader = GetComponent<NitssInputReader>();
            jumpModule = GetComponent<NitssJumpModule>();
            jumpAttackModule = GetComponent<NitssJumpAttackModule>();
            dashModule = GetComponent<NitssDashModule>();
            crouchModule = GetComponent<NitssCrouchModule>();
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
            landingCooldownFrames = 0;
            wasAirborneLastFrame = false;
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
            {
                return;
            }

            inputReader = reader ?? inputReader;

            // Detecta quando acabou de pousar (transição ar → chão)
            bool isGrounded = movementController != null && movementController.IsGrounded;
            
            // Se JumpAttackModule está ativo, não processa NADA (nem landing)
            if (jumpAttackModule != null && jumpAttackModule.IsAirComboActive)
            {
                wasAirborneLastFrame = !isGrounded;
                return;
            }
            
            if (isGrounded && wasAirborneLastFrame && combatController != null)
            {
                Debug.Log($"[GroundAttack] LANDING - Stage antes: {combatController.CurrentStage}, IsComboResetHeld: {combatController.IsComboResetHeld}");
                
                // Força reset completo do combo ao pousar após estar no ar
                // Libera o hold múltiplas vezes para garantir
                for (int i = 0; i < 5; i++)
                {
                    combatController.ReleaseComboReset(true);
                }
                
                Debug.Log($"[GroundAttack] Após ReleaseComboReset - Stage: {combatController.CurrentStage}, IsComboResetHeld: {combatController.IsComboResetHeld}");
                
                if (combatController.CurrentStage > 0)
                {
                    Debug.Log($"[GroundAttack] Cancelando stage ativo: {combatController.CurrentStage}");
                    combatController.CancelActiveAttackStage(false);
                }
                comboActive = false;
                trackedStage = 0;
                comboWindowTimer = 0f;
                queuedRequest = false;
                queuedFromCombo = false;
                queuedTimer = 0f;
                landingCooldownFrames = 10; // Bloqueia ataques por 10 frames (~0.16s) após pousar
            }
            wasAirborneLastFrame = !isGrounded;
            
            // Cooldown após pousar - só aplica quando está NO CHÃO
            if (landingCooldownFrames > 0)
            {
                if (isGrounded)
                {
                    landingCooldownFrames--;
                    return; // Não processa ataques durante cooldown
                }
                else
                {
                    // Se voltou ao ar, limpa o cooldown
                    landingCooldownFrames = 0;
                }
            }

            UpdateDashCancelState();

            // Se o combatController não está em ataque mas nosso módulo acha que está, reseta
            if (comboActive && combatController != null && combatController.CurrentStage == 0)
            {
                comboActive = false;
                trackedStage = 0;
                comboWindowTimer = 0f;
                queuedRequest = false;
                queuedFromCombo = false;
                queuedTimer = 0f;
            }

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
            // Se estiver agachado, deixa o CrouchModule lidar com o ataque
            if (crouchModule != null && crouchModule.IsCrouching)
            {
                return;
            }

            bool isGrounded = movementController != null && movementController.IsGrounded;
            
            // Se JumpAttackModule está ativo (armado ou em combo), não processa NADA
            if (jumpAttackModule != null && jumpAttackModule.IsAirComboActive)
            {
                return;
            }
            
            // Se está no ar após double jump, JumpAttackModule deve processar
            if (!isGrounded && jumpModule != null && jumpModule.HasDoubleJumpedThisAirborne)
            {
                return;
            }
            
            Debug.Log($"[GroundAttack] HandleAttackPressed - isGrounded: {isGrounded}, comboActive: {comboActive}, trackedStage: {trackedStage}, CurrentStage: {combatController?.CurrentStage}");

            if (!comboActive)
            {
                if (!CanStartInitialAttack())
                {
                    return;
                }
                Debug.Log($"[GroundAttack] QueueStage inicial");
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

            Debug.Log($"[GroundAttack] TryExecuteStageRequest - Antes TryRequestAttackStage, CurrentStage: {combatController.CurrentStage}");
            
            if (!combatController.TryRequestAttackStage(out var stage, out _))
            {
                Debug.Log($"[GroundAttack] TryRequestAttackStage FALHOU");
                queuedFromCombo = false;
                return;
            }

            Debug.Log($"[GroundAttack] TryRequestAttackStage SUCESSO - stage retornado: {stage}, disparando Attack{stage}");
            
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
            if (!jumpAttackModule)
            {
                jumpAttackModule = GetComponent<NitssJumpAttackModule>();
            }
            if (!dashModule)
            {
                dashModule = GetComponent<NitssDashModule>();
            }
            if (!crouchModule)
            {
                crouchModule = GetComponent<NitssCrouchModule>();
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
