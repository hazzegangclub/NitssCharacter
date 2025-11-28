using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Gerencia combo aéreo (JumpAttack1→2→3) após double jump.
    /// Funciona independente, disparando triggers diretamente.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NitssJumpAttackModule : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssAnimatorController animatorController;
        [SerializeField] private NitssMovementController movementController;
        [SerializeField] private NitssCombatController combatController;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssJumpModule jumpModule;
        [SerializeField] private Rigidbody body;

        [Header("Triggers de Animação")]
        [SerializeField] private string jumpAttack1Trigger = "JumpAttack1";
        [SerializeField] private string jumpAttack2Trigger = "JumpAttack2";
        [SerializeField] private string jumpAttack3Trigger = "JumpAttack3";

        [Header("Tempos")]
        [SerializeField, Tooltip("Janela para aceitar próximo input após cada ataque")]
        private float comboWindow = 0.5f;
        [SerializeField, Tooltip("Delay antes de disparar o primeiro ataque")]
        private float initialDelay = 0.05f;
        [SerializeField, Tooltip("Delay antes dos ataques subsequentes")]
        private float comboDelay = 0.05f;

        [Header("Impulsos")]
        [SerializeField] private float verticalImpulsePerHit = 2.2f;
        [SerializeField] private float forwardImpulsePerHit = 1.4f;

        [Header("Debug")]
        [SerializeField] private bool debugLogging = true;

        private bool armed;
        private int currentStage;
        private bool comboActive;
        private float comboWindowTimer;
        private bool queuedInput;
        private float queuedTimer;
        private bool usedThisAirborne;
        private bool wasGroundedLastFrame;

        public bool IsAirComboActive => armed || comboActive;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
            movementController = GetComponent<NitssMovementController>();
            combatController = GetComponent<NitssCombatController>();
            inputReader = GetComponent<NitssInputReader>();
            jumpModule = GetComponent<NitssJumpModule>();
            body = GetComponent<Rigidbody>();
        }

        private void OnDisable()
        {
            ResetState();
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
            {
                return;
            }
            if (reader != null)
            {
                inputReader = reader;
            }

            bool grounded = movementController.IsGrounded;

            if (grounded)
            {
                if (!wasGroundedLastFrame)
                {
                    ResetState();
                }
                wasGroundedLastFrame = true;
                return;
            }
            wasGroundedLastFrame = false;

            // Arma após double jump
            if (!armed && !usedThisAirborne && jumpModule.HasDoubleJumpedThisAirborne)
            {
                ArmCombo();
            }

            if (!armed)
            {
                return;
            }

            // Update timers
            if (comboWindowTimer > 0f)
            {
                comboWindowTimer -= dt;
            }

            if (queuedInput && queuedTimer > 0f)
            {
                queuedTimer -= dt;
                if (queuedTimer <= 0f)
                {
                    ExecuteQueuedAttack();
                }
            }

            // Handle input
            bool attackPressed = inputReader.AttackPressed;
            if (attackPressed)
            {
                HandleAttackInput();
            }
        }

        private void ArmCombo()
        {
            armed = true;
            currentStage = 0;
            comboActive = false;
            comboWindowTimer = 0f;
            queuedInput = false;
            queuedTimer = 0f;
            usedThisAirborne = true;
            Log("Combo aéreo ARMADO - JumpAttack1 disponível");
        }

        private void ResetState()
        {
            armed = false;
            currentStage = 0;
            comboActive = false;
            comboWindowTimer = 0f;
            queuedInput = false;
            queuedTimer = 0f;
            usedThisAirborne = false;
            Log("Reset combo aéreo (pousou)");
        }

        private void HandleAttackInput()
        {
            if (!comboActive)
            {
                // Primeira vez ou fora da janela
                if (currentStage == 0)
                {
                    QueueAttack(initialDelay);
                }
                return;
            }

            // Durante combo ativo
            if (currentStage >= 3)
            {
                return;
            }

            if (comboWindowTimer > 0f)
            {
                QueueAttack(comboDelay);
            }
        }

        private void QueueAttack(float delay)
        {
            queuedInput = true;
            queuedTimer = delay;
            Log($"Input enfileirado para JumpAttack{currentStage + 1} em {delay}s");
        }

        private void ExecuteQueuedAttack()
        {
            queuedInput = false;
            int nextStage = currentStage + 1;
            if (nextStage > 3)
            {
                return;
            }

            currentStage = nextStage;
            comboActive = true;
            comboWindowTimer = comboWindow;

            FireAnimation(nextStage);
            ApplyImpulse();
            movementController.ForceAirborneStateForCombo();

            // Agenda finalização do stage após pequeno delay para dar tempo do hitbox detectar
            int airStage = 10 + nextStage;
            StartCoroutine(EndStageAfterDelay(airStage, 0.3f));

            if (nextStage >= 3)
            {
                FinishCombo();
            }
        }

        private System.Collections.IEnumerator EndStageAfterDelay(int stage, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            if (combatController != null)
            {
                combatController.NotifyAttackStageEnded(stage, true);
                Log($"Combat controller notificado: stage {stage} finalizado");
            }
        }

        private void FinishCombo()
        {
            Log("Combo finalizado");
            armed = false;
            comboActive = false;
            comboWindowTimer = 0f;
        }

        private void FireAnimation(int stage)
        {
            if (animatorController == null)
            {
                return;
            }

            // Notifica combat controller para ativar hitbox com stages específicos para air attacks
            // JumpAttack1=11, JumpAttack2=12, JumpAttack3=13
            int airStage = 10 + stage;
            if (combatController != null)
            {
                combatController.NotifyAttackStageStarted(airStage, true);
                Log($"Combat controller notificado: stage {airStage} iniciado (Air=true)");
            }

            string trigger = stage switch
            {
                1 => jumpAttack1Trigger,
                2 => jumpAttack2Trigger,
                3 => jumpAttack3Trigger,
                _ => null
            };

            if (!string.IsNullOrEmpty(trigger))
            {
                animatorController.SetTrigger(trigger);
                Log($"Trigger disparado: {trigger}");
            }
        }

        private void ApplyImpulse()
        {
            if (body == null)
            {
                return;
            }

            Vector3 velocity = body.linearVelocity;
            velocity.y = verticalImpulsePerHit;
            velocity += movementController.transform.forward * forwardImpulsePerHit;
            body.linearVelocity = velocity;
        }

        private bool EnsureReferences()
        {
            if (context == null)
            {
                context = GetComponent<NitssCharacterContext>();
            }
            if (animatorController == null && context != null)
            {
                animatorController = context.AnimatorController;
            }
            if (movementController == null)
            {
                movementController = GetComponent<NitssMovementController>();
            }
            if (combatController == null)
            {
                combatController = GetComponent<NitssCombatController>();
            }
            if (inputReader == null)
            {
                inputReader = GetComponent<NitssInputReader>();
            }
            if (jumpModule == null)
            {
                jumpModule = GetComponent<NitssJumpModule>();
            }
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            return movementController != null && inputReader != null && jumpModule != null && animatorController != null;
        }

        private void Log(string message)
        {
            if (debugLogging)
            {
                Debug.Log($"[NitssJumpAttackModule] {message}");
            }
        }

        /// <summary>
        /// Força o arme do combo aéreo (usado após auto-follow do uppercut).
        /// </summary>
        public void ForceArmCombo()
        {
            if (!usedThisAirborne)
            {
                ArmCombo();
                Log("Combo aéreo FORÇADO após auto-follow");
            }
        }
    }
}
