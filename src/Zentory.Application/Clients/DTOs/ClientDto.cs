namespace Zentory.Application.Clients.DTOs;

public record ClientDto(
    Guid    Id,
    string  Name,
    string  ContactName,
    string? Email,
    string? Phone,
    string? City,
    string? Nit,
    string? Notes,
    int     ActiveProjects,
    decimal TotalBilled);

public record ClientSummaryDto(
    Guid    Id,
    string  Name,
    string  ContactName,
    string? Email,
    string? City,
    int     ActiveProjects,
    decimal TotalBilled);
