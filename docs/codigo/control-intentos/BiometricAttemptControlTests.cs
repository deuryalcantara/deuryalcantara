// Ruta destino:
// tests/micro-person-api.Tests/Application/Biometric/BiometricAttemptControlTests.cs
//
// Estrategia: la política (umbral, bloqueo, reinicio, expiración) se prueba
// contra un repositorio en memoria que reproduce la semántica de Mongo, no
// contra un mock. Con un mock, "el quinto fallo bloquea" sólo probaría que el
// servicio llama al método correcto; con el repositorio en memoria se prueba de
// verdad la secuencia completa de intentos.
//
// Lo que este archivo NO cubre y necesita prueba de integración con Mongo real
// (Testcontainers o Mongo2Go): que $inc y el filtro condicional se comporten de
// forma atómica bajo concurrencia, y que el índice único sobre idT24 impida
// documentos duplicados. Ver la sección de pruebas del documento.

using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using onboarding_micro_person.Application.Common.Behaviours;
using onboarding_micro_person.Application.Facephi.Commands;
using onboarding_micro_person.Common.Configuration;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Exceptions;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;
using onboarding_micro_person.Infrastructure.Biometric;

namespace onboarding_micro_person.Tests.Application.Biometric
{
    // ─────────────────────────────────────────────────────────────────────────
    // Dobles de prueba
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Reloj controlable. TimeProvider es de la BCL en .NET 8, no requiere paquete.</summary>
    public class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    /// <summary>Reproduce la semántica de FacePhiAttemptControlRepository sin Mongo.</summary>
    public class InMemoryAttemptControlRepository : IFacePhiAttemptControlRepository
    {
        private readonly Dictionary<string, FacePhiAttemptControlDocument> _store = new();

        public Task<FacePhiAttemptControlDocument?> GetByIdT24Async(
            string idT24, CancellationToken cancellationToken)
        {
            _store.TryGetValue(idT24, out var document);

            return Task.FromResult(Clone(document));
        }

        public Task<FacePhiAttemptControlDocument> RegisterFailedAttemptAsync(
            string idT24, BiometricFlow flow, int maxFailedAttempts,
            int blockDurationMinutes, DateTime utcNow, CancellationToken cancellationToken)
        {
            var document = GetOrCreate(idT24, utcNow);

            document.FailedAttempts += 1;
            document.LastFlow = flow.ToApiString();
            document.LastAttemptAt = utcNow;
            document.UpdatedAt = utcNow;

            if (document.FailedAttempts >= maxFailedAttempts && !document.IsBlocked)
            {
                document.IsBlocked = true;
                document.BlockedAt = utcNow;
                document.BlockedUntil = utcNow.AddMinutes(blockDurationMinutes);
            }

            return Task.FromResult(Clone(document)!);
        }

        public Task<FacePhiAttemptControlDocument> RegisterSuccessfulAttemptAsync(
            string idT24, BiometricFlow flow, DateTime utcNow, CancellationToken cancellationToken)
        {
            var document = GetOrCreate(idT24, utcNow);

            document.FailedAttempts = 0;
            document.IsBlocked = false;
            document.BlockedAt = null;
            document.BlockedUntil = null;
            document.LastFlow = flow.ToApiString();
            document.LastAttemptAt = utcNow;
            document.UpdatedAt = utcNow;

            return Task.FromResult(Clone(document)!);
        }

        public Task<FacePhiAttemptControlDocument?> ReleaseExpiredBlockAsync(
            string idT24, DateTime utcNow, CancellationToken cancellationToken)
        {
            if (!_store.TryGetValue(idT24, out var document)
                || !document.IsBlocked
                || document.BlockedUntil > utcNow)
            {
                return Task.FromResult<FacePhiAttemptControlDocument?>(null);
            }

            document.IsBlocked = false;
            document.FailedAttempts = 0;
            document.BlockedAt = null;
            document.BlockedUntil = null;
            document.UpdatedAt = utcNow;

            return Task.FromResult(Clone(document));
        }

        public Task TouchAttemptAsync(
            string idT24, BiometricFlow flow, DateTime utcNow, CancellationToken cancellationToken)
        {
            var document = GetOrCreate(idT24, utcNow);

            document.LastFlow = flow.ToApiString();
            document.LastAttemptAt = utcNow;
            document.UpdatedAt = utcNow;

            return Task.CompletedTask;
        }

        private FacePhiAttemptControlDocument GetOrCreate(string idT24, DateTime utcNow)
        {
            if (!_store.TryGetValue(idT24, out var document))
            {
                document = new FacePhiAttemptControlDocument
                {
                    IdT24 = idT24,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                };

                _store[idT24] = document;
            }

            return document;
        }

        private static FacePhiAttemptControlDocument? Clone(FacePhiAttemptControlDocument? source)
        {
            if (source is null)
            {
                return null;
            }

            return new FacePhiAttemptControlDocument
            {
                IdT24 = source.IdT24,
                FailedAttempts = source.FailedAttempts,
                IsBlocked = source.IsBlocked,
                BlockedAt = source.BlockedAt,
                BlockedUntil = source.BlockedUntil,
                LastFlow = source.LastFlow,
                LastAttemptAt = source.LastAttemptAt,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt
            };
        }
    }

    public class RecordingAuditPublisher : IBiometricAuditPublisher
    {
        public List<BiometricAuditEvent> Events { get; } = new();

        public Task PublishAsync(BiometricAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);

            return Task.CompletedTask;
        }

        public int CountOf(BiometricAuditEventType type) => Events.Count(e => e.Type == type);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Política de intentos
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class BiometricAttemptControlServiceTests
    {
        private const string IdT24 = "123456789";
        private const int MaxAttempts = 5;
        private const int BlockMinutes = 20;

        private InMemoryAttemptControlRepository _repository = null!;
        private RecordingAuditPublisher _audit = null!;
        private TestTimeProvider _timeProvider = null!;
        private BiometricAttemptControlService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _repository = new InMemoryAttemptControlRepository();
            _audit = new RecordingAuditPublisher();
            _timeProvider = new TestTimeProvider();
            _service = BuildService(enabled: true);
        }

        private BiometricAttemptControlService BuildService(bool enabled)
        {
            var settings = new AppSettings
            {
                BIOMETRIC_ATTEMPT_CONTROL_ENABLED = enabled,
                BIOMETRIC_MAX_FAILED_ATTEMPTS = MaxAttempts,
                BIOMETRIC_BLOCK_DURATION_MINUTES = BlockMinutes
            };

            return new BiometricAttemptControlService(
                _repository,
                _audit,
                _timeProvider,
                Options.Create(settings),
                NullLogger<BiometricAttemptControlService>.Instance);
        }

        private Task RejectAsync() => _service.RegisterRejectionAsync(
            IdT24, BiometricFlow.ValidateFace, BiometricAttemptContext.Empty, CancellationToken.None);

        private Task ApproveAsync() => _service.RegisterSuccessAsync(
            IdT24, BiometricFlow.ValidateFace, BiometricAttemptContext.Empty, CancellationToken.None);

        private Task GuardAsync() => _service.EnsureNotBlockedAsync(
            IdT24, BiometricFlow.ValidateFace, CancellationToken.None);

        private async Task<FacePhiAttemptControlDocument?> StateAsync() =>
            await _repository.GetByIdT24Async(IdT24, CancellationToken.None);

        [Test]
        public async Task RegisterRejection_IncrementsCounter()
        {
            await RejectAsync();

            var state = await StateAsync();

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.FailedAttempts, Is.EqualTo(1));
            Assert.That(state.IsBlocked, Is.False);
            Assert.That(state.LastFlow, Is.EqualTo("validate-face"));
            Assert.That(state.LastAttemptAt, Is.EqualTo(_timeProvider.UtcNow.UtcDateTime));
        }

        [Test]
        public async Task FourthRejection_DoesNotBlock()
        {
            for (var i = 0; i < 4; i++)
            {
                await RejectAsync();
            }

            var state = await StateAsync();

            Assert.That(state!.FailedAttempts, Is.EqualTo(4));
            Assert.That(state.IsBlocked, Is.False);

            // Y sigue pudiendo intentar.
            Assert.DoesNotThrowAsync(GuardAsync);
        }

        [Test]
        public async Task FifthRejection_BlocksForConfiguredDuration()
        {
            for (var i = 0; i < 5; i++)
            {
                await RejectAsync();
            }

            var state = await StateAsync();

            Assert.That(state!.FailedAttempts, Is.EqualTo(5));
            Assert.That(state.IsBlocked, Is.True);
            Assert.That(
                state.BlockedUntil,
                Is.EqualTo(_timeProvider.UtcNow.UtcDateTime.AddMinutes(BlockMinutes)));

            Assert.That(_audit.CountOf(BiometricAuditEventType.BlockedByExcess), Is.EqualTo(1));
        }

        [Test]
        public async Task AttemptWhileBlocked_Throws423AndAudits()
        {
            for (var i = 0; i < 5; i++)
            {
                await RejectAsync();
            }

            var exception = Assert.ThrowsAsync<BiometricAttemptsBlockedException>(GuardAsync);

            Assert.That(exception!.IdT24, Is.EqualTo(IdT24));
            Assert.That(exception.RetryAfterSeconds, Is.EqualTo(BlockMinutes * 60));
            Assert.That(_audit.CountOf(BiometricAuditEventType.AttemptWhileBlocked), Is.EqualTo(1));
        }

        [Test]
        public async Task AttemptWhileBlocked_DoesNotIncrementCounterFurther()
        {
            // El intento bloqueado no llega a FacePhi, así que tampoco cuenta:
            // de lo contrario, martillear el endpoint alargaría el bloqueo del
            // cliente indefinidamente.
            for (var i = 0; i < 5; i++)
            {
                await RejectAsync();
            }

            Assert.ThrowsAsync<BiometricAttemptsBlockedException>(GuardAsync);
            Assert.ThrowsAsync<BiometricAttemptsBlockedException>(GuardAsync);

            var state = await StateAsync();

            Assert.That(state!.FailedAttempts, Is.EqualTo(5));
            Assert.That(
                state.BlockedUntil,
                Is.EqualTo(_timeProvider.UtcNow.UtcDateTime.AddMinutes(BlockMinutes)),
                "el reloj del bloqueo no se reinicia con cada intento rechazado");
        }

        [Test]
        public async Task SuccessfulAttempt_ResetsCounter()
        {
            await RejectAsync();
            await RejectAsync();
            await RejectAsync();

            await ApproveAsync();

            var state = await StateAsync();

            Assert.That(state!.FailedAttempts, Is.EqualTo(0));
            Assert.That(state.IsBlocked, Is.False);
            Assert.That(_audit.CountOf(BiometricAuditEventType.AttemptSuccess), Is.EqualTo(1));
        }

        [Test]
        public async Task TechnicalError_DoesNotIncrementCounter()
        {
            await RejectAsync();

            await _service.RegisterTechnicalErrorAsync(
                IdT24,
                BiometricFlow.ValidateFace,
                "FacephiIntegrationException",
                BiometricAttemptContext.Empty,
                CancellationToken.None);

            var state = await StateAsync();

            Assert.That(state!.FailedAttempts, Is.EqualTo(1), "un error técnico no es un intento fallido del cliente");
            Assert.That(_audit.CountOf(BiometricAuditEventType.TechnicalError), Is.EqualTo(1));
        }

        [Test]
        public async Task RepeatedTechnicalErrors_NeverBlockTheCustomer()
        {
            for (var i = 0; i < 20; i++)
            {
                await _service.RegisterTechnicalErrorAsync(
                    IdT24, BiometricFlow.ValidateBiometric, "HttpRequestException",
                    BiometricAttemptContext.Empty, CancellationToken.None);
            }

            var state = await StateAsync();

            Assert.That(state!.FailedAttempts, Is.EqualTo(0));
            Assert.That(state.IsBlocked, Is.False);
            Assert.DoesNotThrowAsync(GuardAsync);
        }

        [Test]
        public async Task BlockExpires_AfterConfiguredMinutes()
        {
            for (var i = 0; i < 5; i++)
            {
                await RejectAsync();
            }

            _timeProvider.Advance(TimeSpan.FromMinutes(BlockMinutes - 1));
            Assert.ThrowsAsync<BiometricAttemptsBlockedException>(GuardAsync, "a los 19 minutos sigue bloqueado");

            _timeProvider.Advance(TimeSpan.FromMinutes(2));
            Assert.DoesNotThrowAsync(GuardAsync, "a los 21 minutos ya puede intentar");

            var state = await StateAsync();

            Assert.That(state!.IsBlocked, Is.False);
            Assert.That(state.FailedAttempts, Is.EqualTo(0), "al expirar el bloqueo el contador vuelve a cero");
            Assert.That(_audit.CountOf(BiometricAuditEventType.AutomaticUnblock), Is.EqualTo(1));
        }

        [Test]
        public async Task AfterUnblock_CustomerGetsFullSetOfAttemptsAgain()
        {
            for (var i = 0; i < 5; i++)
            {
                await RejectAsync();
            }

            _timeProvider.Advance(TimeSpan.FromMinutes(BlockMinutes + 1));
            await GuardAsync();

            for (var i = 0; i < 4; i++)
            {
                await RejectAsync();
            }

            var state = await StateAsync();

            Assert.That(state!.IsBlocked, Is.False, "cuatro fallos tras el desbloqueo no vuelven a bloquear");
        }

        [Test]
        public async Task EveryEvent_IsAudited()
        {
            for (var i = 0; i < 5; i++)
            {
                await RejectAsync();
            }

            Assert.ThrowsAsync<BiometricAttemptsBlockedException>(GuardAsync);

            _timeProvider.Advance(TimeSpan.FromMinutes(BlockMinutes + 1));
            await GuardAsync();
            await ApproveAsync();

            Assert.Multiple(() =>
            {
                Assert.That(_audit.CountOf(BiometricAuditEventType.AttemptFailed), Is.EqualTo(5));
                Assert.That(_audit.CountOf(BiometricAuditEventType.BlockedByExcess), Is.EqualTo(1));
                Assert.That(_audit.CountOf(BiometricAuditEventType.AttemptWhileBlocked), Is.EqualTo(1));
                Assert.That(_audit.CountOf(BiometricAuditEventType.AutomaticUnblock), Is.EqualTo(1));
                Assert.That(_audit.CountOf(BiometricAuditEventType.AttemptSuccess), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task AuditEvents_NeverCarryBiometricData()
        {
            await RejectAsync();

            var serialized = System.Text.Json.JsonSerializer.Serialize(_audit.Events);

            Assert.Multiple(() =>
            {
                Assert.That(serialized, Does.Not.Contain("token1"));
                Assert.That(serialized, Does.Not.Contain("token2"));
                Assert.That(serialized, Does.Not.Contain("bestImageToken"));
                Assert.That(serialized, Does.Not.Contain("extraData"));
            });
        }

        [Test]
        public async Task WhenDisabled_NothingIsBlockedButEverythingIsAudited()
        {
            _service = BuildService(enabled: false);

            for (var i = 0; i < 10; i++)
            {
                await RejectAsync();
            }

            Assert.DoesNotThrowAsync(GuardAsync);
            Assert.That(await StateAsync(), Is.Null, "con el control apagado no se escribe estado");
            Assert.That(_audit.CountOf(BiometricAuditEventType.AttemptFailed), Is.EqualTo(10));
        }

        [Test]
        public async Task Counter_IsPerCustomer()
        {
            for (var i = 0; i < 5; i++)
            {
                await RejectAsync();
            }

            Assert.DoesNotThrowAsync(() => _service.EnsureNotBlockedAsync(
                "987654321", BiometricFlow.ValidateFace, CancellationToken.None));
        }

        [Test]
        public async Task FlowsShareTheSameCounter()
        {
            // Cinco fallos repartidos entre los dos endpoints bloquean igual: el
            // control es por cliente, no por endpoint. Si no, bastaría con
            // alternar validate y validate-face para duplicar los intentos.
            for (var i = 0; i < 3; i++)
            {
                await _service.RegisterRejectionAsync(
                    IdT24, BiometricFlow.ValidateBiometric, BiometricAttemptContext.Empty, CancellationToken.None);
            }

            for (var i = 0; i < 2; i++)
            {
                await _service.RegisterRejectionAsync(
                    IdT24, BiometricFlow.ValidateFace, BiometricAttemptContext.Empty, CancellationToken.None);
            }

            Assert.ThrowsAsync<BiometricAttemptsBlockedException>(GuardAsync);

            var state = await StateAsync();

            Assert.That(state!.LastFlow, Is.EqualTo("validate-face"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Behaviour: garantiza que FacePhi no se invoca estando bloqueado
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class BiometricAttemptControlBehaviourTests
    {
        private const string IdT24 = "123456789";

        private Mock<IBiometricAttemptControlService> _attemptControl = null!;
        private BiometricAttemptControlBehaviour<ValidateFaceOnlyCmd, ValidateIdentityV2Result> _behaviour = null!;

        [SetUp]
        public void SetUp()
        {
            _attemptControl = new Mock<IBiometricAttemptControlService>();

            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext()
            };

            httpContextAccessor.HttpContext!.Request.Headers["requestId"] = "req-1";

            _behaviour = new BiometricAttemptControlBehaviour<ValidateFaceOnlyCmd, ValidateIdentityV2Result>(
                _attemptControl.Object,
                httpContextAccessor,
                NullLogger<BiometricAttemptControlBehaviour<ValidateFaceOnlyCmd, ValidateIdentityV2Result>>.Instance);
        }

        private static ValidateFaceOnlyCmd Command() => new()
        {
            IdT24 = IdT24,
            BestImageToken = "best-image-token"
        };

        // Ajusta la construcción del resultado a la firma real de
        // ValidateIdentityV2Result en el micro.
        private static ValidateIdentityV2Result Result(FacialValidationStatus status) =>
            new(status, null!);

        [Test]
        public async Task WhenBlocked_HandlerIsNeverInvoked()
        {
            _attemptControl
                .Setup(x => x.EnsureNotBlockedAsync(IdT24, BiometricFlow.ValidateFace, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new BiometricAttemptsBlockedException(IdT24, DateTime.UtcNow.AddMinutes(20), 1200));

            var handlerInvoked = false;

            Assert.ThrowsAsync<BiometricAttemptsBlockedException>(() => _behaviour.Handle(
                Command(),
                () =>
                {
                    handlerInvoked = true;

                    return Task.FromResult(Result(FacialValidationStatus.Approved));
                },
                CancellationToken.None));

            Assert.That(handlerInvoked, Is.False, "estando bloqueado no puede haber llamada a FacePhi");

            _attemptControl.Verify(
                x => x.RegisterRejectionAsync(
                    It.IsAny<string>(), It.IsAny<BiometricFlow>(),
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Never);

            await Task.CompletedTask;
        }

        [Test]
        public async Task WhenApproved_RegistersSuccess()
        {
            await _behaviour.Handle(
                Command(),
                () => Task.FromResult(Result(FacialValidationStatus.Approved)),
                CancellationToken.None);

            _attemptControl.Verify(
                x => x.RegisterSuccessAsync(
                    IdT24, BiometricFlow.ValidateFace,
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task WhenRejected_RegistersRejection()
        {
            await _behaviour.Handle(
                Command(),
                () => Task.FromResult(Result(FacialValidationStatus.Rejected)),
                CancellationToken.None);

            _attemptControl.Verify(
                x => x.RegisterRejectionAsync(
                    IdT24, BiometricFlow.ValidateFace,
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // FacePhiIntegrationException es la excepción del prerrequisito descrito en
        // la sección 11 del documento. Si en el micro se llama de otra forma,
        // ajusta el nombre aquí y en el behaviour.
        [Test]
        public void WhenTechnicalException_RegistersTechnicalErrorAndRethrows()
        {
            Assert.ThrowsAsync<FacePhiIntegrationException>(() => _behaviour.Handle(
                Command(),
                () => throw new FacePhiIntegrationException("FacePhi unavailable"),
                CancellationToken.None));

            _attemptControl.Verify(
                x => x.RegisterTechnicalErrorAsync(
                    IdT24, BiometricFlow.ValidateFace, "FacePhiIntegrationException",
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Once);

            _attemptControl.Verify(
                x => x.RegisterRejectionAsync(
                    It.IsAny<string>(), It.IsAny<BiometricFlow>(),
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void WhenNotFound_DoesNotTouchTheCounter()
        {
            // El cliente sin documento almacenado no gastó un intento: FacePhi
            // nunca se invocó.
            Assert.ThrowsAsync<NotFoundException>(() => _behaviour.Handle(
                Command(),
                () => throw new NotFoundException("Customer does not have a stored document."),
                CancellationToken.None));

            _attemptControl.Verify(
                x => x.RegisterRejectionAsync(
                    It.IsAny<string>(), It.IsAny<BiometricFlow>(),
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Never);

            _attemptControl.Verify(
                x => x.RegisterTechnicalErrorAsync(
                    It.IsAny<string>(), It.IsAny<BiometricFlow>(), It.IsAny<string>(),
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void WhenValidationFails_DoesNotTouchTheCounter()
        {
            Assert.ThrowsAsync<ValidationException>(() => _behaviour.Handle(
                Command(),
                () => throw new ValidationException("idT24 is required."),
                CancellationToken.None));

            _attemptControl.Verify(
                x => x.RegisterRejectionAsync(
                    It.IsAny<string>(), It.IsAny<BiometricFlow>(),
                    It.IsAny<BiometricAttemptContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
