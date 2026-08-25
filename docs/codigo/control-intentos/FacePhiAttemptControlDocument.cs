// Ruta destino:
// src/micro-person-api/Common/Entities/FacePhiAttemptControlDocument.cs
//
// Colección Mongo: "facephi_attempt_control".
// Es una colección NUEVA y SEPARADA de "facephi_biometrics": aquélla guarda
// datos biométricos (token1/token2), ésta guarda únicamente estado de control.
// Mezclarlas obligaría a escribir en el documento biométrico en cada intento
// fallido, que es justo el escenario en el que no se quiere tocar.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace onboarding_micro_person.Common.Entities
{
    public class FacePhiAttemptControlDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        /// <summary>Identificador del cliente en T24. Clave de negocio, índice único.</summary>
        [BsonElement("idT24")]
        public string IdT24 { get; set; } = string.Empty;

        /// <summary>Intentos biométricos fallidos consecutivos. Se reinicia en 0 tras un éxito o tras expirar el bloqueo.</summary>
        [BsonElement("failedAttempts")]
        public int FailedAttempts { get; set; }

        /// <summary>Estado de bloqueo vigente al momento de la última escritura.</summary>
        [BsonElement("isBlocked")]
        public bool IsBlocked { get; set; }

        /// <summary>Instante en que se aplicó el bloqueo (UTC). Null si nunca se bloqueó o si ya se liberó.</summary>
        [BsonElement("blockedAt")]
        public DateTime? BlockedAt { get; set; }

        /// <summary>Instante en que el bloqueo deja de tener efecto (UTC).</summary>
        [BsonElement("blockedUntil")]
        public DateTime? BlockedUntil { get; set; }

        /// <summary>Último flujo ejecutado: "validate-biometric" o "validate-face".</summary>
        [BsonElement("lastFlow")]
        public string? LastFlow { get; set; }

        /// <summary>Fecha del último intento registrado (UTC).</summary>
        [BsonElement("lastAttemptAt")]
        public DateTime? LastAttemptAt { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// El bloqueo está vigente sólo si la bandera está activa Y la ventana no ha expirado.
        /// La bandera por sí sola no basta: nadie la apaga hasta el siguiente intento.
        /// </summary>
        public bool IsCurrentlyBlocked(DateTime utcNow) =>
            IsBlocked && BlockedUntil is not null && BlockedUntil > utcNow;

        /// <summary>Segundos que faltan para poder reintentar. Nunca negativo.</summary>
        public int RemainingSeconds(DateTime utcNow)
        {
            if (BlockedUntil is null)
            {
                return 0;
            }

            var remaining = (BlockedUntil.Value - utcNow).TotalSeconds;

            return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
        }
    }
}
