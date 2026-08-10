// Ruta destino:
// src/micro-person-api/Application/Facephi/Commands/ValidateIdentityV2Cmd.cs
//
// Requiere que existan primero (ver los otros dos archivos de esta carpeta):
//   - Common/Enums/FacePhi/ValidationOutcome.cs
//   - Common/Models/Biometric/ValidateIdentityV2Response.cs

using FluentValidation;
using MediatR;
using onboarding_micro_person.Common.Enums.FacePhi;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;

namespace onboarding_micro_person.Application.Facephi.Commands
{
    public class ValidateIdentityV2Cmd : IRequest<ValidateIdentityV2Response>
    {
        public string Token1 { get; set; } = string.Empty;

        public string BestImageToken { get; set; } = string.Empty;

        public string Method { get; set; } = string.Empty;

        public FacephiTrackingExtraData? Tracking { get; set; }
    }

    public class ValidateIdentityV2CmdValidator : AbstractValidator<ValidateIdentityV2Cmd>
    {
        public ValidateIdentityV2CmdValidator()
        {
            RuleFor(x => x.Token1)
                .NotEmpty()
                .WithMessage("token1 is required.");

            RuleFor(x => x.BestImageToken)
                .NotEmpty()
                .WithMessage("bestImageToken is required.");

            RuleFor(x => x.Method)
                .NotEmpty()
                .WithMessage("method is required.")
                .Must(method => method is "3" or "5")
                .WithMessage("method must be 3 or 5.");

            When(x => x.Tracking is not null, () =>
            {
                RuleFor(x => x.Tracking!.OperationId)
                    .Must(operationId => string.IsNullOrWhiteSpace(operationId)
                                         || Guid.TryParse(operationId, out _))
                    .WithMessage("tracking.operationId must be a valid UUID.");
            });
        }
    }

    public class ValidateIdentityV2CmdHandler
        : IRequestHandler<ValidateIdentityV2Cmd, ValidateIdentityV2Response>
    {
        // Códigos documentados de Identity Validation V2.
        // facialAuthenticationResult: 0 NONE, 1 NEGATIVE, 3 POSITIVE,
        //                             4 POSE EXCEED, 5 INVALID EXTRACTIONS
        private const int FacialPositive = 3;
        private const int FacialNegative = 1;

        // passiveLivenessResult: 3 Live, 17 NoLive, 1 Spoof (deprecado),
        //                        10 InternalError, 15 LicenseError,
        //                        el resto = no se pudo evaluar
        private const int LivenessLive = 3;
        private const int LivenessNoLive = 17;
        private const int LivenessSpoof = 1;
        private const int LivenessInternalError = 10;
        private const int LivenessLicenseError = 15;

        private readonly IFacePhiService _facePhiService;
        private readonly ILogger<ValidateIdentityV2CmdHandler> _logger;

        public ValidateIdentityV2CmdHandler(
            IFacePhiService facePhiService,
            ILogger<ValidateIdentityV2CmdHandler> logger)
        {
            _facePhiService = facePhiService;
            _logger = logger;
        }

        public async Task<ValidateIdentityV2Response> Handle(
            ValidateIdentityV2Cmd request,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Starting Identity Validation V2. " +
                "Method: {Method}, TrackingProvided: {TrackingProvided}",
                request.Method,
                request.Tracking is not null);

            var result = await _facePhiService.EvaluatePassiveLivenessToken(
                new PassiveLivenessRequest
                {
                    Token1 = request.Token1,
                    BestImageToken = request.BestImageToken,
                    Method = request.Method,
                    TrackingToken = request.Tracking?.ExtraData,
                    OperationId = request.Tracking?.OperationId
                },
                cancellationToken);

            _logger.LogInformation(
                "Identity Validation V2 completed. " +
                "ServiceResultCode={ServiceResultCode}, " +
                "FacialResult={FacialResult}, " +
                "Similarity={Similarity}, " +
                "LivenessResult={LivenessResult}, " +
                "TransactionId={TransactionId}",
                result.ServiceResultCode,
                result.facialAuthenticationResult,
                result.facialAuthenticationSimilarity,
                result.passiveLivenessResult,
                result.ServiceTransactionId);

            var outcome = Evaluate(result);

            _logger.LogInformation(
                "Identity Validation V2 outcome={Outcome}. TransactionId={TransactionId}",
                outcome,
                result.ServiceTransactionId);

            return new ValidateIdentityV2Response
            {
                Outcome = outcome,
                FacialAuthenticationResult = result.facialAuthenticationResult,
                PassiveLivenessResult = (int)result.passiveLivenessResult,
                FacialAuthenticationSimilarity = result.facialAuthenticationSimilarity,
                ServiceTransactionId = result.ServiceTransactionId
            };
        }

        /// <summary>
        /// Traduce la respuesta de Facephi a un veredicto.
        /// serviceResultCode == 0 sólo indica que el módulo se ejecutó:
        /// NO significa que la persona haya sido aprobada.
        /// </summary>
        private static ValidationOutcome Evaluate(PassiveLivenessResult result)
        {
            if (result.ServiceResultCode != 0)
            {
                return ValidationOutcome.Error;
            }

            var liveness = (int)result.passiveLivenessResult;

            // Fallos del motor de Facephi: no es culpa de la captura del cliente.
            if (liveness is LivenessInternalError or LivenessLicenseError)
            {
                return ValidationOutcome.Error;
            }

            // Rechazos reales: el rostro no coincide o no se detectó vida.
            if (result.facialAuthenticationResult == FacialNegative
                || liveness is LivenessNoLive or LivenessSpoof)
            {
                return ValidationOutcome.Rejected;
            }

            // Lista blanca: sólo se aprueba con POSITIVE y Live explícitos.
            // No usar "liveness != NoLive": la doc de evaluatePassiveLiveness
            // muestra un rechazo que devuelve 0, no 17.
            if (result.facialAuthenticationResult == FacialPositive
                && liveness == LivenessLive)
            {
                return ValidationOutcome.Approved;
            }

            // Todo lo demás: la captura no permitió evaluar. El cliente repite.
            return ValidationOutcome.Retry;
        }
    }
}
