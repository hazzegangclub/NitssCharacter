using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Define regras que mapeiam tags em buffs padrão.
/// </summary>
[CreateAssetMenu(fileName = "TagRulesProvider", menuName = "Hazze/Data/Tag Rules Provider", order = 1001)]
public class TagRulesProvider : ScriptableObject
{
    [Serializable]
    public class TagRule
    {
        public string tag;
        public List<BuffDef> buffs = new();
    }

    [Serializable]
    public class BuffDef
    {
        public string attribute;
        public float add;
        public float mult = 1f;
    }

    [Tooltip("Lista de regras tag->buffs")] public List<TagRule> rules = new();

    public void GetBuffsFor(IList<string> tags, List<NitssSeedApplier.Buff> outBuffs)
    {
        if (tags == null || outBuffs == null) return;
        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var rule = rules.Find(r => string.Equals(r.tag, t, StringComparison.OrdinalIgnoreCase));
            if (rule == null || rule.buffs == null) continue;
            foreach (var b in rule.buffs)
            {
                outBuffs.Add(new NitssSeedApplier.Buff
                {
                    attribute = b.attribute,
                    add = b.add,
                    mult = b.mult
                });
            }
        }
    }
}
