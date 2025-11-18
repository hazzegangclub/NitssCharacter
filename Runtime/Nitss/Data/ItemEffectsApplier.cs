using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converte os itens/equipamentos listados no seed em buffs e status runtime.
/// </summary>
[AddComponentMenu("Hazze/Gameplay/Item Effects Applier")]
public class ItemEffectsApplier : MonoBehaviour
{
    public NitssSeedApplier applier;
    public ItemEffectsDatabase database;
    [Tooltip("Opcional: usado para disparar status runtime de acordo com os itens.")]
    public StatusRuntimeApplier statusRuntime;

    private readonly List<NitssSeedApplier.Buff> _runtimeBuffs = new();
    private readonly List<string> _runtimeStatuses = new();

    private void Awake()
    {
        if (!applier) applier = GetComponentInParent<NitssSeedApplier>() ?? GetComponent<NitssSeedApplier>();
        if (!statusRuntime) statusRuntime = GetComponentInParent<StatusRuntimeApplier>();
    }

    private void OnEnable()
    {
        if (applier != null)
        {
            applier.onApplied.AddListener(ApplyItemEffects);
        }
    }

    private void OnDisable()
    {
        if (applier != null)
        {
            applier.onApplied.RemoveListener(ApplyItemEffects);
        }
        ClearRuntimeEffects();
    }

    private void ApplyItemEffects()
    {
        if (applier == null || database == null) return;

        // limpar buffs anteriores
        ClearRuntimeBuffs();
        ClearRuntimeStatuses();

        if (applier.itemIds == null || applier.itemIds.Count == 0)
        {
            applier.Recalculate();
            return;
        }

        bool hasNewBuffs = false;

        foreach (var itemId in applier.itemIds)
        {
            var def = database.Find(itemId);
            if (def == null) continue;

            if (def.buffs != null)
            {
                foreach (var buff in def.buffs)
                {
                    if (buff == null) continue;
                    var clone = new NitssSeedApplier.Buff
                    {
                        attribute = buff.attribute,
                        add = buff.add,
                        mult = buff.mult
                    };
                    _runtimeBuffs.Add(clone);
                    applier.extraBuffs.Add(clone);
                    hasNewBuffs = true;
                }
            }

            if (def.instantHeal > 0f && applier.damageable != null)
            {
                applier.damageable.Heal(def.instantHeal);
            }

            if (def.instantEnergy > 0f)
            {
                applier.RestoreEnergy(def.instantEnergy);
            }

            if (statusRuntime != null && def.statuses != null)
            {
                foreach (var st in def.statuses)
                {
                    if (st == null || string.IsNullOrWhiteSpace(st.id)) continue;
                    statusRuntime.TryApplyStatus(st.id, st.durationOverrideSeconds);
                    _runtimeStatuses.Add(st.id);
                }
            }

            if (statusRuntime != null && def.removeStatusIds != null && def.removeStatusIds.Count > 0)
            {
                foreach (var sid in def.removeStatusIds)
                {
                    if (string.IsNullOrWhiteSpace(sid)) continue;
                    statusRuntime.RemoveStatus(sid);
                }
            }
        }

        if (hasNewBuffs)
        {
            applier.Recalculate();
        }
        else if (_runtimeStatuses.Count == 0)
        {
            // nem status nem buff -> garante que efeitos antigos foram removidos
            applier.Recalculate();
        }
    }

    private void ClearRuntimeEffects()
    {
        ClearRuntimeStatuses();
        ClearRuntimeBuffs();
    }

    private void ClearRuntimeBuffs()
    {
        if (applier == null) return;
        if (_runtimeBuffs.Count == 0) return;
        foreach (var buff in _runtimeBuffs)
        {
            applier.extraBuffs.Remove(buff);
        }
        _runtimeBuffs.Clear();
        applier.Recalculate();
    }

    private void ClearRuntimeStatuses()
    {
        if (statusRuntime == null || _runtimeStatuses.Count == 0) return;
        foreach (var sid in _runtimeStatuses)
        {
            statusRuntime.RemoveStatus(sid);
        }
        _runtimeStatuses.Clear();
    }
}
