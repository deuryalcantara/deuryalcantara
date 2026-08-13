// Ruta destino:
// src/micro-person-api/Application/Facephi/Queries/GetFacePhiDocumentQry.cs
//
// Es una Query, no un Command: no modifica estado. Sigue la convención del
// micro (ExtractDocumentDataQry en Biometrics.cs).
//
// GetFacePhiDocumentResponse ya existe en tu código. Si su propiedad
// DocumentType está declarada como "string" no nullable, cámbiala a "string?"
// o sustituye el null de abajo por string.Empty:
//
//     public class GetFacePhiDocumentResponse
//     {
//         public bool HasDocument { get; set; }
//         public string? DocumentType { get; set; }
//     }

using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Interfaces.Biometric;

namespace onboarding_micro_person.Application.Facephi.Queries
{
    public class GetFacePhiDocumentQry : IRequest<GetFacePhiDocumentResponse>
    {
        public string IdT24 { get; set; } = string.Empty;
    }

    public class GetFacePhiDocumentQryValidator : AbstractValidator<GetFacePhiDocumentQry>
    {
        public GetFacePhiDocumentQryValidator()
        {
            RuleFor(x => x.IdT24)
                .NotEmpty()
                .WithMessage("idT24 is required.");
        }
    }

    public class GetFacePhiDocumentQryHandler(
        IFacePhiBiometricRepository repository,
        ILogger<GetFacePhiDocumentQryHandler> logger)
        : IRequestHandler<GetFacePhiDocumentQry, GetFacePhiDocumentResponse>
    {
        private readonly IFacePhiBiometricRepository _repository = repository;
        private readonly ILogger<GetFacePhiDocumentQryHandler> _logger = logger;

        public async Task<GetFacePhiDocumentResponse> Handle(
            GetFacePhiDocumentQry qry,
            CancellationToken cancellationToken)
        {
            var biometric = await _repository.GetByIdT24Async(qry.IdT24, cancellationToken);

            // Lo que habilita validate-face es el token del documento, no la
            // existencia del registro: un documento sin token1 no sirve.
            var hasDocument = !string.IsNullOrWhiteSpace(biometric?.Token1);

            _logger.LogInformation(
                "Document lookup | IdT24: {IdT24} | HasDocument: {HasDocument}",
                qry.IdT24, hasDocument);

            return new GetFacePhiDocumentResponse
            {
                HasDocument = hasDocument,

                // Nunca se exponen token1 ni token2: son datos biométricos.
                DocumentType = hasDocument ? biometric!.DocumentType : null
            };
        }
    }
}
