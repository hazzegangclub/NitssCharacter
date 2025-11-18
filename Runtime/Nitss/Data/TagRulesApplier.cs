using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aplica buffs provenientes de tags usando um TagRulesProvider.
/// Mantém a lista separada como runtimeBuffs e injeta em applier.extraBuffs.
/// </summary>
[AddComponentMenu("Hazze/Gameplay/Tag Rules Applier")]
public class TagRulesApplier : MonoBehaviour
{
    public NitssSeedApplier applier;
    public TagRulesProvider rulesProvider;

    [Tooltip("Aplica automaticamente quando o seed for aplicado (onApplied).")]
    public bool applyOnSeedApplied = true;

    private readonly List<NitssSeedApplier.Buff> _runtimeBuffs = new();

    private void Awake()
    {
        if (!applier) applier = GetComponentInParent<NitssSeedApplier>() ?? GetComponent<NitssSeedApplier>();
    }

    private void OnEnable()
    {
        if (applyOnSeedApplied && applier != null)
        {
            applier.onApplied.AddListener(ApplyFromTags);
        }
    }

    private void OnDisable()
    {
        if (applier != null)
        {
            applier.onApplied.RemoveListener(ApplyFromTags);
        }
        ClearRuntimeBuffs();
    }

    public void ApplyFromTags()
    {
        if (applier == null || rulesProvider == null) return;

        // remove anteriores primeiro
        ClearRuntimeBuffs(noRecalc: true);

        // cria novos a partir das tags atuais
        var temp = new List<NitssSeedApplier.Buff>();
        rulesProvider.GetBuffsFor(applier.tags, temp);
        foreach (var b in temp)
        {
            _runtimeBuffs.Add(b);
            applier.extraBuffs.Add(b);
        }

        applier.Recalculate();
    }

    public void ClearRuntimeBuffs(bool noRecalc = false)
    {
        if (applier == null) { _runtimeBuffs.Clear(); return; }
        foreach (var b in _runtimeBuffs) applier.extraBuffs.Remove(b);
        _runtimeBuffs.Clear();
        if (!noRecalc) applier.Recalculate();
    }
}
