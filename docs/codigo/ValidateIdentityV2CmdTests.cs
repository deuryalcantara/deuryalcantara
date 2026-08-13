// Ruta destino:
// tests/UnitTests/Application/Facephi/ValidateIdentityV2CmdTests.cs
//
// Si NUnit o Moq no resuelven, vienen de un GlobalUsings.cs del proyecto de pruebas.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using onboarding_micro_person.Application.Facephi.Commands;
using onboarding_micro_person.Common.Configuration;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Enums.FacePhi;
using onboarding_micro_person.Common.Extensions.FacePhi;
using onboarding_micro_person.Common.Interfaces;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;

namespace onboarding_micro_person.Tests.UnitTests.Application.Facephi
{
    [TestFixture]
    [Category("Facephi")]
    public class ValidateIdentityV2CmdTests
    {
        private const int FacialPositive = 3;
        private const int FacialNegative = 1;
        private const int FacialNone = 0;

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

            // Por defecto el umbral se supera; los tests que necesiten lo contrario lo cambian.
            _evaluatorMock
                .Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>()))
                .Returns(true);

            _repositoryMock
                .Setup(x => x.UpsertAsync(
                    It.IsAny<FacePhiBiometricDocument>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _facePhiMock
                .Setup(x => x.FinishTrackingAsync(
                    It.IsAny<bool>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private ValidateIdentityV2CmdHandler BuildHandler() => new(
            _facePhiMock.Object,
            _repositoryMock.Object,
            _evaluatorMock.Object,
            Options.Create(_settings),
            Mock.Of<ILogger<ValidateIdentityV2CmdHandler>>());

        private static ValidateIdentityV2Cmd BuildCommand(
            FacephiTrackingExtraData? tracking = null) => new()
        {
            IdT24 = "123456789",
            DocumentType = "CED",
            Token1 = "reference-token",
            BestImageToken = "best-image-token",
            Tracking = tracking
        };

        private static FacephiTrackingExtraData BuildTracking() => new()
        {
            ExtraData = "tracking-extra-data",
            OperationId = Guid.NewGuid().ToString()
        };

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
                    facialAuthenticationLog = facialResult == FacialPositive ? "Positive" : "Negative",
                    facialAuthenticationSimilarity = similarity,
                    passiveLivenessResult = liveness,
                    passiveLivenessLog = liveness == FacephiLivenessResult.Live ? "Live" : "NoLive"
                });
        }

        // ─────────────────────────────── Validador ───────────────────────────────

        [Test]
        public void Validator_Fails_WhenCommandIsEmpty()
        {
            var result = new ValidateIdentityV2CmdValidator().Validate(new ValidateIdentityV2Cmd());

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_Succeeds_WithAValidCommand()
        {
            var result = new ValidateIdentityV2CmdValidator().Validate(BuildCommand(BuildTracking()));

            Assert.That(result.IsValid, Is.True);
        }

        [TestCase("CED")]
        [TestCase("PASSPORT")]
        public void Validator_AcceptsSupportedDocumentTypes(string documentType)
        {
            var command = BuildCommand();
            command.DocumentType = documentType;

            var result = new ValidateIdentityV2CmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.True);
        }

        [TestCase("")]
        [TestCase("DNI")]
        [TestCase("ced")]
        public void Validator_RejectsUnsupportedDocumentTypes(string documentType)
        {
            var command = BuildCommand();
            command.DocumentType = documentType;

            var result = new ValidateIdentityV2CmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_RequiresIdT24()
        {
            var command = BuildCommand();
            command.IdT24 = string.Empty;

            var result = new ValidateIdentityV2CmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_RequiresToken1()
        {
            var command = BuildCommand();
            command.Token1 = string.Empty;

            var result = new ValidateIdentityV2CmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_RequiresBestImageToken()
        {
            var command = BuildCommand();
            command.BestImageToken = string.Empty;

            var result = new ValidateIdentityV2CmdValidator().Validate(command);

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

            var result = new ValidateIdentityV2CmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_AllowsTrackingWithoutOperationId()
        {
            var command = BuildCommand(new FacephiTrackingExtraData
            {
                ExtraData = "extra",
                OperationId = string.Empty
            });

            var result = new ValidateIdentityV2CmdValidator().Validate(command);

            Assert.That(result.IsValid, Is.True);
        }

        // ──────────────────────── Mapeo hacia Facephi ────────────────────────────

        [Test]
        public async Task Handler_MapsTheRequestToFacephi()
        {
            SetupFacePhi();

            var tracking = BuildTracking();
            var command = BuildCommand(tracking);

            await BuildHandler().Handle(command, CancellationToken.None);

            _facePhiMock.Verify(
                x => x.EvaluatePassiveLivenessToken(
                    It.Is<PassiveLivenessRequest>(request =>
                        request.Token1 == command.Token1 &&
                        request.BestImageToken == command.BestImageToken &&
                        request.TrackingToken == tracking.ExtraData &&
                        request.OperationId == tracking.OperationId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task Handler_SendsTheConfiguredAuthenticationMethod()
        {
            SetupFacePhi();

            var expectedMethod = ((int)FacePhiAuthenticateMethod.TokenToTemplate).ToString();

            await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            _facePhiMock.Verify(
                x => x.EvaluatePassiveLivenessToken(
                    It.Is<PassiveLivenessRequest>(request => request.Method == expectedMethod),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ──────────── Veredicto: toggle de custom liveness APAGADO ───────────────

        [TestCase(FacialPositive, FacephiLivenessResult.Live, FacialValidationStatus.Approved)]
        [TestCase(FacialNegative, FacephiLivenessResult.Live, FacialValidationStatus.Rejected)]
        [TestCase(FacialNone, FacephiLivenessResult.Live, FacialValidationStatus.Rejected)]
        [TestCase(FacialPositive, FacephiLivenessResult.NoLive, FacialValidationStatus.Rejected)]
        [TestCase(FacialPositive, FacephiLivenessResult.None, FacialValidationStatus.Rejected)]
        [TestCase(FacialPositive, FacephiLivenessResult.NoneBecauseBadQuality, FacialValidationStatus.Rejected)]
        public async Task Handler_TrustsFacephiVerdict_WhenCustomCheckIsDisabled(
            int facialResult, FacephiLivenessResult liveness, FacialValidationStatus expected)
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = false;
            SetupFacePhi(facialResult: facialResult, liveness: liveness);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(expected));
        }

        [Test]
        public async Task Handler_DoesNotUseTheEvaluator_WhenCustomCheckIsDisabled()
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = false;
            SetupFacePhi();

            await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            _evaluatorMock.Verify(
                x => x.MeetsSimilarityThreshold(It.IsAny<double>()), Times.Never);
        }

        // ──────────── Veredicto: toggle de custom liveness ENCENDIDO ─────────────

        [Test]
        public async Task Handler_Approves_WhenThresholdIsMetAndSubjectIsAlive()
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = true;
            _evaluatorMock.Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>())).Returns(true);
            SetupFacePhi(liveness: FacephiLivenessResult.Live);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Approved));
        }

        [Test]
        public async Task Handler_Rejects_WhenThresholdIsNotMet()
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = true;
            _evaluatorMock.Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>())).Returns(false);
            SetupFacePhi(liveness: FacephiLivenessResult.Live, similarity: 0.60);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Rejected));
        }

        [Test]
        public async Task Handler_Rejects_WhenThresholdIsMetButSubjectIsNotAlive()
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = true;
            _evaluatorMock.Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>())).Returns(true);
            SetupFacePhi(liveness: FacephiLivenessResult.NoLive);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Rejected));
        }

        [Test]
        public async Task Handler_PassesTheSimilarityToTheEvaluator()
        {
            _settings.FACIAL_VALIDATION_CUSTOM_LIVENESS_CHECK = true;
            SetupFacePhi(similarity: 0.8123);

            await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            _evaluatorMock.Verify(
                x => x.MeetsSimilarityThreshold(0.8123), Times.Once);
        }

        // ───────────────────────── Fallo del servicio ────────────────────────────

        [Test]
        public async Task Handler_Rejects_WhenServiceResultCodeIsNotZero()
        {
            // Comportamiento actual: un fallo de Facephi se reporta como rechazo.
            // Si se decide diferenciarlo, este test debe cambiar.
            SetupFacePhi(serviceResultCode: 1);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(FacialValidationStatus.Rejected));
        }

        // ────────────────────────────- Persistencia ──────────────────────────────

        [Test]
        public async Task Handler_PersistsTheBiometricDocument()
        {
            SetupFacePhi();

            var tracking = BuildTracking();
            var command = BuildCommand(tracking);

            await BuildHandler().Handle(command, CancellationToken.None);

            _repositoryMock.Verify(
                x => x.UpsertAsync(
                    It.Is<FacePhiBiometricDocument>(document =>
                        document.IdT24 == command.IdT24 &&
                        document.DocumentType == command.DocumentType &&
                        document.Token1 == command.Token1 &&
                        document.Token2 == command.BestImageToken &&
                        document.Tracking != null &&
                        document.Tracking.ExtraData == tracking.ExtraData &&
                        document.Tracking.OperationId == tracking.OperationId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task Handler_PersistsWithoutTracking_WhenTrackingIsNotProvided()
        {
            SetupFacePhi();

            await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            _repositoryMock.Verify(
                x => x.UpsertAsync(
                    It.Is<FacePhiBiometricDocument>(document => document.Tracking == null),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void Handler_PropagatesTheException_WhenPersistenceFails()
        {
            SetupFacePhi();

            _repositoryMock
                .Setup(x => x.UpsertAsync(
                    It.IsAny<FacePhiBiometricDocument>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            Assert.ThrowsAsync<InvalidOperationException>(
                () => BuildHandler().Handle(BuildCommand(BuildTracking()), CancellationToken.None));
        }

        [Test]
        public void Handler_DoesNotCloseTracking_WhenPersistenceFails()
        {
            // Este test documenta una consecuencia del "throw" en el catch:
            // si Mongo falla, la operación queda ABIERTA en Facephi.
            // Si se quita el throw, este test debe invertirse.
            SetupFacePhi();

            _repositoryMock
                .Setup(x => x.UpsertAsync(
                    It.IsAny<FacePhiBiometricDocument>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            Assert.ThrowsAsync<InvalidOperationException>(
                () => BuildHandler().Handle(BuildCommand(BuildTracking()), CancellationToken.None));

            _facePhiMock.Verify(
                x => x.FinishTrackingAsync(
                    It.IsAny<bool>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ────────────────────────────── Tracking ─────────────────────────────────

        [Test]
        public async Task Handler_ClosesTrackingAsApproved_WhenValidationSucceeds()
        {
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

        // ─────────────────────────────── Resultado ───────────────────────────────

        [Test]
        public async Task Handler_ReturnsTheFacephiResponseUntouched()
        {
            SetupFacePhi(similarity: 0.9876);

            var result = await BuildHandler().Handle(BuildCommand(), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Data, Is.Not.Null);
                Assert.That(result.Data.facialAuthenticationSimilarity, Is.EqualTo(0.9876).Within(0.00001));
                Assert.That(result.Data.ServiceTransactionId, Is.Not.Empty);
            });
        }
    }
}
