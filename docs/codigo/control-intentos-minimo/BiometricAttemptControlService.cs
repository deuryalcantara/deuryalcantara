// Ruta destino:
// src/micro-person-api/Infrastructure/Biometric/BiometricAttemptControlService.cs
//
// Toda la política vive aquí: umbral, ventana de bloqueo, desbloqueo automático
// y qué se audita. Los handlers sólo reportan el resultado de su intento.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using onboarding_micro_person.Common.Configuration;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Exceptions;
using onboarding_micro_person.Common.Interfaces.Biometric;

namespace onboarding_micro_person.Infrastructure.Biometric
{
    public interface IBiometricAttemptControlService
    {
        /// <summary>
        /// Se llama ANTES de invocar a FacePhi. Si el cliente está bloqueado lanza
        /// BiometricAttemptsBlockedException (→ 423). Si el bloqueo ya expiró, lo
        /// libera y deja continuar.
        /// </summary>
        Task EnsureNotBlockedAsync(string idT24, string flow, CancellationToken cancellationToken);

        Task RegisterSuccessAsync(
            string idT24, string flow, string? serviceTransactionId,
            CancellationToken cancellationToken);

        Task RegisterRejectionAsync(
            string idT24, string flow, string? serviceTransactionId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Error técnico: se audita pero NO incrementa el contador. Un cliente no
        /// puede quedar bloqueado porque FacePhi estuviera caído.
        /// </summary>
        Task RegisterTechnicalErrorAsync(
            string idT24, string flow, string errorDetail,
            CancellationToken cancellationToken);
    }

    public class BiometricAttemptControlService(
        IFacePhiAttemptControlRepository repository,
        IBiometricAuditPublisher auditPublisher,
        IHttpContextAccessor httpContextAccessor,
        IOptions<AppSettings> settings,
        ILogger<BiometricAttemptControlService> logger) : IBiometricAttemptControlService
    {
        public const string FlowValidateBiometric = "validate-biometric";
        public const string FlowValidateFace = "validate-face";

        private readonly IFacePhiAttemptControlRepository _repository = repository;
        private readonly IBiometricAuditPublisher _auditPublisher = auditPublisher;
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
        private readonly AppSettings _settings = settings.Value;
        private readonly ILogger<BiometricAttemptControlService> _logger = logger;

        public async Task EnsureNotBlockedAsync(
            string idT24,
            string flow,
            CancellationToken cancellationToken)
        {
            if (!_settings.BIOMETRIC_ATTEMPT_CONTROL_ENABLED)
            {
                return;
            }

            var utcNow = DateTime.UtcNow;
            var state = await _repository.GetByIdT24Async(idT24, cancellationToken);

            if (state is null || !state.IsBlocked)
            {
                return;
            }

            // Desbloqueo automático perezoso: el bloqueo sólo tiene efecto cuando
            // alguien intenta validar, así que es aquí donde se comprueba si la
            // ventana venció. No hace falta un job programado.
            if (!state.IsCurrentlyBlocked(utcNow))
            {
                var released = await _repository.ReleaseExpiredBlockAsync(idT24, cancellationToken);

                // released == null significa que otra petición simultánea ya lo
                // liberó. El resultado es el mismo: el cliente puede continuar.
                if (released is not null)
                {
                    _logger.LogInformation(
                        "Biometric block expired and was released | IdT24: {IdT24} | Flow: {Flow}",
                        idT24, flow);

                    await PublishAsync(
                        BiometricAuditEventType.AutomaticUnblock, idT24, flow, utcNow,
                        "UNBLOCKED", released, null, null, cancellationToken);
                }

                return;
            }

            var retryAfterSeconds = state.RemainingSeconds(utcNow);

            _logger.LogWarning(
                "Biometric validation attempted while blocked | IdT24: {IdT24} | Flow: {Flow} | " +
                "BlockedUntil: {BlockedUntil} | RetryAfterSeconds: {RetryAfterSeconds}",
                idT24, flow, state.BlockedUntil, retryAfterSeconds);

            await PublishAsync(
                BiometricAuditEventType.AttemptWhileBlocked, idT24, flow, utcNow,
                "BLOCKED", state, null, null, cancellationToken);

            throw new BiometricAttemptsBlockedException(
                idT24, state.BlockedUntil!.Value, retryAfterSeconds);
        }

        public async Task RegisterSuccessAsync(
            string idT24,
            string flow,
            string? serviceTransactionId,
            CancellationToken cancellationToken)
        {
            var utcNow = DateTime.UtcNow;

            FacePhiAttemptControlDocument? state = null;

            if (_settings.BIOMETRIC_ATTEMPT_CONTROL_ENABLED)
            {
                state = await _repository.RegisterSuccessfulAttemptAsync(idT24, flow, cancellationToken);

                _logger.LogInformation(
                    "Biometric attempt counter reset after success | IdT24: {IdT24} | Flow: {Flow}",
                    idT24, flow);
            }

            await PublishAsync(
                BiometricAuditEventType.AttemptSuccess, idT24, flow, utcNow,
                "APPROVED", state, serviceTransactionId, null, cancellationToken);
        }

        public async Task RegisterRejectionAsync(
            string idT24,
            string flow,
            string? serviceTransactionId,
            CancellationToken cancellationToken)
        {
            var utcNow = DateTime.UtcNow;

            if (!_settings.BIOMETRIC_ATTEMPT_CONTROL_ENABLED)
            {
                // Con el control apagado se sigue auditando: se pierde el bloqueo,
                // no la trazabilidad.
                await PublishAsync(
                    BiometricAuditEventType.AttemptFailed, idT24, flow, utcNow,
                    "REJECTED", null, serviceTransactionId, null, cancellationToken);

                return;
            }

            var state = await _repository.RegisterFailedAttemptAsync(
                idT24,
                flow,
                _settings.BIOMETRIC_MAX_FAILED_ATTEMPTS,
                _settings.BIOMETRIC_BLOCK_DURATION_MINUTES,
                cancellationToken);

            _logger.LogWarning(
                "Biometric attempt rejected | IdT24: {IdT24} | Flow: {Flow} | " +
                "FailedAttempts: {FailedAttempts} | Blocked: {Blocked}",
                idT24, flow, state.FailedAttempts, state.IsBlocked);

            await PublishAsync(
                BiometricAuditEventType.AttemptFailed, idT24, flow, utcNow,
                "REJECTED", state, serviceTransactionId, null, cancellationToken);

            // El bloqueo es un evento distinto del fallo que lo provoca: soporte
            // necesita poder responder "¿cuándo se bloqueó?" sin recomponer la
            // secuencia de cinco fallos.
            if (state.IsCurrentlyBlocked(utcNow))
            {
                _logger.LogWarning(
                    "Customer blocked for biometric validation | IdT24: {IdT24} | " +
                    "FailedAttempts: {FailedAttempts} | BlockedUntil: {BlockedUntil}",
                    idT24, state.FailedAttempts, state.BlockedUntil);

                await PublishAsync(
                    BiometricAuditEventType.BlockedByExcess, idT24, flow, utcNow,
                    "BLOCKED", state, serviceTransactionId, null, cancellationToken);
            }
        }

        public async Task RegisterTechnicalErrorAsync(
            string idT24,
            string flow,
            string errorDetail,
            CancellationToken cancellationToken)
        {
            var utcNow = DateTime.UtcNow;

            // Deliberadamente NO se llama a RegisterFailedAttemptAsync.
            // Un error técnico no es un intento fallido del cliente.
            var state = await _repository.GetByIdT24Async(idT24, cancellationToken);

            _logger.LogError(
                "Biometric technical error, counter not incremented | IdT24: {IdT24} | " +
                "Flow: {Flow} | Detail: {Detail}",
                idT24, flow, errorDetail);

            await PublishAsync(
                BiometricAuditEventType.TechnicalError, idT24, flow, utcNow,
                "ERROR", state, null, errorDetail, cancellationToken);
        }

        private Task PublishAsync(
            string eventType,
            string idT24,
            string flow,
            DateTime utcNow,
            string result,
            FacePhiAttemptControlDocument? state,
            string? serviceTransactionId,
            string? errorDetail,
            CancellationToken cancellationToken)
        {
            return _auditPublisher.PublishAsync(
                new BiometricAuditEvent
                {
                    EventType = eventType,
                    IdT24 = idT24,
                    Flow = flow,
                    OccurredAt = utcNow,
                    Result = result,
                    FailedAttempts = state?.FailedAttempts ?? 0,
                    Blocked = state?.IsCurrentlyBlocked(utcNow) ?? false,
                    BlockedUntil = state?.BlockedUntil,
                    RequestId = _httpContextAccessor.HttpContext?
                        .Request.Headers["requestId"].FirstOrDefault(),
                    ServiceTransactionId = serviceTransactionId,
                    ErrorDetail = errorDetail
                },
                cancellationToken);
        }
    }
}
