using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using helpdesk.Api.Services;
using helpdesk.Api.Utils;

namespace helpdesk.Api.Controllers
{
    [ApiController]
    [Route("admin")]
    public class AdminController : ControllerBase
    {
        private readonly string _docsPath;
        private readonly long _maxFileBytes = 50 * 1024 * 1024; // 50 MB cap
        private readonly HuggingFaceClient _hf;
        private readonly PineconeClient _pine;
        private readonly ILogger<AdminController> _log;

        public AdminController(IConfiguration cfg, HuggingFaceClient hf, PineconeClient pine, ILogger<AdminController> log)
        {
            _hf = hf ?? throw new ArgumentNullException(nameof(hf));
            _pine = pine ?? throw new ArgumentNullException(nameof(pine));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            // Resolve configured path or fall back to Desktop/helpDeskPdfs
            var configured = cfg["Storage:DocsPath"];
            if (string.IsNullOrWhiteSpace(configured))
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                _docsPath = Path.Combine(desktop, "helpDeskPdfs");
            }
            else
            {
                var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
                if (!Path.IsPathRooted(expanded))
                {
                    expanded = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
                }
                _docsPath = expanded;
            }

            try
            {
                Directory.CreateDirectory(_docsPath);
                _log.LogInformation("Using docs path: {Path}", _docsPath);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to create docs path: {Path}", _docsPath);
                throw;
            }
        }

        // POST /admin/upload
        [HttpPost("upload")]
        [RequestSizeLimit(60_000_000)]
        public async Task<IActionResult> Upload(IFormFile? file)
        {
            if (file == null) return BadRequest(new { error = "No file uploaded. Use form-data field 'file'." });

            var ext = Path.GetExtension(file.FileName) ?? "";
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Only PDF files are allowed." });

            if (file.Length == 0) return BadRequest(new { error = "Empty file." });
            if (file.Length > _maxFileBytes) return BadRequest(new { error = $"File too large. Max {_maxFileBytes} bytes." });

            // Safe filename
            var baseName = Path.GetFileNameWithoutExtension(file.FileName);
            foreach (var c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
            var destFileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{baseName}.pdf";
            var destPath = Path.Combine(_docsPath, destFileName);

            try
            {
                await using (var fs = System.IO.File.Create(destPath))
                {
                    await file.CopyToAsync(fs);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to save uploaded file");
                return StatusCode(500, new { error = "Failed to save file", detail = ex.Message });
            }

            int pagesCount = 0;
            int chunksIndexed = 0;
            try
            {
                // Re-open the saved file for processing
                await using var openStream = System.IO.File.OpenRead(destPath);
                var pages = PdfUtils.ReadPages(openStream); // Dictionary<int,string>
                pagesCount = pages.Count;
                var chunks = PdfUtils.ChunkPages(pages); // List<(int Page, string Text)>

                _log.LogInformation("Indexing {Chunks} chunks from {File}", chunks.Count, destFileName);

                // Process chunks sequentially (ok for POC). For larger loads, consider batching or background work.
                foreach (var (page, text) in chunks)
                {
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 20) continue;

                    float[] emb;
                    try
                    {
                        emb = await _hf.EmbedAsync(text);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Embedding failed for {File} page {Page}", destFileName, page);
                        // skip this chunk but continue others
                        continue;
                    }

                    var id = $"{Path.GetFileNameWithoutExtension(destFileName)}_p{page}_{Guid.NewGuid():N}";
                    var metadata = new
                    {
                        title = Path.GetFileNameWithoutExtension(file.FileName),
                        page = page,
                        snippet = text.Length > 300 ? text.Substring(0, 300) : text
                    };

                    try
                    {
                        _log.LogInformation("Embedding length: {Length}", emb.Length);
                        await _pine.UpsertAsync(id, emb, metadata);
                        chunksIndexed++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Pinecone upsert failed for id {Id}", id);
                        // skip this upsert and continue
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to process file {File}", destFileName);
                return StatusCode(500, new { error = "Failed to process file", detail = ex.Message });
            }

            return Ok(new
            {
                message = "Uploaded and indexed (POC mode)",
                file = destFileName,
                pages = pagesCount,
                chunksIndexed
            });
        }

        // Optional: list uploaded files for admin UI
        [HttpGet("files")]
        public IActionResult Files()
        {
            IEnumerable<object> files = Enumerable.Empty<object>();

            if (Directory.Exists(_docsPath))
            {
                files = Directory.GetFiles(_docsPath)
                    .Select(p => (object)new { name = Path.GetFileName(p), size = new FileInfo(p).Length })
                    .ToList();
            }

            return Ok(files);
        }
    }
}
