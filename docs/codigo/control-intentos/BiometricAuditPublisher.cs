// Ruta destino:
// src/micro-person-api/Infrastructure/Biometric/BiometricAuditPublisher.cs
//
// La interfaz va en el mismo archivo; muévela a Common/Interfaces/Biometric/
// si el micro separa contratos.
//
// ── ANTES DE USAR ESTE ARCHIVO ────────────────────────────────────────────────
// El micro YA tiene integración con audit-log (micro-user-audit). Localízala:
//
//   grep -rn "audit" --include=*.cs src/ -il
//   grep -rn "IAuditLog\|AuditService\|AuditClient\|audit-log" --include=*.cs src/
//   grep -rni "audit" src/micro-person-api/appsettings*.json
//
// Sustituye IAuditLogService / RegisterAsync por el cliente y la firma reales.
// Lo único que NO debe cambiar es el envoltorio: publicación no bloqueante y
// errores tragados, por el motivo que se explica abajo.
// ──────────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Logging;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;

namespace onboarding_micro_person.Infrastructure.Biometric
{
    public interface IBiometricAuditPublisher
    {
        Task PublishAsync(BiometricAuditEvent auditEvent, CancellationToken cancellationToken);
    }

    public class BiometricAuditPublisher(
        IAuditLogService auditLog,
        ILogger<BiometricAuditPublisher> logger) : IBiometricAuditPublisher
    {
        private readonly IAuditLogService _auditLog = auditLog;
        private readonly ILogger<BiometricAuditPublisher> _logger = logger;

        public async Task PublishAsync(
            BiometricAuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            try
            {
                // Adapta este payload al contrato real de audit-log.
                await _auditLog.RegisterAsync(new
                {
                    eventType = auditEvent.Type.ToApiString(),
                    idT24 = auditEvent.IdT24,
                    occurredAt = auditEvent.OccurredAt,
                    flow = auditEvent.Flow,
                    result = auditEvent.Result,
                    failedAttempts = auditEvent.FailedAttempts,
                    blocked = auditEvent.Blocked,
                    blockedUntil = auditEvent.BlockedUntil,
                    requestId = auditEvent.RequestId,
                    operationId = auditEvent.OperationId,
                    serviceTransactionId = auditEvent.ServiceTransactionId,
                    errorDetail = auditEvent.ErrorDetail
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                // Decisión consciente: la auditoría no puede tumbar la validación
                // biométrica de un cliente. Si audit-log está caído, el cliente
                // sigue pudiendo activar su token y el contador sigue siendo
                // correcto, porque la fuente de verdad del contador es Mongo, no
                // la auditoría. Queda el error en los logs del micro para que la
                // pérdida de eventos sea detectable y alertable.
                _logger.LogError(
                    ex,
                    "Could not publish biometric audit event {EventType} | IdT24: {IdT24}",
                    auditEvent.Type.ToApiString(), auditEvent.IdT24);
            }
        }
    }
}
