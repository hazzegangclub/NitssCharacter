// Disabled duplicate copy after moving to Characters/Nitss/Simple.
#if false
using System.Collections.Generic;
using UnityEngine;
using Hazze.Gameplay.Combat;
using Hazze.Gameplay.Characters.Nitss;

/// <summary>
/// Componente simples para aplicar dano em inimigos em alcance quando um estágio de ataque começa.
/// Escuta NitssLocomotionController.OnAttackStageStart e faz um OverlapSphere/Box.
/// Ideal para validar rapidamente animações de dano em um dummy duplicado.
/// </summary>
[DisallowMultipleComponent]
public class SimpleAttackImpact : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Controller de locomotion do atacante (compatibilidade). Se vazio, procura no próprio GameObject.")]
    public NitssLocomotionController locomotion;
    [Tooltip("Opcional: se não houver locomotion, usa o controlador de combate para eventos de ataque.")]
    public NitssCombatController combat;
    [Tooltip("Opcional: ponto de origem do hit (ex.: ponta da arma). Se vazio, usa o transform do locomotion.")]
    public Transform hitOriginOverride;

    // Alcance estático removido – componente opera apenas no modo dinâmico

    [Header("Dano por Estágio")]
    [Tooltip("Dano aplicado no Attack1.")] public float attack1Damage = 12f;
    [Tooltip("Dano aplicado no Attack2.")] public float attack2Damage = 16f;
    [Tooltip("Dano aplicado no Attack3.")] public float attack3Damage = 22f;
    [Tooltip("Tipo do dano (Melee/Projectile)")] public DamageType damageType = DamageType.Melee;
    [Tooltip("Ignora block/stamina (unblockable).")] public bool unblockable = false;
    [Tooltip("Dano aplicado no Uppercut (stage especial). Se 0, reutiliza attack3Damage.")] public float uppercutDamage = 26f;
    [Header("Uppercut Config")]
    [Tooltip("Stage lógico que representa o Uppercut informado pelo CombatController.")] public int uppercutStage = 4;
    [Tooltip("Tratar uppercut como heavy para guard break / stamina.")] public bool uppercutIsHeavy = true;
    [Tooltip("Impulso vertical aplicado ao alvo quando acerta uppercut (m/s). 0 = sem lançamento.")] public float uppercutVerticalLaunch = 5.5f;
    [Tooltip("Delay antes de aplicar o hitbox/dano do uppercut (segundos) para sincronizar com a animação.")] public float uppercutHitDelaySeconds = 0.12f;

    // Delay normais removidos — dinamico já segue a animação

    [Header("Filtros")]
    [Tooltip("Layers considerados como alvos válidos.")] public LayerMask targetLayers = ~0;
    [Tooltip("Opcional: exigir componente Damageable para aplicar dano (recomendado).")]
    public bool requireDamageable = true;
    [Tooltip("Distância mínima para considerar alvo (>=0). 0 ignora.")] public float minDistance = 0f;
    [Tooltip("Se informado, só acerta objetos com esta Tag (ex.: 'Enemy'). Vazio = sem filtro por Tag.")]
    public string requiredTargetTag = "";

    // Forma do hit estático removida – dinâmica usa sua própria cápsula entre base e ponta

    // Ajustes por estágio removidos: offsets/multiplicadores por estágio não são mais suportados

    [Header("Debug")] public bool debugDraw = true;

    [Header("Dynamic Weapon Mode (Único)")]
    [Tooltip("Usar hitbox dinâmico seguindo base/ponta da arma ao longo do estágio.")] public bool useDynamicWeaponHitbox = true;
    [Tooltip("Transform da base da arma (ex.: mão/empunhadura). ")] public Transform weaponBase;
    [Tooltip("Transform da ponta da arma (ex.: ponta do bastão). ")] public Transform weaponTip;
    [Tooltip("Raio da cápsula formada entre base e ponta.")] public float dynamicCapsuleRadius = 0.35f;
    [Tooltip("Fazer sweep da ponta para cobrir deslocamentos rápidos.")] public bool dynamicUseTipSweep = true;
    [Tooltip("Raio do sweep da ponta (0 usa dynamicCapsuleRadius). ")] public float dynamicTipSweepRadius = 0.35f;
    [Tooltip("Subamostras adicionais entre ponta anterior e atual (0..6)")][Range(0,6)] public int dynamicSweepSubSamples = 2;
    [Tooltip("Evitar múltiplos hits no mesmo estágio.")] public bool dynamicPreventMultipleHitsSameStage = true;

    // Estado interno do modo dinâmico
    private bool _dynActive;
    private int _dynStage;
    private bool _dynIsAir;
    private Vector3 _dynPrevTip;
    private readonly HashSet<Damageable> _dynHitThisStage = new HashSet<Damageable>();
    // Snapshot de geometria removido (somente dinâmica)

    /// <summary>Verdadeiro se modo dinâmico está ativo neste frame.</summary>
    public bool DynamicActive => useDynamicWeaponHitbox && _dynActive;
    /// <summary>Estágio atual do modo dinâmico (0 se inativo).</summary>
    public int DynamicStage => _dynStage;
    /// <summary>Posição atual da base da arma (fallback: transform).</summary>
    public Vector3 DynamicBase => weaponBase ? weaponBase.position : transform.position;
    /// <summary>Posição atual da ponta da arma (fallback: transform).</summary>
    public Vector3 DynamicTip => weaponTip ? weaponTip.position : transform.position;
    // Propriedades de snapshot removidas

    private void Awake()
    {
        if (!locomotion)
            locomotion = GetComponentInParent<NitssLocomotionController>() ?? GetComponent<NitssLocomotionController>();
        if (locomotion)
        {
            locomotion.OnAttackStageStart += OnAttackStageStart;
            locomotion.OnAttackStageEnd += OnAttackStageEnd;
        }
        if (!combat)
            combat = GetComponentInParent<NitssCombatController>() ?? GetComponent<NitssCombatController>();
        if (!locomotion && combat)
        {
            combat.AttackStageStarted += OnAttackStageStart;
            combat.AttackStageEnded += OnAttackStageEnd;
        }
    }

    private void OnDestroy()
    {
        if (locomotion)
        {
            locomotion.OnAttackStageStart -= OnAttackStageStart;
            locomotion.OnAttackStageEnd -= OnAttackStageEnd;
        }
        if (combat)
        {
            combat.AttackStageStarted -= OnAttackStageStart;
            combat.AttackStageEnded -= OnAttackStageEnd;
        }
    }

    private void OnAttackStageStart(int stage, bool isAir)
    {
        // Apenas modo dinâmico: ativa varredura após o delay do estágio
        if (!useDynamicWeaponHitbox || !weaponBase || !weaponTip) return;
        float dynDelay = 0f;
        if (stage == uppercutStage) dynDelay = uppercutHitDelaySeconds;

        _dynStage = stage; _dynIsAir = isAir; _dynHitThisStage.Clear();
        if (dynDelay > 0f)
        {
            StartCoroutine(DynamicEnableAfter(dynDelay));
        }
        else
        {
            _dynActive = true;
            _dynPrevTip = weaponTip.position;
        }
    }

    // Rotina de atraso para habilitar o modo dinâmico

    private System.Collections.IEnumerator DynamicEnableAfter(float delay)
    {
        float t = 0f;
        while (t < delay)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (this && enabled && weaponTip)
        {
            _dynActive = true;
            _dynPrevTip = weaponTip.position;
        }
    }

    private void OnAttackStageEnd(int stage, bool isAir)
    {
        if (useDynamicWeaponHitbox)
        {
            if (stage == _dynStage)
            {
                _dynActive = false;
                _dynStage = 0;
                _dynHitThisStage.Clear();
            }
        }
    }

    private void Update()
    {
        if (!useDynamicWeaponHitbox || !_dynActive) return;
        if (!weaponBase || !weaponTip) return;
        // Coleta cápsula entre base e ponta
        Vector3 p0 = weaponBase.position;
        Vector3 p1 = weaponTip.position;
        float r = Mathf.Max(0f, dynamicCapsuleRadius);
        var cols = Physics.OverlapCapsule(p0, p1, r, targetLayers, QueryTriggerInteraction.Collide);
        if (debugDraw)
        {
            Debug.DrawLine(p0, p1, Color.cyan, 0.02f);
        }
        ProcessDynamicColliders(cols);
        // Sweep da ponta
        if (dynamicUseTipSweep)
        {
            Vector3 from = _dynPrevTip;
            Vector3 to = weaponTip.position;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist > 0.0001f)
            {
                Vector3 dir = delta / dist;
                int samples = dynamicSweepSubSamples + 1;
                float rr = dynamicTipSweepRadius > 0f ? dynamicTipSweepRadius : r;
                for (int i = 0; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    Vector3 pos = from + dir * (dist * t);
                    var s = Physics.OverlapSphere(pos, rr, targetLayers, QueryTriggerInteraction.Collide);
                    if (debugDraw)
                    {
                        DebugDrawSphere(pos, rr, Color.yellow, 0.02f);
                    }
                    ProcessDynamicColliders(s);
                }
            }
            _dynPrevTip = to;
        }
    }

    // VFX: métodos removidos deste componente

    private void ProcessDynamicColliders(Collider[] hits)
    {
        if (hits == null || hits.Length == 0) return;
        var tr = locomotion ? locomotion.transform : (combat ? combat.transform : transform);
        bool uppercutLaunchAppliedDynamic = false;
        float verticalLaunch = (_dynStage == uppercutStage) ? Mathf.Max(0f, uppercutVerticalLaunch) : 0f;
        foreach (var h in hits)
        {
            if (!h) continue;
            if (h.transform.root == tr.root) continue; // ignora self
            if (!string.IsNullOrEmpty(requiredTargetTag))
            {
                var root = h.transform.root;
                if (!root || !root.CompareTag(requiredTargetTag)) continue;
            }
            if (minDistance > 0f && Vector3.Distance(tr.position, h.transform.position) < minDistance) continue;
            Damageable dmgComp = null;
            if (requireDamageable)
            {
                dmgComp = h.GetComponentInParent<Damageable>() ?? h.GetComponent<Damageable>();
                if (!dmgComp) continue;
            }
            else
            {
                dmgComp = h.GetComponentInParent<Damageable>() ?? h.GetComponent<Damageable>();
            }
            if (dynamicPreventMultipleHitsSameStage && dmgComp != null && _dynHitThisStage.Contains(dmgComp))
                continue;

            // Resolve dano e flags a partir da configuração existente
            float damage = (_dynStage == 1) ? attack1Damage : (_dynStage == 2 ? attack2Damage : (_dynStage == 3 ? attack3Damage : attack3Damage));
            if (_dynStage == uppercutStage)
            {
                damage = uppercutDamage > 0f ? uppercutDamage : attack3Damage;
            }
            bool heavy = (_dynStage == 3) || (_dynStage == uppercutStage && uppercutIsHeavy);

            if (dmgComp)
            {
                dmgComp.ApplyDamageCombo(damage, tr.position, _dynStage, locomotion ? (Object)locomotion : (Object)tr.root,
                    damageType, heavyGuardBreak: heavy, unblockable: unblockable);
            }

            var targetLocomotion = h.GetComponentInParent<NitssLocomotionController>() ?? h.GetComponent<NitssLocomotionController>();
            if (targetLocomotion)
            {
                var hitInfo = new NitssLocomotionController.HitInfo
                {
                    damage = damage,
                    heavy = heavy,
                    ignoresStamina = unblockable,
                    attackerWorldPos = tr.position,
                    isProjectile = damageType == DamageType.Projectile,
                    verticalLaunchVelocity = verticalLaunch,
                    suppressPlanarPush = false,
                    planarLaunchSpeed = 0f
                };
                targetLocomotion.OnHit(in hitInfo);
                // Sustentação de combo aéreo: atacante recebe impulso/hover por hit aéreo
                if (combat && combat.IsAirAttacking)
                {
                    combat.NotifyAirAttackHit();
                }
                if (verticalLaunch > 0f)
                {
                    uppercutLaunchAppliedDynamic = true;
                }
            }

            if (dynamicPreventMultipleHitsSameStage && dmgComp != null)
                _dynHitThisStage.Add(dmgComp);
        }

        if (_dynStage == uppercutStage && uppercutLaunchAppliedDynamic && combat)
        {
            combat.NotifyUppercutLaunchHit();
        }
    }

    // Removido: ApplyStageAdjustments

    // Nenhum impulso planar customizado: uppercut agora aplica apenas lançamento vertical

    private void DebugDrawSphere(Vector3 center, float r, Color c, float duration)
    {
        // Desenha 'wireframe' improvisado
        int steps = 16;
        for (int i = 0; i < steps; i++)
        {
            float a0 = (i / (float)steps) * Mathf.PI * 2f;
            float a1 = ((i + 1) / (float)steps) * Mathf.PI * 2f;
            Vector3 p0 = center + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
            Debug.DrawLine(p0, p1, c, duration);
        }
    }
}
#endif
