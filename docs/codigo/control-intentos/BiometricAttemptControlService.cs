// Ruta destino:
// src/micro-person-api/Infrastructure/Biometric/BiometricAttemptControlService.cs
//
// La interfaz va en el mismo archivo por comodidad de lectura. Si el micro
// separa contratos, muévela a Common/Interfaces/Biometric/.
//
// Aquí vive TODA la política: umbral, ventana de bloqueo, desbloqueo automático
// y qué se audita. Los handlers no saben nada de esto; sólo reportan el
// resultado de su intento.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using onboarding_micro_person.Common.Configuration;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Exceptions;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;

namespace onboarding_micro_person.Infrastructure.Biometric
{
    public interface IBiometricAttemptControlService
    {
        /// <summary>
        /// Verifica el bloqueo ANTES de invocar a FacePhi. Si está bloqueado lanza
        /// BiometricAttemptsBlockedException (→ HTTP 423). Si el bloqueo ya expiró,
        /// lo libera y deja seguir.
        /// </summary>
        Task EnsureNotBlockedAsync(
            string idT24,
            BiometricFlow flow,
            CancellationToken cancellationToken);

        Task RegisterSuccessAsync(
            string idT24,
            BiometricFlow flow,
            BiometricAttemptContext context,
            CancellationToken cancellationToken);

        Task RegisterRejectionAsync(
            string idT24,
            BiometricFlow flow,
            BiometricAttemptContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Error técnico: se audita pero NO incrementa el contador. Un cliente no
        /// puede quedar bloqueado porque FacePhi estuviera caído.
        /// </summary>
        Task RegisterTechnicalErrorAsync(
            string idT24,
            BiometricFlow flow,
            string errorDetail,
            BiometricAttemptContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>Trazabilidad de la petición. Ninguno de estos campos es dato biométrico.</summary>
    public class BiometricAttemptContext
    {
        public string? RequestId { get; set; }

        public string? OperationId { get; set; }

        public string? ServiceTransactionId { get; set; }

        public static readonly BiometricAttemptContext Empty = new();
    }

    public class BiometricAttemptControlService(
        IFacePhiAttemptControlRepository repository,
        IBiometricAuditPublisher auditPublisher,
        TimeProvider timeProvider,
        IOptions<AppSettings> settings,
        ILogger<BiometricAttemptControlService> logger) : IBiometricAttemptControlService
    {
        private readonly IFacePhiAttemptControlRepository _repository = repository;
        private readonly IBiometricAuditPublisher _auditPublisher = auditPublisher;
        private readonly TimeProvider _timeProvider = timeProvider;
        private readonly AppSettings _settings = settings.Value;
        private readonly ILogger<BiometricAttemptControlService> _logger = logger;

        public async Task EnsureNotBlockedAsync(
            string idT24,
            BiometricFlow flow,
            CancellationToken cancellationToken)
        {
            if (!_settings.BIOMETRIC_ATTEMPT_CONTROL_ENABLED)
            {
                return;
            }

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var state = await _repository.GetByIdT24Async(idT24, cancellationToken);

            if (state is null || !state.IsBlocked)
            {
                return;
            }

            // Desbloqueo automático: no hace falta un job. El bloqueo sólo tiene
            // efecto en el momento en que alguien intenta validar, así que es ahí
            // donde se comprueba si la ventana ya venció. Un job programado
            // añadiría un componente más, dependería del reloj de cada pod y no
            // cambiaría nada de lo que ve el cliente.
            if (!state.IsCurrentlyBlocked(utcNow))
            {
                var released = await _repository.ReleaseExpiredBlockAsync(
                    idT24, utcNow, cancellationToken);

                // released == null significa que otra petición simultánea ya lo
                // liberó. El resultado es el mismo: el cliente puede continuar.
                if (released is not null)
                {
                    _logger.LogInformation(
                        "Biometric block expired and was released | IdT24: {IdT24} | Flow: {Flow}",
                        idT24, flow.ToApiString());

                    await PublishAsync(
                        BiometricAuditEventType.AutomaticUnblock,
                        idT24, flow, utcNow, "UNBLOCKED", released,
                        BiometricAttemptContext.Empty, null, cancellationToken);
                }

                return;
            }

            var retryAfterSeconds = state.RemainingSeconds(utcNow);

            _logger.LogWarning(
                "Biometric validation attempted while blocked | IdT24: {IdT24} | Flow: {Flow} | " +
                "BlockedUntil: {BlockedUntil} | RetryAfterSeconds: {RetryAfterSeconds}",
                idT24, flow.ToApiString(), state.BlockedUntil, retryAfterSeconds);

            await PublishAsync(
                BiometricAuditEventType.AttemptWhileBlocked,
                idT24, flow, utcNow, "BLOCKED", state,
                BiometricAttemptContext.Empty, null, cancellationToken);

            throw new BiometricAttemptsBlockedException(
                idT24, state.BlockedUntil!.Value, retryAfterSeconds);
        }

        public async Task RegisterSuccessAsync(
            string idT24,
            BiometricFlow flow,
            BiometricAttemptContext context,
            CancellationToken cancellationToken)
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

            if (!_settings.BIOMETRIC_ATTEMPT_CONTROL_ENABLED)
            {
                // Con el control apagado se sigue auditando: se pierde el bloqueo,
                // no la trazabilidad.
                await PublishAsync(
                    BiometricAuditEventType.AttemptSuccess,
                    idT24, flow, utcNow, "APPROVED", null, context, null, cancellationToken);

                return;
            }

            var state = await _repository.RegisterSuccessfulAttemptAsync(
                idT24, flow, utcNow, cancellationToken);

            _logger.LogInformation(
                "Biometric attempt counter reset after success | IdT24: {IdT24} | Flow: {Flow}",
                idT24, flow.ToApiString());

            await PublishAsync(
                BiometricAuditEventType.AttemptSuccess,
                idT24, flow, utcNow, "APPROVED", state, context, null, cancellationToken);
        }

        public async Task RegisterRejectionAsync(
            string idT24,
            BiometricFlow flow,
            BiometricAttemptContext context,
            CancellationToken cancellationToken)
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

            if (!_settings.BIOMETRIC_ATTEMPT_CONTROL_ENABLED)
            {
                await PublishAsync(
                    BiometricAuditEventType.AttemptFailed,
                    idT24, flow, utcNow, "REJECTED", null, context, null, cancellationToken);

                return;
            }

            var state = await _repository.RegisterFailedAttemptAsync(
                idT24,
                flow,
                _settings.BIOMETRIC_MAX_FAILED_ATTEMPTS,
                _settings.BIOMETRIC_BLOCK_DURATION_MINUTES,
                utcNow,
                cancellationToken);

            _logger.LogWarning(
                "Biometric attempt rejected | IdT24: {IdT24} | Flow: {Flow} | " +
                "FailedAttempts: {FailedAttempts} | Blocked: {Blocked}",
                idT24, flow.ToApiString(), state.FailedAttempts, state.IsBlocked);

            await PublishAsync(
                BiometricAuditEventType.AttemptFailed,
                idT24, flow, utcNow, "REJECTED", state, context, null, cancellationToken);

            // El bloqueo es un evento distinto del fallo que lo provoca: soporte
            // necesita poder responder "¿cuándo se bloqueó?" sin recomponer la
            // secuencia de fallos.
            if (state.IsCurrentlyBlocked(utcNow))
            {
                _logger.LogWarning(
                    "Customer blocked for biometric validation | IdT24: {IdT24} | " +
                    "FailedAttempts: {FailedAttempts} | BlockedUntil: {BlockedUntil}",
                    idT24, state.FailedAttempts, state.BlockedUntil);

                await PublishAsync(
                    BiometricAuditEventType.BlockedByExcess,
                    idT24, flow, utcNow, "BLOCKED", state, context, null, cancellationToken);
            }
        }

        public async Task RegisterTechnicalErrorAsync(
            string idT24,
            BiometricFlow flow,
            string errorDetail,
            BiometricAttemptContext context,
            CancellationToken cancellationToken)
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

            // Deliberadamente NO se llama a RegisterFailedAttemptAsync.
            // Un error técnico no es un intento fallido del cliente.
            if (_settings.BIOMETRIC_ATTEMPT_CONTROL_ENABLED)
            {
                await _repository.TouchAttemptAsync(idT24, flow, utcNow, cancellationToken);
            }

            var state = await _repository.GetByIdT24Async(idT24, cancellationToken);

            _logger.LogError(
                "Biometric technical error, counter not incremented | IdT24: {IdT24} | " +
                "Flow: {Flow} | Detail: {Detail}",
                idT24, flow.ToApiString(), errorDetail);

            await PublishAsync(
                BiometricAuditEventType.TechnicalError,
                idT24, flow, utcNow, "ERROR", state, context, errorDetail, cancellationToken);
        }

        private Task PublishAsync(
            BiometricAuditEventType type,
            string idT24,
            BiometricFlow flow,
            DateTime utcNow,
            string result,
            FacePhiAttemptControlDocument? state,
            BiometricAttemptContext context,
            string? errorDetail,
            CancellationToken cancellationToken)
        {
            var auditEvent = BiometricAuditEvent.For(type, idT24, flow, utcNow, result);

            auditEvent.FailedAttempts = state?.FailedAttempts ?? 0;
            auditEvent.Blocked = state?.IsCurrentlyBlocked(utcNow) ?? false;
            auditEvent.BlockedUntil = state?.BlockedUntil;
            auditEvent.RequestId = context.RequestId;
            auditEvent.OperationId = context.OperationId;
            auditEvent.ServiceTransactionId = context.ServiceTransactionId;
            auditEvent.ErrorDetail = errorDetail;

            return _auditPublisher.PublishAsync(auditEvent, cancellationToken);
        }
    }
}
