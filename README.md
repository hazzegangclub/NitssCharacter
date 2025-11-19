# NitssCharacter

Apenas os scripts do personagem Nitss, organizados por domínio. Pensado para virar um repositório/Unity package dedicado, a ser consumido por outros projetos do Hazze.

## Estrutura
- `Runtime/Nitss/Core` — contexto e glue do personagem
- `Runtime/Nitss/Input` — leitura de input
- `Runtime/Nitss/Movement` — locomoção
- `Runtime/Nitss/Modules` — extensões de pulo/dash e outros comportamentos
- `Runtime/Nitss/Combat` — combate básico
- `Runtime/Nitss/Locomotion` — orquestração de ciclo de jogo
- `Runtime/Nitss/Animation` — ponte com Animator
- `Runtime/Nitss/Health` — sync de vida com API
- `Runtime/Nitss/Data` — seed loader/applier e aplicadores de efeitos/tag/status

## Dependências (fora deste pacote)
- `CharacterAnimatorController` (comum) — base usada por `NitssAnimatorController`
- `Damageable` (comum) — usado por `NitssSeedApplier`/vida
- `Hazze.Networking.ApiClient` — usado por `NitssSeedLoader`
- Unity Input System (opcional) — `ENABLE_INPUT_SYSTEM` em `NitssInputReader`

## Como usar (resumo)
1. Adicione os scripts deste pacote no seu projeto.
2. No prefab do Nitss, referencie:
   - `NitssCharacterContext`, `NitssInputReader`, `NitssMovementController`, `NitssCombatController`, `NitssLocomotionController`
   - `NitssAnimatorController` (no GO com `Animator`)
   - `NitssHealthSync` se quiser sincronização via API
   - Em Data: `NitssSeedLoader`, `NitssSeedApplier`, `ItemEffectsApplier`, `StatusRuntimeApplier`, `TagRulesApplier` + `ItemEffectsDatabase`, `StatusEffectsDatabase`, `TagRulesProvider` assets

## Licença
Interno Hazze. 