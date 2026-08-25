// Ruta destino:
// src/micro-person-api/Common/Interfaces/Biometric/IFacePhiAttemptControlRepository.cs

using onboarding_micro_person.Common.Entities;

namespace onboarding_micro_person.Common.Interfaces.Biometric
{
    public interface IFacePhiAttemptControlRepository
    {
        Task<FacePhiAttemptControlDocument?> GetByIdT24Async(
            string idT24,
            CancellationToken cancellationToken);

        /// <summary>Incrementa el contador de forma atómica y bloquea si alcanza el umbral.</summary>
        Task<FacePhiAttemptControlDocument> RegisterFailedAttemptAsync(
            string idT24,
            string flow,
            int maxFailedAttempts,
            int blockDurationMinutes,
            CancellationToken cancellationToken);

        /// <summary>Reinicia el contador y limpia el bloqueo tras un intento exitoso.</summary>
        Task<FacePhiAttemptControlDocument> RegisterSuccessfulAttemptAsync(
            string idT24,
            string flow,
            CancellationToken cancellationToken);

        /// <summary>
        /// Libera un bloqueo cuya ventana ya expiró y reinicia el contador.
        /// Condicional: devuelve null si otra petición simultánea se adelantó.
        /// </summary>
        Task<FacePhiAttemptControlDocument?> ReleaseExpiredBlockAsync(
            string idT24,
            CancellationToken cancellationToken);
    }
}
