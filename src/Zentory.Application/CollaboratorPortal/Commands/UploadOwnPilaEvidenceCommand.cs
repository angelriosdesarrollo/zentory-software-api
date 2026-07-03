using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Subida de PILA desde el portal — el colaborador puede reportar cualquier período, no
// solo los que la empresa pidió explícitamente. Reusa la misma lógica de "buscar o crear
// por período" de UploadPilaEvidenceCommandHandler; la diferencia es de dónde sale el
// collaboratorId/organizationId (sesión de portal, no un token de solicitud puntual).
//
// Source se decide solo al CREAR la fila, nunca al actualizar una existente: si ya había
// una fila para ese período (la empresa la solicitó, o el colaborador ya había subido algo
// antes), su origen no cambia solo porque se suba una nueva versión.
public sealed record UploadOwnPilaEvidenceCommand(
    string  Period,
    string  StorageKey,
    string? FileName    = null,
    long?   FileSize    = null,
    string? ContentType = null) : IRequest;

public sealed class UploadOwnPilaEvidenceCommandValidator : AbstractValidator<UploadOwnPilaEvidenceCommand>
{
    public UploadOwnPilaEvidenceCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
        RuleFor(x => x.StorageKey).NotEmpty();
    }
}

public sealed class UploadOwnPilaEvidenceCommandHandler : IRequestHandler<UploadOwnPilaEvidenceCommand>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly ICollaboratorPortalContext  _portal;
    private readonly IUnitOfWork                 _uow;

    public UploadOwnPilaEvidenceCommandHandler(
        IPilaVerificationRepository verifications, ICollaboratorPortalContext portal, IUnitOfWork uow)
    {
        _verifications = verifications;
        _portal        = portal;
        _uow           = uow;
    }

    public async Task Handle(UploadOwnPilaEvidenceCommand request, CancellationToken cancellationToken)
    {
        var target = await _verifications.GetByCollaboratorAndPeriodAsync(
            _portal.ActiveCollaboratorId, request.Period, cancellationToken);

        if (target is null)
        {
            target = PilaVerification.Create(
                _portal.ActiveOrganizationId, _portal.ActiveCollaboratorId, request.Period,
                createdBy: null, source: "self_service");
            target.MarkReceived(request.StorageKey, request.FileName, request.FileSize, request.ContentType);
            await _verifications.AddAsync(target, cancellationToken);
        }
        else
        {
            target.MarkReceived(request.StorageKey, request.FileName, request.FileSize, request.ContentType);
            await _verifications.UpdateAsync(target, cancellationToken);
        }

        var document = PilaVerificationDocument.Create(
            target.Id, request.StorageKey, request.FileName, request.FileSize, request.ContentType);
        await _verifications.AddDocumentAsync(document, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
    }
}
