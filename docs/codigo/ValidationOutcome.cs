// Ruta destino:
// src/micro-person-api/Common/Enums/FacePhi/ValidationOutcome.cs
//
// Misma carpeta donde ya vive FacephiLivenessResult.

namespace onboarding_micro_person.Common.Enums.FacePhi
{
    /// <summary>
    /// Veredicto de la validación biométrica de Identity Validation V2.
    /// </summary>
    public enum ValidationOutcome
    {
        /// <summary>
        /// Rostro POSITIVE (3) y prueba de vida Live (3).
        /// Continuar con la activación del Soft Token.
        /// </summary>
        Approved,

        /// <summary>
        /// El rostro no coincide (NEGATIVE) o no se detectó vida (NoLive).
        /// Cortar el flujo: no pedir recaptura.
        /// </summary>
        Rejected,

        /// <summary>
        /// La captura no permitió evaluar: mala calidad, rostro ocluido,
        /// ojos cerrados. Pedir al cliente que repita la captura.
        /// </summary>
        Retry,

        /// <summary>
        /// Falló Facephi o nuestra integración.
        /// </summary>
        Error
    }
}
