using System;
using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Orquestração básica do personagem Nitss. Agrega input, movimento, combate e sincroniza eventos.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    [RequireComponent(typeof(NitssMovementController))]
    [RequireComponent(typeof(NitssCombatController))]
    public sealed class NitssLocomotionController : MonoBehaviour
    {
        [Serializable]
        public struct HitInfo
        {
            public float damage;
            public bool heavy;
            public bool ignoresStamina;
            public Vector3 attackerWorldPos;
            public bool isProjectile;
            public float verticalLaunchVelocity;
            public bool suppressPlanarPush;
            public float planarLaunchSpeed;
        }

        public event Action<int, bool> OnAttackStageStart;
        public event Action<int, bool> OnAttackStageEnd;

        [Header("Referências")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssMovementController movement;
        [SerializeField] private NitssCombatController combat;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            inputReader = GetComponent<NitssInputReader>();
            movement = GetComponent<NitssMovementController>();
            combat = GetComponent<NitssCombatController>();
        }

        private void Awake()
        {
            if (!context) context = GetComponent<NitssCharacterContext>();
            if (!inputReader) inputReader = GetComponent<NitssInputReader>();
            if (!movement) movement = GetComponent<NitssMovementController>();
            if (!combat) combat = GetComponent<NitssCombatController>();

            if (combat != null)
            {
                combat.AttackStageStarted += HandleAttackStageStarted;
                combat.AttackStageEnded += HandleAttackStageEnded;
            }
        }

        private void OnDestroy()
        {
            if (combat != null)
            {
                combat.AttackStageStarted -= HandleAttackStageStarted;
                combat.AttackStageEnded -= HandleAttackStageEnded;
            }
        }

        private void Update()
        {
            if (!inputReader || !movement || !combat)
                return;

            inputReader.Sample();
            float dt = Time.deltaTime;
            movement.Tick(dt, inputReader);
            combat.Tick(dt, inputReader);
        }

        private void HandleAttackStageStarted(int stage, bool isAir)
        {
            OnAttackStageStart?.Invoke(stage, isAir);
        }

        private void HandleAttackStageEnded(int stage, bool isAir)
        {
            OnAttackStageEnd?.Invoke(stage, isAir);
        }

        public void OnHit(in HitInfo hit)
        {
            combat?.ProcessIncomingHit(hit);
        }

        public bool IsKnockedDown => combat != null && combat.IsKnockedDown;
        public bool IsBlocking => combat != null && combat.IsBlocking;
        public bool GuardBroken => combat != null && combat.GuardBroken;
        public bool IsAirAttacking => combat != null && combat.IsAirAttacking;
        public float KnockdownStaminaNormalized => combat != null ? combat.KnockdownStaminaNormalized : 0f;
        public float KnockdownStamina => combat != null ? combat.KnockdownStamina : 0f;

        public bool EvaluateBlock(Vector3 attackerWorldPosition, ref float damage, bool isProjectile)
        {
            return combat != null && combat.EvaluateBlock(attackerWorldPosition, ref damage, isProjectile);
        }

        public bool EvaluateBlockByDirection(Vector3 attackDirectionWorld, ref float damage, bool isProjectile)
        {
            return combat != null && combat.EvaluateBlockByDirection(attackDirectionWorld, ref damage, isProjectile);
        }

        public void BreakBlock()
        {
            combat?.BreakBlock();
        }

        public void EnterPermanentKnockdown(bool heavy)
        {
            combat?.EnterPermanentKnockdown(heavy);
        }

        public void EnterKnockdown(bool heavy, float duration)
        {
            combat?.EnterKnockdown(heavy, duration);
        }

        public void ExitKnockdown()
        {
            combat?.ExitKnockdown();
        }
    }
}
