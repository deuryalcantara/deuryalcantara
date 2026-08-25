// Ruta destino:
// src/micro-person-api/Common/Exceptions/BiometricAttemptsBlockedException.cs
//
// Se traduce a HTTP 423 Locked. No hereda de ninguna excepción existente a
// propósito: confundirla con otra familia haría que el manejador genérico la
// convirtiera en 500.

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

        public DateTime BlockedUntil { get; }

        public int RetryAfterSeconds { get; }
    }
}
