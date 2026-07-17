using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Maki.Services;

/// <summary>
/// Cliente para la API de LM Studio (compatible con OpenAI).
/// Endpoint por defecto: http://localhost:1234/v1
/// </summary>
public sealed class LMStudioService : IDisposable
{
    private readonly HttpClient _http;

    // Historial de conversación (contexto acumulado)
    private readonly List<(string Role, string Content)> _history = new();

    public string BaseUrl   { get; set; } = "http://localhost:1234";
    public string ModelId   { get; set; } = "local-model";
    public float  Temperature { get; set; } = 0.7f;
    public int    MaxTokens   { get; set; } = 1024;

    public LMStudioService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>
    /// Envía un mensaje y devuelve la respuesta token a token (streaming SSE).
    /// </summary>
    public async IAsyncEnumerable<string> SendMessageAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _history.Add(("user", userMessage));

        var body = JsonSerializer.Serialize(new
        {
            model       = ModelId,
            messages    = _history.Select(h => new { role = h.Role, content = h.Content }).ToArray(),
            stream      = true,
            temperature = Temperature,
            max_tokens  = MaxTokens
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        var fullResponse = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null || !line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line[6..];
            if (data.Equals("[DONE]", StringComparison.Ordinal)) break;

            string? chunk = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta");

                if (delta.TryGetProperty("content", out var prop))
                    chunk = prop.GetString();
            }
            catch { continue; }

            if (!string.IsNullOrEmpty(chunk))
            {
                fullResponse.Append(chunk);
                yield return chunk;
            }
        }

        _history.Add(("assistant", fullResponse.ToString()));
    }

    /// <summary>
    /// Comprueba si el servidor de LM Studio está disponible.
    /// </summary>
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var r = await _http.GetAsync($"{BaseUrl}/v1/models", ct);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Limpia el historial de conversación.
    /// </summary>
    public void ClearHistory() => _history.Clear();

    public void Dispose() => _http.Dispose();
}
