using UnityEngine;
using Hazze.Gameplay.Combat;
using Hazze.Gameplay.Characters.Nitss;

/// <summary>
/// Spawner de VFX de slash desacoplado do dano. Escuta eventos de estágio de ataque
/// e instancia o VFX configurado, com controles de orientação, posição fixa e
/// ajustes por estágio (rotação/offset/escala).
/// </summary>
[DisallowMultipleComponent]
public class AttackSlashVFX : MonoBehaviour
{
    [System.Serializable]
    public struct StageOffsetConfig
    {
        [Range(1, 3)] public int stage;
        [Tooltip("Se verdadeiro, aplica quando o golpe estiver no ar.")]
        public bool appliesToAir;
        [Tooltip("Delta adicional no offset à frente (metros).")] public float forwardOffsetDelta;
        [Tooltip("Delta adicional no offset vertical (metros). ")] public float upOffsetDelta;
        [Tooltip("Multiplicador aplicado ao raio do hit (1 = sem mudança). ")] public float radiusMultiplier;
    }

    [System.Serializable]
    public struct VFXStageAdjustment
    {
        [Range(1, 5)] public int stage; // 1..3 + 4 opcional uppercut
        [Tooltip("Rotação adicional (Euler) aplicada SOMENTE a este estágio.")]
        public Vector3 eulerOffset;
        [Tooltip("Offset de posição adicional para este estágio.")]
        public Vector3 positionOffset;
        [Tooltip("Interpretar positionOffset em espaço local do atacante/âncora.")]
        public bool positionOffsetLocal;
        [Tooltip("Multiplicador extra de escala apenas para este estágio (1 = sem mudança).")]
        public float scaleMultiplier;
    }

    [Header("Referências")]
    [Tooltip("Controller de locomotion do atacante (compatibilidade). Se vazio, procura no próprio GameObject.")]
    public NitssLocomotionController locomotion;
    [Tooltip("Opcional: se não houver locomotion, usa o controlador de combate para eventos de ataque.")]
    public NitssCombatController combat;
    [Tooltip("Opcional: ponto de origem do hit (ex.: ponta da arma). Se vazio, usa o transform do locomotion.")]
    public Transform hitOriginOverride;

    [Header("Alcance do Hit (Ground)")]
    [Tooltip("Raio base do hit para ataques no chão.")] public float groundRadius = 0.7f;
    [Tooltip("Offset à frente em metros.")] public float groundForwardOffset = 0.6f;
    [Tooltip("Offset vertical.")] public float groundUpOffset = 0.9f;

    [Header("Alcance do Hit (Air)")]
    [Tooltip("Raio para ataques aéreos (pode ser maior/menor que o de chão). ")]
    public float airRadius = 0.9f;
    [Tooltip("Offset à frente ataques aéreos. ")] public float airForwardOffset = 0.7f;
    [Tooltip("Offset vertical ataques aéreos. ")] public float airUpOffset = 1.1f;

    [Header("Estágios & Delays")]
    [Tooltip("Stage lógico que representa o Uppercut informado pelo CombatController.")]
    public int uppercutStage = 4;
    [Tooltip("Delay antes do hit do Attack1 (segundos)")] public float attack1HitDelaySeconds = 0f;
    [Tooltip("Delay antes do hit do Attack2 (segundos)")] public float attack2HitDelaySeconds = 0f;
    [Tooltip("Delay antes do hit do Attack3 (segundos)")] public float attack3HitDelaySeconds = 0f;
    [Tooltip("Delay antes de aplicar o hitbox do uppercut (segundos) para sincronizar com a animação.")]
    public float uppercutHitDelaySeconds = 0.12f;

    [Header("Ajustes por Estágio de Hit (geom.)")]
    [Tooltip("Offsets adicionais/multiplicadores por estágio do combo.")]
    public StageOffsetConfig[] stageOffsetAdjustments = System.Array.Empty<StageOffsetConfig>();

    [Header("Slash VFX (por estágio)")]
    [Tooltip("Prefab de VFX exibido no impacto do Attack1 (slash/impact). Opcional.")]
    public GameObject vfxSlashAttack1;
    [Tooltip("Prefab de VFX exibido no impacto do Attack2 (slash/impact). Opcional.")]
    public GameObject vfxSlashAttack2;
    [Tooltip("Prefab de VFX exibido no impacto do Attack3 (slash/impact). Opcional.")]
    public GameObject vfxSlashAttack3;
    [Tooltip("Prefab de VFX exibido no impacto do Uppercut. Opcional.")]
    public GameObject vfxSlashUppercut;

    [Tooltip("Se verdadeiro, orienta o VFX para a frente do atacante.")]
    public bool vfxOrientToFacing = true;
    [Tooltip("Escala multiplicadora aplicada ao VFX instanciado (1 = original do prefab).")]
    public float vfxScaleMultiplier = 1f;
    [Tooltip("Rotação extra (Euler) aplicada ao VFX após a orientação base.")]
    public Vector3 vfxEulerOffset = Vector3.zero;
    [Tooltip("Em jogos side-scroller, vira o VFX 180º no Y quando o atacante estiver virado para a esquerda (signX = -1)")]
    public bool vfxYawFlipWithFacingX = true;
    // Simplificado: apenas yaw flip por rotação quando virado para esquerda

    [Header("Opacidade")]
    [Tooltip("Habilita controle de opacidade para o VFX instanciado.")]
    public bool vfxEnableOpacity = false;
    [Range(0f, 1f)]
    [Tooltip("Opacidade do VFX (0 = transparente, 1 = opaco)")]
    public float vfxOpacity = 1f;
    [Tooltip("Aplica a opacidade em toda a hierarquia do VFX (Renderers e SpriteRenderers)")]
    public bool vfxAffectChildren = true;
    [Tooltip("Aplica opacidade também em ParticleSystems (altera o startColor alpha).")]
    public bool vfxAffectParticleSystems = true;
    [Tooltip("Nome do property de cor no material (opcional). Se vazio, tenta _BaseColor, _Color, _Tint, _TintColor")]
    public string vfxRendererColorProperty = "";
    [Tooltip("Usa MaterialPropertyBlock para evitar instanciar materiais ao alterar cor/alpha")]
    public bool vfxUseMaterialPropertyBlock = true;

    // caches para aplicar opacidade
    private static readonly int[] s_DefaultColorPropIds = new int[] {
        -1, // placeholder para vfxRendererColorProperty quando preenchido em runtime
        0,  // Shader.PropertyToID("_BaseColor") atribuído em static ctor lazy
        0,  // Shader.PropertyToID("_Color")
        0,  // Shader.PropertyToID("_Tint")
        0   // Shader.PropertyToID("_TintColor")
    };
    private static bool s_ColorPropIdsInitialized = false;
    private MaterialPropertyBlock _mpb;
    private readonly System.Collections.Generic.Dictionary<UnityEngine.Renderer, int> _rendererPropId = new System.Collections.Generic.Dictionary<UnityEngine.Renderer, int>();
    private readonly System.Collections.Generic.Dictionary<UnityEngine.Renderer, UnityEngine.Color> _rendererBaseColor = new System.Collections.Generic.Dictionary<UnityEngine.Renderer, UnityEngine.Color>();
    private readonly System.Collections.Generic.Dictionary<UnityEngine.SpriteRenderer, UnityEngine.Color> _spriteBaseColor = new System.Collections.Generic.Dictionary<UnityEngine.SpriteRenderer, UnityEngine.Color>();
    [Tooltip("Anexar o VFX como filho do atacante (mantém acompanhando). Se falso, fica solto no mundo.")]
    public bool vfxParentToAttacker = false;
    [Tooltip("Se verdadeiro, a escala do VFX é proporcional ao raio do hit (usa vfxRadiusToScale como referência). ")]
    public bool vfxScaleByRadius = true;
    [Tooltip("Raio de referência para cálculo da escala automática (1.0 = sem ajuste adicional). ")]
    public float vfxRadiusToScale = 1.0f;

    [Tooltip("Usar uma posição fixa padrão para todos os estágios em vez do centro do hit calculado.")]
    public bool vfxUseFixedPosition = false;
    [Tooltip("Âncora da posição fixa (se vazio usa hitOriginOverride ou o Transform do atacante). ")]
    public Transform vfxFixedAnchor;
    [Tooltip("Offset para frente (metros) relativo ao forward da âncora quando vfxUseFixedPosition estiver ativo. ")]
    public float vfxFixedForward = 0f;
    [Tooltip("Offset vertical (metros) relativo à âncora quando vfxUseFixedPosition estiver ativo. ")]
    public float vfxFixedUp = 0f;
    [Tooltip("Offset lateral (metros) relativo ao right da âncora quando vfxUseFixedPosition estiver ativo. ")]
    public float vfxFixedRight = 0f;
    [Tooltip("Offset extra aplicado SEMPRE ao VFX (após posição base). Pode ser local (em relação ao atacante) ou em espaço mundial. ")]
    public Vector3 vfxExtraOffset = Vector3.zero;
    [Tooltip("Quando verdadeiro, vfxExtraOffset é interpretado no espaço local do atacante/âncora. ")]
    public bool vfxExtraOffsetInLocalSpace = true;

    [Header("Rotação direta por estágio (simplificado)")]
    public Vector3 vfxEulerAttack1 = Vector3.zero;
    public Vector3 vfxEulerAttack2 = Vector3.zero;
    public Vector3 vfxEulerAttack3 = Vector3.zero;
    public Vector3 vfxEulerUppercut = Vector3.zero;

    [Header("Slash VFX por Estágio (Ajustes)")]
    [Tooltip("Overrides de rotação/posição/escala por estágio do combo.")]
    public VFXStageAdjustment[] vfxStageAdjustments = System.Array.Empty<VFXStageAdjustment>();


    [Header("Debug")]
    public bool debugDraw = false;

    // Snapshot para estágios com delay (para VFX usar a mesma geometria capturada)
    private Vector3 _snapshotCenter;
    private float _snapshotRadius;
    private int _snapshotStage;

    private void Awake()
    {
        if (!locomotion)
            locomotion = GetComponentInParent<NitssLocomotionController>() ?? GetComponent<NitssLocomotionController>();
        if (!combat)
            combat = GetComponentInParent<NitssCombatController>() ?? GetComponent<NitssCombatController>();

        if (locomotion)
        {
            locomotion.OnAttackStageStart += OnAttackStageStart;
            locomotion.OnAttackStageEnd += OnAttackStageEnd;
        }
        else if (combat)
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
        float delay = 0f;
        if (stage == 1) delay = attack1HitDelaySeconds;
        else if (stage == 2) delay = attack2HitDelaySeconds;
        else if (stage == 3) delay = attack3HitDelaySeconds;
        else if (stage == uppercutStage) delay = uppercutHitDelaySeconds;

        if (delay > 0f)
        {
            Vector3 center; float radius;
            ComputeHitGeometrySnapshot(stage, isAir, out center, out radius);
            _snapshotCenter = center; _snapshotRadius = radius; _snapshotStage = stage;
            StartCoroutine(SpawnAfterDelay(stage, isAir, delay, center, radius));
        }
        else
        {
            _snapshotStage = 0;
            Vector3 center; float radius;
            ComputeHitGeometrySnapshot(stage, isAir, out center, out radius);
            TrySpawnSlashVFX(stage, isAir, center, radius);
        }
    }

    private System.Collections.IEnumerator SpawnAfterDelay(int stage, bool isAir, float delay, Vector3 center, float radius)
    {
        float t = 0f;
        while (t < delay)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (this && enabled)
        {
            TrySpawnSlashVFX(stage, isAir, center, radius);
        }
    }

    private void OnAttackStageEnd(int stage, bool isAir)
    {
        if (stage == _snapshotStage)
            StartCoroutine(ClearSnapshotNextFrame());
    }

    private System.Collections.IEnumerator ClearSnapshotNextFrame()
    {
        yield return null;
        _snapshotStage = 0;
    }

    private void ComputeHitGeometrySnapshot(int stage, bool isAir, out Vector3 center, out float radius)
    {
        center = Vector3.zero; radius = 0f;
        var tr = locomotion ? locomotion.transform : (combat ? combat.transform : transform);
        if (!tr) return;
        var origin = hitOriginOverride ? hitOriginOverride : tr;
        Vector3 fwd = tr.forward; fwd.y = 0f; fwd.Normalize();
        radius = isAir ? airRadius : groundRadius;
        float fwdOffset = isAir ? airForwardOffset : groundForwardOffset;
        float upOffset = isAir ? airUpOffset : groundUpOffset;
        ApplyStageAdjustments(stage, isAir, ref fwdOffset, ref upOffset, ref radius);
        center = origin.position + fwd * fwdOffset + Vector3.up * upOffset;
    }

    private void ApplyStageAdjustments(int stage, bool isAir, ref float forwardOffset, ref float upOffset, ref float radius)
    {
        if (stageOffsetAdjustments == null || stageOffsetAdjustments.Length == 0) return;
        for (int i = 0; i < stageOffsetAdjustments.Length; i++)
        {
            var cfg = stageOffsetAdjustments[i];
            if (cfg.stage != stage) continue;
            if (cfg.appliesToAir != isAir) continue;
            forwardOffset += cfg.forwardOffsetDelta;
            upOffset += cfg.upOffsetDelta;
            if (cfg.radiusMultiplier > 0f) radius *= cfg.radiusMultiplier;
            break;
        }
    }

    private GameObject TrySpawnSlashVFX(int stage, bool isAir, Vector3 position, float radius)
    {
        var prefab = GetSlashVFXPrefab(stage);
        if (!prefab) return null;
        Transform tr = locomotion ? locomotion.transform : (combat ? combat.transform : transform);
        Vector3 pos = ComputeVFXPosition(stage, isAir, position, tr);

        // Determina o facing (signX) do atacante usando múltiplos fallbacks
        int sign = GetFacingSignX();

        Quaternion rot = Quaternion.identity;
        if (vfxOrientToFacing && tr)
        {
            Vector3 fwd = tr.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            rot = Quaternion.LookRotation(fwd, Vector3.up);
            if (vfxYawFlipWithFacingX && sign < 0)
                rot *= Quaternion.Euler(0f, 180f, 0f);
        }
        rot *= Quaternion.Euler(vfxEulerOffset);
        // rotação direta por estágio
        Vector3 eulerDirect = GetDirectStageEuler(stage);
        if (eulerDirect != Vector3.zero) rot *= Quaternion.Euler(eulerDirect);
        // ajustes complexos por estágio
        ApplyVFXStageAdjustments(stage, tr, ref pos, ref rot, ref radius);

        var go = GameObject.Instantiate(prefab, pos, rot);
        if (vfxParentToAttacker && tr)
            go.transform.SetParent(tr, true);

        float scaleMul = Mathf.Max(0.0001f, vfxScaleMultiplier);
        if (vfxScaleByRadius && vfxRadiusToScale > 0.00001f)
            scaleMul *= (radius / vfxRadiusToScale);

        float extraStageScale = GetVFXStageScaleMultiplier(stage);
        if (extraStageScale > 0f && Mathf.Abs(extraStageScale - 1f) > 0.0001f)
            scaleMul *= extraStageScale;

        if (Mathf.Abs(scaleMul - 1f) > 0.0001f)
            go.transform.localScale = go.transform.localScale * scaleMul;

        // Sem mirror por escala – apenas rotação já aplicada acima

        // Opacidade (estática)
        if (vfxEnableOpacity)
            ApplyOpacity(go, Mathf.Clamp01(vfxOpacity));

        if (debugDraw)
            DebugDrawSphere(position, radius, Color.magenta, 0.35f);

        return go;
    }

    private Vector3 ComputeVFXPosition(int stage, bool isAir, Vector3 computedCenter, Transform attackerTr)
    {
        Transform anchor = vfxFixedAnchor ? vfxFixedAnchor : (hitOriginOverride ? hitOriginOverride : attackerTr);
        Vector3 basePos = computedCenter;
        if (vfxUseFixedPosition && anchor)
            basePos = anchor.position + (anchor.right * vfxFixedRight) + (anchor.forward * vfxFixedForward) + (Vector3.up * vfxFixedUp);
        if (vfxExtraOffset != Vector3.zero && anchor)
            basePos += vfxExtraOffsetInLocalSpace ? anchor.TransformDirection(vfxExtraOffset) : vfxExtraOffset;
        return basePos;
    }

    private void ApplyVFXStageAdjustments(int stage, Transform attacker, ref Vector3 pos, ref Quaternion rot, ref float radius)
    {
        if (vfxStageAdjustments == null || vfxStageAdjustments.Length == 0) return;
        for (int i = 0; i < vfxStageAdjustments.Length; i++)
        {
            var cfg = vfxStageAdjustments[i];
            if (cfg.stage != stage) continue;
            if (cfg.positionOffset != Vector3.zero && attacker)
                pos += cfg.positionOffsetLocal ? attacker.TransformDirection(cfg.positionOffset) : cfg.positionOffset;
            if (cfg.eulerOffset != Vector3.zero)
                rot *= Quaternion.Euler(cfg.eulerOffset);
            break;
        }
    }

    private float GetVFXStageScaleMultiplier(int stage)
    {
        if (vfxStageAdjustments == null || vfxStageAdjustments.Length == 0) return 1f;
        for (int i = 0; i < vfxStageAdjustments.Length; i++)
        {
            var cfg = vfxStageAdjustments[i];
            if (cfg.stage == stage && cfg.scaleMultiplier > 0f)
                return cfg.scaleMultiplier;
        }
        return 1f;
    }

    private int GetFacingSignX()
    {
        if (locomotion != null)
        {
            try
            {
                var fi = locomotion.GetType().GetField("attackFacingSignX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fi != null) return (int)fi.GetValue(locomotion);
            }
            catch { }
        }
        var tr = locomotion ? locomotion.transform : (combat ? combat.transform : transform);
        if (tr && tr.localScale.x < 0f) return -1;
        return 1;
    }

    private Vector3 GetDirectStageEuler(int stage)
    {
        if (stage == uppercutStage) return vfxEulerUppercut;
        if (stage == 1) return vfxEulerAttack1;
        if (stage == 2) return vfxEulerAttack2;
        if (stage == 3) return vfxEulerAttack3;
        return Vector3.zero;
    }

    private GameObject GetSlashVFXPrefab(int stage)
    {
        if (stage == uppercutStage && vfxSlashUppercut) return vfxSlashUppercut;
        if (stage == 1) return vfxSlashAttack1 ? vfxSlashAttack1 : null;
        if (stage == 2) return vfxSlashAttack2 ? vfxSlashAttack2 : null;
        if (stage == 3) return vfxSlashAttack3 ? vfxSlashAttack3 : null;
        return null;
    }

    private void DebugDrawSphere(Vector3 center, float r, Color c, float duration)
    {
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

    // (removido) TriggerDodgeVFX agora está no componente DodgeVFX separado.

    // ====== Opacidade (estática) ======
    private void ApplyOpacity(GameObject go, float opacity)
    {
        if (!go) return;

        if (vfxAffectChildren)
        {
            var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < srs.Length; i++) ApplyOpacitySprite(srs[i], opacity);

            var rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                // Evita duplicar SpriteRenderer, que já foi ajustado acima
                if (rends[i] is SpriteRenderer) continue;
                ApplyOpacityRenderer(rends[i], opacity);
            }
            if (vfxAffectParticleSystems)
            {
                var psArr = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < psArr.Length; i++) ApplyOpacityParticleSystem(psArr[i], opacity);
            }
        }
        else
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr) ApplyOpacitySprite(sr, opacity);
            var r = go.GetComponent<Renderer>();
            if (r && !(r is SpriteRenderer)) ApplyOpacityRenderer(r, opacity);
            if (vfxAffectParticleSystems)
            {
                var ps = go.GetComponent<ParticleSystem>();
                if (ps) ApplyOpacityParticleSystem(ps, opacity);
            }
        }
    }

    private void ApplyOpacitySprite(SpriteRenderer sr, float opacity)
    {
        if (!sr) return;
        if (!_spriteBaseColor.TryGetValue(sr, out var baseCol))
        {
            baseCol = sr.color;
            _spriteBaseColor[sr] = baseCol;
        }
        var c = baseCol;
        c.a = Mathf.Clamp01(baseCol.a * opacity);
        sr.color = c;
    }

    private void ApplyOpacityRenderer(Renderer r, float opacity)
    {
        if (!r) return;
        // Inicializa ids de propriedades de cor uma vez
        if (!s_ColorPropIdsInitialized)
        {
            s_DefaultColorPropIds[1] = Shader.PropertyToID("_BaseColor");
            s_DefaultColorPropIds[2] = Shader.PropertyToID("_Color");
            s_DefaultColorPropIds[3] = Shader.PropertyToID("_Tint");
            s_DefaultColorPropIds[4] = Shader.PropertyToID("_TintColor");
            s_ColorPropIdsInitialized = true;
        }

        // Escolhe a propriedade de cor
        // Itera todas as materials do renderer
        var materials = vfxUseMaterialPropertyBlock ? r.sharedMaterials : r.materials;
        if (materials == null || materials.Length == 0) return;

        for (int m = 0; m < materials.Length; m++)
        {
            var matRef = materials[m];
            if (!matRef) continue;
            int propId;
            // chave por renderer + index
            if (!_rendererPropId.TryGetValue(r, out propId) || propId == -1)
            {
                propId = -1;
                if (!string.IsNullOrEmpty(vfxRendererColorProperty))
                {
                    int customId = Shader.PropertyToID(vfxRendererColorProperty);
                    if (matRef.HasProperty(customId)) propId = customId;
                }
                if (propId == -1)
                {
                    for (int i = 1; i < s_DefaultColorPropIds.Length; i++)
                    {
                        int id = s_DefaultColorPropIds[i];
                        if (id != 0 && matRef.HasProperty(id)) { propId = id; break; }
                    }
                }
                _rendererPropId[r] = propId; // guarda último encontrado
            }
            if (propId == -1) continue;

            if (!_rendererBaseColor.TryGetValue(r, out var baseCol))
            {
                if (matRef.HasProperty(propId)) baseCol = matRef.GetColor(propId); else baseCol = Color.white;
                _rendererBaseColor[r] = baseCol; // guarda cor base
            }

            var newCol = baseCol; newCol.a = Mathf.Clamp01(baseCol.a * opacity);

            if (vfxUseMaterialPropertyBlock)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(propId, newCol);
                r.SetPropertyBlock(_mpb);
            }
            else
            {
                if (matRef.HasProperty(propId)) matRef.SetColor(propId, newCol);
            }
        }
    }

    private void ApplyOpacityParticleSystem(ParticleSystem ps, float opacity)
    {
        if (!ps) return;
        var main = ps.main;
        var startCol = main.startColor;
        Color c = startCol.color;
        c.a = Mathf.Clamp01(c.a * opacity);
        main.startColor = new ParticleSystem.MinMaxGradient(c);
    }
}
