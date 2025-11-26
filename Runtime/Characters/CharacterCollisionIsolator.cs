using UnityEngine;

namespace Hazze.Gameplay.Characters
{
    /// <summary>
    /// Isola personagens de transferência de momentum através de colisões.
    /// Evita que um personagem "puxe" o outro quando pula ou se move.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class CharacterCollisionIsolator : MonoBehaviour
    {
        [Header("Configuração")]
        [Tooltip("Layer que identifica outros personagens")]
        [SerializeField] private LayerMask characterLayers = ~0;
        
        [Tooltip("Se verdadeiro, ignora completamente colisões entre personagens (atravessam)")]
        [SerializeField] private bool ignoreCharacterCollisions = false;
        
        [Tooltip("Se verdadeiro, bloqueia transferência de velocidade entre personagens")]
        [SerializeField] private bool preventMomentumTransfer = true;

        private Rigidbody rb;
        private Vector3 velocityBeforeCollision;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            
            if (ignoreCharacterCollisions)
            {
                SetupCollisionIgnoring();
            }
        }

        private void SetupCollisionIgnoring()
        {
            // Encontra todos os colliders do personagem
            var myColliders = GetComponentsInChildren<Collider>();
            
            // Encontra todos os outros personagens na cena
            var allCharacters = FindObjectsByType<CharacterCollisionIsolator>(FindObjectsSortMode.None);
            
            foreach (var otherChar in allCharacters)
            {
                if (otherChar == this || otherChar == null)
                    continue;
                
                var otherColliders = otherChar.GetComponentsInChildren<Collider>();
                
                // Ignora colisões entre todos os colliders deste personagem com os do outro
                foreach (var myCol in myColliders)
                {
                    foreach (var otherCol in otherColliders)
                    {
                        if (myCol != null && otherCol != null)
                        {
                            Physics.IgnoreCollision(myCol, otherCol, true);
                        }
                    }
                }
            }
        }

        private void FixedUpdate()
        {
            if (preventMomentumTransfer && !ignoreCharacterCollisions)
            {
                velocityBeforeCollision = rb.linearVelocity;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!preventMomentumTransfer || ignoreCharacterCollisions)
                return;

            // Verifica se colidiu com outro personagem
            if (IsCharacterLayer(collision.gameObject.layer))
            {
                // Restaura velocidade anterior para evitar transferência de momentum
                rb.linearVelocity = velocityBeforeCollision;
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!preventMomentumTransfer || ignoreCharacterCollisions)
                return;

            // Verifica se está em contato com outro personagem
            if (IsCharacterLayer(collision.gameObject.layer))
            {
                // Mantém velocidade controlada durante contato
                Vector3 currentVel = rb.linearVelocity;
                
                // Permite movimento vertical (pulos) mas limita influência horizontal
                currentVel.x = Mathf.Lerp(currentVel.x, velocityBeforeCollision.x, 0.5f);
                currentVel.z = Mathf.Lerp(currentVel.z, velocityBeforeCollision.z, 0.5f);
                
                rb.linearVelocity = currentVel;
            }
        }

        private bool IsCharacterLayer(int layer)
        {
            return ((1 << layer) & characterLayers) != 0;
        }

        // Método público para adicionar/remover personagens da lista de ignore em runtime
        public void IgnoreCollisionsWith(CharacterCollisionIsolator other, bool ignore = true)
        {
            var myColliders = GetComponentsInChildren<Collider>();
            var otherColliders = other.GetComponentsInChildren<Collider>();
            
            foreach (var myCol in myColliders)
            {
                foreach (var otherCol in otherColliders)
                {
                    if (myCol != null && otherCol != null)
                    {
                        Physics.IgnoreCollision(myCol, otherCol, ignore);
                    }
                }
            }
        }
    }
}
