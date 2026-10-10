using MediaIntelligence.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediaIntelligence.Api.Controllers
{
    public sealed record AskRequest(string Question);

    [ApiController]
    [Route("api/agent")]
    public sealed class AgentController : ControllerBase
    {
        private readonly MediaAgentService _agent;

        public AgentController(MediaAgentService agent)
        {
            _agent = agent;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask(
            [FromBody] AskRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question is required.");

            var answer = await _agent.AskAsync(
                request.Question,
                cancellationToken);

            return Ok(new { answer });
        }
    }
}
