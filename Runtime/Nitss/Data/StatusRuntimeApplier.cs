using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aplica efeitos de status temporizados no Nitss, convertendo-os em buffs de runtime
/// para o NitssSeedApplier (via extraBuffs) e recalc quando há mudanças.
/// </summary>
[AddComponentMenu("Hazze/Gameplay/Status Runtime Applier")]
public class StatusRuntimeApplier : MonoBehaviour
{
    [Tooltip("Fonte de atributos/extraBuffs/recalculate.")]
    public NitssSeedApplier applier;
    [Tooltip("Database de efeitos. Se vazio, não aplica nada.")]
    public StatusEffectsDatabase database;

    [Header("Fluxo")]
    [Tooltip("Quando ativo, aplica os status listados em applier.statusIds ao receber onApplied.")]
    public bool applyFromSeedOnApplied = true;

    private readonly List<NitssSeedApplier.Buff> _runtimeBuffs = new();
    private readonly List<ActiveStatus> _active = new();

    private void Awake()
    {
        if (!applier) applier = GetComponentInParent<NitssSeedApplier>() ?? GetComponent<NitssSeedApplier>();
    }

    private void OnEnable()
    {
        if (applier != null && applyFromSeedOnApplied)
        {
            applier.onApplied.AddListener(OnSeedApplied);
        }
    }

    private void OnDisable()
    {
        if (applier != null)
        {
            applier.onApplied.RemoveListener(OnSeedApplied);
        }
        ClearRuntimeBuffs();
        _active.Clear();
    }

    private void Update()
    {
        if (_active.Count == 0) return;
        bool changed = false;
        float now = Time.time;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].endTime <= now)
            {
                _active.RemoveAt(i);
                changed = true;
            }
        }
        if (changed)
        {
            RebuildRuntimeBuffs();
        }
    }

    private void OnSeedApplied()
    {
        if (!database || applier == null) return;
        if (applier.statusIds == null || applier.statusIds.Count == 0) return;
        foreach (var id in applier.statusIds)
        {
            TryApplyStatus(id);
        }
    }

    public void TryApplyStatus(string id, float durationOverride = -1f)
    {
        if (!database || string.IsNullOrWhiteSpace(id)) return;
        var def = database.Find(id);
        if (def == null) return;

        // Stacks simples
        int currentStacks = _active.FindAll(a => a.id == id).Count;
        if (!def.stackable && currentStacks > 0) return;
        if (def.stackable && currentStacks >= Mathf.Max(1, def.maxStacks)) return;

        float duration = durationOverride > 0f ? durationOverride : def.duration;
        if (duration <= 0f) duration = 0.01f;
        _active.Add(new ActiveStatus
        {
            id = id,
            endTime = Time.time + duration
        });
        RebuildRuntimeBuffs();
    }

    public void RemoveStatus(string id)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].id == id) _active.RemoveAt(i);
        }
        RebuildRuntimeBuffs();
    }

    public void ClearAll()
    {
        _active.Clear();
        RebuildRuntimeBuffs();
    }

    private void RebuildRuntimeBuffs()
    {
        if (applier == null) return;

        // Remove os buffs de runtime antigos de extraBuffs
        if (_runtimeBuffs.Count > 0)
        {
            foreach (var b in _runtimeBuffs) applier.extraBuffs.Remove(b);
            _runtimeBuffs.Clear();
        }

        // Recria a lista a partir dos status ativos
        if (database)
        {
            foreach (var st in _active)
            {
                var def = database.Find(st.id);
                if (def == null || def.buffs == null) continue;
                foreach (var bd in def.buffs)
                {
                    var nb = new NitssSeedApplier.Buff
                    {
                        attribute = bd.attribute,
                        add = bd.add,
                        mult = bd.mult
                    };
                    _runtimeBuffs.Add(nb);
                    applier.extraBuffs.Add(nb);
                }
            }
        }

        applier.Recalculate();
    }

    private void ClearRuntimeBuffs()
    {
        if (applier == null) return;
        foreach (var b in _runtimeBuffs) applier.extraBuffs.Remove(b);
        _runtimeBuffs.Clear();
        applier.Recalculate();
    }

    [Serializable]
    private class ActiveStatus
    {
        public string id;
        public float endTime;
    }
}
