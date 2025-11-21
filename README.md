# NitssCharacter

Apenas os scripts do personagem Nitss, organizados por domínio. Pensado para virar um repositório/Unity package dedicado, a ser consumido por outros projetos do Hazze.

## Instalação (UPM via Git)
Adicione ao `Packages/manifest.json` do seu projeto Unity:

```json
{
  "dependencies": {
    "com.hazzegangclub.nitsscharacter": "https://github.com/hazzegangclub/NitssCharacter.git#0.4.2"
  }
}
```

Para atualizar de versão, altere apenas o sufixo da tag (por exemplo `#0.4.3`).

## Novidades 0.4.2
- Novo módulo aéreo `NitssJumpAttackModule` com combo JumpAttack1 → JumpAttack2 → JumpAttack3 após double jump.
- Delays e janelas configuráveis entre estágios; cai em JumpFall quando a janela expira.
- Impulso vertical/horizontal leve por golpe para sensação de impacto.

## Estrutura
- `Runtime/Nitss/Core` — contexto e glue do personagem
- `Runtime/Nitss/Input` — leitura de input
- `Runtime/Nitss/Movement` — locomoção
- `Runtime/Nitss/Modules` — extensões de pulo/dash/ataque
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
1. Adicione o pacote via UPM (seção acima).
2. No prefab do Nitss, referencie:
   - `NitssCharacterContext`, `NitssInputReader`, `NitssMovementController`, `NitssCombatController`, `NitssLocomotionController`
   - `NitssAnimatorController` (no GO com `Animator`)
   - `NitssHealthSync` se quiser sincronização via API
   - Em Data: `NitssSeedLoader`, `NitssSeedApplier`, `ItemEffectsApplier`, `StatusRuntimeApplier`, `TagRulesApplier` + `ItemEffectsDatabase`, `StatusEffectsDatabase`, `TagRulesProvider` assets

## Licença
Interno Hazze.
