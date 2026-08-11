// Ruta destino:
// tests/UnitTests/Application/Facephi/ValidateIdentityV2CmdTests.cs
// (la carpeta se llamaba SoftTokenFacephi; renómbrala a Facephi)
//
// Si NUnit o Moq no resuelven, es porque vienen de un GlobalUsings.cs del
// proyecto de pruebas. En ese caso no agregues nada; ya están.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using onboarding_micro_person.Application.Facephi.Commands;
using onboarding_micro_person.Common.Enums.FacePhi;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;

namespace onboarding_micro_person.Tests.UnitTests.Application.Facephi
{
    [TestFixture]
    [Category("Facephi")]
    public class ValidateIdentityV2CmdTests
    {
        private Mock<IFacePhiService> _facePhiMock = default!;
        private Mock<IIdentityValidationEvaluator> _evaluatorMock = default!;
        private ValidateIdentityV2CmdHandler _handler = default!;

        [SetUp]
        public void Setup()
        {
            _facePhiMock = new Mock<IFacePhiService>();
            _evaluatorMock = new Mock<IIdentityValidationEvaluator>();

            // Por defecto el umbral se supera. Cada test que necesite
            // lo contrario lo sobrescribe.
            _evaluatorMock
                .Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>()))
                .Returns(true);

            _handler = new ValidateIdentityV2CmdHandler(
                _facePhiMock.Object,
                _evaluatorMock.Object,
                Mock.Of<ILogger<ValidateIdentityV2CmdHandler>>());
        }

        // ───────────────────────────── Validador ─────────────────────────────

        [Test]
        public void Validator_Fails_WhenFieldsAreEmpty()
        {
            var validator = new ValidateIdentityV2CmdValidator();

            var result = validator.Validate(new ValidateIdentityV2Cmd());

            Assert.That(result.IsValid, Is.False);
        }

        [TestCase("3")]
        [TestCase("5")]
        public void Validator_AcceptsSupportedMethods(string method)
        {
            var validator = new ValidateIdentityV2CmdValidator();

            var command = new ValidateIdentityV2Cmd
            {
                Token1 = "reference-token",
                BestImageToken = "best-image-token",
                Method = method
            };

            var result = validator.Validate(command);

            Assert.That(result.IsValid, Is.True);
        }

        [TestCase("4")]
        [TestCase("1")]
        [TestCase("")]
        public void Validator_RejectsUnsupportedMethods(string method)
        {
            var validator = new ValidateIdentityV2CmdValidator();

            var command = new ValidateIdentityV2Cmd
            {
                Token1 = "reference-token",
                BestImageToken = "best-image-token",
                Method = method
            };

            var result = validator.Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_RejectsOperationIdThatIsNotAUuid()
        {
            var validator = new ValidateIdentityV2CmdValidator();

            var command = new ValidateIdentityV2Cmd
            {
                Token1 = "reference-token",
                BestImageToken = "best-image-token",
                Method = "3",
                Tracking = new FacephiTrackingExtraData
                {
                    ExtraData = "tracking-extra-data",
                    OperationId = "no-es-uuid"
                }
            };

            var result = validator.Validate(command);

            Assert.That(result.IsValid, Is.False);
        }

        // ─────────────────────────────── Handler ─────────────────────────────

        [Test]
        public async Task Handler_MapsRequest_AndReturnsResult()
        {
            var expected = new PassiveLivenessResult
            {
                ServiceTransactionId = Guid.NewGuid().ToString(),
                ServiceResultCode = 0,
                facialAuthenticationResult = 3,
                facialAuthenticationLog = "Positive",
                facialAuthenticationSimilarity = 0.9921,
                passiveLivenessResult = FacephiLivenessResult.Live,
                passiveLivenessLog = "Live"
            };

            _facePhiMock
                .Setup(x => x.EvaluatePassiveLivenessToken(
                    It.IsAny<PassiveLivenessRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            var operationId = Guid.NewGuid().ToString();

            var command = new ValidateIdentityV2Cmd
            {
                Token1 = "reference-token",
                BestImageToken = "best-image-token",
                Method = "3",
                Tracking = new FacephiTrackingExtraData
                {
                    ExtraData = "tracking-extra-data",
                    OperationId = operationId
                }
            };

            var result = await _handler.Handle(command, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(ValidationOutcome.Approved));
                Assert.That(result.Facephi.ServiceResultCode, Is.EqualTo(0));
                Assert.That(result.Facephi.facialAuthenticationResult, Is.EqualTo(3));
                Assert.That(
                    result.Facephi.passiveLivenessResult,
                    Is.EqualTo(FacephiLivenessResult.Live));
            });

            _facePhiMock.Verify(
                x => x.EvaluatePassiveLivenessToken(
                    It.Is<PassiveLivenessRequest>(request =>
                        request.Token1 == command.Token1 &&
                        request.BestImageToken == command.BestImageToken &&
                        request.Method == command.Method &&
                        request.TrackingToken == command.Tracking.ExtraData &&
                        request.OperationId == command.Tracking.OperationId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ─────────────────────── Regla de decisión ───────────────────────────
        // Códigos según la documentación de Identity Validation V2.
        // facial:   1 NEGATIVE · 3 POSITIVE · 0, 4, 5 no evaluable
        // liveness: 3 Live · 17 NoLive · 10, 15 error técnico · resto no evaluable

        [TestCase(3, 3, ValidationOutcome.Approved)]
        [TestCase(1, 3, ValidationOutcome.Rejected)]
        [TestCase(3, 17, ValidationOutcome.Rejected)]
        [TestCase(3, 0, ValidationOutcome.Retry)]
        [TestCase(3, 4, ValidationOutcome.Retry)]
        [TestCase(3, 18, ValidationOutcome.Retry)]
        [TestCase(0, 3, ValidationOutcome.Retry)]
        [TestCase(4, 3, ValidationOutcome.Retry)]
        [TestCase(3, 10, ValidationOutcome.Error)]
        [TestCase(3, 15, ValidationOutcome.Error)]
        public async Task Handler_EvaluatesOutcomeFromFacephiCodes(
            int facialResult, int livenessResult, ValidationOutcome expectedOutcome)
        {
            _facePhiMock
                .Setup(x => x.EvaluatePassiveLivenessToken(
                    It.IsAny<PassiveLivenessRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PassiveLivenessResult
                {
                    ServiceResultCode = 0,
                    facialAuthenticationResult = facialResult,
                    facialAuthenticationSimilarity = 0.99,
                    passiveLivenessResult = (FacephiLivenessResult)livenessResult
                });

            var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
        }

        [Test]
        public async Task Handler_ReturnsError_WhenServiceResultCodeIsNotZero()
        {
            _facePhiMock
                .Setup(x => x.EvaluatePassiveLivenessToken(
                    It.IsAny<PassiveLivenessRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PassiveLivenessResult
                {
                    ServiceResultCode = 1,
                    facialAuthenticationResult = 3,
                    passiveLivenessResult = FacephiLivenessResult.Live
                });

            var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(ValidationOutcome.Error));
        }

        // ──────────────────────────── Umbral ─────────────────────────────────

        [Test]
        public async Task Handler_Rejects_WhenSimilarityIsBelowThreshold()
        {
            _evaluatorMock
                .Setup(x => x.MeetsSimilarityThreshold(It.IsAny<double>()))
                .Returns(false);

            _facePhiMock
                .Setup(x => x.EvaluatePassiveLivenessToken(
                    It.IsAny<PassiveLivenessRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PassiveLivenessResult
                {
                    ServiceResultCode = 0,
                    facialAuthenticationResult = 3,
                    facialAuthenticationSimilarity = 0.60,
                    passiveLivenessResult = FacephiLivenessResult.Live
                });

            var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(ValidationOutcome.Rejected));
        }

        [Test]
        public async Task Handler_DoesNotCheckThreshold_WhenFacephiRejects()
        {
            // El umbral es un filtro adicional sobre un POSITIVE,
            // nunca un sustituto del veredicto de Facephi.
            _facePhiMock
                .Setup(x => x.EvaluatePassiveLivenessToken(
                    It.IsAny<PassiveLivenessRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PassiveLivenessResult
                {
                    ServiceResultCode = 0,
                    facialAuthenticationResult = 1,          // NEGATIVE
                    facialAuthenticationSimilarity = 0.99,   // similitud alta, da igual
                    passiveLivenessResult = FacephiLivenessResult.Live
                });

            var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(ValidationOutcome.Rejected));

            _evaluatorMock.Verify(
                x => x.MeetsSimilarityThreshold(It.IsAny<double>()),
                Times.Never);
        }

        // ────────────────────────── Deserialización ──────────────────────────

        [Test]
        public void Deserializes_TheDocumentedFacephiResponse()
        {
            // Ejemplo tomado de la documentación de Identity Validation V2.
            // Este test detecta un binding roto: construir el objeto en memoria
            // nunca ejercita los nombres del JSON.
            const string json = """
            {
              "serviceTransactionId": "2db602ee-3564-4304-af95-92a52eaae12d",
              "serviceResultCode": 0,
              "serviceResultLog": "[identity] Service executed ok",
              "serviceTime": "2235",
              "facialAuthenticationResult": 3,
              "facialAuthenticationLog": "Positive",
              "facialAuthenticationSimilarity": 0.99214232,
              "passiveLivenessResult": 3,
              "passiveLivenessLog": "Live"
            }
            """;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<PassiveLivenessResult>(json, options)!;

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceTransactionId, Is.Not.Empty);
                Assert.That(result.ServiceResultCode, Is.EqualTo(0));
                Assert.That(result.ServiceTime, Is.EqualTo("2235"));
                Assert.That(result.facialAuthenticationResult, Is.EqualTo(3));
                Assert.That(result.facialAuthenticationSimilarity, Is.EqualTo(0.99214232).Within(0.0000001));
                Assert.That(
                    result.passiveLivenessResult,
                    Is.EqualTo(FacephiLivenessResult.Live));
            });
        }

        // ───────────────────────────── Auxiliares ────────────────────────────

        private static ValidateIdentityV2Cmd BuildCommand() => new()
        {
            Token1 = "reference-token",
            BestImageToken = "best-image-token",
            Method = "3"
        };
    }
}
