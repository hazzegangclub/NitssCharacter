using UnityEngine;
using Hazze.Gameplay.Characters.Nitss;

namespace Hazze.Gameplay.Combat
{
    /// <summary>
    /// Controla o sistema de Knockdown (KD) baseado em stamina.
    /// Quando o personagem sofre um heavy attack sem stamina, entra em knockdown (LyingBack).
    /// Após um tempo, executa WakeUp para levantar, a menos que esteja morto (fica em LyingBack permanente).
    /// </summary>
    [DisallowMultipleComponent]
    public class KnockdownController : MonoBehaviour
    {
        [Header("Stamina Configuration")]
        [Tooltip("Stamina máxima para knockdown (separada do block).")]
        [Min(1f)] public float maxKnockdownStamina = 100f;
        [Tooltip("Regeneração de stamina por segundo quando não está em knockdown.")]
        [Min(0f)] public float staminaRegenPerSecond = 20f;
        [Tooltip("Stamina consumida ao receber um light attack.")]
        [Min(0f)] public float lightAttackStaminaCost = 15f;
        [Tooltip("Stamina consumida ao receber um heavy attack.")]
        [Min(0f)] public float heavyAttackStaminaCost = 50f;

        [Header("Knockdown Settings")]
        [Tooltip("Duração mínima em LyingBack antes de poder levantar (segundos).")]
        [Min(0f)] public float knockdownDuration = 2f;
        [Tooltip("Duração da invulnerabilidade durante WakeUp (segundos).")]
        [Min(0f)] public float wakeUpInvulnerabilityDuration = 0.3f;
        [Tooltip("Trigger do Animator para entrar em knockdown com heavy attack.")]
        public string knockdownHeavyTrigger = "KnockDown_B_Heavy";
        [Tooltip("Trigger do Animator para entrar em knockdown com light attack.")]
        public string knockdownLightTrigger = "KnockDown_B_Light";
        [Tooltip("Trigger do Animator para levantar. Deve apontar para a transição LyingBack->LyingFront_WakeUp.")]
        public string wakeUpTrigger = "LyingFront_WakeUp";
        [Tooltip("Bool do Animator para indicar morte permanente (mantém em LyingBack sem levantar).")]
        public string isDeadBool = "IsDead";

        [Header("References")]
        [SerializeField] private CharacterAnimatorController animatorController;
        [SerializeField] private Damageable damageable;

        [Header("Debug/Runtime")]
        [Tooltip("Ativa logs detalhados no console.")]
        public bool enableDebugLogs = false;
        [SerializeField] private float currentStamina;
        private bool isKnockedDown;
        private float knockdownTimer;
        private bool isDead;
        private float wakeUpInvulnerabilityTimer;

        public float CurrentStamina => currentStamina;
        public float MaxStamina => maxKnockdownStamina;
        public float StaminaNormalized => Mathf.Clamp01(currentStamina / Mathf.Max(1f, maxKnockdownStamina));
        public bool IsKnockedDown => isKnockedDown;
        public bool IsDead => isDead;
        public bool IsInvulnerableWakeUp => wakeUpInvulnerabilityTimer > 0f;

        private void Awake()
        {
            if (!animatorController)
                animatorController = GetComponentInChildren<CharacterAnimatorController>();
            if (!damageable)
                damageable = GetComponent<Damageable>();

            currentStamina = maxKnockdownStamina;
            
            Debug.Log($"[KnockdownController] Inicializado. Stamina: {currentStamina}/{maxKnockdownStamina}");
        }

        private void Update()
        {
            // Regenera stamina se não estiver em knockdown e não estiver morto
            if (!isKnockedDown && !isDead && currentStamina < maxKnockdownStamina)
            {
                currentStamina = Mathf.Min(maxKnockdownStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
            }

            // Gerencia timer de knockdown
            if (isKnockedDown && !isDead)
            {
                knockdownTimer -= Time.deltaTime;
                if (knockdownTimer <= 0f)
                {
                    WakeUp();
                }
            }

            // Atualiza timer de invulnerabilidade do WakeUp
            if (wakeUpInvulnerabilityTimer > 0f)
            {
                wakeUpInvulnerabilityTimer -= Time.deltaTime;
            }
        }

        /// <summary>
        /// Processa um light attack recebido. Consome stamina.
        /// </summary>
        public void OnLightAttackReceived()
        {
            if (isDead) return;

            // Consome stamina
            currentStamina = Mathf.Max(0f, currentStamina - lightAttackStaminaCost);

            if (enableDebugLogs)
                Debug.Log($"[KnockdownController] Light attack recebido. Stamina: {currentStamina:F1}/{maxKnockdownStamina}");
        }

        /// <summary>
        /// Processa um heavy attack recebido. Consome stamina e entra em knockdown se stamina zerada.
        /// </summary>
        public void OnHeavyAttackReceived()
        {
            if (isDead) return;

            // Consome stamina
            currentStamina = Mathf.Max(0f, currentStamina - heavyAttackStaminaCost);

            if (enableDebugLogs)
                Debug.Log($"[KnockdownController] Heavy attack recebido. Stamina: {currentStamina:F1}/{maxKnockdownStamina}");

            // Se stamina zerou, entra em knockdown
            if (currentStamina <= 0f && !isKnockedDown)
            {
                EnterKnockdown();
            }
        }

        /// <summary>
        /// Força entrada em knockdown (LyingBack).
        /// </summary>
        public void EnterKnockdown()
        {
            if (isKnockedDown || isDead)
            {
                Debug.Log($"[KnockdownController] EnterKnockdown bloqueado - isKnockedDown={isKnockedDown}, isDead={isDead}");
                return;
            }

            Debug.Log($"[KnockdownController] EnterKnockdown chamado por stamina=0 | Time={Time.time:F2}");

            isKnockedDown = true;
            knockdownTimer = knockdownDuration;

            // Ativa invulnerabilidade durante todo o knockdown + WakeUp
            wakeUpInvulnerabilityTimer = knockdownDuration + wakeUpInvulnerabilityDuration;

            // Dispara trigger de knockdown via Animator diretamente (fallback se CharacterAnimatorController falhar)
            var animator = animatorController != null ? animatorController.GetComponent<Animator>() : GetComponentInChildren<Animator>();
            
            // Knockdown por stamina sempre usa Heavy trigger
            string triggerToUse = knockdownHeavyTrigger;
            
            Debug.Log($"[KnockdownController] Usando trigger: {triggerToUse}");
            
            if (animator != null && !string.IsNullOrEmpty(triggerToUse))
            {
                animator.SetTrigger(triggerToUse);
                Debug.Log($"[KnockdownController] Trigger '{triggerToUse}' disparado no Animator");
            }
            else
            {
                Debug.LogWarning($"[KnockdownController] NÃO pode disparar trigger! Animator={animator != null}, Trigger='{triggerToUse}'");
            }

            if (enableDebugLogs)
                Debug.Log($"[KnockdownController] Entrou em knockdown. Stamina: {currentStamina:F1}/{maxKnockdownStamina}");
        }

        /// <summary>
        /// Levanta do knockdown (WakeUp).
        /// </summary>
        private void WakeUp()
        {
            if (!isKnockedDown || isDead) return;

            isKnockedDown = false;

            // Mantém invulnerabilidade durante WakeUp (já foi ativada no EnterKnockdown)
            // Se por algum motivo o timer já expirou, reativa por segurança
            if (wakeUpInvulnerabilityTimer <= 0f)
            {
                wakeUpInvulnerabilityTimer = wakeUpInvulnerabilityDuration;
            }

            // Dispara trigger de wake up via Animator diretamente
            var animator = animatorController != null ? animatorController.GetComponent<Animator>() : GetComponentInChildren<Animator>();
            
            if (animator != null && !string.IsNullOrEmpty(wakeUpTrigger))
            {
                if (enableDebugLogs)
                    Debug.Log($"[KnockdownController] Disparando trigger: {wakeUpTrigger} no Animator");
                animator.SetTrigger(wakeUpTrigger);
            }
            else if (enableDebugLogs)
            {
                Debug.LogWarning($"[KnockdownController] NÃO pode disparar wake up! AnimatorController: {animatorController != null}, Trigger: '{wakeUpTrigger}'");
            }

            // Restaura stamina completamente ao levantar
            currentStamina = maxKnockdownStamina;

            if (enableDebugLogs)
                Debug.Log($"[KnockdownController] Levantou do knockdown (WakeUp). Stamina restaurada: {currentStamina:F1}/{maxKnockdownStamina}");
        }

        /// <summary>
        /// Marca o personagem como morto. Fica em LyingBack permanentemente.
        /// </summary>
        /// <param name="fromHeavyAttack">Se true, usa KnockDown_B_Heavy; se false, usa KnockDown_B_Light</param>
        public void OnDeath(bool fromHeavyAttack = true)
        {
            isDead = true;
            
            Debug.Log($"[KnockdownController] OnDeath chamado - fromHeavyAttack={fromHeavyAttack}, Time={Time.time:F3}");

            // Busca Animator diretamente
            var animator = animatorController != null ? animatorController.GetComponent<Animator>() : GetComponentInChildren<Animator>();
            
            if (animator != null)
            {
                // SEMPRE entra em knockdown quando morre
                isKnockedDown = true;
                
                // Escolhe o estado correto baseado no tipo de ataque
                string stateToPlay = fromHeavyAttack ? "KnockDown_B_Heavy" : "KnockDown_B_Light";
                
                // Log estado atual do Animator
                var currentState = animator.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[KnockdownController] Estado atual antes: hash={currentState.fullPathHash}, normalizedTime={currentState.normalizedTime:F2}");
                
                Debug.Log($"[KnockdownController] Forçando estado para morte: {stateToPlay}");
                
                // RESETA triggers de knockdown conhecidos para evitar conflitos
                if (!string.IsNullOrEmpty(knockdownHeavyTrigger))
                    animator.ResetTrigger(knockdownHeavyTrigger);
                if (!string.IsNullOrEmpty(knockdownLightTrigger))
                    animator.ResetTrigger(knockdownLightTrigger);
                
                // Força entrada direta no estado (mais confiável para morte)
                animator.Play(stateToPlay, 0, 0f);
                
                Debug.Log($"[KnockdownController] animator.Play('{stateToPlay}') executado para morte");
                
                // Espera a transição para LyingBack acontecer antes de setar IsDead
                StartCoroutine(SetIsDeadAfterTransition());
                
                if (enableDebugLogs)
                    Debug.Log($"[KnockdownController] Usou estado '{stateToPlay}' para morte");
            }

            if (enableDebugLogs)
                Debug.Log("[KnockdownController] Personagem morreu. Permanece em LyingBack.");
        }

        /// <summary>
        /// Coroutine que seta IsDead após a transição para LyingBack acontecer.
        /// </summary>
        private System.Collections.IEnumerator SetIsDeadAfterTransition()
        {
            var animator = animatorController != null ? animatorController.GetComponent<Animator>() : GetComponentInChildren<Animator>();
            if (animator == null) yield break;
            
            // Monitora a transição frame a frame
            for (int i = 0; i < 60; i++) // 60 frames = ~2 segundos
            {
                yield return null;
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                var nextStateInfo = animator.GetNextAnimatorStateInfo(0);
                
                if (i % 10 == 0) // Log a cada 10 frames
                {
                    Debug.Log($"[KnockdownController] Frame {i}: Current={stateInfo.fullPathHash}, IsInTransition={animator.IsInTransition(0)}, Next={nextStateInfo.fullPathHash}");
                }
            }
            
            // Seta IsDead após monitorar
            if (!string.IsNullOrEmpty(isDeadBool))
            {
                animator.SetBool(isDeadBool, true);
                Debug.Log($"[KnockdownController] SetBool('{isDeadBool}', true) - após monitoramento");
            }
        }

        /// <summary>
        /// Força o personagem a levantar imediatamente (útil para debug ou eventos especiais).
        /// </summary>
        public void ForceWakeUp()
        {
            if (isDead) return;
            knockdownTimer = 0f;
            WakeUp();
        }

        /// <summary>
        /// Reseta stamina para o máximo (útil para respawn ou debug).
        /// </summary>
        public void ResetStamina()
        {
            currentStamina = maxKnockdownStamina;
        }
    }
}
