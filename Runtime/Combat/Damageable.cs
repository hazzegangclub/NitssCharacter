using Hazze.Gameplay.Characters.Nitss;
using UnityEngine;
using UnityEngine.Events;

namespace Hazze.Gameplay.Combat
{
    public enum DamageType { Melee, Projectile }

    /// <summary>
    /// Componente genérico de vida/dano que integra com o NitssLocomotionController para respeitar Block.
    /// Encaminhe dano usando posição do atacante (ou direção) para aplicar redução por block e chip damage.
    /// </summary>
    [DisallowMultipleComponent]
    public class Damageable : MonoBehaviour
    {
        [Header("Vida")]
    [Min(1f)] public float maxHealth = 100f; // Valor inicial local, será sobrescrito pelo NitssHealthSync se presente.
    [SerializeField] private float currentHealth = 100f; // Inicial; sincronizado depois pelo healthSync.

        [Header("Opções")]
        [Tooltip("Se ligado, ignora dano enquanto 'invulnerável' após ser atingido.")]
        public bool useInvulnerability = true;
        [Tooltip("Duração de i-frames após tomar dano (s).")]
        [Min(0f)] public float invulnerabilitySeconds = 0.2f;

    [Tooltip("Trigger opcional no Animator para dano LEVE (ex.: 'Damage1' ou 'Hit').")]
    public string hitTriggerLight = "";
    [Tooltip("Trigger opcional no Animator para dano MÉDIO/PESADO (ex.: 'Damage2' ou 'HeavyHit').")]
    public string hitTriggerHeavy = "";
        [Tooltip("Trigger opcional no Animator quando morre (ex.: 'Death').")]
        public string deathTrigger = "";

    [Header("Animações de Dano")]
    [Tooltip("Se ligado, alterna entre os triggers abaixo a cada golpe. Se a lista estiver vazia, usa hitTriggerLight/heavy.")]
    public bool alternateDamageTriggers = false;
    [Tooltip("Lista de triggers no Animator para tocar. Para uma animação única, deixe vazio e use 'hitTrigger'.")]
    public string[] damageTriggers = System.Array.Empty<string>();
        [Header("Consistência de Reação")]
        [Tooltip("Se verdadeiro, ainda toca animação de dano durante knockdown (caso não seja morte).")]
        public bool playDamageAnimationDuringKnockdown = false;
        [Tooltip("Se verdadeiro, toca animação mesmo quando o bloqueio reduz o dano a 0.")]
        public bool playDamageAnimationOnBlockedZeroDamage = true;
    [Tooltip("Se verdadeiro, ignora dano enquanto estiver derrubado (útil para evitar interrupções no knockdown).")]
    public bool preventDamageWhileKnockedDown = false;
        [Tooltip("Intervalo mínimo entre animações de dano (s). 0 = sem limite além de i-frames.")]
        [Min(0f)] public float minIntervalBetweenDamageAnimations = 0f;

    [Header("Cambaleio (Stagger)")]
    [Tooltip("Se verdadeiro, aplica um breve cambaleio a cada golpe recebido.")]
    public bool applyStaggerOnHit = true;
    [Tooltip("Duração base do cambaleio (s) aplicada quando o dano for recebido.")]
    [Min(0f)] public float staggerDurationSeconds = 0.01f;
    [Tooltip("Duração aplicada quando o golpe for considerado pesado.")]
    [Min(0f)] public float heavyStaggerDurationSeconds = 0.01f;
    [Tooltip("Fração do dano em relação à vida máxima para tratar como golpe pesado.")]
    [Range(0f, 1f)] public float heavyHitFractionThreshold = 0.2f;
    [Tooltip("Quantidade mínima de dano para disparar o cambaleio. 0 = qualquer dano.")]
    [Min(0f)] public float staggerMinDamage = 0f;

        [Header("Eventos")]
        public UnityEvent<float, float> onDamaged; // (amountApplied, currentHealth)
        public UnityEvent onDeath;

        private float iFrameTimer;
        private NitssLocomotionController locomotion; // para EvaluateBlock
        private CharacterAnimatorController anim;
        private Animator cachedAnimator; // para checar existência de parâmetros
    private int _nextDamageTriggerIndex = 0;
    private float _lastDamageAnimTime;
        private NitssHealthSync healthSync;
    private NitssCombatController combat;
        private KnockdownController knockdownController;
        
    [Header("On Death: Traversable")]
    [Tooltip("Ao morrer, tornar o personagem atravessável (não bloqueia outros).")]
    public bool makeTraversableOnDeath = true;
    [Tooltip("Estratégia: true = marcar colliders como Trigger; false = desabilitar colliders.")]
    public bool traversableByMakingTriggers = true;
    [Tooltip("Mantém um collider de chão sólido para não atravessar o piso.")]
    public bool preserveGroundContactOnDeath = true;
    [Tooltip("Índice do collider a preservar (0 = primeiro da lista obtida em GetComponentsInChildren).")] public int groundColliderIndex = 0;

        [Header("Combo (Bypass de I-Frames)")]
        [Tooltip("Permite que golpes sucessivos do MESMO atacante dentro de uma janela curta atravessem os i-frames (pensado para combos 1-2-3).")]
        public bool allowComboBypassIFrames = true;
        [Tooltip("Janela máxima (s) entre hits do mesmo atacante para considerar parte do mesmo combo.")]
        [Min(0f)] public float comboBypassMaxGapSeconds = 1.2f;
        [Tooltip("Se ligado, exige que o 'stage' do combo aumente (1→2→3) para bypass de i-frames.")]
        public bool comboBypassRequireIncreasingStage = true;

        private int _lastComboStage = -1;
        private int _lastAttackerId = 0;
        private float _lastComboHitTime = -999f;

        [Header("Air Juggle")]
        [Tooltip("Ativa sistema de animações de dano aéreo (juggle).")]
        public bool useAirJuggleAnimations = true;
        [Tooltip("Velocidade vertical mínima para considerar que está subindo (m/s).")]
        public float airJuggleRisingThreshold = 1f;
        [Tooltip("Velocidade vertical máxima para considerar que está caindo (m/s).")]
        public float airJuggleFallingThreshold = -1f;
        
        [Header("Air Juggle Animation Speed")]
        [Tooltip("Velocidade de lançamento base (m/s) para animação normal (speed = 1.0).")]
        public float baseLaunchVelocity = 8f;
        [Tooltip("Velocidade mínima das animações (para lançamentos fracos).")]
        public float minAnimationSpeed = 0.7f;
        [Tooltip("Velocidade máxima das animações (para lançamentos fortes).")]
        public float maxAnimationSpeed = 1.5f;
        [Tooltip("Suavização da mudança de velocidade da animação.")]
        public float animationSpeedSmoothTime = 0.1f;
        
        private bool _isInAirJuggle;
        private bool _lastAirHitWasHeavy;
        private Rigidbody _rigidbody;
        private NitssMovementController _movementController;
        private float _launchIntensity = 1f;
        private float _currentAnimSpeed = 1f;
        private float _animSpeedVelocity;
        private bool _isPlayingLandingAnim;
        private float _landingAnimStartTime;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public bool IsAlive => currentHealth > 0f;
        public bool IsInAirJuggle => _isInAirJuggle;

    [Header("Death Behaviour")] 
    [Tooltip("Se verdadeiro, ao morrer entra em Knockdown permanente (sem levantar) em vez de tocar 'deathTrigger'.")]
    public bool knockdownOnDeath = true;
    [Tooltip("Quando Knockdown On Death está ativo, usa animação pesada em vez de leve.")]
    public bool knockdownOnDeathHeavy = true;

    // Removido bloco de Dummy para retornar ao comportamento padrão (dummy usa a mesma seed/API do Nitss).

        private void Awake()
        {
            locomotion = GetComponentInParent<NitssLocomotionController>() ?? GetComponent<NitssLocomotionController>();
            anim = GetComponentInChildren<CharacterAnimatorController>();
            cachedAnimator = anim ? anim.GetComponent<Animator>() : GetComponentInChildren<Animator>();
            healthSync = GetComponentInParent<NitssHealthSync>() ?? GetComponent<NitssHealthSync>();
            combat = GetComponentInParent<NitssCombatController>() ?? GetComponent<NitssCombatController>();
            knockdownController = GetComponent<KnockdownController>();
            _rigidbody = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
            _movementController = GetComponent<NitssMovementController>() ?? GetComponentInParent<NitssMovementController>();
            
            if (knockdownController != null)
            {
                Debug.Log($"[Damageable] KnockdownController encontrado em {gameObject.name}");
            }
            else
            {
                Debug.LogWarning($"[Damageable] KnockdownController NÃO encontrado em {gameObject.name}. Adicione o componente para usar sistema de KD.");
            }
            
            if (healthSync != null)
            {
                maxHealth = Mathf.Max(1f, healthSync.MaxHealth);
                currentHealth = Mathf.Clamp(healthSync.CurrentHealth, 0f, maxHealth);
                healthSync.onHealthChanged.AddListener(HandleHealthSyncChanged);
            }
            else
            {
                currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            }

            // Mantém lógica padrão; se houver NitssHealthSync, maxHealth/currentHealth serão conduzidos pela API.
        }

        private void Update()
        {
            if (useInvulnerability && iFrameTimer > 0f)
            {
                iFrameTimer -= Time.deltaTime;
            }
            
            if (useAirJuggleAnimations && _isInAirJuggle)
            {
                UpdateAirJuggleAnimation();
            }
        }
        
        private void UpdateAirJuggleAnimation()
        {
            if (!_rigidbody || !cachedAnimator) return;
            
            // Não atualiza air juggle se morreu
            if (!IsAlive)
            {
                _isInAirJuggle = false;
                _isPlayingLandingAnim = false;
                cachedAnimator.SetFloat("DamageAir", -1f);
                cachedAnimator.speed = 1f;
                return;
            }
            
            bool isGrounded = _movementController != null && _movementController.IsGrounded;
            
            // Se pousar, inicia animação de landing
            if (isGrounded)
            {
                if (!_isPlayingLandingAnim)
                {
                    _isPlayingLandingAnim = true;
                    _landingAnimStartTime = Time.time;
                    
                    // End_Heavy (0.75) ou End_Light (1.0)
                    float endValue = _lastAirHitWasHeavy ? 0.75f : 1.0f;
                    cachedAnimator.SetFloat("DamageAir", endValue);
                    cachedAnimator.SetBool("IsJumping", false);
                    cachedAnimator.speed = 1f;
                    
                    Debug.Log($"[Damageable] LANDING - Playing End animation: DamageAir={endValue} ({(_lastAirHitWasHeavy ? "Heavy" : "Light")})");
                }
                
                // Após pequeno delay, reseta completamente e inicia knockdown
                if (Time.time - _landingAnimStartTime >= 0.2f)
                {
                    _isInAirJuggle = false;
                    _isPlayingLandingAnim = false;
                    cachedAnimator.SetFloat("DamageAir", -1f);
                    _currentAnimSpeed = 1f;
                    
                    Debug.Log("[Damageable] Air juggle ENDED - Iniciando knockdown (LyingBack + WakeUp)");
                    
                    // Inicia knockdown após air juggle
                    if (knockdownController != null)
                    {
                        knockdownController.EnterKnockdown();
                    }
                }
                return;
            }
            
            // Mantém IsJumping false durante todo o air juggle
            cachedAnimator.SetBool("IsJumping", false);
            
            // Atualiza parâmetro baseado na velocidade vertical
            float verticalVelocity = _rigidbody.linearVelocity.y;
            float damageAirValue;
            
            if (verticalVelocity >= airJuggleRisingThreshold)
            {
                // SUBINDO: DamageAir_Start (0.1)
                damageAirValue = 0.1f;
            }
            else if (verticalVelocity > airJuggleFallingThreshold && verticalVelocity < airJuggleRisingThreshold)
            {
                // NO TOPO (transição entre subida e queda): DamageAir_Hit_Down (0.25)
                damageAirValue = 0.25f;
            }
            else
            {
                // CAINDO: DamageAir_Fall (0.5)
                damageAirValue = 0.5f;
            }
            
            cachedAnimator.SetFloat("DamageAir", damageAirValue);
            
            // Ajusta velocidade da animação baseado na intensidade do lançamento
            float targetSpeed = Mathf.Lerp(minAnimationSpeed, maxAnimationSpeed, (_launchIntensity - 0.5f) / 1.5f);
            _currentAnimSpeed = Mathf.SmoothDamp(_currentAnimSpeed, targetSpeed, ref _animSpeedVelocity, animationSpeedSmoothTime);
            cachedAnimator.speed = _currentAnimSpeed;
        }

        private System.Collections.IEnumerator ResetDamageAirAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (cachedAnimator)
            {
                cachedAnimator.SetFloat("DamageAir", -1f);
                Debug.Log("[Damageable] DamageAir reset to -1 (exiting state)");
            }
        }

        /// <summary>
        /// Limpa imediatamente a janela de invulnerabilidade por i-frames (útil ao sair de estados longos como knockdown).
        /// </summary>
        public void ClearInvulnerabilityIFrames()
        {
            iFrameTimer = 0f;
        }
        
        /// <summary>
        /// Inicia o sistema de air juggle (chamado quando personagem é lançado para cima).
        /// </summary>
        public void StartAirJuggle(bool wasHeavyHit)
        {
            if (!useAirJuggleAnimations) return;
            
            _isInAirJuggle = true;
            _lastAirHitWasHeavy = wasHeavyHit;
            _isPlayingLandingAnim = false;
            
            // Captura intensidade do lançamento baseado na velocidade vertical inicial
            if (_rigidbody != null)
            {
                float initialVelocity = Mathf.Abs(_rigidbody.linearVelocity.y);
                // Clamp mais restrito para evitar animações muito rápidas
                _launchIntensity = Mathf.Clamp(initialVelocity / baseLaunchVelocity, 0.7f, 1.3f);
                Debug.Log($"[Damageable] Launch intensity calculated: {_launchIntensity:F2} (velocity={initialVelocity:F1} m/s, base={baseLaunchVelocity} m/s)");
            }
            else
            {
                _launchIntensity = 1f;
            }
            
            if (cachedAnimator != null)
            {
                // Inicia com DamageAir_Start (0.1 para garantir que está no primeiro threshold)
                cachedAnimator.SetFloat("DamageAir", 0.1f);
                // Desabilita IsJumping para prevenir interferência da animação de pulo
                cachedAnimator.SetBool("IsJumping", false);
                Debug.Log($"[Damageable] AIR JUGGLE STARTED (Launch) - DamageAir=0.1, Heavy={wasHeavyHit}, Intensity={_launchIntensity:F2}, IsJumping=false");
            }
        }

        /// <summary>
        /// Aplica dano informando a POSIÇÃO do atacante (melhor para melee). Respeita Block direcional.
        /// </summary>
        public void ApplyDamage(float amount, Vector3 attackerWorldPosition, DamageType type = DamageType.Melee, bool heavyGuardBreak = false, bool unblockable = false, bool isHeavyAttack = false)
        {
            if (amount <= 0f || !IsAlive) return;
            if (knockdownController != null && knockdownController.IsDead) return; // Invulnerável quando morto
            if (knockdownController != null && knockdownController.IsInvulnerableWakeUp) return; // Invulnerável durante WakeUp
            bool inKnockdown = locomotion != null && locomotion.IsKnockedDown;
            if (preventDamageWhileKnockedDown && inKnockdown) return;
            if (useInvulnerability && iFrameTimer > 0f) return;

            float applied = amount;
            bool blocked = false;

            // Heavy pode quebrar guarda antes de calcular redução (opcional)
            if (heavyGuardBreak && locomotion != null)
            {
                locomotion.BreakBlock();
            }

            // Redução por block (se não for unblockable)
            if (!unblockable && locomotion != null)
            {
                bool isProj = (type == DamageType.Projectile);
                blocked = locomotion.EvaluateBlock(attackerWorldPosition, ref applied, isProj);
            }

            ApplyFinalDamage(applied, blocked, heavyGuardBreak, false, isHeavyAttack);
        }

        /// <summary>
        /// Variante com informações de combo: permite ignorar i-frames quando o mesmo atacante encadeia golpes dentro da janela.
        /// 'attackerTag' pode ser qualquer UnityEngine.Object que identifique o atacante (ex.: controller/raiz do personagem).
        /// </summary>
        public void ApplyDamageCombo(float amount, Vector3 attackerWorldPosition, int comboStage, Object attackerTag,
            DamageType type = DamageType.Melee, bool heavyGuardBreak = false, bool unblockable = false, bool isHeavyAttack = false)
        {
            if (amount <= 0f || !IsAlive) return;
            if (knockdownController != null && knockdownController.IsDead) return; // Invulnerável quando morto
            if (knockdownController != null && knockdownController.IsInvulnerableWakeUp) return; // Invulnerável durante WakeUp
            bool inKnockdown = locomotion != null && locomotion.IsKnockedDown;
            if (preventDamageWhileKnockedDown && inKnockdown) return;

            int attackerId = attackerTag ? attackerTag.GetInstanceID() : 0;
            bool bypassIFrames = false;
            bool animationOnly = false;
            
            if (useInvulnerability && iFrameTimer > 0f)
            {
                if (allowComboBypassIFrames && attackerId == _lastAttackerId)
                {
                    bool withinWindow = (Time.time - _lastComboHitTime) <= comboBypassMaxGapSeconds;
                    bool stageOK = !comboBypassRequireIncreasingStage || (comboStage > _lastComboStage);
                    bypassIFrames = withinWindow && stageOK;
                }
                
                // Se não bypass completo, mas é do mesmo atacante, toca só a animação
                if (!bypassIFrames)
                {
                    bool sameAttacker = (attackerId == _lastAttackerId);
                    bool withinWindow = (Time.time - _lastComboHitTime) <= comboBypassMaxGapSeconds;
                    animationOnly = sameAttacker && withinWindow;
                    
                    if (!animationOnly) return; // Nem dano nem animação
                }
            }

            float applied = amount;
            bool blocked = false;

            if (heavyGuardBreak && locomotion != null && !animationOnly)
            {
                locomotion.BreakBlock();
            }

            if (!unblockable && locomotion != null)
            {
                bool isProj = (type == DamageType.Projectile);
                blocked = locomotion.EvaluateBlock(attackerWorldPosition, ref applied, isProj);
            }

            // Atualiza tracking de combo (antes da animação) para próxima decisão de bypass
            _lastAttackerId = attackerId;
            _lastComboStage = comboStage;
            _lastComboHitTime = Time.time;

            ApplyFinalDamage(applied, blocked, heavyGuardBreak, animationOnly, isHeavyAttack);
        }

        /// <summary>
        /// Aplica dano informando a DIREÇÃO do ataque (útil para projéteis). Direção deve apontar DO atacante PARA o alvo.
        /// </summary>
    public void ApplyDamageByDirection(float amount, Vector3 attackDirectionWorld, DamageType type = DamageType.Melee, bool heavyGuardBreak = false, bool unblockable = false)
        {
            if (amount <= 0f || !IsAlive) return;
            bool inKnockdown = locomotion != null && locomotion.IsKnockedDown;
            if (preventDamageWhileKnockedDown && inKnockdown) return;
            if (useInvulnerability && iFrameTimer > 0f) return;

            float applied = amount;
            bool blocked = false;

            if (heavyGuardBreak && locomotion != null)
            {
                locomotion.BreakBlock();
            }

            if (!unblockable && locomotion != null)
            {
                bool isProj = (type == DamageType.Projectile);
                blocked = locomotion.EvaluateBlockByDirection(attackDirectionWorld, ref applied, isProj);
            }

            ApplyFinalDamage(applied, blocked, heavyGuardBreak, false, false);
        }

        /// <summary>
        /// Cura vida (valor positivo). Retorna a vida atual.
        /// </summary>
        public float Heal(float amount)
        {
            if (amount <= 0f || !IsAlive) return currentHealth;
            if (healthSync != null)
            {
                healthSync.Heal(amount);
                // Não sobrescreva maxHealth aqui; ele vem da API e será atualizado via evento de sync.
                currentHealth = Mathf.Clamp(healthSync.CurrentHealth, 0f, Mathf.Max(1f, maxHealth));
                return currentHealth;
            }
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            return currentHealth;
        }

        private void ApplyFinalDamage(float amount, bool blocked, bool heavyGuardBreak = false, bool animationOnly = false, bool isHeavyAttack = false)
        {
            float applied = amount;
            bool heavyFlag = isHeavyAttack; // Usa isHeavyAttack para determinar tipo de ataque
            bool zeroBlockedReaction = blocked && applied <= 0f && playDamageAnimationOnBlockedZeroDamage;
            if (applied <= 0f && !zeroBlockedReaction && !animationOnly) return;

            // Se for apenas animação (durante i-frames), não aplica dano/stamina/stagger
            if (!animationOnly)
            {
                if (healthSync != null)
                {
                    healthSync.ApplyDamage(applied);
                    // Não sobrescreva maxHealth a cada dano; confie no evento de sync para mudanças de max.
                    currentHealth = Mathf.Clamp(healthSync.CurrentHealth, 0f, Mathf.Max(1f, maxHealth));
                }
                else
                {
                    currentHealth -= applied;
                }
                
                // Tenta aplicar stagger e captura se foi aplicado
                bool staggerApplied = TryApplyStagger(applied, heavyFlag);
                
                // Notifica KnockdownController para consumir stamina (light ou heavy)
                if (knockdownController != null)
                {
                    if (heavyFlag)
                    {
                        knockdownController.OnHeavyAttackReceived();
                    }
                    else
                    {
                        knockdownController.OnLightAttackReceived();
                    }
                }
                
                if (useInvulnerability && invulnerabilitySeconds > 0f)
                {
                    // Reduz i-frames para heavy hits para não bloquear animações subsequentes
                    iFrameTimer = heavyFlag ? (invulnerabilitySeconds * 0.5f) : invulnerabilitySeconds;
                }
            }

            onDamaged?.Invoke(applied, Mathf.Max(0f, currentHealth));

            if (currentHealth <= 0f)
            {
                currentHealth = 0f;
                
                Debug.Log($"[Damageable] MORTE - heavyFlag={heavyFlag}, Time={Time.time:F3}");
                
                // Notifica KnockdownController sobre morte (fica em LyingBack permanente)
                if (knockdownController != null)
                {
                    knockdownController.OnDeath(heavyFlag);
                }
                else if (knockdownOnDeath && locomotion != null)
                {
                    // Fallback: entra em knockdown permanente (sem auto-standup)
                    locomotion.EnterPermanentKnockdown(knockdownOnDeathHeavy);
                }
                else if (!string.IsNullOrEmpty(deathTrigger) && anim != null)
                {
                    anim.SetTrigger(deathTrigger);
                }
                if (makeTraversableOnDeath)
                {
                    MakeTraversable();
                }
                onDeath?.Invoke();
            }
            else if (!animationOnly)
            {
                // NÃO toca animação de dano se estiver em knockdown
                bool inKnockdown = knockdownController != null && knockdownController.IsKnockedDown;
                if (inKnockdown)
                {
                    Debug.Log($"[Damageable] Animação de dano bloqueada - personagem em knockdown");
                    return;
                }
                
                // Verifica se está no ar para iniciar air juggle
                bool isAirborne = _movementController != null && !_movementController.IsGrounded;
                if (useAirJuggleAnimations && isAirborne)
                {
                    _isInAirJuggle = true;
                    _lastAirHitWasHeavy = heavyFlag;
                    
                    if (cachedAnimator != null)
                    {
                        // Inicia com DamageAir_Start (0.0)
                        cachedAnimator.SetFloat("DamageAir", 0f);
                        Debug.Log($"[Damageable] AIR JUGGLE STARTED - Heavy={heavyFlag}");
                    }
                    
                    // Não toca animação de dano terrestre quando no ar
                    return;
                }
                
                // Toca animação de dano quando NÃO morreu (usa Blend Tree com parâmetro float)
                if (anim != null && AnimatorHasTrigger("Damage1"))
                {
                    // Pega o Animator para setar o parâmetro float do Blend Tree
                    var animator = anim.GetComponent<Animator>();
                    if (animator != null)
                    {
                        // Seta o parâmetro float para escolher a animação no Blend Tree
                        // IMPORTANTE: O parâmetro do Blend Tree deve ser "DamageLevel" (Float), não "Damage1" (Trigger)
                        // 0 = Damage1 (light attack), 1 = Damage2 (heavy attack)
                        float damageValue = heavyFlag ? 1f : 0f;
                        
                        // Usa "DamageLevel" como nome do float parameter (evita conflito com o trigger "Damage1")
                        animator.SetFloat("DamageLevel", damageValue);
                        
                        Debug.Log($"[Damageable] {(heavyFlag ? "Heavy" : "Light")} attack → DamageLevel setado para {damageValue} | Time: {Time.time:F2}");
                    }
                    
                    // Dispara o trigger para entrar no estado Damage
                    anim.ResetTrigger("Damage1");
                    anim.SetTrigger("Damage1");
                }
            }
        }

        private void MakeTraversable()
        {
            try
            {
                var cols = GetComponentsInChildren<Collider>(true);
                if (cols == null || cols.Length == 0) return;
                Collider groundCol = null;
                if (preserveGroundContactOnDeath)
                {
                    int gi = Mathf.Clamp(groundColliderIndex, 0, cols.Length - 1);
                    groundCol = cols[gi];
                }
                foreach (var c in cols)
                {
                    if (!c) continue;
                    if (preserveGroundContactOnDeath && c == groundCol) continue; // mantém sólido
                    if (traversableByMakingTriggers)
                    {
                        c.isTrigger = true;
                    }
                    else
                    {
                        c.enabled = false;
                    }
                }
                if (groundCol && preserveGroundContactOnDeath)
                {
                    // Congela deslocamento horizontal para evitar empurrões residuais.
                    var ctx = GetComponent<NitssCharacterContext>();
                    if (ctx && ctx.Body)
                    {
                        var rb = ctx.Body;
                        rb.constraints |= RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
                    }
                    // Garante que outros personagens possam atravessar o corpo preservando apenas contato com o chão
                    try
                    {
                        var myRoot = GetComponentInParent<NitssLocomotionController>() ?? GetComponent<NitssLocomotionController>();
                        var others = Object.FindObjectsOfType<NitssLocomotionController>(true);
                        foreach (var other in others)
                        {
                            if (!other || other == myRoot) continue;
                            var otherCols = other.GetComponentsInChildren<Collider>(true);
                            foreach (var oc in otherCols)
                            {
                                if (!oc) continue;
                                Physics.IgnoreCollision(groundCol, oc, true);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private bool AnimatorHasTrigger(string name)
        {
            if (cachedAnimator == null || string.IsNullOrEmpty(name)) return false;
            var ps = cachedAnimator.parameters;
            if (ps == null) return false;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == name) return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            if (healthSync != null)
            {
                healthSync.onHealthChanged.RemoveListener(HandleHealthSyncChanged);
            }
        }

        private void HandleHealthSyncChanged(float current, float max)
        {
            // Atualize maxHealth somente quando o servidor indicar (e max > 0)
            if (max > 0f && !Mathf.Approximately(maxHealth, max))
            {
                maxHealth = Mathf.Max(1f, max);
            }
            currentHealth = Mathf.Clamp(current, 0f, maxHealth);
        }

        private bool TryApplyStagger(float damageApplied, bool heavyFlag)
        {
            if (!applyStaggerOnHit) return false;
            if (combat == null) return false;
            if (damageApplied <= 0f) return false;
            if (staggerMinDamage > 0f && damageApplied < staggerMinDamage) return false;
            if (combat.IsKnockedDown) return false;

            float duration = Mathf.Max(0f, staggerDurationSeconds);
            if (duration <= 0f) return false;

            float severity = damageApplied / Mathf.Max(1f, maxHealth);
            if ((heavyFlag || severity >= heavyHitFractionThreshold) && heavyStaggerDurationSeconds > duration)
            {
                duration = heavyStaggerDurationSeconds;
            }

            combat.ApplyStagger(duration);
            return true;
        }

    }
}
