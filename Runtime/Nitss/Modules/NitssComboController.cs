using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Controlador unificado de combos terrestres: Attack1 > Attack2 > Attack3/Uppercut.
    /// Gerencia input buffering, timing e transições de combo.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssComboController : MonoBehaviour
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
        [SerializeField] private string uppercutTrigger = "Uppercut";

        [Header("Tempos de Combo")]
        [SerializeField, Tooltip("Delay inicial para Attack1 (s).")]
        private float initialAttackDelay = 0.06f;
        [SerializeField, Tooltip("Delay entre ataques no combo (s).")]
        private float comboChainDelay = 0.05f;
        [SerializeField, Tooltip("Janela de input para o próximo ataque (s).")]
        private float comboInputWindow = 0.4f;

        [Header("Uppercut")]
        [SerializeField, Tooltip("Input vertical mínimo para uppercut.")]
        private float minVerticalInput = 0.5f;
        [SerializeField, Tooltip("Tempo de buffer para direção + ataque (s).")]
        private float uppercutBufferWindow = 0.3f;
        [SerializeField, Tooltip("Stage lógico do uppercut.")]
        private int uppercutStage = 4;
        [SerializeField, Tooltip("Cooldown entre uppercuts (s).")]
        private float uppercutCooldown = 0.2f;
        [SerializeField, Tooltip("Exige estar no chão para uppercut.")]
        private bool uppercutRequireGrounded = true;
        [SerializeField, Tooltip("Tempo de coyote após sair do chão (s).")]
        private float groundGraceSeconds = 0.12f;
        
        [Header("Uppercut Launch")]
        [SerializeField, Tooltip("Velocidade vertical do lançamento do uppercut (m/s).")]
        private float uppercutLaunchVelocity = 12f;
        [SerializeField, Tooltip("Altura máxima do lançamento do uppercut (m).")]
        private float uppercutMaxHeight = 7f;
        [SerializeField, Tooltip("Delay antes de aplicar o lançamento vertical (s). Use para sincronizar com o momento do golpe na animação.")]
        private float uppercutLaunchDelay = 0.3f;

        [Header("Regras")]
        [SerializeField, Tooltip("Permite iniciar combo no ar.")]
        private bool allowAirStart = true;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = false;

        // Estado do combo
        private int trackedStage;
        private bool comboActive;
        private float comboWindowTimer;
        
        // Queue de ataque
        private bool queuedRequest;
        private bool queuedFromCombo;
        private float queuedTimer;
        
        // Uppercut
        private float attackBufferTimer;
        private float upBufferTimer;
        private float groundGraceTimer;
        private float uppercutCooldownTimer;
        
        // Controles
        private bool wasAirborneLastFrame;
        private int landingCooldownFrames;
        
        // Propriedades públicas
        public float UppercutLaunchVelocity => uppercutLaunchVelocity;
        public float UppercutMaxHeight => uppercutMaxHeight;
        public float UppercutLaunchDelay => uppercutLaunchDelay;

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
            ResetState();
        }

        private void ResetState()
        {
            queuedRequest = false;
            queuedFromCombo = false;
            queuedTimer = 0f;
            comboWindowTimer = 0f;
            trackedStage = 0;
            comboActive = false;
            landingCooldownFrames = 0;
            wasAirborneLastFrame = false;
            attackBufferTimer = 0f;
            upBufferTimer = 0f;
            uppercutCooldownTimer = 0f;
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
                return;

            inputReader = reader ?? inputReader;
            UpdateTimers(dt);
            HandleLanding();

            // Não processa nada se JumpAttack ativo ou em cooldown de pouso
            if (IsBlocked())
                return;

            ProcessInput();
            ProcessQueue(dt);
            ProcessComboWindow(dt);
        }

        private void UpdateTimers(float dt)
        {
            if (queuedTimer > 0f)
                queuedTimer = Mathf.Max(0f, queuedTimer - dt);

            if (comboWindowTimer > 0f)
                comboWindowTimer = Mathf.Max(0f, comboWindowTimer - dt);

            if (attackBufferTimer > 0f)
                attackBufferTimer = Mathf.Max(0f, attackBufferTimer - dt);

            if (upBufferTimer > 0f)
                upBufferTimer = Mathf.Max(0f, upBufferTimer - dt);

            if (uppercutCooldownTimer > 0f)
                uppercutCooldownTimer = Mathf.Max(0f, uppercutCooldownTimer - dt);

            // Ground grace timer
            if (movementController != null)
            {
                if (movementController.IsGrounded)
                    groundGraceTimer = groundGraceSeconds;
                else if (groundGraceTimer > 0f)
                    groundGraceTimer = Mathf.Max(0f, groundGraceTimer - dt);
            }
        }

        private void HandleLanding()
        {
            bool isGrounded = movementController != null && movementController.IsGrounded;

            if (isGrounded && wasAirborneLastFrame)
            {
                if (enableDebugLogs)
                    Debug.Log($"[NitssCombo] LANDING - Resetando combo");

                // Reset completo ao pousar
                if (combatController != null)
                {
                    for (int i = 0; i < 5; i++)
                        combatController.ReleaseComboReset(true);

                    if (combatController.CurrentStage > 0)
                        combatController.CancelActiveAttackStage(false);
                }

                comboActive = false;
                trackedStage = 0;
                comboWindowTimer = 0f;
                queuedRequest = false;
                queuedFromCombo = false;
                queuedTimer = 0f;
                landingCooldownFrames = 10; // ~0.16s de cooldown
            }

            wasAirborneLastFrame = !isGrounded;

            // Decrementa cooldown apenas quando no chão
            if (landingCooldownFrames > 0 && isGrounded)
                landingCooldownFrames--;
        }

        private bool IsBlocked()
        {
            if (jumpAttackModule != null && jumpAttackModule.IsAirComboActive)
                return true;

            if (landingCooldownFrames > 0)
                return true;

            return false;
        }

        private void ProcessInput()
        {
            if (inputReader == null)
                return;

            // Buffer de uppercut (direção + ataque)
            if (inputReader.Current.Move.y >= minVerticalInput)
                upBufferTimer = Mathf.Max(upBufferTimer, uppercutBufferWindow);

            if (inputReader.AttackPressed)
            {
                // Se tem direção para cima OU buffer ativo, tenta uppercut
                bool wantsUppercut = inputReader.Current.Move.y >= minVerticalInput || upBufferTimer > 0f;
                
                if (wantsUppercut)
                {
                    // Uppercut direto (não precisa estar no combo) ou após Attack2
                    attackBufferTimer = Mathf.Max(attackBufferTimer, uppercutBufferWindow);
                }
                else
                {
                    // Ataque normal
                    QueueAttack(fromCombo: comboActive && trackedStage > 0);
                }
            }

            // Tenta uppercut se ambos buffers ativos
            if (attackBufferTimer > 0f && upBufferTimer > 0f)
            {
                TryStartUppercut();
            }
        }

        private void QueueAttack(bool fromCombo)
        {
            if (!CanStartCombo())
                return;

            queuedRequest = true;
            queuedFromCombo = fromCombo;
            queuedTimer = fromCombo ? comboChainDelay : initialAttackDelay;

            if (enableDebugLogs)
                Debug.Log($"[NitssCombo] Ataque enfileirado - fromCombo: {fromCombo}, delay: {queuedTimer:F2}s");
        }

        private bool CanStartCombo()
        {
            if (combatController != null && combatController.IsKnockedDown)
                return false;

            if (combatController != null && combatController.IsStaggered)
                return false;

            if (crouchModule != null && crouchModule.IsCrouching)
                return false;

            if (dashModule != null && dashModule.IsDashing)
                return false;

            bool isGrounded = movementController != null && movementController.IsGrounded;
            bool canJump = jumpModule != null && jumpModule.CanPerformDoubleJump;

            if (!isGrounded && !allowAirStart)
                return false;

            if (!isGrounded && !canJump)
                return false;

            return true;
        }

        private void ProcessQueue(float dt)
        {
            if (!queuedRequest)
                return;

            if (queuedTimer > 0f)
                return;

            // Executa ataque enfileirado
            if (combatController != null && combatController.TryRequestAttackStage(out int stage, out bool isAir))
            {
                string trigger = GetTriggerForStage(stage);
                if (!string.IsNullOrEmpty(trigger) && animatorController != null)
                {
                    animatorController.SetTrigger(trigger);

                    if (enableDebugLogs)
                        Debug.Log($"[NitssCombo] Trigger disparado: {trigger}, Stage: {stage}, Air: {isAir}");
                }
            }

            queuedRequest = false;
            queuedFromCombo = false;
        }

        private void ProcessComboWindow(float dt)
        {
            if (comboWindowTimer <= 0f && comboActive)
            {
                comboActive = false;
                trackedStage = 0;

                if (enableDebugLogs)
                    Debug.Log("[NitssCombo] Janela de combo expirou");
            }
        }

        private void TryStartUppercut()
        {
            if (uppercutCooldownTimer > 0f)
            {
                if (enableDebugLogs)
                    Debug.Log("[NitssCombo] Uppercut em cooldown");
                return;
            }

            if (!CanStartUppercut())
            {
                if (enableDebugLogs)
                    Debug.Log("[NitssCombo] Não pode fazer uppercut agora");
                return;
            }

            if (combatController != null)
            {
                // Force stage 4 (uppercut)
                // fromCombo = true se trackedStage == 2 (após Attack2)
                bool isFromCombo = trackedStage == 2;
                combatController.OverrideNextStage(uppercutStage, false, isFromCombo);

                if (combatController.TryRequestAttackStage(out int stage, out bool isAir))
                {
                    if (animatorController != null)
                    {
                        animatorController.SetTrigger(uppercutTrigger);

                        if (enableDebugLogs)
                            Debug.Log($"[NitssCombo] Uppercut! Stage: {stage}, FromCombo: {isFromCombo}");
                    }

                    uppercutCooldownTimer = uppercutCooldown;
                    attackBufferTimer = 0f;
                    upBufferTimer = 0f;
                    comboActive = false;
                    trackedStage = 0;
                }
            }
        }

        private bool CanStartUppercut()
        {
            // Permite uppercut direto (trackedStage == 0) ou após Attack2 (trackedStage == 2)
            bool inValidComboState = trackedStage == 0 || trackedStage == 2;
            if (!inValidComboState)
                return false;

            if (combatController != null && combatController.IsKnockedDown)
                return false;

            if (combatController != null && combatController.IsStaggered)
                return false;

            if (crouchModule != null && crouchModule.IsCrouching)
                return false;

            if (dashModule != null && dashModule.IsDashing)
                return false;

            if (uppercutRequireGrounded)
            {
                bool hasGround = groundGraceTimer > 0f;
                if (!hasGround)
                    return false;
            }

            return true;
        }

        private string GetTriggerForStage(int stage)
        {
            return stage switch
            {
                1 => attack1Trigger,
                2 => attack2Trigger,
                3 => attack3Trigger,
                4 => uppercutTrigger,
                _ => null
            };
        }

        private void HandleAttackStageStarted(int stage, bool isAir)
        {
            trackedStage = stage;
            comboActive = true;
            comboWindowTimer = comboInputWindow;

            if (enableDebugLogs)
                Debug.Log($"[NitssCombo] Stage {stage} iniciado (Air: {isAir})");
        }

        private void HandleAttackStageEnded(int stage, bool isAir)
        {
            if (stage != trackedStage)
                return;

            if (enableDebugLogs)
                Debug.Log($"[NitssCombo] Stage {stage} finalizado");

            // Não reseta trackedStage aqui - deixa a janela de combo decidir
        }

        private bool EnsureReferences()
        {
            if (context == null) context = GetComponent<NitssCharacterContext>();
            if (animatorController == null && context != null)
                animatorController = context.AnimatorController;
            if (movementController == null) movementController = GetComponent<NitssMovementController>();
            if (combatController == null) combatController = GetComponent<NitssCombatController>();
            if (inputReader == null) inputReader = GetComponent<NitssInputReader>();
            if (jumpModule == null) jumpModule = GetComponent<NitssJumpModule>();
            if (jumpAttackModule == null) jumpAttackModule = GetComponent<NitssJumpAttackModule>();
            if (dashModule == null) dashModule = GetComponent<NitssDashModule>();
            if (crouchModule == null) crouchModule = GetComponent<NitssCrouchModule>();

            return context != null && combatController != null && movementController != null;
        }
    }
}
