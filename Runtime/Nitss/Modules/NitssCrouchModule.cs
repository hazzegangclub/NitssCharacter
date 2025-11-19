using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssCrouchModule : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssMovementController movementController;
        [SerializeField] private NitssAnimatorController animatorController;

        [Header("Configuração")]
        [SerializeField, Range(-1f, 0f)] private float crouchPressThreshold = -0.6f;
        [SerializeField, Range(-1f, 0f)] private float crouchReleaseThreshold = -0.3f;
        [SerializeField] private string crouchAttackTrigger = "CrouchAttack";
        [SerializeField] private bool lockMovementWhileCrouched = true;

        private bool isCrouching;
        private bool movementLockApplied;
        private bool suppressUntilRelease;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            inputReader = GetComponent<NitssInputReader>();
            movementController = GetComponent<NitssMovementController>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void OnDisable()
        {
            UpdateCrouchState(false);
            ApplyMovementLock(false);
            suppressUntilRelease = false;
        }

        public void Tick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
            {
                return;
            }

            inputReader = reader ?? inputReader;
            float vertical = inputReader != null ? inputReader.Current.Move.y : 0f;
            bool wantsCrouch = inputReader != null && EvaluateCrouchIntent(vertical);

            if (suppressUntilRelease)
            {
                if (inputReader == null || vertical > crouchReleaseThreshold)
                {
                    suppressUntilRelease = false;
                }
                else
                {
                    wantsCrouch = false;
                }
            }

            UpdateCrouchState(wantsCrouch);

            if (isCrouching && inputReader != null)
            {
                movementController?.ForceFacingDirection(inputReader.Current.Move.x);

                if (inputReader.AttackPressed)
                {
                    animatorController?.SetTrigger(crouchAttackTrigger);
                }
            }
        }

        private bool EnsureReferences()
        {
            bool animatorWasNull = animatorController == null;

            if (!context)
            {
                context = GetComponent<NitssCharacterContext>();
            }
            if (!inputReader && context)
            {
                inputReader = context.InputReader;
            }
            if (!movementController)
            {
                movementController = GetComponent<NitssMovementController>();
            }
            if (!animatorController && context)
            {
                animatorController = context.AnimatorController;
            }
            if (animatorWasNull && animatorController)
            {
                animatorController.SetCrouching(isCrouching);
            }
            return movementController != null && animatorController != null;
        }

        private bool EvaluateCrouchIntent(float verticalAxis)
        {
            if (!isCrouching)
            {
                return verticalAxis <= crouchPressThreshold;
            }
            return verticalAxis <= crouchReleaseThreshold;
        }

        private void UpdateCrouchState(bool crouching)
        {
            if (isCrouching == crouching)
            {
                if (lockMovementWhileCrouched)
                {
                    ApplyMovementLock(isCrouching);
                }
                return;
            }

            isCrouching = crouching;
            animatorController?.SetCrouching(isCrouching);
            movementController?.SetCrouchYawActive(isCrouching);
            ApplyMovementLock(lockMovementWhileCrouched && isCrouching);
        }

        private void ApplyMovementLock(bool shouldLock)
        {
            if (!lockMovementWhileCrouched || movementController == null)
            {
                return;
            }

            if (shouldLock)
            {
                if (!movementLockApplied)
                {
                    movementController.AddMovementLock();
                    movementLockApplied = true;
                }
            }
            else if (movementLockApplied)
            {
                movementController.RemoveMovementLock();
                movementLockApplied = false;
            }
        }

        public void ForceExitCrouch()
        {
            if (!EnsureReferences())
            {
                return;
            }

            suppressUntilRelease = true;
            UpdateCrouchState(false);
        }
    }
}
