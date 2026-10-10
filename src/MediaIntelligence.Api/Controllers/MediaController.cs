
using MediaIntelligence.Core.Interfaces;
using MediaIntelligence.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace MediaIntelligence.Api.Controllers
{
    [ApiController]
    [Route("api/media")]
    public sealed class MediaController : ControllerBase
    {
        private readonly IMediaRepository _repository;
        private readonly IAudienceAnalytics _analytics;

        public MediaController(
            IMediaRepository repository,
            IAudienceAnalytics analytics)
        {
            _repository = repository;
            _analytics = analytics;
        }

        [HttpGet]
        public ActionResult<IReadOnlyList<MediaContent>> GetContents()
        {
            return Ok(_repository.GetContents());
        }

        [HttpGet("{id:guid}/analytics")]
        public IActionResult GetAnalytics(Guid id)
        {
            var content = _repository.GetContent(id);

            if (content is null)
                return NotFound();

            var events = _repository.GetViewingEvents(id);

            return Ok(new
            {
                content.Id,
                content.Title,
                ViewingSessions = events.Count,
                CompletionRate = _analytics.GetCompletionRate(id, events),
                AdExposureRate = _analytics.GetAdExposureRate(id, events)
            });
        }
    }
}
