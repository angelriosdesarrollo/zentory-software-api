using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Projects.Commands;
using Zentory.Application.Projects.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/files")]
public sealed class ProjectFilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectFilesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/projects/{projectId}/files</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectFilesQuery(projectId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/projects/{projectId}/files/upload-url — pide URL pre-firmada antes de subir</summary>
    [HttpPost("upload-url")]
    public async Task<IActionResult> GetUploadUrl(
        Guid projectId, [FromBody] GetProjectFileUploadUrlCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { ProjectId = projectId }, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/projects/{projectId}/files — registra el archivo tras subirlo al UploadUrl</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId, [FromBody] CreateProjectFileCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { ProjectId = projectId }, ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/projects/{projectId}/files/{fileId}/download-url</summary>
    [HttpGet("{fileId:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid projectId, Guid fileId, CancellationToken ct = default)
    {
        var url = await _mediator.Send(new GetProjectFileDownloadUrlQuery(projectId, fileId), ct);
        return Ok(new { url });
    }
}
