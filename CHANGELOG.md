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

