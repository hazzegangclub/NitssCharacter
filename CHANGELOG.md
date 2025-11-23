# NitssCharacter 0.4.4 (2025-11-22)
Consolida o combo aéreo com lógica simplificada, integra uppercut independente e sincroniza todos os módulos com o projeto principal.
## Highlights
- `NitssJumpAttackModule` reescrito com fila de ataques, janela configurável e integração direta ao Animator (JumpAttack1/2/3).
- `NitssUppercutModule` exportado com suporte a stage 4 e triggers sincronizados.
- `NitssGroundAttackModule`, `NitssCombatController` e `NitssLocomotionController` atualizados para coexistir com combos aéreos.
- Ajustes em `NitssBlockModule`/`NitssCrouchModule` para respeitar novos estados.
## Notes
- Compatível com as animações e GUIDs utilizados na branch principal do jogo.
- Requer atualizar o prefab do Nitss para referenciar o novo módulo de uppercut.

# NitssCharacter 0.4.3 (2025-11-22)

Introduces configurable heavy "Uppercut" stage integration (ground → launch) and docs updates.

## Highlights
- Adds heavy uppercut stage concept used by downstream (HazzeGangClubStrikers) for launch combos
- Documentation: README bump + installation tag update
- Version metadata bump to 0.4.3 for UPM consumption

## Notes
- Uppercut stage vertical launch and indexing are defined downstream (main project) until a dedicated module lands
- Consumers should update manifest to `#0.4.3`

# NitssCharacter 0.4.2 (2025-11-21)

Adds aerial combo module with staged JumpAttacks after double jump.

## Highlights
- New `NitssJumpAttackModule` implements JumpAttack1 → JumpAttack2 → JumpAttack3 with configurable delay and window per stage
- Predictable fallback to JumpFall when next input window expires
- Light impulse tuning for better aerial feel

## Notes
- Timings are exposed via inspector fields (attack2/3 min delay + window)
- Recommended to playtest and fine tune `attack2WindowSeconds` / `attack3WindowSeconds`

# NitssCharacter 0.4.1 (2025-11-19)

Adds Unity `.meta` files so the package imports cleanly when consumed via Git.

## Highlights
- Ships `.meta` for every runtime script and documentation asset to satisfy UPM's immutable cache
- Unblocks HazzeGangClubStrikers from using NitssCharacter without manual package embedding

# NitssCharacter 0.4.0 (2025-11-19)

Aligns the package manifest with semantic versioning requirements for Git-based delivery.

## Highlights
- Bumps package metadata to `0.4.0` for consumption by the HazzeGangClubStrikers project
- Keeps runtime content identical to 0.04 but fixes version parsing errors in Unity

# NitssCharacter 0.04 (2025-11-19)

Paridade de manifesto do pacote com a versão utilizada pelo projeto principal.

## Destaques
- Inclui `package.json` no repositório NitssCharacter
- Mantém os binários e scripts de runtime da versão 0.03

# NitssCharacter 0.03 (2025-11-19)

Adiciona o controlador de combos terrestres do Nitss.

## Highlights
- Implementa `NitssGroundAttackModule` com filas e cancelamentos
- Estende o `NitssCombatController` para orquestrar os estágios de combo
- Ajusta o dash para respeitar estados de ataque e travações de movimento

# NitssCharacter 0.02 (2025-11-19)

Adds dedicated dash and jump modules along with updated locomotion integration.

## Highlights
- Introduces `NitssDashModule` with air dash polish and JumpFall recovery control
- Adds `NitssJumpModule` supporting buffered jumps, double jump trigger e ajustes anti-loop
- Atualiza `NitssMovementController` e `NitssLocomotionController` para dar suporte aos novos módulos

# NitssCharacter 0.01 (2025-11-18)

Initial Nitss character package for Unity via UPM.

## Highlights
- Rigidbody-based locomotion and movement smoothing
- Animator wrapper for Nitss (`NitssAnimatorController`)
- Context wiring (`NitssCharacterContext`)
- Combat basics and orchestration (`NitssCombatController`, `NitssLocomotionController`)
- Input reader with optional Unity Input System
- Health sync scaffolding
- Data seed + status/item/tag appliers

## Rotation targets
- Right idle: 165°
- Left idle: 230°
- Left moving: 250°

## Removed
- Dash functionality removed from movement controller
