using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Hazze.Gameplay.Combat;

/// <summary>
/// Consome o JSON (seed) do Nitss e popula atributos básicos do personagem.
/// - Vida base → Damageable.maxHealth/current
/// - Atributos (atk/def/speed/stamina) → struct serializável exposta no Inspector
/// - Habilidades/slots/itens → listas simples (ids) para sistemas de UI usarem
/// Também se conecta automaticamente ao NitssSeedLoader (se presente) para receber o evento.
/// </summary>
[AddComponentMenu("Hazze/Gameplay/Nitss Seed Applier")]
[DisallowMultipleComponent]
public class NitssSeedApplier : MonoBehaviour
{
    [Header("Refs (opcional)")]
    [Tooltip("Componente de vida/dano no mesmo GO ou em pais/filhos. Se vazio, é resolvido automaticamente.")]
    public Damageable damageable;

    [Header("Atributos (resultado do parse)")]
    public Attributes attributes;

    [Header("Atributos Primários (seed)")]
    public PrimaryAttributes primaries; // STR, VIT, INT

    [Header("Coleções (ids)")]
    [Tooltip("IDs de habilidades mapeados do seed (se houver).")]
    public List<string> abilityIds = new List<string>();
    [Tooltip("IDs de itens/equipamentos/consumíveis mapeados do seed (se houver).")]
    public List<string> itemIds = new List<string>();
    [Tooltip("Slots de habilidades/equipamentos conforme seed (se houver).")]
    public int abilitySlots;

    [Header("Classificação / Metadados (seed)")]
    public string tipo;   // equipamento, arma, consumivel, invocacao, personagem, etc.
    public string classe; // dano, defesa, suporte, mobilidade, estrategico, controle
    public string origem; // universal ou facção
    public string raridade; // comum, raro, epico, lendario
    public List<string> tags = new List<string>();
    public List<string> statusIds = new List<string>();

    [Header("Cálculo (buffs e multiplicadores)")]
    [Tooltip("Recalcula automaticamente após Apply(json).")]
    public bool autoRecalculateOnApply = true;
    [Tooltip("Fator K para reduzir dano por Defesa: mit = def/(def+K). K maior = retorno decrescente mais forte.")]
    [Min(1f)] public float defenseMitigationK = 100f;
    [Tooltip("Buffs extras (editáveis no Inspector) que serão aplicados além dos buffs vindos do seed.")]
    public List<Buff> extraBuffs = new List<Buff>();
    [Tooltip("Atributos finais após o cálculo de buffs/multiplicadores.")]
    public FinalAttributes finalAttributes;

    [Header("Energia Runtime")]
    [Tooltip("Energia atual do Nitss. Inicializa no máximo ao aplicar o seed.")]
    public float currentEnergy;
    [Tooltip("Disparado quando a energia é atualizada.")]
    public UnityEvent<float> onEnergyChanged;

    [Header("Curvas de Primários → Secundários")]
    [Tooltip("Ataque ganho por ponto de STR")] public float attackPerSTR = 1f;
    [Tooltip("Defesa ganha por ponto de VIT")] public float defensePerVIT = 0.5f;
    [Tooltip("VidaMáx ganha por ponto de VIT")] public float lifePerVIT = 5f;
    [Tooltip("EnergiaMáx ganha por ponto de INT")] public float energyPerINT = 5f;
    [Tooltip("Regen de Vida por ponto de VIT (por segundo)")] public float regenLifePerVIT = 0.02f;
    [Tooltip("Regen base de Energia (por segundo)")] public float regenEnergyBase = 1f;
    [Tooltip("Regen de Energia por ponto de INT (por segundo)")] public float regenEnergyPerINT = 0.02f;
    [Tooltip("Aplicar MaxLife final ao Damageable após o recálculo")] public bool applyFinalMaxHealthToDamageable = true;

    [Header("Eventos")] 
    public UnityEvent onApplied; // disparado quando Apply() conclui com sucesso
    public UnityEvent<string> onApplyError; // mensagem de erro (parse)
    [Tooltip("Disparado quando o recálculo termina (após Apply se autoRecalculateOnApply=true).")]
    public UnityEvent<FinalAttributes> onRecalculated;

    [Serializable]
    public struct Attributes
    {
        public float attack;
        public float defense;
        public float speed;
        public float stamina;
        public float critChance;
        public float critDamage;

        public override string ToString()
        {
            return $"atk={attack} def={defense} spd={speed} sta={stamina} crit%={critChance} critDmg={critDamage}";
        }
    }

    [Serializable]
    public struct PrimaryAttributes
    {
        public float STR;
        public float VIT;
        public float INT;

        public override string ToString()
        {
            return $"STR={STR} VIT={VIT} INT={INT}";
        }
    }

    // -------------------- Runtime wiring --------------------
    private NitssSeedLoader loader;

    private void Awake()
    {
        if (!damageable)
            damageable = GetComponentInParent<Damageable>() ?? GetComponent<Damageable>();
        loader = GetComponentInParent<NitssSeedLoader>() ?? GetComponent<NitssSeedLoader>();
    }

    private void OnEnable()
    {
        if (loader != null)
        {
            loader.onSeedLoadedJson.AddListener(Apply);
        }
    }

    private void OnDisable()
    {
        if (loader != null)
        {
            loader.onSeedLoadedJson.RemoveListener(Apply);
        }
    }

    [ContextMenu("Apply From Loader.lastJson")] 
    private void ApplyFromLoader()
    {
        if (loader != null && !string.IsNullOrWhiteSpace(loader.lastJson))
        {
            Apply(loader.lastJson);
        }
        else
        {
            onApplyError?.Invoke("Loader ou lastJson vazio");
        }
    }

    /// <summary>
    /// Método compatível com UnityEvent<string>. Recebe JSON bruto do seed, faz o parse e aplica.
    /// </summary>
    public void Apply(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            onApplyError?.Invoke("JSON vazio");
            return;
        }

        _energyInitialized = false;
        currentEnergy = 0f;

        try
        {
            var env = JsonUtility.FromJson<SeedEnvelope>(json);

            // 1) Vida base
            float life = FirstNonZero(
                env.vida_base,
                env.vidaBase,
                env.vida,
                env.maxLife,
                env.stats_base.vida,
                env.stats_base.maxLife,
                env.stats.vida,
                env.stats.maxLife
            );
            _baseLifeFromSeed = life;
            if (damageable != null && life > 0f)
            {
                damageable.maxHealth = life;
                // opcional: iniciar cheio
                var cur = typeof(Damageable).GetField("currentHealth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (cur != null) cur.SetValue(damageable, life);
            }

            // 2) Atributos
            attributes.attack = FirstNonZero(
                env.stats_base.ataque,
                env.stats_base.atk,
                env.stats_base.dano,
                env.stats_base.damage,
                env.stats.ataque,
                env.stats.atk,
                env.stats.dano,
                env.stats.damage
            );
            attributes.defense = FirstNonZero(env.stats_base.defesa, env.stats_base.def, env.stats.defesa, env.stats.def);
            attributes.speed = FirstNonZero(env.stats_base.velocidade, env.stats_base.speed, env.stats.velocidade, env.stats.speed);
            attributes.stamina = FirstNonZero(env.stats_base.stamina, env.stats_base.energia, env.stats.stamina, env.stats.energia);
            attributes.critChance = FirstNonZero(
                env.stats_base.critChance,
                env.stats_base.crit,
                env.stats.critChance,
                env.stats.crit
            );
            attributes.critDamage = FirstNonZero(
                env.stats_base.critDamage,
                env.stats_base.critDano,
                env.stats_base.crit_mult,
                env.stats.critDamage,
                env.stats.critDano,
                env.stats.crit_mult
            );
            _baseEnergyFromSeed = FirstNonZero(env.stats_base.energia, env.stats.energia, attributes.stamina);

            // 3) Habilidades / Itens / Slots
            abilityIds.Clear();
            itemIds.Clear();
            abilitySlots = 0;

            if (env.habilidades != null)
            {
                foreach (var h in env.habilidades)
                {
                    var id = CoalesceNonEmpty(h.id, h.nome);
                    if (!string.IsNullOrWhiteSpace(id)) abilityIds.Add(id);
                }
            }
            if (env.habilidades_ids != null)
            {
                foreach (var id in env.habilidades_ids)
                    if (!string.IsNullOrWhiteSpace(id)) abilityIds.Add(id);
            }

            if (env.inventory != null)
            {
                AddMany(itemIds, env.inventory.equipamentos);
                AddMany(itemIds, env.inventory.consumiveis);
                AddMany(itemIds, env.inventory.itens);
            }
            AddMany(itemIds, env.itens);

            if (env.slots != null)
            {
                abilitySlots = Mathf.Max(abilitySlots, env.slots.habilidades);
            }
            abilitySlots = Mathf.Max(abilitySlots, env.slots_habilidades);

            // 3.1) Classificação/Metadados
            tipo = CoalesceNonEmpty(env.tipo, env.type);
            classe = CoalesceNonEmpty(env.classe, env.className, env.clazz);
            origem = CoalesceNonEmpty(env.origem, env.faccao, env.faction);
            raridade = CoalesceNonEmpty(env.raridade, env.rarity);
            tags.Clear();
            if (env.tags != null) { foreach (var t in env.tags) if (!string.IsNullOrWhiteSpace(t)) tags.Add(t); }
            statusIds.Clear();
            if (env.status != null) { foreach (var s in env.status) if (!string.IsNullOrWhiteSpace(s)) statusIds.Add(s); }

            // 3.2) Primários (STR/VIT/INT) com guardas contra null
            float pSTR = 0f, pVIT = 0f, pINT = 0f;
            if (env.primarios != null)
            {
                pSTR = FirstNonZero(env.primarios.STR, env.primarios.str);
                pVIT = FirstNonZero(env.primarios.VIT, env.primarios.vit);
                pINT = FirstNonZero(env.primarios.INT, env.primarios._int);
            }
            if (env.primary != null)
            {
                pSTR = FirstNonZero(pSTR, env.primary.STR, env.primary.str);
                pVIT = FirstNonZero(pVIT, env.primary.VIT, env.primary.vit);
                pINT = FirstNonZero(pINT, env.primary.INT, env.primary._int);
            }
            primaries.STR = pSTR;
            primaries.VIT = pVIT;
            primaries.INT = pINT;

            // 4) Buffs do seed (se houver)
            _seedBuffs.Clear();
            if (env.buffs != null)
            {
                foreach (var b in env.buffs)
                {
                    if (b == null) continue;
                    var buff = new Buff
                    {
                        attribute = CoalesceNonEmpty(b.attribute, b.attr, b.stat),
                        add = b.add,
                        mult = b.mult == 0f ? 1f : b.mult
                    };
                    if (!string.IsNullOrWhiteSpace(buff.attribute))
                        _seedBuffs.Add(buff);
                }
            }

            onApplied?.Invoke();

            if (autoRecalculateOnApply)
            {
                Recalculate();
            }
        }
        catch (Exception e)
        {
            onApplyError?.Invoke($"parse_error: {e.Message}");
        }
    }

    // ---- Demais membros (envelope de json, cálculo e helpers) foram omitidos por brevidade.
    // No projeto original estes continuavam abaixo e são necessários.

    // Placeholders para compilar quando for usado isoladamente (substitua pelos originais se necessário)
    [Serializable]
    public struct FinalAttributes { public float attack, defense, speed, stamina, critChance, critDamage, maxLife, maxEnergy; }
    [Serializable]
    public struct SeedEnvelope
    {
        public float vida_base, vidaBase, vida, maxLife;
        public Stats stats_base, stats;
        public Habilidade[] habilidades; public string[] habilidades_ids;
        public Inventory inventory; public string[] itens;
        public Slots slots; public int slots_habilidades;
        public string tipo, type, classe, className, clazz, origem, faccao, faction, raridade, rarity;
        public string[] tags; public string[] status; public BuffDef[] buffs;
        public Primarios primarios; public Primary primary;
    }
    [Serializable] public struct Stats { public float vida, maxLife, ataque, atk, dano, damage, defesa, def, velocidade, speed, stamina, energia, critChance, crit, critDamage, critDano, crit_mult; }
    [Serializable] public struct Habilidade { public string id, nome; }
    [Serializable] public struct Inventory { public string[] equipamentos, consumiveis, itens; }
    [Serializable] public struct Slots { public int habilidades; }
    [Serializable] public struct BuffDef { public string attribute, attr, stat; public float add, mult; }
    [Serializable] public struct Primarios { public float STR, VIT, INT; public float str, vit, _int; }
    [Serializable] public struct Primary { public float STR, VIT, INT; public float str, vit, _int; }

    [Serializable]
    public struct Buff { public string attribute; public float add; public float mult; }

    private readonly List<Buff> _seedBuffs = new List<Buff>();
    private float _baseLifeFromSeed; private float _baseEnergyFromSeed; private bool _energyInitialized;

    private static float FirstNonZero(params float[] values)
    {
        foreach (var v in values) if (v > 0f) return v; return 0f;
    }
    private static void AddMany(List<string> list, string[] arr)
    {
        if (arr == null) return; foreach (var s in arr) if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
    }
    private static string CoalesceNonEmpty(params string[] v)
    {
        foreach (var s in v) if (!string.IsNullOrWhiteSpace(s)) return s; return null;
    }

    public void Recalculate() { /* ver implementação completa no repo principal */ }
    public void RestoreEnergy(float e) { currentEnergy += Mathf.Max(0f, e); onEnergyChanged?.Invoke(currentEnergy); }
}
