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

