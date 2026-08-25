// Ruta destino:
// src/micro-person-api/Common/Interfaces/Biometric/IBiometricAttemptControlled.cs
//
// Marca los Commands que consumen un intento biométrico. Sólo con implementar
// esta interfaz, un Command queda cubierto por el control de intentos: el
// behaviour se engancha por la restricción genérica, sin tocar el handler.
//
// Lo implementan ValidateIdentityV2Cmd y ValidateFaceOnlyCmd.
// NO lo implementa GetFacePhiDocumentQry: consultar si hay documento almacenado
// no invoca a FacePhi y por tanto no es un intento.

using onboarding_micro_person.Common.Enums.Biometric;

namespace onboarding_micro_person.Common.Interfaces.Biometric
{
    public interface IBiometricAttemptControlled
    {
        string IdT24 { get; }

        /// <summary>Flujo al que pertenece el intento. Se persiste como "último flujo ejecutado".</summary>
        BiometricFlow Flow { get; }

        /// <summary>operationId del tracking de FacePhi, si la petición lo trae. Sólo para trazabilidad.</summary>
        string? TrackingOperationId { get; }
    }
}
