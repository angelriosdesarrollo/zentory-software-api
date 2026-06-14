namespace Zentory.Application.Integrations.DTOs;

public record IntegrationDto(
    string  Id,
    string  Name,
    string  Description,
    bool    Enabled,
    bool    Connected,
    string? ConnectedAs);
