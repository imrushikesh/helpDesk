using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace helpdesk.Api.Services
{
    public class HuggingFaceClient
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _embeddingModel;
        private readonly string _generationModel;
        private readonly ILogger<HuggingFaceClient> _log;

        public HuggingFaceClient(IHttpClientFactory factory, IConfiguration cfg, ILogger<HuggingFaceClient> log)
        {
            _http = factory.CreateClient();
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _apiKey = cfg["HuggingFace:ApiKey"] ?? throw new ArgumentNullException("HuggingFace:ApiKey");
            _embeddingModel = cfg["HuggingFace:EmbeddingModel"] ?? "nomic-ai/nomic-embed-text-v1";
            _generationModel = cfg["HuggingFace:GenerationModel"] ?? "google/flan-t5-small";
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        /// <summary>
        /// Call HF embeddings endpoint and return float[] embedding.
        /// Handles and logs errors; throws HttpRequestException with details on failure.
        /// </summary>
        public async Task<float[]> EmbedAsync(string text)
        {
            var url = $"https://api-inference.huggingface.co/models/{_embeddingModel}";
            var payload = new { inputs = text };
            string raw = "";

            try
            {
                using var res = await _http.PostAsJsonAsync(url, payload);
                raw = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    _log.LogError("HF Embed failed. URL={Url} Status={Status} Body={Body}", url, (int)res.StatusCode, raw);
                    throw new HttpRequestException($"HF embedding request failed ({(int)res.StatusCode}): {raw}");
                }

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    // HuggingFace returns [[floats]]
                    var embeddingArray = root;
                    return JsonArrayToFloatArray(embeddingArray);
                }

                _log.LogError("HF embedding response parsing failed. Raw: {Raw}", raw);
                throw new HttpRequestException("HF embedding response did not contain an embedding.");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (JsonException jex)
            {
                _log.LogError(jex, "Failed to parse HF embedding JSON. URL={Url} Body={Body}", url, raw);
                throw new HttpRequestException("Failed to parse HF embedding response JSON.", jex);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error calling HF embedding. URL={Url} Body={Body}", url, raw);
                throw new HttpRequestException("Unexpected error calling Hugging Face embeddings.", ex);
            }
        }
        /// <summary>
        /// Call HF model to generate text from prompt. Returns plain string.
        /// Logs and wraps errors similarly to EmbedAsync.
        /// </summary>
        public async Task<string> GenerateAsync(string prompt)
        {
            var url = $"https://api-inference.huggingface.co/models/{_generationModel}";
            var req = new { inputs = prompt, parameters = new { max_new_tokens = 256 } };
            string raw = "";

            try
            {
                using var res = await _http.PostAsJsonAsync(url, req);
                raw = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    _log.LogError("HF Generate failed. URL={Url} Status={Status} Body={Body}", url, (int)res.StatusCode, raw);
                    throw new HttpRequestException($"HF generation request failed ({(int)res.StatusCode}): {raw}");
                }

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("generated_text", out var gt))
                        return gt.GetString() ?? "";

                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    {
                        var first = root[0];
                        if (first.TryGetProperty("generated_text", out var g2)) return g2.GetString() ?? "";
                    }
                }
                catch (JsonException)
                {
                    // not JSON — fall through and return raw
                    throw;
                }

                return raw.Trim('"');
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error calling HF generation. URL={Url} Body={Body}", url, raw);
                throw new HttpRequestException("Unexpected error calling Hugging Face generation.", ex);
            }
        }

        // Helper: convert JsonElement array to float[] robustly (handles double/floating numbers)
        private static float[] JsonArrayToFloatArray(JsonElement arr)
        {
            if (arr.ValueKind == JsonValueKind.Array)
            {
                var temp = new List<float>(arr.GetArrayLength());
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number)
                        temp.Add((float)el.GetDouble());
                    else if (el.ValueKind == JsonValueKind.String && float.TryParse(el.GetString(), out var f))
                        temp.Add(f);
                }
                return temp.ToArray();
            }
            else if (arr.ValueKind == JsonValueKind.Number)
            {
                return new float[] { (float)arr.GetDouble() };
            }
            else if (arr.ValueKind == JsonValueKind.String && float.TryParse(arr.GetString(), out var f))
            {
                return new float[] { f };
            }

            throw new ArgumentException("Expected array or number, got " + arr.ValueKind);
        }

    
    public async Task<string> ChatAsync(string systemPrompt, string userPrompt)
{
    var url = "https://router.huggingface.co/v1/chat/completions";
    var body = new
    {
        model = _generationModel,  
        messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        },
        max_tokens = 256
    };

    string raw = "";
    using var res = await _http.PostAsJsonAsync(url, body);
    raw = await res.Content.ReadAsStringAsync();

    if (!res.IsSuccessStatusCode)
    {
        _log.LogError("HF Chat failed. Status={Status} Body={Body}", (int)res.StatusCode, raw);
        throw new HttpRequestException($"HF Chat request failed ({(int)res.StatusCode}): {raw}");
    }

    using var doc = JsonDocument.Parse(raw);
    var root = doc.RootElement;

    var msg = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    return msg ?? "";
}

    
    }
}
