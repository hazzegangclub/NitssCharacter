using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Define efeitos derivados de itens e equipamentos (status, buffs) aplicados ao Nitss.
/// </summary>
[CreateAssetMenu(menuName = "Hazze/Gameplay/Item Effects Database", fileName = "ItemEffectsDatabase")]
public class ItemEffectsDatabase : ScriptableObject
{
    [Serializable]
    public class StatusEntry
    {
        public string id;
        [Tooltip("Override de duração em segundos. Use valor <= 0 para manter o valor da database de status.")]
        public float durationOverrideSeconds = 0f;
    }

    [Serializable]
    public class ItemEffectDef
    {
        [Tooltip("Id do item/equipamento conforme seed (ex.: ampola_nanorregeneracao). ")]
        public string id;
        [Tooltip("Buffs que permanecem enquanto o item estiver listado no seed.")]
        public List<NitssSeedApplier.Buff> buffs = new();
        [Tooltip("Statuses adicionais disparados ao aplicar o seed.")]
        public List<StatusEntry> statuses = new();
        [Tooltip("Cura instantânea aplicada ao receber o item (Damageable.Heal).")]
        public float instantHeal = 0f;
        [Tooltip("Energia restaurada instantaneamente ao aplicar o item.")]
        public float instantEnergy = 0f;
        [Tooltip("Remove os status informados imediatamente após aplicar o item.")]
        public List<string> removeStatusIds = new();
    }

    public List<ItemEffectDef> items = new();

    public ItemEffectDef Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return items.Find(i => string.Equals(i?.id, id, StringComparison.OrdinalIgnoreCase));
    }
}
