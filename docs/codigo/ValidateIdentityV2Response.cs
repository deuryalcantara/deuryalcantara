// Ruta destino:
// src/micro-person-api/Common/Models/Biometric/ValidateIdentityV2Response.cs
//
// Junto a PassiveLivenessResults.cs.

using System.Text.Json.Serialization;
using onboarding_micro_person.Common.Enums.FacePhi;

namespace onboarding_micro_person.Common.Models.Biometric
{
    /// <summary>
    /// Respuesta de POST /facephi/validate. Expone el veredicto ya interpretado
    /// y los códigos crudos de Facephi para trazabilidad y soporte.
    /// </summary>
    public class ValidateIdentityV2Response
    {
        /// <summary>Approved | Rejected | Retry | Error</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ValidationOutcome Outcome { get; set; }

        /// <summary>Atajo para el consumidor: true sólo cuando Outcome es Approved.</summary>
        public bool Approved => Outcome == ValidationOutcome.Approved;

        /// <summary>
        /// Código de coincidencia facial de Facephi.
        /// 0 NONE, 1 NEGATIVE, 3 POSITIVE, 4 POSE EXCEED, 5 INVALID EXTRACTIONS.
        /// </summary>
        public int FacialAuthenticationResult { get; set; }

        /// <summary>
        /// Código de prueba de vida de Facephi.
        /// 3 Live, 17 NoLive, el resto indica que no se pudo evaluar.
        /// </summary>
        public int PassiveLivenessResult { get; set; }

        /// <summary>Similitud facial. 1.0 = 100 %.</summary>
        public double FacialAuthenticationSimilarity { get; set; }

        /// <summary>Identificador de la transacción en Facephi. Para soporte.</summary>
        public string ServiceTransactionId { get; set; } = string.Empty;
    }
}
