// Ruta destino:
// src/micro-person-api/Application/Facephi/Commands/ValidateFaceOnlyCmd.cs
//
// OJO: el archivo actual se llama ValidateFaceOnlyCms.cs — renómbralo a Cmd.
//
// Ajusta estos dos usings a lo que exista realmente en el micro:
//   - el namespace de NotFoundException (grep -rn "class NotFoundException" --include=*.cs src/)
//   - el namespace donde vive ValidateIdentityV2Result (abre ValidateIdentityV2Results.cs)

using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using onboarding_micro_person.Common.Configuration;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Enums.FacePhi;
using onboarding_micro_person.Common.Exceptions;
using onboarding_micro_person.Common.Extensions.FacePhi;
using onboarding_micro_person.Common.Interfaces;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;

namespace onboarding_micro_person.Application.Facephi.Commands
{
    public class ValidateFaceOnlyCmd : IRequest<ValidateIdentityV2Result>
    {
        public string IdT24 { get; set; } = string.Empty;

        public string BestImageToken { get; set; } = string.Empty;

        public FacephiTrackingExtraData? Tracking { get; set; }
    }

    public class ValidateFaceOnlyCmdValidator : AbstractValidator<ValidateFaceOnlyCmd>
    {
        public ValidateFaceOnlyCmdValidator()
        {
            RuleFor(x => x.IdT24)
                .NotEmpty()
                .WithMessage("idT24 is required.");

            RuleFor(x => x.BestImageToken)
                .NotEmpty()
                .WithMessage("bestImageToken is required.");

            When(x => x.Tracking is not null, () =>
            {
                RuleFor(x => x.Tracking!.OperationId)
                    .Must(operationId => string.IsNullOrWhiteSpace(operationId)
                                         || Guid.TryParse(operationId, out _))
                    .WithMessage("tracking.operationId must be a valid UUID.");
            });
        }
    }

    public class ValidateFaceOnlyCmdHandler(
        IFacePhiService facePhi,
        IFacePhiBiometricRepository repository,
        IIdentityValidationEvaluator identityValidationEvaluator,
        IOptions<AppSettings> settings,
        ILogger<ValidateFaceOnlyCmdHandler> logger
    ) : IRequestHandler<ValidateFaceOnlyCmd, ValidateIdentityV2Result>
    {
        private const int FacialAuthenticationPositive = 3;

        private readonly IFacePhiService _facePhi = facePhi;
        private readonly IFacePhiBiometricRepository _repository = repository;
        private readonly IIdentityValidationEvaluator _identityValidationEvaluator = identityValidationEvaluator;
        private readonly AppSettings _settings = settings.Value;
        private readonly ILogger<ValidateFaceOnlyCmdHandler> _logger = logger;

        public async Task<ValidateIdentityV2Result> Handle(
            ValidateFaceOnlyCmd cmd,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting face-only validation for IdT24: {IdT24}", cmd.IdT24);

            var biometric = await _repository.GetByIdT24Async(cmd.IdT24, cancellationToken);

            // No basta con que exista el registro: tiene que tener el token del documento.
            if (string.IsNullOrWhiteSpace(biometric?.Token1))
            {
                _logger.LogWarning("No stored document found | IdT24: {IdT24}", cmd.IdT24);

                throw new NotFoundException("Customer does not have a stored document.");
            }

            var result = await _facePhi.EvaluatePassiveLivenessToken(
                new PassiveLivenessRequest
                {
                    Token1 = biometric.Token1,
                    BestImageToken = cmd.BestImageToken,
                    Method = biometric.Method.ToString(),
                    TrackingToken = cmd.Tracking?.ExtraData,
                    OperationId = cmd.Tracking?.OperationId
                },
                cancellationToken);

            _logger.LogInformation(
                "LivenessLog: {LivenessLog} | AuthenticationLog: {AuthenticationLog} | IdT24: {IdT24}",
                result.passiveLivenessLog, result.facialAuthenticationLog, cmd.IdT24);

            var approved = false;
            var similarity = result.facialAuthenticationSimilarity;

            if (result.ServiceResultCode != 0)
            {
                _logger.LogWarning(
                    "Unsuccessful service result | ServiceResultCode: {ServiceResultCode} | " +
                    "TransactionId: {TransactionId} | IdT24: {IdT24}",
                    result.ServiceResultCode, result.ServiceTransactionId, cmd.IdT24);
            }
            else
            {
                // La prueba de vida es el control principal de este flujo:
                // el cliente ya no presenta el documento físico, sólo su rostro.
                var isAlive = result.passiveLivenessResult == FacephiLivenessResult.Live;

                if (_settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK)
                {
                    var meetsSimilarityThreshold =
                        _identityValidationEvaluator.MeetsSimilarityThreshold(similarity);

                    approved = meetsSimilarityThreshold && isAlive;

                    _logger.LogInformation(
                        "Custom liveness check result | Similarity: {Similarity:F4} | " +
                        "MeetsSimilarityThreshold: {MeetsSimilarityThreshold} | IsAlive: {IsAlive} | IdT24: {IdT24}",
                        similarity, meetsSimilarityThreshold, isAlive, cmd.IdT24);
                }
                else
                {
                    approved = result.facialAuthenticationResult == FacialAuthenticationPositive && isAlive;
                }
            }

            var validationStatus = approved
                ? FacialValidationStatus.Approved
                : FacialValidationStatus.Rejected;

            _logger.LogInformation(
                "Face-only validation result: {Status} | Similarity: {Similarity:F4} | IdT24: {IdT24}",
                validationStatus, similarity, cmd.IdT24);

            var trackingToken = cmd.Tracking?.ExtraData;

            if (!string.IsNullOrWhiteSpace(trackingToken))
            {
                var reason = (approved
                    ? FacePhiTrackingReason.None
                    : FacePhiTrackingReason.FacialAuthenticationNotPassed).ToApiString();

                await _facePhi.FinishTrackingAsync(approved, reason, trackingToken, cancellationToken);
            }

            _logger.LogInformation(
                "Face-only validation completed for IdT24: {IdT24} with Status: {Status} " +
                "and TransactionId: {TransactionId}",
                cmd.IdT24, validationStatus, result.ServiceTransactionId);

            return new ValidateIdentityV2Result(validationStatus, result);
        }
    }
}
