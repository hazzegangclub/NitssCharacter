using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Coordena o bloqueio do Nitss: lê a entrada (L2), encaminha para o controlador de combate
    /// e mantém o parâmetro <c>IsBlocking</c> sincronizado no animator.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NitssCharacterContext))]
    public sealed class NitssBlockModule : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private NitssCharacterContext context;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssCombatController combatController;
        [SerializeField] private NitssAnimatorController animatorController;
        [SerializeField] private NitssMovementController movementController;

        [Header("Restrição Direcional")]
        [SerializeField, Range(0f, 45f)] private float blockAngleTolerance = 10f;

        private bool lastAnimatorState;
        private bool movementLockApplied;
        private bool suppressBlockUntilRelease;

        private const float BlockAngleRight = 110f;
        private const float BlockAngleRightStationary = 165f;
        private const float BlockAngleLeft = 250f;

        private void Reset()
        {
            context = GetComponent<NitssCharacterContext>();
            inputReader = GetComponent<NitssInputReader>();
            combatController = GetComponent<NitssCombatController>();
            animatorController = context ? context.AnimatorController : GetComponentInChildren<NitssAnimatorController>();
            movementController = GetComponent<NitssMovementController>();
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void OnDisable()
        {
            if (animatorController != null)
            {
                animatorController.SetBlocking(false);
            }
            lastAnimatorState = false;
            combatController?.SetBlockRequest(false);
            UpdateMovementLock(false);
            suppressBlockUntilRelease = false;
        }

        public void PreCombatTick(float dt, NitssInputReader reader)
        {
            if (!EnsureReferences())
            {
                return;
            }

            inputReader = reader ?? inputReader;
            bool rawBlockHeld = inputReader != null && inputReader.Current.BlockHeld;
            if (!rawBlockHeld)
            {
                suppressBlockUntilRelease = false;
            }

            bool blockHeld = rawBlockHeld && !suppressBlockUntilRelease;
            UpdateMovementLock(blockHeld);

            bool wantsBlock = ShouldAllowBlock(blockHeld);
            if (wantsBlock)
            {
                combatController?.SetBlockRequest(true);
            }
        }

        public void PostCombatTick()
        {
            if (!EnsureReferences())
            {
                return;
            }

            bool rawBlockHeld = inputReader != null && inputReader.Current.BlockHeld;
            if (!rawBlockHeld)
            {
                suppressBlockUntilRelease = false;
            }

            bool blockHeld = rawBlockHeld && !suppressBlockUntilRelease;
            UpdateMovementLock(blockHeld);

            bool isBlocking = blockHeld && combatController != null && combatController.IsBlocking && ShouldAllowBlock(blockHeld);
            if (animatorController != null && isBlocking != lastAnimatorState)
            {
                animatorController.SetBlocking(isBlocking);
                lastAnimatorState = isBlocking;
            }
        }

        public void ForceBlockRelease()
        {
            if (!EnsureReferences())
            {
                return;
            }

            suppressBlockUntilRelease = true;

            combatController?.CancelBlock();
            UpdateMovementLock(false);

            if (animatorController != null && lastAnimatorState)
            {
                animatorController.SetBlocking(false);
                lastAnimatorState = false;
            }
        }

        private bool EnsureReferences()
        {
            if (!context)
            {
                context = GetComponent<NitssCharacterContext>();
            }
            if (!inputReader && context)
            {
                inputReader = context.InputReader;
            }
            if (!combatController)
            {
                combatController = GetComponent<NitssCombatController>();
            }
            if (!animatorController && context)
            {
                animatorController = context.AnimatorController;
            }
            if (!movementController)
            {
                movementController = GetComponent<NitssMovementController>();
            }
            return combatController != null;
        }

        private bool ShouldAllowBlock(bool blockHeld)
        {
            if (!blockHeld)
            {
                return false;
            }

            if (!movementController)
            {
                return true;
            }

            Transform visual = context ? context.VisualRoot : null;
            if (!visual)
            {
                return true;
            }

            float angle = visual.localEulerAngles.y;
            bool facingRight = movementController.FacingDirection.x >= 0f;
            float primary = facingRight ? BlockAngleRight : BlockAngleLeft;
            float secondary = facingRight ? BlockAngleRightStationary : BlockAngleLeft;
            float deltaPrimary = Mathf.Abs(Mathf.DeltaAngle(angle, primary));
            float deltaSecondary = facingRight ? Mathf.Abs(Mathf.DeltaAngle(angle, secondary)) : float.PositiveInfinity;
            float delta = Mathf.Min(deltaPrimary, deltaSecondary);
            return delta <= blockAngleTolerance;
        }

        private void UpdateMovementLock(bool shouldLock)
        {
            if (!movementController)
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
    }
}
