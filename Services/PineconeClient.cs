using System.Net.Http.Json;
using System.Text.Json;

namespace helpdesk.Api.Services
{
    // Minimal Pinecone client that calls the index URL you paste into appsettings
    public class PineconeClient
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _indexUrl; // base: e.g. https://<your-index>.<region>.pinecone.io

        public PineconeClient(IHttpClientFactory factory, IConfiguration cfg)
        {
            _http = factory.CreateClient();
            _apiKey = cfg["Pinecone:ApiKey"] ?? throw new ArgumentNullException("Pinecone:ApiKey");
            _indexUrl = cfg["Pinecone:IndexUrl"] ?? throw new ArgumentNullException("Pinecone:IndexUrl");
            _http.DefaultRequestHeaders.Add("Api-Key", _apiKey);
        }

        // Upsert a single vector with metadata
        public async Task UpsertAsync(string id, float[] values, object metadata)
        {
            var url = $"{_indexUrl}/vectors/upsert";
            var body = new
            {
                vectors = new[] {
                    new {
                        id,
                        values,
                        metadata
                    }
                }
            };
            using var res = await _http.PostAsJsonAsync(url, body);
            res.EnsureSuccessStatusCode();
        }

        // Query topK; returns list of (id, score, metadata)
        public async Task<List<(string id, float score, JsonElement metadata)>> QueryAsync(float[] vector, int topK = 10)
        {
            var url = $"{_indexUrl}/query";
            var body = new
            {
                vector,
                topK,
                includeMetadata = true
            };
            using var res = await _http.PostAsJsonAsync(url, body);
            res.EnsureSuccessStatusCode();
            var raw = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var results = new List<(string, float, JsonElement)>();
            if (doc.RootElement.TryGetProperty("matches", out var matches))
            {
                foreach (var m in matches.EnumerateArray())
                {
                    var id = m.GetProperty("id").GetString() ?? "";
                    var score = m.GetProperty("score").GetSingle();
                    var meta = m.GetProperty("metadata").Clone();
                    results.Add((id, score, meta));
                }
            }
            return results;
        }
    }
}
