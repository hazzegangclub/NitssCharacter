using UnityEngine;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Armazena referências comuns do personagem Nitss.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NitssCharacterContext : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private NitssInputReader inputReader;
        [SerializeField] private NitssHealthSync healthSync;
        [SerializeField] private NitssAnimatorController animatorController;

        public Rigidbody Body => body;
        public Animator Animator => animator;
        public Transform VisualRoot => visualRoot ? visualRoot : transform;
        public NitssInputReader InputReader => inputReader;
        public NitssHealthSync HealthSync => healthSync;
        public NitssAnimatorController AnimatorController => animatorController;

        private void Reset()
        {
            body = GetComponentInChildren<Rigidbody>();
            animator = GetComponentInChildren<Animator>();
            visualRoot = transform;
            inputReader = GetComponent<NitssInputReader>();
            healthSync = GetComponent<NitssHealthSync>();
            animatorController = GetComponentInChildren<NitssAnimatorController>();
        }

        private void Awake()
        {
            if (!body) body = GetComponentInChildren<Rigidbody>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!visualRoot)
            {
                if (animator)
                {
                    visualRoot = animator.transform;
                }
                else
                {
                    visualRoot = transform;
                }
            }
            if (!inputReader) inputReader = GetComponent<NitssInputReader>();
            if (!healthSync) healthSync = GetComponent<NitssHealthSync>();
            if (!animatorController) animatorController = GetComponentInChildren<NitssAnimatorController>();

            if (animatorController)
            {
                if (animator) animatorController.TrackAnimator(animator);
                if (body) animatorController.TrackRigidbody(body);
            }

            // Configura Rigidbody para evitar fricção entre personagens
            if (body)
            {
                body.maxDepenetrationVelocity = 1f; // Reduz velocidade de separação
                body.sleepThreshold = 0.005f; // Mantém padrão mas garante configuração
            }
        }
    }
}
