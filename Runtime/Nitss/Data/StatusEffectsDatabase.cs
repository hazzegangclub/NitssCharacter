using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Database de efeitos de status (id → duração e buffs) para runtime.
/// Inspirado em docs/manuals/status_e_efeitos.md (ex.: barreira_cinetica, nanorregeneracao, etc.).
/// </summary>
[CreateAssetMenu(menuName = "Hazze/Gameplay/Status Effects Database", fileName = "StatusEffectsDatabase")]
public class StatusEffectsDatabase : ScriptableObject
{
    [Serializable]
    public class BuffDef
    {
        public string attribute; // attack/atk, defense/def, speed, stamina/energia, critChance/crit, critDamage/critDmg, vida/maxLife, energia/maxEnergy
        public float add;
        public float mult = 1f;
    }

    [Serializable]
    public class StatusEffectDef
    {
        public string id;          // ex.: barreira_cinetica, nanorregeneracao
        public float duration = 5; // em segundos
        public bool stackable = false;
        public int maxStacks = 1;
        public List<BuffDef> buffs = new List<BuffDef>();
    }

    public List<StatusEffectDef> effects = new List<StatusEffectDef>();

    public StatusEffectDef Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return effects.Find(e => string.Equals(e?.id, id, StringComparison.OrdinalIgnoreCase));
    }
}
