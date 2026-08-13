// Ruta destino:
// tests/UnitTests/Endpoints/FacephiEndpointsTests.cs
//
// REQUISITOS PARA QUE ESTE ARCHIVO COMPILE:
//
// 1. Los métodos de Endpoints/FacePhi.cs deben ser "public static" en vez de
//    "private static". Es además lo que hace Biometrics.cs en el resto del micro.
//
// 2. El proyecto de pruebas necesita la referencia al framework de ASP.NET Core
//    para reconocer IResult y los tipos de Microsoft.AspNetCore.Http.HttpResults:
//
//    <ItemGroup>
//      <FrameworkReference Include="Microsoft.AspNetCore.App" />
//    </ItemGroup>

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using onboarding_micro_person.Application.Facephi.Commands;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Enums.FacePhi;
using onboarding_micro_person.Common.Interfaces.Biometric;
using onboarding_micro_person.Common.Models.Biometric;
using onboarding_micro_person.Endpoints;

namespace onboarding_micro_person.Tests.UnitTests.Endpoints
{
    [TestFixture]
    [Category("Facephi")]
    public class FacephiEndpointsTests
    {
        private Mock<ISender> _senderMock = default!;
        private Mock<IFacePhiBiometricRepository> _repositoryMock = default!;

        [SetUp]
        public void Setup()
        {
            _senderMock = new Mock<ISender>();
            _repositoryMock = new Mock<IFacePhiBiometricRepository>();
        }

        private static PassiveLivenessResult BuildFacephiResult() => new()
        {
            ServiceTransactionId = Guid.NewGuid().ToString(),
            ServiceResultCode = 0,
            facialAuthenticationResult = 3,
            facialAuthenticationSimilarity = 0.9921,
            passiveLivenessResult = FacephiLivenessResult.Live
        };

        private void SetupSender<TRequest>(FacialValidationStatus status, PassiveLivenessResult data)
            where TRequest : IRequest<ValidateIdentityV2Result>
        {
            _senderMock
                .Setup(x => x.Send(It.IsAny<TRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidateIdentityV2Result(status, data));
        }

        // ─────────────────────── POST /api/v1/facephi/validate ───────────────────

        [Test]
        public async Task ValidateIdentityV2_Returns200_WhenApproved()
        {
            var data = BuildFacephiResult();
            SetupSender<ValidateIdentityV2Cmd>(FacialValidationStatus.Approved, data);

            var result = await Facephi.ValidateIdentityV2(
                new ValidateIdentityV2Cmd(), _senderMock.Object, CancellationToken.None);

            var json = result as JsonHttpResult<PassiveLivenessResult>;

            Assert.Multiple(() =>
            {
                Assert.That(json, Is.Not.Null, "Se esperaba un JsonHttpResult<PassiveLivenessResult>.");
                Assert.That(json!.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(json.Value, Is.SameAs(data), "El cuerpo debe ser la respuesta de Facephi sin modificar.");
            });
        }

        [Test]
        public async Task ValidateIdentityV2_Returns422_WhenRejected()
        {
            var data = BuildFacephiResult();
            SetupSender<ValidateIdentityV2Cmd>(FacialValidationStatus.Rejected, data);

            var result = await Facephi.ValidateIdentityV2(
                new ValidateIdentityV2Cmd(), _senderMock.Object, CancellationToken.None);

            var json = result as JsonHttpResult<PassiveLivenessResult>;

            Assert.Multiple(() =>
            {
                Assert.That(json, Is.Not.Null);
                Assert.That(json!.StatusCode, Is.EqualTo(StatusCodes.Status422UnprocessableEntity));
                Assert.That(json.Value, Is.SameAs(data));
            });
        }

        [Test]
        public async Task ValidateIdentityV2_SendsTheCommandThroughMediatR()
        {
            SetupSender<ValidateIdentityV2Cmd>(FacialValidationStatus.Approved, BuildFacephiResult());

            var command = new ValidateIdentityV2Cmd { IdT24 = "123456789" };

            await Facephi.ValidateIdentityV2(command, _senderMock.Object, CancellationToken.None);

            _senderMock.Verify(
                x => x.Send(command, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ────────────────── POST /api/v1/facephi/validate-face ───────────────────

        [Test]
        public async Task ValidateFaceOnly_Returns200_WhenApproved()
        {
            var data = BuildFacephiResult();
            SetupSender<ValidateFaceOnlyCmd>(FacialValidationStatus.Approved, data);

            var result = await Facephi.ValidateFaceOnly(
                new ValidateFaceOnlyCmd(), _senderMock.Object, CancellationToken.None);

            var json = result as JsonHttpResult<PassiveLivenessResult>;

            Assert.Multiple(() =>
            {
                Assert.That(json, Is.Not.Null,
                    "validate-face debe responder igual que validate: JsonHttpResult, no Results.Ok del objeto completo.");
                Assert.That(json!.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(json.Value, Is.SameAs(data));
            });
        }

        [Test]
        public async Task ValidateFaceOnly_Returns422_WhenRejected()
        {
            var data = BuildFacephiResult();
            SetupSender<ValidateFaceOnlyCmd>(FacialValidationStatus.Rejected, data);

            var result = await Facephi.ValidateFaceOnly(
                new ValidateFaceOnlyCmd(), _senderMock.Object, CancellationToken.None);

            var json = result as JsonHttpResult<PassiveLivenessResult>;

            Assert.Multiple(() =>
            {
                Assert.That(json, Is.Not.Null);
                Assert.That(json!.StatusCode, Is.EqualTo(StatusCodes.Status422UnprocessableEntity));
            });
        }

        [Test]
        public async Task BothValidationEndpoints_ReturnTheSameShape()
        {
            // Si estos dos divergen, el front tiene que parsear dos formatos distintos.
            var data = BuildFacephiResult();

            _senderMock
                .Setup(x => x.Send(It.IsAny<ValidateIdentityV2Cmd>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidateIdentityV2Result(FacialValidationStatus.Approved, data));

            _senderMock
                .Setup(x => x.Send(It.IsAny<ValidateFaceOnlyCmd>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidateIdentityV2Result(FacialValidationStatus.Approved, data));

            var full = await Facephi.ValidateIdentityV2(
                new ValidateIdentityV2Cmd(), _senderMock.Object, CancellationToken.None);

            var faceOnly = await Facephi.ValidateFaceOnly(
                new ValidateFaceOnlyCmd(), _senderMock.Object, CancellationToken.None);

            Assert.That(faceOnly.GetType(), Is.EqualTo(full.GetType()));
        }

        // ──────────────── GET /api/v1/facephi/document/{idT24} ───────────────────

        [Test]
        public async Task GetDocument_ReturnsHasDocumentFalse_WhenThereIsNoRecord()
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FacePhiBiometricDocument?)null);

            var result = await Facephi.GetDocumentByIdT24(
                "123456789", _repositoryMock.Object, CancellationToken.None);

            var ok = result as Ok<GetFacePhiDocumentResponse>;

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.Not.Null);
                Assert.That(ok!.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(ok.Value!.HasDocument, Is.False);
            });
        }

        [Test]
        public async Task GetDocument_ReturnsHasDocumentTrue_WhenTheDocumentHasToken1()
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FacePhiBiometricDocument
                {
                    IdT24 = "123456789",
                    DocumentType = "CED",
                    Token1 = "stored-document-token",
                    Method = 5
                });

            var result = await Facephi.GetDocumentByIdT24(
                "123456789", _repositoryMock.Object, CancellationToken.None);

            var ok = result as Ok<GetFacePhiDocumentResponse>;

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.Not.Null);
                Assert.That(ok!.Value!.HasDocument, Is.True);
                Assert.That(ok.Value.DocumentType, Is.EqualTo("CED"));
            });
        }

        [TestCase("")]
        [TestCase("   ")]
        public async Task GetDocument_ReturnsHasDocumentFalse_WhenToken1IsMissing(string token1)
        {
            // El front usa este endpoint para decidir si puede llamar a validate-face.
            // Un registro sin token1 no sirve para eso: debe reportarse como false.
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FacePhiBiometricDocument
                {
                    IdT24 = "123456789",
                    DocumentType = "CED",
                    Token1 = token1,
                    Method = 5
                });

            var result = await Facephi.GetDocumentByIdT24(
                "123456789", _repositoryMock.Object, CancellationToken.None);

            var ok = result as Ok<GetFacePhiDocumentResponse>;

            Assert.That(ok!.Value!.HasDocument, Is.False);
        }

        [Test]
        public async Task GetDocument_NeverExposesTheStoredTokens()
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FacePhiBiometricDocument
                {
                    IdT24 = "123456789",
                    DocumentType = "CED",
                    Token1 = "stored-document-token",
                    Token2 = "stored-best-image",
                    Method = 5
                });

            var result = await Facephi.GetDocumentByIdT24(
                "123456789", _repositoryMock.Object, CancellationToken.None);

            var ok = result as Ok<GetFacePhiDocumentResponse>;
            var serialized = System.Text.Json.JsonSerializer.Serialize(ok!.Value);

            Assert.Multiple(() =>
            {
                Assert.That(serialized, Does.Not.Contain("stored-document-token"));
                Assert.That(serialized, Does.Not.Contain("stored-best-image"));
            });
        }
    }
}
