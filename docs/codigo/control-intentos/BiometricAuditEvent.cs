// Ruta destino:
// src/micro-person-api/Common/Models/Biometric/BiometricAuditEvent.cs
//
// Modelo del evento que se envía a audit-log (micro-user-audit).
// Regla dura: aquí NO entra ningún dato biométrico. Nada de token1, token2,
// bestImageToken ni extraData. Lo que se audita es el hecho y su trazabilidad,
// no la evidencia.

using onboarding_micro_person.Common.Enums.Biometric;

namespace onboarding_micro_person.Common.Models.Biometric
{
    /// <summary>Catálogo cerrado de eventos auditables del control de intentos.</summary>
    public enum BiometricAuditEventType
    {
        AttemptSuccess = 1,
        AttemptFailed = 2,
        BlockedByExcess = 3,
        AttemptWhileBlocked = 4,
        AutomaticUnblock = 5,
        TechnicalError = 6
    }

    public static class BiometricAuditEventTypeExtensions
    {
        public static string ToApiString(this BiometricAuditEventType type) => type switch
        {
            BiometricAuditEventType.AttemptSuccess => "BIOMETRIC_ATTEMPT_SUCCESS",
            BiometricAuditEventType.AttemptFailed => "BIOMETRIC_ATTEMPT_FAILED",
            BiometricAuditEventType.BlockedByExcess => "BIOMETRIC_BLOCKED_BY_EXCESS",
            BiometricAuditEventType.AttemptWhileBlocked => "BIOMETRIC_ATTEMPT_WHILE_BLOCKED",
            BiometricAuditEventType.AutomaticUnblock => "BIOMETRIC_AUTOMATIC_UNBLOCK",
            BiometricAuditEventType.TechnicalError => "BIOMETRIC_TECHNICAL_ERROR",
            _ => "BIOMETRIC_UNKNOWN"
        };
    }

    public class BiometricAuditEvent
    {
        public BiometricAuditEventType Type { get; set; }

        /// <summary>Cliente.</summary>
        public string IdT24 { get; set; } = string.Empty;

        /// <summary>Fecha del evento en UTC.</summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>Flujo: "validate-biometric" o "validate-face".</summary>
        public string Flow { get; set; } = string.Empty;

        /// <summary>Resultado legible: APPROVED | REJECTED | BLOCKED | ERROR | UNBLOCKED.</summary>
        public string Result { get; set; } = string.Empty;

        /// <summary>Contador de fallos consecutivos después de aplicar este evento.</summary>
        public int FailedAttempts { get; set; }

        /// <summary>Estado de bloqueo después de aplicar este evento.</summary>
        public bool Blocked { get; set; }

        public DateTime? BlockedUntil { get; set; }

        /// <summary>requestId de la petición, para cruzar con los logs del micro y del facade.</summary>
        public string? RequestId { get; set; }

        /// <summary>operationId del tracking de FacePhi, cuando la petición lo trae.</summary>
        public string? OperationId { get; set; }

        /// <summary>serviceTransactionId devuelto por FacePhi, cuando llegó a responder.</summary>
        public string? ServiceTransactionId { get; set; }

        /// <summary>Sólo para TechnicalError: tipo de la excepción o código de servicio. Sin stack trace.</summary>
        public string? ErrorDetail { get; set; }

        public static BiometricAuditEvent For(
            BiometricAuditEventType type,
            string idT24,
            BiometricFlow flow,
            DateTime utcNow,
            string result) => new()
            {
                Type = type,
                IdT24 = idT24,
                Flow = flow.ToApiString(),
                OccurredAt = utcNow,
                Result = result
            };
    }
}
