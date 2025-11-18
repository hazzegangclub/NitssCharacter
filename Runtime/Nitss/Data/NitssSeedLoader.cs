using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Hazze.Networking;

/// <summary>
/// Carrega os dados (seed) do Nitss a partir da API e disponibiliza o JSON bruto
/// para outros sistemas aplicarem cálculos/atributos no personagem.
/// </summary>
[AddComponentMenu("Hazze/Networking/Nitss Seed Loader")]
public class NitssSeedLoader : MonoBehaviour
{
    [Header("API")]
    [Tooltip("Base URL do gateway local.")]
    public string baseUrl = "http://127.0.0.1:8080";
    [Tooltip("Endpoint relativo do seed (ex.: /v1/catalog/seeds/nitss).")]
    public string endpointPath = "/v1/catalog/seeds/nitss";
    [Tooltip("Bearer token para rotas protegidas (ex.: dev-bearer no ambiente local).")]
    public string bearerToken = "dev-bearer";

    [Header("Fluxo")]
    [Tooltip("Dispara o load automaticamente no Start.")]
    public bool loadOnStart = true;

    [Tooltip("Quando ativo, loga sucesso/erro no Console do Unity.")]
    public bool debugLog = true;

    [Header("Saída")]
    [TextArea(3, 6)]
    public string lastJson; // armazenamos o JSON bruto para debug/integração inicial
    public UnityEvent<string> onSeedLoadedJson; // devolve o JSON para quem quiser consumir
    public UnityEvent<string> onSeedLoadError; // mensagem de erro (HTTP, rede, etc.)

    public string ComposeUrl()
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return endpointPath;
        if (string.IsNullOrWhiteSpace(endpointPath)) return baseUrl;
        if (baseUrl.EndsWith("/") && endpointPath.StartsWith("/"))
            return baseUrl.TrimEnd('/') + endpointPath;
        if (!baseUrl.EndsWith("/") && !endpointPath.StartsWith("/"))
            return baseUrl + "/" + endpointPath;
        return baseUrl + endpointPath;
    }

    private void Start()
    {
        if (loadOnStart)
        {
            Load();
        }
    }

    [ContextMenu("Load Seed Now")]
    public void Load()
    {
        var url = ComposeUrl();
        if (debugLog)
        {
            Debug.Log($"[NitssSeedLoader] Request -> {url}", this);
        }
        StartCoroutine(Hazze.Networking.ApiClient.GetJson(url, bearerToken, (err, json) =>
        {
            if (!string.IsNullOrEmpty(err))
            {
                if (debugLog)
                {
                    Debug.LogWarning($"[NitssSeedLoader] Error: {err}", this);
                }
                onSeedLoadError?.Invoke(err);
                return;
            }
            lastJson = json;
            if (debugLog)
            {
                var preview = string.IsNullOrEmpty(json)
                    ? "<empty>"
                    : (json.Length > 200 ? json.Substring(0, 200) + "…" : json);
                Debug.Log($"[NitssSeedLoader] OK ({json?.Length ?? 0} chars) -> {preview}", this);
            }
            onSeedLoadedJson?.Invoke(json);
        }));
    }
}
