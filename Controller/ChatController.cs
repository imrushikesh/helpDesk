using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using helpdesk.Api.Services;

namespace helpDeskApi.Api.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly HuggingFaceClient _hf;
        private readonly PineconeClient _pine;

        // Constructor injection: ensure these are registered in Program.cs
        public ChatController(HuggingFaceClient hf, PineconeClient pine)
        {
            _hf = hf;
            _pine = pine;
        }

        // Request DTO
        public record AskReq(string question);

        // POST api/chat/ask
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] AskReq req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.question))
                return BadRequest(new { error = "question is required" });

            try
            {
                // 1) Embed the incoming question
                var qVec = await _hf.EmbedAsync(req.question);

                // 2) Query Pinecone for nearest chunks
                var hits = await _pine.QueryAsync(qVec);

                // If no hits, return a polite "I don't know" answer and empty citations
                if (hits == null || hits.Count == 0)
                {
                    return Ok(new
                    {
                        answer = "I don't know based on the current documents.",
                        citations = new object[0]
                    });
                }

                // 3) Build a small CONTEXT string and collect citations
                var sb = new StringBuilder();
                var citations = new List<object>();
                foreach (var (id, score, meta) in hits)
                {
                    // meta is JsonElement coming from Pinecone client
                    string title = "doc";
                    int page = -1;
                    string snippet = "";

                    if (meta.TryGetProperty("title", out var t)) title = t.GetString() ?? title;
                    if (meta.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number) page = p.GetInt32();
                    if (meta.TryGetProperty("snippet", out var s)) snippet = s.GetString() ?? "";

                    sb.AppendLine($"[{title}, p{page}] {snippet}");
                    citations.Add(new { title, page, score });
                }

                // 4) Create prompt for generation (concise, instruct to cite pages)
                var prompt = new StringBuilder();
                prompt.AppendLine("You are a concise enterprise policy assistant. Use ONLY the CONTEXT to answer.");
                prompt.AppendLine("If the answer can't be found in the context, reply: \"I don't know based on the current documents.\"");
                prompt.AppendLine();
                prompt.AppendLine("QUESTION:");
                prompt.AppendLine(req.question);
                prompt.AppendLine();
                prompt.AppendLine("CONTEXT:");
                prompt.AppendLine(sb.ToString());
                prompt.AppendLine();
                prompt.AppendLine("Answer briefly and cite pages inline like (DocTitle, pX):");

                // 5) Call HF generation (may return plain text or JSON; client wraps parsing)
                //var answer = await _hf.GenerateAsync(prompt.ToString());
                var ans =   await _hf.ChatAsync(
    "You are a concise enterprise policy assistant. Use ONLY the CONTEXT to answer. If the answer can't be found, reply 'I don't know based on the current documents.'",
    prompt.ToString()
);
                // 6) Return answer + citations
                return Ok(new
                {
                    ans,
                    citations
                });
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                 
                return StatusCode(502, new { error = ex.Message, detail = ex.Message });
            }
            catch (System.Exception ex)
            {
                // generic failure
                return StatusCode(500, new { error = "Internal error", detail = ex.Message });
            }
        }
    }
}
