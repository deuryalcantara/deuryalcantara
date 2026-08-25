// Ruta destino:
// src/micro-person-api/Common/Entities/FacePhiAttemptControlDocument.cs
//
// Colección Mongo NUEVA: "facephi_attempt_control".
// Separada de "facephi_biometrics" a propósito: aquélla guarda token1/token2,
// y no queremos escribir en ese documento en cada intento fallido.
//
// Ningún campo biométrico entra aquí.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace onboarding_micro_person.Common.Entities
{
    public class FacePhiAttemptControlDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("idT24")]
        public string IdT24 { get; set; } = string.Empty;

        /// <summary>Fallos consecutivos. Vuelve a 0 tras un éxito o al expirar el bloqueo.</summary>
        [BsonElement("failedAttempts")]
        public int FailedAttempts { get; set; }

        [BsonElement("isBlocked")]
        public bool IsBlocked { get; set; }

        [BsonElement("blockedAt")]
        public DateTime? BlockedAt { get; set; }

        [BsonElement("blockedUntil")]
        public DateTime? BlockedUntil { get; set; }

        /// <summary>"validate-biometric" o "validate-face".</summary>
        [BsonElement("lastFlow")]
        public string? LastFlow { get; set; }

        [BsonElement("lastAttemptAt")]
        public DateTime? LastAttemptAt { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// El bloqueo está vigente sólo si la bandera está activa Y la ventana no ha
        /// expirado. La bandera por sí sola no basta: nadie la apaga hasta el
        /// siguiente intento.
        /// </summary>
        public bool IsCurrentlyBlocked(DateTime utcNow) =>
            IsBlocked && BlockedUntil is not null && BlockedUntil > utcNow;

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
