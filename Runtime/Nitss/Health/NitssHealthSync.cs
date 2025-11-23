using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace Hazze.Gameplay.Characters.Nitss
{
    /// <summary>
    /// Sincroniza a vida do personagem com a API do player service.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NitssHealthSync : MonoBehaviour
    {
        [Header("API")]
        [SerializeField] private string gatewayBaseUrl = "http://127.0.0.1:8080";
        [SerializeField] private string playerId = "nitss_local";
        [SerializeField] private string bearerToken = "dev-bearer";
        [SerializeField] private float pollIntervalSeconds = 2f;

        [Header("Fallback Local Values")]
        [SerializeField, Min(1f)] private float defaultMaxHealth = 100f;
        [SerializeField, Min(0f)] private float defaultCurrentHealth = 100f;

        [Header("Events")]
        public UnityEvent<float, float> onHealthChanged;
        public UnityEvent onHealthDepleted;

    private float maxHealth;
        private float currentHealth;
        private Coroutine pollRoutine;
        private bool pushInFlight;
    [Header("Push Settings")]
    [SerializeField, Tooltip("Quando true, envia também o 'max' no payload de push. Mantenha false para evitar sobrescrever o valor vindo da API.")]
    private bool includeMaxInPush = false;
    private bool hasRemoteMax = false;
        public bool HasRemoteMax => hasRemoteMax;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public string PlayerId => playerId;

        private void Awake()
        {
            maxHealth = defaultMaxHealth;
            currentHealth = Mathf.Clamp(defaultCurrentHealth, 0f, maxHealth);
        }

        private void OnEnable()
        {
            if (pollRoutine == null)
            {
                pollRoutine = StartCoroutine(PollLoop());
            }
        }

        private void OnDisable()
        {
            if (pollRoutine != null)
            {
                StopCoroutine(pollRoutine);
                pollRoutine = null;
            }
        }

        /// <summary>
        /// Aplica dano localmente e agenda envio para a API.
        /// </summary>
        public void ApplyDamage(float amount)
        {
            if (amount <= 0f) return;
            float before = currentHealth;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            if (!Mathf.Approximately(before, currentHealth))
            {
                // Não emite 'max' em eventos locais para evitar que ouvintes (Damageable) regredam o MaxHealth para o fallback.
                onHealthChanged?.Invoke(currentHealth, 0f);
            }
            if (currentHealth <= 0f)
            {
                onHealthDepleted?.Invoke();
            }
            if (!pushInFlight)
            {
                StartCoroutine(PushHealth());
            }
        }

        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            float before = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            if (!Mathf.Approximately(before, currentHealth))
            {
                // Não altera 'max' em heals locais
                onHealthChanged?.Invoke(currentHealth, 0f);
                if (!pushInFlight)
                {
                    StartCoroutine(PushHealth());
                }
            }
        }

        private IEnumerator PollLoop()
        {
            while (enabled)
            {
                yield return FetchHealth();
                if (pollIntervalSeconds <= 0.01f) yield return null;
                else yield return new WaitForSeconds(pollIntervalSeconds);
            }
        }

        private IEnumerator FetchHealth()
        {
            string url = BuildUrl();
            using (var req = UnityWebRequest.Get(url))
            {
                ConfigureHeaders(req);
                yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool failed = req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError;
#else
                bool failed = req.isNetworkError || req.isHttpError;
#endif
                if (failed)
                {
                    yield break;
                }

                var payload = JsonUtility.FromJson<HealthPayload>(req.downloadHandler.text);
                if (payload != null)
                {
                    maxHealth = payload.max > 0f ? payload.max : maxHealth;
                    float before = currentHealth;
                    currentHealth = Mathf.Clamp(payload.current, 0f, maxHealth);
                    if (payload.max > 0f) hasRemoteMax = true;
                    if (!Mathf.Approximately(before, currentHealth))
                    {
                        onHealthChanged?.Invoke(currentHealth, maxHealth);
                        if (currentHealth <= 0f)
                        {
                            onHealthDepleted?.Invoke();
                        }
                    }
                }
            }
        }

        private IEnumerator PushHealth()
        {
            pushInFlight = true;
            string url = BuildUrl();
            string json;
            if (includeMaxInPush && hasRemoteMax)
            {
                var payload = new HealthPayload { current = currentHealth, max = maxHealth };
                json = JsonUtility.ToJson(payload);
            }
            else
            {
                var payload = new HealthCurrentPayload { current = currentHealth };
                json = JsonUtility.ToJson(payload);
            }
            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT))
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(data);
                req.downloadHandler = new DownloadHandlerBuffer();
                ConfigureHeaders(req);
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
            }
            pushInFlight = false;
        }

        private string BuildUrl()
        {
            string baseUrl = string.IsNullOrWhiteSpace(gatewayBaseUrl) ? "http://127.0.0.1:8080" : gatewayBaseUrl;
            string pid = string.IsNullOrWhiteSpace(playerId) ? "nitss_local" : playerId;
            return baseUrl.TrimEnd('/') + "/v1/players/" + pid + "/health";
        }

        private void ConfigureHeaders(UnityWebRequest req)
        {
            if (!string.IsNullOrEmpty(bearerToken))
            {
                req.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            }
        }

        [System.Serializable]
        private class HealthPayload
        {
            public float current;
            public float max;
        }

        [System.Serializable]
        private class HealthCurrentPayload
        {
            public float current;
        }
    }
}
