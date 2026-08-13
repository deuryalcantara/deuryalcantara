// Ruta destino:
// tests/UnitTests/Application/Facephi/ValidateFaceOnlyCmdTests.cs
//
// Ajusta el using de NotFoundException al namespace real del micro.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using onboarding_micro_person.Application.Facephi.Commands;
using onboarding_micro_person.Common.Configuration;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Enums.FacePhi;
using onboarding_micro_person.Common.Exceptions;
using onboarding_micro_person.Common.Extensions.FacePhi;
using onboarding_micro_person.Common.Interfaces;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;

namespace onboarding_micro_person.Tests.UnitTests.Application.Facephi
{
    [TestFixture]
    [Category("Facephi")]
    public class ValidateFaceOnlyCmdTests
    {
        private const int FacialPositive = 3;
        private const int FacialNegative = 1;
        private const int FacialNone = 0;
        private const int StoredMethod = 5;

        private Mock<IFacePhiService> _facePhiMock = default!;
        private Mock<IFacePhiBiometricRepository> _repositoryMock = default!;
        private Mock<IIdentityValidationEvaluator> _evaluatorMock = default!;
        private AppSettings _settings = default!;

        [SetUp]
        public void Setup()
        {
            _facePhiMock = new Mock<IFacePhiService>();
            _repositoryMock = new Mock<IFacePhiBiometricRepository>();
            _evaluatorMock = new Mock<IIdentityValidationEvaluator>();

            _settings = new AppSettings
            {
                FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = false
            };

            _evaluatorMock
                .Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>()))
                .Returns(true);

            _facePhiMock
                .Setup(x => x.FinishTrackingAsync(
                    It.IsAny<bool>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private ValidateFaceOnlyCmdHandler BuildHandler() => new(
            _facePhiMock.Object,
            _repositoryMock.Object,
            _evaluatorMock.Object,
            Options.Create(_settings),
            Mock.Of<ILogger<ValidateFaceOnlyCmdHandler>>());

        private static ValidateFaceOnlyCmd BuildCommand(
            FacephiTrackingExtraData? tracking = null) => new()
        {
            IdT24 = "123456789",
            BestImageToken = "best-image-token",
            Tracking = tracking
        };

        private static FacephiTrackingExtraData BuildTracking() => new()
        {
            ExtraData = "tracking-extra-data",
            OperationId = Guid.NewGuid().ToString()
        };

        private void SetupStoredDocument(string? token1 = "stored-document-token")
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FacePhiBiometricDocument
                {
                    IdT24 = "123456789",
                    DocumentType = "CED",
                    Token1 = token1!,
                    Token2 = "stored-best-image",
                    Method = StoredMethod
                });
        }

        private void SetupFacePhi(
            int serviceResultCode = 0,
            int facialResult = FacialPositive,
            FacephiLivenessResult liveness = FacephiLivenessResult.Live,
            double similarity = 0.9921)
        {
            _facePhiMock
                .Setup(x => x.EvaluatePassiveLivenessToken(
                    It.IsAny<PassiveLivenessRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PassiveLivenessResult
                {
                    ServiceTransactionId = Guid.NewGuid().ToString(),
                    ServiceResultCode = serviceResultCode,
                    facialAuthenticationResult = facialResult,
                    facialAuthenticationSimilarity = similarity,
                    passiveLivenessResult = liveness
                });
        }

        // ─────────────────────────────── Validador ───────────────────────────────

        [Test]
        public void Validator_Fails_WhenCommandIsEmpty()
        {
            var result = new ValidateFaceOnlyCmdValidator().Validate(new ValidateFaceOnlyCmd());

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_Succeeds_WithAValidCommand()
        {
            var result = new ValidateFaceOnlyCmdValidator().Validate(BuildCommand(BuildTracking()));

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Validator_RequiresIdT24()
        {
            var command = BuildCommand();
            command.IdT24 = string.Empty;

            var result = new ValidateFaceOnlyCmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_RequiresBestImageToken()
        {
            var command = BuildCommand();
            command.BestImageToken = string.Empty;

            var result = new ValidateFaceOnlyCmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_RejectsOperationIdThatIsNotAUuid()
        {
            var command = BuildCommand(new FacephiTrackingExtraData
            {
                ExtraData = "extra",
                OperationId = "no-es-uuid"
            });

            var result = new ValidateFaceOnlyCmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        // ─────────────────────── Documento almacenado ────────────────────────────

        [Test]
        public void Handler_Throws_WhenTheCustomerHasNoDocument()
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FacePhiBiometricDocument?)null);

            Assert.ThrowsAsync<NotFoundException>(
                () => BuildHandler().Handle(BuildCommand(), CancellationToken.None));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Handler_Throws_WhenTheStoredDocumentHasNoToken1(string token1)
        {
            // Que exista el registro no basta: sin token1 no hay nada contra qué comparar.
            SetupStoredDocument(token1);

            Assert.ThrowsAsync<NotFoundException>(
                () => BuildHandler().Handle(BuildCommand(), CancellationToken.None));
        }

        [Test]
        public void Handler_DoesNotCallFacephi_WhenThereIsNoDocument()
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FacePhiBiometricDocument?)null);

            Assert.ThrowsAsync<NotFoundException>(
                () => BuildHandler().Handle(BuildCommand(), CancellationToken.None));

            _facePhiMock.Verify(
                x => x.EvaluatePassiveLivenessToken(
                    It.IsAny<PassiveLivenessRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task Handler_LooksUpTheDocumentByIdT24()
        {
            SetupStoredDocument();
            SetupFacePhi();

            var command = BuildCommand();

            await BuildHandler().Handle(command, CancellationToken.None);

            _repositoryMock.Verify(
                x => x.GetByIdT24Async(command.IdT24, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ──────────────────────── Mapeo hacia Facephi ────────────────────────────

        [Test]
        public async Task Handler_UsesTheStoredToken1AndTheIncomingBestImageToken()
        {
            SetupStoredDocument();
            SetupFacePhi();

            var command = BuildCommand();

            await BuildHandler().Handle(command, CancellationToken.None);

            _facePhiMock.Verify(
                x => x.EvaluatePassiveLivenessToken(
                    It.Is<PassiveLivenessRequest>(request =>
                        request.Token1 == "stored-document-token" &&
                        request.BestImageToken == command.BestImageToken &&
                        request.Method == StoredMethod.ToString()),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task Handler_ForwardsTheTrackingData()
        {
            SetupStoredDocument();
            SetupFacePhi();

            var tracking = BuildTracking();

            await BuildHandler().Handle(BuildCommand(tracking), CancellationToken.None);

            _facePhiMock.Verify(
                x => x.EvaluatePassiveLivenessToken(
                    It.Is<PassiveLivenessRequest>(request =>
                        request.TrackingToken == tracking.ExtraData &&
                        request.OperationId == tracking.OperationId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ─────────────────────────────── Veredicto ───────────────────────────────

        [TestCase(FacialPositive, FacephiLivenessResult.Live, FacialValidationStatus.Approved)]
        [TestCase(FacialNegative, FacephiLivenessResult.Live, FacialValidationStatus.Rejected)]
        [TestCase(FacialNone, FacephiLivenessResult.Live, FacialValidationStatus.Rejected)]
        [TestCase(FacialPositive, FacephiLivenessResult.NoLive, FacialValidationStatus.Rejected)]
        [TestCase(FacialPositive, FacephiLivenessResult.None, FacialValidationStatus.Rejected)]
        [TestCase(FacialPositive, FacephiLivenessResult.NoneBecauseEyesClosed, FacialValidationStatus.Rejected)]
        public async Task Handler_TrustsFacephiVerdict_WhenCustomCheckIsDisabled(
            int facialResult, FacephiLivenessResult liveness, FacialValidationStatus expected)
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = false;
            SetupStoredDocument();
            SetupFacePhi(facialResult: facialResult, liveness: liveness);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(expected));
        }

        [Test]
        public async Task Handler_Rejects_WhenSubjectIsNotAlive_EvenIfThresholdIsMet()
        {
            // Este es el control clave de este endpoint: el cliente ya no presenta
            // el documento físico, así que la prueba de vida no es opcional.
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = true;
            _evaluatorMock.Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>())).Returns(true);

            SetupStoredDocument();
            SetupFacePhi(liveness: FacephiLivenessResult.NoLive, similarity: 0.99);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Rejected));
        }

        [Test]
        public async Task Handler_Rejects_WhenThresholdIsNotMet()
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = true;
            _evaluatorMock.Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>())).Returns(false);

            SetupStoredDocument();
            SetupFacePhi(similarity: 0.55);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Rejected));
        }

        [Test]
        public async Task Handler_DoesNotUseTheEvaluator_WhenCustomCheckIsDisabled()
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = false;
            SetupStoredDocument();
            SetupFacePhi();

            await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            _evaluatorMock.Verify(
                x => x.MeetsSimilarityThreshold(It.IsAny<double>()), Times.Never);
        }

        [Test]
        public async Task Handler_Rejects_WhenServiceResultCodeIsNotZero()
        {
            SetupStoredDocument();
            SetupFacePhi(serviceResultCode: 1);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Rejected));
        }

        // ────────────────────────────── Tracking ─────────────────────────────────

        [Test]
        public async Task Handler_ClosesTrackingAsApproved_WhenValidationSucceeds()
        {
            SetupStoredDocument();
            SetupFacePhi();

            var tracking = BuildTracking();

            await BuildHandler().Handle(BuildCommand(tracking), CancellationToken.None);

            _facePhiMock.Verify(
                x => x.FinishTrackingAsync(
                    true,
                    FacePhiTrackingReason.None.ToApiString(),
                    tracking.ExtraData,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task Handler_ClosesTrackingAsNotPassed_WhenValidationFails()
        {
            SetupStoredDocument();
            SetupFacePhi(facialResult: FacialNegative);

            var tracking = BuildTracking();

            await BuildHandler().Handle(BuildCommand(tracking), CancellationToken.None);

            _facePhiMock.Verify(
                x => x.FinishTrackingAsync(
                    false,
                    FacePhiTrackingReason.FacialAuthenticationNotPassed.ToApiString(),
                    tracking.ExtraData,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task Handler_DoesNotCloseTracking_WhenNoTrackingWasProvided()
        {
            SetupStoredDocument();
            SetupFacePhi();

            await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            _facePhiMock.Verify(
                x => x.FinishTrackingAsync(
                    It.IsAny<bool>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ───────────────────────────── Persistencia ──────────────────────────────

        [Test]
        public async Task Handler_DoesNotPersistAnything()
        {
            // Face-only reutiliza el documento existente: no lo modifica.
            // Si se decide dejar rastro de estas validaciones, este test debe cambiar.
            SetupStoredDocument();
            SetupFacePhi();

            await BuildHandler().Handle(BuildCommand(BuildTracking()), CancellationToken.None);

            _repositoryMock.Verify(
                x => x.UpsertAsync(
                    It.IsAny<FacePhiBiometricDocument>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ─────────────────────────────── Resultado ───────────────────────────────

        [Test]
        public async Task Handler_ReturnsTheFacephiResponseUntouched()
        {
            SetupStoredDocument();
            SetupFacePhi(similarity: 0.9345);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Approved));
                Assert.That(result.Data.facialAuthenticationSimilarity, Is.EqualTo(0.9345).Within(0.00001));
                Assert.That(result.Data.ServiceTransactionId, Is.Not.Empty);
            });
        }
    }
}
