// Ruta destino:
// src/micro-person-api/Persistence/FacePhiAttemptControlIndexInitializer.cs
//
// El índice único sobre idT24 no es decorativo: sin él, dos peticiones
// concurrentes con upsert pueden insertar DOS documentos para el mismo cliente,
// y a partir de ahí cada uno lleva su propio contador. Con el índice único, la
// segunda inserción falla y Mongo reintenta el update sobre el documento que ya
// existe, que es el comportamiento que queremos.
//
// Registro en Program.cs:  builder.Services.AddHostedService<FacePhiAttemptControlIndexInitializer>();
//
// Si el micro ya tiene un inicializador de índices para facephi_biometrics,
// añade aquí el índice en lugar de crear una clase nueva (y aprovecha para
// crear también el índice único de idT24 en facephi_biometrics, que sigue
// pendiente).

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using onboarding_micro_person.Common.Entities;

namespace onboarding_micro_person.Persistence
{
    public class FacePhiAttemptControlIndexInitializer(
        IMongoDatabase database,
        ILogger<FacePhiAttemptControlIndexInitializer> logger) : IHostedService
    {
        private readonly IMongoDatabase _database = database;
        private readonly ILogger<FacePhiAttemptControlIndexInitializer> _logger = logger;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var collection = _database.GetCollection<FacePhiAttemptControlDocument>(
                    FacePhiAttemptControlRepository.CollectionName);

                var uniqueIdT24 = new CreateIndexModel<FacePhiAttemptControlDocument>(
                    Builders<FacePhiAttemptControlDocument>.IndexKeys.Ascending(d => d.IdT24),
                    new CreateIndexOptions { Unique = true, Name = "ux_idT24" });

                // Soporte y monitoreo: "¿cuántos clientes están bloqueados ahora mismo?"
                var blockedUntil = new CreateIndexModel<FacePhiAttemptControlDocument>(
                    Builders<FacePhiAttemptControlDocument>.IndexKeys
                        .Ascending(d => d.IsBlocked)
                        .Ascending(d => d.BlockedUntil),
                    new CreateIndexOptions { Name = "ix_isBlocked_blockedUntil" });

                await collection.Indexes.CreateManyAsync(
                    new[] { uniqueIdT24, blockedUntil },
                    cancellationToken);

                _logger.LogInformation(
                    "Indexes ensured for collection {Collection}",
                    FacePhiAttemptControlRepository.CollectionName);
            }
            catch (Exception ex)
            {
                // Un fallo creando índices no debe impedir el arranque del micro,
                // pero tiene que quedar visible: sin el índice único el control
                // de intentos es vulnerable a la carrera de inserción.
                _logger.LogError(
                    ex,
                    "Could not ensure indexes for collection {Collection}",
                    FacePhiAttemptControlRepository.CollectionName);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
