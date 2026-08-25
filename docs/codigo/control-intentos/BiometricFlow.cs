// Ruta destino:
// src/micro-person-api/Common/Enums/Biometric/BiometricFlow.cs
//
// Sigue la convención de FacePhiTrackingReason: enum + extensión ToApiString(),
// para que el valor que viaja a Mongo y a audit-log sea una cadena estable y no
// el número del enum (renumerar el enum no debe cambiar el histórico).

namespace onboarding_micro_person.Common.Enums.Biometric
{
    public enum BiometricFlow
    {
        /// <summary>POST /api/v1/facephi/validate — documento capturado + selfie.</summary>
        ValidateBiometric = 1,

        /// <summary>POST /api/v1/facephi/validate-face — selfie contra el documento ya almacenado.</summary>
        ValidateFace = 2
    }

    public static class BiometricFlowExtensions
    {
        public static string ToApiString(this BiometricFlow flow) => flow switch
        {
            BiometricFlow.ValidateBiometric => "validate-biometric",
            BiometricFlow.ValidateFace => "validate-face",
            _ => "unknown"
        };
    }
}
