// Ruta destino:
// src/micro-person-api/Common/Exceptions/BiometricAttemptsBlockedException.cs
//
// Se traduce a HTTP 423 Locked en el IExceptionHandler del micro.
// No hereda de ninguna excepción de validación ni de negocio existente a
// propósito: el pipeline de control de intentos la deja pasar sin tocar el
// contador, y confundirla con otra familia rompería esa regla.

namespace onboarding_micro_person.Common.Exceptions
{
    public class BiometricAttemptsBlockedException : Exception
    {
        public const string ErrorCode = "BIOMETRIC_ATTEMPTS_BLOCKED";

        public BiometricAttemptsBlockedException(
            string idT24,
            DateTime blockedUntil,
            int retryAfterSeconds)
            : base("Biometric validation is temporarily blocked for this customer.")
        {
            IdT24 = idT24;
            BlockedUntil = blockedUntil;
            RetryAfterSeconds = retryAfterSeconds;
        }

        public string IdT24 { get; }

        /// <summary>Instante UTC en el que el cliente puede volver a intentar.</summary>
        public DateTime BlockedUntil { get; }

        /// <summary>Segundos restantes. Viaja en la cabecera Retry-After y en el cuerpo.</summary>
        public int RetryAfterSeconds { get; }
    }
}
