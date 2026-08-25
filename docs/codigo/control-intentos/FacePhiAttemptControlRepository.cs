// Ruta destino:
// src/micro-person-api/Persistence/FacePhiAttemptControlRepository.cs
//
// Ajusta el using de la conexión Mongo al que ya usa FacePhiBiometricRepository
// (ahí se resuelve el IMongoDatabase). Este repositorio NO usa ReplaceOneAsync:
// un contador de seguridad se incrementa con $inc del lado del servidor, nunca
// leyendo-modificando-escribiendo desde la aplicación. Con read-modify-write dos
// peticiones concurrentes leen 4, escriben 5 las dos, y el cliente consigue un
// intento gratis en cada carrera.

using MongoDB.Driver;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Interfaces.Biometric;

namespace onboarding_micro_person.Persistence
{
    public class FacePhiAttemptControlRepository : IFacePhiAttemptControlRepository
    {
        public const string CollectionName = "facephi_attempt_control";

        private readonly IMongoCollection<FacePhiAttemptControlDocument> _collection;

        private static readonly FilterDefinitionBuilder<FacePhiAttemptControlDocument> Filter =
            Builders<FacePhiAttemptControlDocument>.Filter;

        private static readonly UpdateDefinitionBuilder<FacePhiAttemptControlDocument> Update =
            Builders<FacePhiAttemptControlDocument>.Update;

        public FacePhiAttemptControlRepository(IMongoDatabase database)
        {
            _collection = database.GetCollection<FacePhiAttemptControlDocument>(CollectionName);
        }

        public async Task<FacePhiAttemptControlDocument?> GetByIdT24Async(
            string idT24,
            CancellationToken cancellationToken)
        {
            return await _collection
                .Find(d => d.IdT24 == idT24)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<FacePhiAttemptControlDocument> RegisterFailedAttemptAsync(
            string idT24,
            BiometricFlow flow,
            int maxFailedAttempts,
            int blockDurationMinutes,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            // Paso 1: incremento atómico. El upsert crea el documento la primera vez;
            // el idT24 lo siembra Mongo a partir de la igualdad del filtro.
            var increment = Update
                .Inc(d => d.FailedAttempts, 1)
                .Set(d => d.LastFlow, flow.ToApiString())
                .Set(d => d.LastAttemptAt, utcNow)
                .Set(d => d.UpdatedAt, utcNow)
                .SetOnInsert(d => d.IsBlocked, false)
                .SetOnInsert(d => d.CreatedAt, utcNow);

            var state = await _collection.FindOneAndUpdateAsync(
                Filter.Eq(d => d.IdT24, idT24),
                increment,
                new FindOneAndUpdateOptions<FacePhiAttemptControlDocument>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);

            if (state.FailedAttempts < maxFailedAttempts || state.IsBlocked)
            {
                return state;
            }

            // Paso 2: bloqueo condicional. El filtro exige isBlocked == false, así que
            // si dos peticiones cruzan el umbral a la vez sólo una escribe la ventana
            // y la otra recibe null (no reinicia el reloj del bloqueo).
            var blockUpdate = Update
                .Set(d => d.IsBlocked, true)
                .Set(d => d.BlockedAt, utcNow)
                .Set(d => d.BlockedUntil, utcNow.AddMinutes(blockDurationMinutes))
                .Set(d => d.UpdatedAt, utcNow);

            var blocked = await _collection.FindOneAndUpdateAsync(
                Filter.And(
                    Filter.Eq(d => d.IdT24, idT24),
                    Filter.Eq(d => d.IsBlocked, false)),
                blockUpdate,
                new FindOneAndUpdateOptions<FacePhiAttemptControlDocument>
                {
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);

            return blocked ?? state;
        }

        public async Task<FacePhiAttemptControlDocument> RegisterSuccessfulAttemptAsync(
            string idT24,
            BiometricFlow flow,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            var update = Update
                .Set(d => d.FailedAttempts, 0)
                .Set(d => d.IsBlocked, false)
                .Set(d => d.BlockedAt, (DateTime?)null)
                .Set(d => d.BlockedUntil, (DateTime?)null)
                .Set(d => d.LastFlow, flow.ToApiString())
                .Set(d => d.LastAttemptAt, utcNow)
                .Set(d => d.UpdatedAt, utcNow)
                .SetOnInsert(d => d.CreatedAt, utcNow);

            return await _collection.FindOneAndUpdateAsync(
                Filter.Eq(d => d.IdT24, idT24),
                update,
                new FindOneAndUpdateOptions<FacePhiAttemptControlDocument>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);
        }

        public async Task<FacePhiAttemptControlDocument?> ReleaseExpiredBlockAsync(
            string idT24,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            var update = Update
                .Set(d => d.IsBlocked, false)
                .Set(d => d.FailedAttempts, 0)
                .Set(d => d.BlockedAt, (DateTime?)null)
                .Set(d => d.BlockedUntil, (DateTime?)null)
                .Set(d => d.UpdatedAt, utcNow);

            return await _collection.FindOneAndUpdateAsync(
                Filter.And(
                    Filter.Eq(d => d.IdT24, idT24),
                    Filter.Eq(d => d.IsBlocked, true),
                    Filter.Lte(d => d.BlockedUntil, utcNow)),
                update,
                new FindOneAndUpdateOptions<FacePhiAttemptControlDocument>
                {
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);
        }

        public async Task TouchAttemptAsync(
            string idT24,
            BiometricFlow flow,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            var update = Update
                .Set(d => d.LastFlow, flow.ToApiString())
                .Set(d => d.LastAttemptAt, utcNow)
                .Set(d => d.UpdatedAt, utcNow)
                .SetOnInsert(d => d.FailedAttempts, 0)
                .SetOnInsert(d => d.IsBlocked, false)
                .SetOnInsert(d => d.CreatedAt, utcNow);

            await _collection.UpdateOneAsync(
                Filter.Eq(d => d.IdT24, idT24),
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
    }
}
