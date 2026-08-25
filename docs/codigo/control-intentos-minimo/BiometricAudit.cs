// Ruta destino:
// src/micro-person-api/Infrastructure/Biometric/BiometricAudit.cs
//
// ── ESTE ES EL PUNTO DE EXTENSIÓN PARA LA AUDITORÍA ──────────────────────────
//
// Todavía no se sabe cuál es la base de datos ni el contrato de audit-log en
// micro-user-audit. Este archivo deja el desarrollo COMPLETO y funcionando sin
// esa información:
//
//   · BiometricAuditEvent   → el payload, ya definido y cerrado.
//   · IBiometricAuditPublisher → el contrato, ya definido y cerrado.
//   · LoggingBiometricAuditPublisher → implementación provisional que escribe
//     el evento en el log del micro como JSON.
//
// Cuando llegue el contrato real, se escribe UNA clase nueva
// (AuditLogBiometricAuditPublisher) que implemente la misma interfaz y se
// cambia UNA línea en Program.cs. Nada más del desarrollo se toca: ni el
// servicio, ni los handlers, ni el repositorio, ni las pruebas.
//
// Comandos para localizar la integración existente cuando toque:
//   grep -rn "IAuditLog\|AuditService\|AuditClient\|audit-log" --include=*.cs src/
//   grep -rni "audit" src/micro-person-api/appsettings*.json
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace onboarding_micro_person.Infrastructure.Biometric
{
    /// <summary>Catálogo cerrado de eventos auditables del control de intentos.</summary>
    public static class BiometricAuditEventType
    {
        public const string AttemptSuccess = "BIOMETRIC_ATTEMPT_SUCCESS";
        public const string AttemptFailed = "BIOMETRIC_ATTEMPT_FAILED";
        public const string BlockedByExcess = "BIOMETRIC_BLOCKED_BY_EXCESS";
        public const string AttemptWhileBlocked = "BIOMETRIC_ATTEMPT_WHILE_BLOCKED";
        public const string AutomaticUnblock = "BIOMETRIC_AUTOMATIC_UNBLOCK";
        public const string TechnicalError = "BIOMETRIC_TECHNICAL_ERROR";
    }

    /// <summary>
    /// Lo que se envía a la tabla de auditoría.
    ///
    /// Regla dura: aquí NO entra ningún dato biométrico. Nada de token1, token2,
    /// bestImageToken ni extraData. Se audita el hecho, no la evidencia.
    /// </summary>
    public class BiometricAuditEvent
    {
        /// <summary>Uno de los valores de BiometricAuditEventType.</summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>Cliente.</summary>
        public string IdT24 { get; set; } = string.Empty;

        /// <summary>Fecha del evento, UTC.</summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>"validate-biometric" o "validate-face".</summary>
        public string Flow { get; set; } = string.Empty;

        /// <summary>APPROVED | REJECTED | BLOCKED | UNBLOCKED | ERROR.</summary>
        public string Result { get; set; } = string.Empty;

        /// <summary>Contador de fallos consecutivos después de aplicar este evento.</summary>
        public int FailedAttempts { get; set; }

        /// <summary>Estado de bloqueo después de aplicar este evento.</summary>
        public bool Blocked { get; set; }

        public DateTime? BlockedUntil { get; set; }

        /// <summary>Trazabilidad: cabecera requestId de la petición.</summary>
        public string? RequestId { get; set; }

        /// <summary>Trazabilidad: serviceTransactionId devuelto por FacePhi.</summary>
        public string? ServiceTransactionId { get; set; }

        /// <summary>Sólo en TechnicalError: tipo de excepción o código de servicio. Sin stack trace.</summary>
        public string? ErrorDetail { get; set; }
    }

    public interface IBiometricAuditPublisher
    {
        Task PublishAsync(BiometricAuditEvent auditEvent, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Implementación provisional: deja el evento en el log del micro, en JSON,
    /// con un prefijo fijo para poder filtrarlo.
    ///
    /// Sirve para que el desarrollo esté completo y verificable antes de conocer
    /// el destino real, y para no perder los eventos de los ambientes de prueba
    /// mientras tanto.
    /// </summary>
    public class LoggingBiometricAuditPublisher(
        ILogger<LoggingBiometricAuditPublisher> logger) : IBiometricAuditPublisher
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ILogger<LoggingBiometricAuditPublisher> _logger = logger;

        public Task PublishAsync(BiometricAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "BIOMETRIC_AUDIT {AuditEvent}",
                JsonSerializer.Serialize(auditEvent, SerializerOptions));

            return Task.CompletedTask;
        }
    }

    // ── PLANTILLA PARA LA IMPLEMENTACIÓN REAL ────────────────────────────────
    // Descomentar y ajustar cuando se conozca el contrato de audit-log.
    // Al registrarla en Program.cs en lugar de LoggingBiometricAuditPublisher,
    // el resto del desarrollo sigue igual.
    //
    // public class AuditLogBiometricAuditPublisher(
    //     IAuditLogService auditLog,
    //     ILogger<AuditLogBiometricAuditPublisher> logger) : IBiometricAuditPublisher
    // {
    //     public async Task PublishAsync(BiometricAuditEvent auditEvent, CancellationToken cancellationToken)
    //     {
    //         try
    //         {
    //             await auditLog.RegisterAsync(new
    //             {
    //                 eventType = auditEvent.EventType,
    //                 idT24 = auditEvent.IdT24,
    //                 occurredAt = auditEvent.OccurredAt,
    //                 flow = auditEvent.Flow,
    //                 result = auditEvent.Result,
    //                 failedAttempts = auditEvent.FailedAttempts,
    //                 blocked = auditEvent.Blocked,
    //                 blockedUntil = auditEvent.BlockedUntil,
    //                 requestId = auditEvent.RequestId,
    //                 serviceTransactionId = auditEvent.ServiceTransactionId,
    //                 errorDetail = auditEvent.ErrorDetail
    //             }, cancellationToken);
    //         }
    //         catch (Exception ex)
    //         {
    //             // La auditoría no puede tumbar la validación biométrica de un
    //             // cliente. La fuente de verdad del contador es Mongo, no
    //             // audit-log, así que el control sigue siendo correcto aunque se
    //             // pierda un evento. El error queda en logs para poder alertarlo.
    //             logger.LogError(
    //                 ex,
    //                 "Could not publish biometric audit event {EventType} | IdT24: {IdT24}",
    //                 auditEvent.EventType, auditEvent.IdT24);
    //         }
    //     }
    // }
}
