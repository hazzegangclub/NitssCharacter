using System;
using UnityEngine;
using Hazze.Gameplay.Combat;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Controlador simplificado de combate do Nitss. Dispara eventos de estágios de ataque,
    /// gerencia bloqueio e estados básicos de knockdown.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssCombatController : MonoBehaviour
    {
        [Header("Tempos de combo")]
        [SerializeField] private float comboStageDuration = 0.35f;
        [SerializeField] private float comboResetTime = 0.6f;

        [Header("Block")]
        [SerializeField] private float blockDamageReduction = 0.75f;
        [SerializeField] private float blockStaminaMax = 100f;
        [SerializeField] private float blockStaminaRegenPerSecond = 40f;
        [SerializeField] private float blockStaminaCostHeavy = 40f;

        [Header("Knockdown")]
        [SerializeField] private float knockdownDuration = 1.5f;

        [Header("Uppercut Launch")]
        [Tooltip("Altura máxima que o personagem pode ser lançado por um uppercut (unidades).")]
        [SerializeField] private float uppercutMaxLaunchHeight = 5f;

        [Header("Input")]
        [SerializeField, Tooltip("Quando verdadeiro, o controlador usa AttackPressed do InputReader diretamente (modo legado).")]
        private bool useInternalAttackInput;

        [Header("Debug")]
        [Tooltip("Ativa logs detalhados no console.")]
        public bool enableDebugLogs = false;

        public event Action<int, bool> AttackStageStarted;
        public event Action<int, bool> AttackStageEnded;
        public event Action<bool> KnockdownStateChanged;

        private const int MaxOverrideStage = 4;

        private NitssCharacterContext context;
        private NitssMovementController movement;
        private NitssInputReader input;

        private int currentStage;
        private float stageTimer;
        private float comboResetTimer;
        private bool stageIsAir;

        private bool isBlocking;
        private float blockStamina;
        private bool guardBroken;
        private bool blockRequest;

        private bool isStaggered;
        private float staggerTimer;

        private bool isKnockedDown;
        private float knockdownTimer;
        private bool knockdownPermanent;
        private bool comboResetHold;
        private int nextStageOverride;
        private bool nextStageOverrideForceAir;
        private bool uppercutFromCombo;

        public bool IsBlocking => isBlocking;
        public bool GuardBroken => guardBroken;
        public bool IsAttacking => currentStage > 0 && stageTimer > 0f;
        public bool IsAirAttacking => IsAttacking && stageIsAir;
        public bool IsKnockedDown => isKnockedDown;
        public bool IsStaggered => isStaggered;
        public float KnockdownStaminaNormalized => Mathf.Clamp01(blockStamina / Mathf.Max(1f, blockStaminaMax));
        public float KnockdownStamina => blockStamina;
        public int CurrentStage => currentStage;
        public bool IsUppercutFromCombo => uppercutFromCombo && currentStage == 4;

        private void Awake()
        {
            context = GetComponent<NitssCharacterContext>();
            movement = GetComponent<NitssMovementController>();
            input = context ? context.InputReader : null;
            blockStamina = blockStaminaMax;
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            input = reader ?? input;
            UpdateKnockdown(dt);
            UpdateStagger(dt);
            UpdateBlocking(dt);
            UpdateCombo(dt);
        }

        public void SetBlockRequest(bool wantsBlock)
        {
            if (wantsBlock)
            {
                blockRequest = true;
            }
        }

        public void CancelBlock()
        {
            blockRequest = false;
            if (isBlocking)
            {
                isBlocking = false;
            }
        }

        private void UpdateBlocking(float dt)
        {
            bool wantsBlock = blockRequest;
            blockRequest = false;
            if (guardBroken)
            {
                wantsBlock = false;
            }
            if (wantsBlock != isBlocking)
            {
                isBlocking = wantsBlock;
            }

            if (!isBlocking && blockStamina < blockStaminaMax)
            {
                blockStamina = Mathf.Min(blockStaminaMax, blockStamina + blockStaminaRegenPerSecond * dt);
            }
        }

        private void UpdateCombo(float dt)
        {
            if (IsKnockedDown) return;
            if (useInternalAttackInput && input != null && input.AttackPressed)
            {
                TryStartNextStage(out _, out _);
            }

            if (stageTimer > 0f)
            {
                stageTimer -= dt;
                if (stageTimer <= 0f)
                {
                    AttackStageEnded?.Invoke(currentStage, stageIsAir);
                    currentStage = 0;
                    uppercutFromCombo = false;
                }
                else
                {
                    comboResetTimer = comboResetTime;
                }
            }
            else if (comboResetTimer > 0f)
            {
                if (!comboResetHold)
                {
                    comboResetTimer = Mathf.Max(0f, comboResetTimer - dt);
                }
            }
        }

        public bool TryRequestAttackStage(out int stage, out bool isAir)
        {
            if (IsKnockedDown)
            {
                stage = 0;
                isAir = false;
                return false;
            }

            return TryStartNextStage(out stage, out isAir);
        }

        public bool CancelActiveAttackStage(bool notify = true)
        {
            if (currentStage <= 0)
            {
                return false;
            }

            if (notify)
            {
                AttackStageEnded?.Invoke(currentStage, stageIsAir);
            }

            currentStage = 0;
            stageTimer = 0f;
            comboResetTimer = 0f;
            stageIsAir = false;
            nextStageOverride = 0;
            nextStageOverrideForceAir = false;
            uppercutFromCombo = false;
            return true;
        }

        private bool TryStartNextStage(out int stage, out bool isAir)
        {
            stage = 0;
            isAir = false;

            if (enableDebugLogs)
                Debug.Log($"[CombatController] TryStartNextStage - currentStage ANTES: {currentStage}, comboResetTimer: {comboResetTimer}");

            if (isStaggered)
            {
                return false;
            }

            if (nextStageOverride > 0)
            {
                currentStage = Mathf.Clamp(nextStageOverride, 1, MaxOverrideStage);
                stageIsAir = nextStageOverrideForceAir || (movement && !movement.IsGrounded);
                nextStageOverride = 0;
                nextStageOverrideForceAir = false;
            }
            else
            {
                if (comboResetTimer <= 0f)
                {
                    if (enableDebugLogs)
                        Debug.Log($"[CombatController] comboResetTimer <= 0, zerando currentStage");
                    currentStage = 0;
                }
                currentStage = Mathf.Clamp(currentStage + 1, 1, 3);
                stageIsAir = movement && !movement.IsGrounded;
            }

            if (enableDebugLogs)
                Debug.Log($"[CombatController] TryStartNextStage - currentStage DEPOIS: {currentStage}, stageIsAir: {stageIsAir}");

            stageTimer = comboStageDuration;
            AttackStageStarted?.Invoke(currentStage, stageIsAir);
            stage = currentStage;
            isAir = stageIsAir;
            comboResetTimer = comboResetTime;
            return true;
        }

        public void HoldComboReset()
        {
            comboResetTimer = Mathf.Max(comboResetTimer, comboResetTime);
            comboResetHold = true;
        }

        public void ReleaseComboReset(bool resetTimer = true)
        {
            if (enableDebugLogs)
                Debug.Log($"[CombatController] ReleaseComboReset(resetTimer={resetTimer}) - currentStage ANTES: {currentStage}, comboResetTimer ANTES: {comboResetTimer}");
            
            comboResetHold = false;
            if (resetTimer)
            {
                comboResetTimer = 0f;
                currentStage = 0; // Zera o stage também para forçar reset do combo
            }
            
            if (enableDebugLogs)
                Debug.Log($"[CombatController] ReleaseComboReset - currentStage DEPOIS: {currentStage}, comboResetTimer DEPOIS: {comboResetTimer}");
        }

        public bool IsComboResetHeld => comboResetHold;

        public void OverrideNextStage(int stage, bool forceAir, bool fromCombo = false)
        {
            nextStageOverride = Mathf.Clamp(stage, 1, MaxOverrideStage);
            nextStageOverrideForceAir = forceAir;
            
            // Marca se uppercut (stage 4) veio de um combo
            if (stage == 4)
            {
                uppercutFromCombo = fromCombo;
            }
        }

        private void UpdateKnockdown(float dt)
        {
            if (!isKnockedDown) return;
            if (knockdownPermanent) return;
            knockdownTimer -= dt;
            if (knockdownTimer <= 0f)
            {
                isKnockedDown = false;
                KnockdownStateChanged?.Invoke(false);
            }
        }

        private void UpdateStagger(float dt)
        {
            if (!isStaggered) return;
            staggerTimer -= dt;
            if (staggerTimer <= 0f)
            {
                isStaggered = false;
            }
        }

        public bool EvaluateBlock(Vector3 attackerWorldPosition, ref float damage, bool isProjectile)
        {
            if (!isBlocking || movement == null || isStaggered)
                return false;

            Vector3 toAttacker = attackerWorldPosition - movement.transform.position;
            float dot = Vector3.Dot(movement.FacingDirection.normalized, toAttacker.normalized);
            bool facingAttacker = dot < 0f; // atacante está em frente
            if (!facingAttacker)
                return false;

            float reduction = Mathf.Clamp01(blockDamageReduction);
            damage *= (1f - reduction);
            return true;
        }

        public bool EvaluateBlockByDirection(Vector3 attackDirectionWorld, ref float damage, bool isProjectile)
        {
            if (!isBlocking || movement == null || isStaggered)
                return false;
            attackDirectionWorld.Normalize();
            Vector3 facing = movement.FacingDirection.normalized;
            bool facingAttack = Vector3.Dot(facing, -attackDirectionWorld) > 0f;
            if (!facingAttack)
                return false;
            float reduction = Mathf.Clamp01(blockDamageReduction);
            damage *= (1f - reduction);
            return true;
        }

        public void BreakBlock()
        {
            if (!isBlocking) return;
            guardBroken = true;
            isBlocking = false;
            blockStamina = 0f;
        }

        public void ProcessIncomingHit(in NitssLocomotionController.HitInfo hit)
        {
            if (movement == null) return;
            if (isBlocking && hit.heavy)
            {
                blockStamina = Mathf.Max(0f, blockStamina - blockStaminaCostHeavy);
                if (blockStamina <= 0f)
                {
                    BreakBlock();
                }
            }
            if (hit.verticalLaunchVelocity > 0f)
            {
                // Se tem limite de altura (uppercut), usa método com clamp
                if (hit.maxLaunchHeight > 0f)
                {
                    movement.BumpVerticalClamped(hit.verticalLaunchVelocity, hit.maxLaunchHeight);
                    if (enableDebugLogs)
                        Debug.Log($"[CombatController] Uppercut launch com altura máxima {hit.maxLaunchHeight}m");
                }
                else
                {
                    movement.BumpVertical(hit.verticalLaunchVelocity);
                }
                movement.ForceAirborneStateForCombo();
                
                // Notifica Damageable para iniciar air juggle
                var damageable = GetComponent<Damageable>();
                if (damageable != null)
                {
                    damageable.StartAirJuggle(hit.heavy);
                }
            }
            if (!hit.suppressPlanarPush && hit.planarLaunchSpeed != 0f)
            {
                Vector3 dir = (movement.transform.position - hit.attackerWorldPos).normalized;
                dir.y = 0f;
                movement.AddExternalPush(dir * hit.planarLaunchSpeed);
            }
            
            // Não causa knockdown se tiver lançamento vertical (uppercut)
            // O uppercut lança para cima mas não derruba
            if (hit.heavy && hit.verticalLaunchVelocity <= 0f)
            {
                EnterKnockdown(hit.heavy, knockdownDuration);
            }
        }

        public void NotifyAirAttackHit()
        {
            movement?.NotifyAirComboHit(0f, 0f);
        }

        public void NotifyUppercutLaunchHit()
        {
            // Pequeno impulso extra para enfatizar lançamento
            movement?.BumpVertical(5f);
        }

        public void EnterKnockdown(bool heavy, float duration)
        {
            isKnockedDown = true;
            knockdownPermanent = false;
            knockdownTimer = duration > 0f ? duration : knockdownDuration;
            KnockdownStateChanged?.Invoke(true);
            isStaggered = false;
        }

        public void EnterPermanentKnockdown(bool heavy)
        {
            isKnockedDown = true;
            knockdownPermanent = true;
            KnockdownStateChanged?.Invoke(true);
            isStaggered = false;
        }

        public void ExitKnockdown()
        {
            isKnockedDown = false;
            knockdownPermanent = false;
            KnockdownStateChanged?.Invoke(false);
            isStaggered = false;
        }

        public void HealGuard(float amount)
        {
            blockStamina = Mathf.Clamp(blockStamina + amount, 0f, blockStaminaMax);
            if (blockStamina > 0f)
            {
                guardBroken = false;
            }
        }

        public void ApplyStagger(float duration)
        {
            if (duration <= 0f) return;
            
            // Apenas seta flag de stagger e timer, NÃO cancela stage atual
            // (permite continuar recebendo hits e animações durante stagger)
            isStaggered = true;
            staggerTimer = Mathf.Max(staggerTimer, duration);
            isBlocking = false;
        }

        /// <summary>
        /// Dispara manualmente o evento AttackStageStarted.
        /// Usado por módulos externos como JumpAttackModule para notificar hitboxes.
        /// </summary>
        public void NotifyAttackStageStarted(int stage, bool isAir)
        {
            AttackStageStarted?.Invoke(stage, isAir);
            if (enableDebugLogs)
                Debug.Log($"[CombatController] AttackStageStarted notificado manualmente: stage={stage}, isAir={isAir}");
        }

        /// <summary>
        /// Dispara manualmente o evento AttackStageEnded.
        /// Usado por módulos externos como JumpAttackModule para notificar hitboxes.
        /// </summary>
        public void NotifyAttackStageEnded(int stage, bool isAir)
        {
            AttackStageEnded?.Invoke(stage, isAir);
            if (enableDebugLogs)
                Debug.Log($"[CombatController] AttackStageEnded notificado manualmente: stage={stage}, isAir={isAir}");
        }
    }
}
