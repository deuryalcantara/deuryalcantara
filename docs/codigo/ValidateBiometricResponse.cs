// Ruta destino:
// src/micro-person-api/Common/Models/Biometric/ValidateBiometricResponse.cs
//
// Ajusta el namespace al que uses para los modelos de respuesta.

namespace onboarding_micro_person.Common.Models.Biometric
{
    /// <summary>
    /// Respuesta pública de los endpoints de validación biométrica.
    ///
    /// El consumidor sólo necesita saber si la validación pasó. La
    /// interpretación de los códigos de Facephi —comparación facial, prueba de
    /// vida y umbral de similitud— vive completa en este microservicio.
    /// </summary>
    public class ValidateBiometricResponse
    {
        public bool IsValid { get; set; }
    }
}
