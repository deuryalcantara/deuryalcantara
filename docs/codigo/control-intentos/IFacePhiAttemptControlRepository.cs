// Ruta destino:
// src/micro-person-api/Common/Interfaces/Biometric/IFacePhiAttemptControlRepository.cs

using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;

namespace onboarding_micro_person.Common.Interfaces.Biometric
{
    public interface IFacePhiAttemptControlRepository
    {
        /// <summary>Lee el estado de control del cliente. Null si nunca ha tenido intentos.</summary>
        Task<FacePhiAttemptControlDocument?> GetByIdT24Async(
            string idT24,
            CancellationToken cancellationToken);

        /// <summary>
        /// Incrementa el contador de fallos de forma atómica y, si se alcanza el umbral,
        /// aplica el bloqueo. Devuelve el estado resultante.
        /// </summary>
        Task<FacePhiAttemptControlDocument> RegisterFailedAttemptAsync(
            string idT24,
            BiometricFlow flow,
            int maxFailedAttempts,
            int blockDurationMinutes,
            DateTime utcNow,
            CancellationToken cancellationToken);

        /// <summary>Reinicia el contador y limpia el bloqueo tras un intento exitoso.</summary>
        Task<FacePhiAttemptControlDocument> RegisterSuccessfulAttemptAsync(
            string idT24,
            BiometricFlow flow,
            DateTime utcNow,
            CancellationToken cancellationToken);

        /// <summary>
        /// Libera un bloqueo cuya ventana ya expiró y reinicia el contador.
        /// Condicional: sólo actúa si el documento sigue bloqueado y BlockedUntil ya pasó,
        /// para que dos peticiones simultáneas no produzcan dos desbloqueos.
        /// Devuelve null si otra petición se adelantó.
        /// </summary>
        Task<FacePhiAttemptControlDocument?> ReleaseExpiredBlockAsync(
            string idT24,
            DateTime utcNow,
            CancellationToken cancellationToken);

        /// <summary>
        /// Deja constancia de un intento que no altera el contador (error técnico).
        /// Sólo actualiza lastFlow / lastAttemptAt / updatedAt.
        /// </summary>
        Task TouchAttemptAsync(
            string idT24,
            BiometricFlow flow,
            DateTime utcNow,
            CancellationToken cancellationToken);
    }
}
