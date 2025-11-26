using System.Collections.Generic;
using UnityEngine;
using Hazze.Gameplay.Combat; // Damageable
using Hazze.Gameplay.Characters.Nitss;

/// <summary>
/// Configuração de lançamento vertical para um stage específico.
/// </summary>
[System.Serializable]
public struct LaunchConfig
{
    [Tooltip("Stage que aplica este lançamento (0 = desabilitado).")]
    public int stageIndex;
    
    [Tooltip("Velocidade vertical do lançamento (m/s). 0 = sem lançamento.")]
    public float launchVelocity;
    
    [Tooltip("Altura máxima do lançamento (unidades). 0 = sem limite.")]
    public float maxHeight;
    
    [Tooltip("Só aplica se vier de combo (precisa de stages anteriores).")]
    public bool requiresCombo;
}

/// <summary>
/// Hitbox dinâmico que segue o movimento real do bastão/arma.
/// 1) Cada frame gera uma cápsula entre base e ponta da arma.
/// 2) Opcionalmente faz um sweep (SphereCast) da ponta anterior até a ponta atual para pegar trajetórias rápidas.
/// 3) Evita múltiplos hits no mesmo estágio usando um HashSet.
/// Escuta eventos de estágio de ataque do NitssCombatController ou NitssLocomotionController.
/// </summary>
[DisallowMultipleComponent]
public class DynamicWeaponHitbox : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Locomotion ou root para encontrar eventos.")] public NitssLocomotionController locomotion;
    [Tooltip("Combat controller (fallback se locomotion nulo). ")] public NitssCombatController combat;
    [Tooltip("Transform na base da arma (ex.: mão ou ponto fixo).")] public Transform weaponBase;
    [Tooltip("Transform na ponta da arma (ex.: extremidade do bastão).")] public Transform weaponTip;

    [Header("Configuração de Estágios")]
    [Tooltip("Quais estágios usam este hitbox dinâmico.")] public int[] stages = new int[] { 1, 2, 3, 4 };
    [Tooltip("Dano por estágio (tamanho deve cobrir maior índice requerido). Se vazio cai em defaultDamage.")] public float[] damagePerStage = System.Array.Empty<float>();
    [Tooltip("Dano padrão quando não houver entrada dedicada.")] public float defaultDamage = 10f;
    [Tooltip("Marcar estágio como heavy (knockdown/stamina) – índices 1..n.")] public bool[] heavyStageFlags = System.Array.Empty<bool>();
    [Tooltip("Tratar uppercut (stage especial) como heavy se não listado em heavyStageFlags.")] public bool heavyFallbackForStage4 = true;

    [Header("Forma da Hitbox")]
    [Tooltip("Raio da cápsula formada entre base e ponta.")] public float capsuleRadius = 0.35f;
    [Tooltip("Usar sweep da ponta para cobrir deslocamento rápido.")] public bool useTipSweep = true;
    [Tooltip("Raio utilizado no sweep da ponta (se <=0 usa capsuleRadius). ")] public float tipSweepRadius = 0.35f;
    [Tooltip("Quantas subamostras extras (intervalos) entre a ponta anterior e atual para melhorar cobertura (0 = nenhuma). ")] [Range(0,6)] public int sweepSubSamples = 2;

    [Header("Filtros")]
    [Tooltip("Layers que podem ser atingidos.")] public LayerMask targetLayers = ~0;
    [Tooltip("Exigir componente Damageable nos alvos.")] public bool requireDamageable = true;
    [Tooltip("Tag obrigatória (vazio = ignora). ")] public string requiredTargetTag = "";
    [Tooltip("Evitar múltiplos hits no mesmo estágio.")] public bool preventMultipleHitsSameStage = true;
    [Tooltip("Tempo (segundos) após o qual um alvo pode ser atingido novamente, mesmo no mesmo stage contínuo. 0 = bloqueio permanente até trocar stage.")]
    [Min(0f)] public float hitCooldownPerTarget = 0.15f;

    [Header("Debug")] public bool debugDraw = true;

    [Header("Performance")]
    [Tooltip("Número máximo de colisões consideradas em cada amostra. Valores maiores aumentam o custo, menores podem ignorar alvos extras.")]
    [Min(1)] public int maxCollidersPerSample = 16;
    [Tooltip("Avisar no console quando o buffer de colisões lotar e for necessário aumentar maxCollidersPerSample.")]
    public bool warnOnBufferOverflow = true;

    [Header("Vertical Launch System")]
    [Tooltip("Configurações de lançamento vertical por stage. Permite múltiplos ataques com diferentes alturas.")]
    public LaunchConfig[] launchConfigs = new LaunchConfig[]
    {
        new LaunchConfig { stageIndex = 4, launchVelocity = 8f, maxHeight = 5f, requiresCombo = true }
    };
    
    [Header("Uppercut Follow-Up Auto (Opcional)")]
    [Tooltip("Stage considerado como uppercut (default 4). Usado para decidir se deve lançar combo aéreo automático.")] public int uppercutStageIndex = 4;
    [Tooltip("Notificar CombatController para iniciar combo aéreo após uppercut conectar.")] public bool notifyCombatOnUppercutLaunch = true;

    private bool _active;
    private int _currentStage;
    private bool _currentStageIsAir;
    private Vector3 _prevTip;
    private readonly HashSet<Damageable> _hitThisStage = new HashSet<Damageable>();
    private readonly Dictionary<Damageable, float> _lastHitTime = new Dictionary<Damageable, float>();
    private Collider[] _overlapBuffer;
    private Transform _attackerRoot;
    private NitssComboController _comboController;

    private void Awake()
    {
        AllocateBuffers();
        if (!locomotion)
            locomotion = GetComponentInParent<NitssLocomotionController>() ?? GetComponent<NitssLocomotionController>();
        if (!combat)
            combat = GetComponentInParent<NitssCombatController>() ?? GetComponent<NitssCombatController>();
        
        // Busca combo controller para pegar configurações de uppercut
        _comboController = GetComponentInParent<NitssComboController>() ?? GetComponent<NitssComboController>();
        if (locomotion)
        {
            locomotion.OnAttackStageStart += HandleStageStart;
            locomotion.OnAttackStageEnd += HandleStageEnd;
        }
        else if (combat)
        {
            combat.AttackStageStarted += HandleStageStart;
            combat.AttackStageEnded += HandleStageEnd;
        }
    }

    private void OnDestroy()
    {
        if (locomotion)
        {
            locomotion.OnAttackStageStart -= HandleStageStart;
            locomotion.OnAttackStageEnd -= HandleStageEnd;
        }
        if (combat)
        {
            combat.AttackStageStarted -= HandleStageStart;
            combat.AttackStageEnded -= HandleStageEnd;
        }
    }

    private void OnValidate()
    {
        if (maxCollidersPerSample < 1) maxCollidersPerSample = 1;
        if (!Application.isPlaying) AllocateBuffers();
    }

    private void AllocateBuffers()
    {
        int size = Mathf.Max(1, maxCollidersPerSample);
        if (_overlapBuffer == null || _overlapBuffer.Length != size)
            _overlapBuffer = new Collider[size];
    }

    private void HandleStageStart(int stage, bool isAir)
    {
        if (!EnabledForStage(stage)) return;
        if (!weaponBase || !weaponTip) return;
        _active = true;
        _currentStage = stage;
        _currentStageIsAir = isAir;
        _hitThisStage.Clear();
        _prevTip = weaponTip.position;
        _attackerRoot = locomotion ? locomotion.transform.root : (combat ? combat.transform.root : transform.root);
    }

    private void HandleStageEnd(int stage, bool isAir)
    {
        if (stage != _currentStage) return;
        _active = false;
        _currentStage = 0;
        _hitThisStage.Clear();
        _attackerRoot = null;
    }

    private bool EnabledForStage(int stage)
    {
        if (stages == null || stages.Length == 0) return true; // se não definido, assume todos
        for (int i = 0; i < stages.Length; i++)
            if (stages[i] == stage) return true;
        return false;
    }

    private void Update()
    {
        if (!_active) return;
        if (!weaponBase || !weaponTip) return;

        Vector3 tip = weaponTip.position;
        Vector3 basePos = weaponBase.position;
        // Captura cápsula instantânea
        ApplyCapsuleDamage(basePos, tip);
        // Sweep da ponta
        if (useTipSweep)
        {
            float r = tipSweepRadius > 0f ? tipSweepRadius : capsuleRadius;
            SweepTip(_prevTip, tip, r);
        }
        _prevTip = tip;
    }

    private void ApplyCapsuleDamage(Vector3 p0, Vector3 p1)
    {
        float radius = capsuleRadius;
        int hitCount = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, _overlapBuffer, targetLayers, QueryTriggerInteraction.Collide);
        WarnIfOverflow(hitCount);
        if (debugDraw)
        {
            Debug.DrawLine(p0, p1, Color.cyan, 0.02f, false);
            DebugDrawCircle(p0, radius, Color.cyan);
            DebugDrawCircle(p1, radius, Color.cyan);
        }
        ProcessColliders(hitCount);
    }

    private void SweepTip(Vector3 from, Vector3 to, float radius)
    {
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= 0.0001f) return;
        Vector3 dir = delta / dist;

        int samples = sweepSubSamples + 1;
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 pos = from + dir * (dist * t);
            int hitCount = Physics.OverlapSphereNonAlloc(pos, radius, _overlapBuffer, targetLayers, QueryTriggerInteraction.Collide);
            WarnIfOverflow(hitCount);
            if (debugDraw)
            {
                DebugDrawCircle(pos, radius, Color.yellow);
            }
            ProcessColliders(hitCount);
        }
    }

    private void ProcessColliders(int hitCount)
    {
        if (hitCount <= 0) return;
        var attackerRoot = _attackerRoot ? _attackerRoot : (locomotion ? locomotion.transform.root : (combat ? combat.transform.root : transform.root));
        bool notifiedUppercutLaunch = false;
        bool isUppercutStage = _currentStage == uppercutStageIndex;
        
        // Procura configuração de launch para o stage atual
        LaunchConfig? activeLaunch = null;
        float verticalLaunch = 0f;
        float maxLaunchHeight = 0f;
        bool isUppercutFromCombo = isUppercutStage && combat != null && combat.IsUppercutFromCombo;
        
        // Se for uppercut de combo, usa valores do ComboController (prioritário)
        if (isUppercutFromCombo && _comboController != null)
        {
            verticalLaunch = _comboController.UppercutLaunchVelocity;
            maxLaunchHeight = _comboController.UppercutMaxHeight;
            Debug.Log($"[DynamicWeaponHitbox] Usando valores do ComboController - velocity={verticalLaunch}, maxHeight={maxLaunchHeight}");
        }
        else
        {
            // Caso contrário, procura nos launchConfigs
            foreach (var config in launchConfigs)
            {
                if (config.stageIndex == _currentStage)
                {
                    // Verifica se precisa de combo
                    bool canApply = !config.requiresCombo || (combat != null && combat.IsUppercutFromCombo);
                    
                    if (canApply)
                    {
                        activeLaunch = config;
                        verticalLaunch = Mathf.Max(0f, config.launchVelocity);
                        maxLaunchHeight = config.maxHeight;
                        Debug.Log($"[DynamicWeaponHitbox] Launch config found for stage {_currentStage}: velocity={verticalLaunch}, maxHeight={maxLaunchHeight}, requiresCombo={config.requiresCombo}");
                        break;
                    }
                }
            }
        }
        for (int i = 0; i < hitCount; i++)
        {
            var c = _overlapBuffer[i];
            if (!c) continue;
            if (c.transform.root == attackerRoot) continue; // ignora self
            if (!string.IsNullOrEmpty(requiredTargetTag))
            {
                var root = c.transform.root;
                if (!root || !root.CompareTag(requiredTargetTag)) continue;
            }
            Damageable dmg = null;
            if (requireDamageable)
            {
                dmg = c.GetComponentInParent<Damageable>() ?? c.GetComponent<Damageable>();
                if (!dmg) continue;
            }
            else
            {
                dmg = c.GetComponentInParent<Damageable>() ?? c.GetComponent<Damageable>();
            }
            
            // Prevenir múltiplos hits no mesmo stage
            if (preventMultipleHitsSameStage && dmg != null)
            {
                // Se já acertou este alvo no stage atual, bloqueia SEMPRE
                if (_hitThisStage.Contains(dmg))
                {
                    Debug.Log($"[DynamicWeaponHitbox] HIT BLOQUEADO - Target {dmg.gameObject.name} já foi acertado no Stage={_currentStage}");
                    continue;
                }
            }

            float damage = ResolveDamageForStage(_currentStage);
            bool heavy = ResolveHeavyForStage(_currentStage);
            Vector3 attackerPos = attackerRoot.position;
            if (dmg)
            {
                Debug.Log($"[DynamicWeaponHitbox] Hit aplicado: Stage={_currentStage}, Heavy={heavy}, Damage={damage}, Target={dmg.gameObject.name}, Time={Time.time:F3}");
                
                dmg.ApplyDamageCombo(damage, attackerPos, _currentStage, locomotion ? (Object)locomotion : (Object)attackerRoot,
                    DamageType.Melee, heavyGuardBreak: heavy, unblockable: false, isHeavyAttack: heavy);
                
                // Registra hit e timestamp IMEDIATAMENTE
                if (preventMultipleHitsSameStage)
                {
                    _hitThisStage.Add(dmg);
                    _lastHitTime[dmg] = Time.time;
                    Debug.Log($"[DynamicWeaponHitbox] Registrado hit em {dmg.gameObject.name} no Time={Time.time:F3}");
                }
            }
            // Notifica locomotion alvo (knockdown / push)
            var targetLocomotion = c.GetComponentInParent<NitssLocomotionController>() ?? c.GetComponent<NitssLocomotionController>();
            if (targetLocomotion)
            {
                // Se tem delay e é uppercut de combo, aplica hit sem lançamento primeiro e agenda o lançamento
                float launchDelay = (isUppercutFromCombo && _comboController != null) ? _comboController.UppercutLaunchDelay : 0f;
                
                var hitInfo = new NitssLocomotionController.HitInfo
                {
                    damage = damage,
                    heavy = heavy,
                    ignoresStamina = false,
                    attackerWorldPos = attackerPos,
                    isProjectile = false,
                    verticalLaunchVelocity = launchDelay > 0f ? 0f : verticalLaunch, // Se tem delay, não lança agora
                    maxLaunchHeight = launchDelay > 0f ? 0f : maxLaunchHeight,
                    suppressPlanarPush = false,
                    planarLaunchSpeed = 0f
                };
                targetLocomotion.OnHit(in hitInfo);
                
                // Agenda lançamento com delay se necessário
                if (launchDelay > 0f && verticalLaunch > 0f)
                {
                    StartCoroutine(ApplyDelayedLaunch(targetLocomotion, verticalLaunch, maxLaunchHeight, launchDelay, dmg, heavy));
                    Debug.Log($"[DynamicWeaponHitbox] Uppercut hit aplicado. Launch agendado para {launchDelay}s depois.");
                }
                // Log de lançamento vertical imediato
                else if (verticalLaunch > 0f)
                {
                    string comboStatus = isUppercutFromCombo ? "COMBO" : "DIRETO";
                    Debug.Log($"[DynamicWeaponHitbox] Launch aplicado ({comboStatus})! Stage={_currentStage}, Velocidade={verticalLaunch}m/s, Altura máx={maxLaunchHeight}m, Target={c.gameObject.name}");
                    notifiedUppercutLaunch = true;
                }
                else if (isUppercutStage)
                {
                    Debug.Log($"[DynamicWeaponHitbox] Stage {_currentStage} sem lançamento (nenhuma config ativa)");
                }
                
                if (combat && combat.IsAirAttacking)
                {
                    combat.NotifyAirAttackHit();
                }
            }
            if (preventMultipleHitsSameStage && dmg != null)
                _hitThisStage.Add(dmg);
        }

        if (notifyCombatOnUppercutLaunch && combat && _currentStage == uppercutStageIndex && notifiedUppercutLaunch)
        {
            combat.NotifyUppercutLaunchHit();
        }
    }

    // Nenhum impulso planar customizado: uppercut agora aplica apenas lançamento vertical

    private float ResolveDamageForStage(int stage)
    {
        if (damagePerStage != null && damagePerStage.Length >= stage && stage > 0)
        {
            float v = damagePerStage[stage - 1];
            if (v > 0f) return v;
        }
        return defaultDamage;
    }

    private System.Collections.IEnumerator ApplyDelayedLaunch(NitssLocomotionController targetLoco, float velocity, float maxHeight, float delay, Damageable damageable, bool heavy)
    {
        yield return new WaitForSeconds(delay);
        
        if (targetLoco != null)
        {
            // Aplica o lançamento vertical
            var launchInfo = new NitssLocomotionController.HitInfo
            {
                damage = 0f, // Sem dano adicional
                heavy = heavy,
                ignoresStamina = true,
                attackerWorldPos = transform.position,
                isProjectile = false,
                verticalLaunchVelocity = velocity,
                maxLaunchHeight = maxHeight,
                suppressPlanarPush = true, // Não empurra horizontalmente
                planarLaunchSpeed = 0f
            };
            targetLoco.OnHit(in launchInfo);
            
            // Inicia air juggle
            if (damageable != null)
            {
                damageable.StartAirJuggle(heavy);
            }
            
            Debug.Log($"[DynamicWeaponHitbox] DELAYED LAUNCH aplicado! Velocidade={velocity}m/s, Altura máx={maxHeight}m");
        }
    }

    private bool ResolveHeavyForStage(int stage)
    {
        if (heavyStageFlags != null && heavyStageFlags.Length >= stage && stage > 0)
        {
            return heavyStageFlags[stage - 1];
        }
        // Stage 3 (Attack3/JumpAttack3) e Stage 4 (Uppercut) são heavy attacks (Damage2)
        if (stage == 3) return true;
        if (stage == 4 && heavyFallbackForStage4) return true;
        return false;
    }

    private void DebugDrawCircle(Vector3 center, float r, Color c)
    {
        const int steps = 14;
        for (int i = 0; i < steps; i++)
        {
            float a0 = (i / (float)steps) * Mathf.PI * 2f;
            float a1 = ((i + 1) / (float)steps) * Mathf.PI * 2f;
            Vector3 p0 = center + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
            Debug.DrawLine(p0, p1, c, 0f);
        }
    }

    private void WarnIfOverflow(int hitCount)
    {
        if (!warnOnBufferOverflow) return;
        if (hitCount < _overlapBuffer.Length) return;
        Debug.LogWarning($"DynamicWeaponHitbox atingiu o limite do buffer ({_overlapBuffer.Length}). Considere aumentar 'maxCollidersPerSample'.", this);
    }
}
