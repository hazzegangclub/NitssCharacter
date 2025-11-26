using UnityEngine;

/// <summary>
/// Character-specific animator driver for Nitss. Currently it relies entirely on the
/// base <see cref="CharacterAnimatorController"/> behaviour, but this dedicated class
/// keeps our prefab wired to a Nitss-only script so we can extend it with unique logic
/// later without touching other characters.
/// </summary>
public class NitssAnimatorController : CharacterAnimatorController
{
    // Future Nitss-specific animation hooks can live here.
}
