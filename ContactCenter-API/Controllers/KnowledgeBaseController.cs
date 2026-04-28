using ContactCenterPOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContactCenterPOC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly KnowledgeBaseService _knowledgeBaseService;
        private readonly ILogger<KnowledgeBaseController> _logger;

        public KnowledgeBaseController(KnowledgeBaseService knowledgeBaseService, ILogger<KnowledgeBaseController> logger)
        {
            _knowledgeBaseService = knowledgeBaseService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> ListDocuments([FromQuery] string? campaignId = null)
        {
            var documents = await _knowledgeBaseService.ListDocumentsAsync(campaignId);
            return Ok(documents);
        }

        [HttpPost]
        [RequestSizeLimit(52_428_800)] // 50MB
        public async Task<IActionResult> UploadDocument(IFormFile file, [FromForm] string? campaignId = null)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            try
            {
                using var stream = file.OpenReadStream();
                var doc = await _knowledgeBaseService.UploadDocumentAsync(stream, file.FileName, file.Length, campaignId);
                return Created($"/api/KnowledgeBase/{doc.Id}", doc);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload document");
                return StatusCode(500, new { error = "Failed to upload document", message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDocument(string id)
        {
            var doc = await _knowledgeBaseService.GetDocumentAsync(id);
            if (doc == null)
                return NotFound(new { error = "Document not found" });
            return Ok(doc);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDocument(string id)
        {
            await _knowledgeBaseService.DeleteDocumentAsync(id);
            return NoContent();
        }

        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryProcessing(string id)
        {
            var doc = await _knowledgeBaseService.GetDocumentAsync(id);
            if (doc == null)
                return NotFound(new { error = "Document not found" });

            _ = Task.Run(async () => await _knowledgeBaseService.ProcessDocumentAsync(id));
            return Accepted(new { message = "Document reprocessing started", id });
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] SearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Query))
                return BadRequest(new { error = "Query is required" });

            var results = await _knowledgeBaseService.SearchAsync(request.Query, request.Top, request.CampaignId);
            return Ok(new { results, count = results.Count });
        }
    }

    public class SearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public int Top { get; set; } = 5;
        public string? CampaignId { get; set; }
    }
}
