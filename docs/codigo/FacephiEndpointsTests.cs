// Ruta destino:
// tests/UnitTests/Endpoints/FacephiEndpointsTests.cs
//
// Versión para cuando los endpoints devuelven { isValid } con HTTP 200 en
// ambos resultados: la interpretación de los códigos de Facephi vive en el
// handler y el consumidor sólo recibe el booleano.
//
// REQUISITOS PARA QUE ESTE ARCHIVO COMPILE:
//
// 1. Los métodos de Endpoints/FacePhi.cs deben ser "public static".
// 2. El proyecto de pruebas necesita:
//    <ItemGroup>
//      <FrameworkReference Include="Microsoft.AspNetCore.App" />
//    </ItemGroup>

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using onboarding_micro_person.Application.Facephi.Commands;
using onboarding_micro_person.Application.Facephi.Queries;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Enums.Biometric;
using onboarding_micro_person.Common.Enums.FacePhi;
using onboarding_micro_person.Common.Models.Biometric;
using onboarding_micro_person.Endpoints;

namespace onboarding_micro_person.Tests.UnitTests.Endpoints
{
    [TestFixture]
    [Category("Facephi")]
    public class FacephiEndpointsTests
    {
        private Mock<ISender> _senderMock = default!;

        [SetUp]
        public void Setup()
        {
            _senderMock = new Mock<ISender>();
        }

        private static PassiveLivenessResult BuildFacephiResult() => new()
        {
            ServiceTransactionId = Guid.NewGuid().ToString(),
            ServiceResultCode = 0,
            facialAuthenticationResult = 3,
            facialAuthenticationSimilarity = 0.9921,
            passiveLivenessResult = FacephiLivenessResult.Live
        };

        private void SetupSender<TRequest>(FacialValidationStatus status)
            where TRequest : IRequest<ValidateIdentityV2Result>
        {
            _senderMock
                .Setup(x => x.Send(It.IsAny<TRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidateIdentityV2Result(status, BuildFacephiResult()));
        }

        // ─────────────────────── POST /api/v1/facephi/validate ───────────────────

        [Test]
        public async Task ValidateIdentityV2_ReturnsIsValidTrue_WhenApproved()
        {
            SetupSender<ValidateIdentityV2Cmd>(FacialValidationStatus.Approved);

            var result = await Facephi.ValidateIdentityV2(
                new ValidateIdentityV2Cmd(), _senderMock.Object, CancellationToken.None);

            var ok = result as Ok<ValidateBiometricResponse>;

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.Not.Null, "Se esperaba un Ok<ValidateBiometricResponse>.");
                Assert.That(ok!.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(ok.Value!.IsValid, Is.True);
            });
        }

        [Test]
        public async Task ValidateIdentityV2_ReturnsIsValidFalse_WhenRejected()
        {
            SetupSender<ValidateIdentityV2Cmd>(FacialValidationStatus.Rejected);

            var result = await Facephi.ValidateIdentityV2(
                new ValidateIdentityV2Cmd(), _senderMock.Object, CancellationToken.None);

            var ok = result as Ok<ValidateBiometricResponse>;

            Assert.Multiple(() =>
            {
                // Un rechazo biométrico no es un error: responde 200 con isValid false.
                Assert.That(ok, Is.Not.Null);
                Assert.That(ok!.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(ok.Value!.IsValid, Is.False);
            });
        }

        [Test]
        public async Task ValidateIdentityV2_NeverExposesTheFacephiCodes()
        {
            SetupSender<ValidateIdentityV2Cmd>(FacialValidationStatus.Approved);

            var result = await Facephi.ValidateIdentityV2(
                new ValidateIdentityV2Cmd(), _senderMock.Object, CancellationToken.None);

            var ok = result as Ok<ValidateBiometricResponse>;
            var serialized = System.Text.Json.JsonSerializer.Serialize(ok!.Value);

            Assert.Multiple(() =>
            {
                Assert.That(serialized, Does.Not.Contain("facialAuthentication"));
                Assert.That(serialized, Does.Not.Contain("passiveLiveness"));
                Assert.That(serialized, Does.Not.Contain("serviceTransactionId"));
            });
        }

        [Test]
        public async Task ValidateIdentityV2_SendsTheCommandThroughMediatR()
        {
            SetupSender<ValidateIdentityV2Cmd>(FacialValidationStatus.Approved);

            var command = new ValidateIdentityV2Cmd { IdT24 = "123456789" };

            await Facephi.ValidateIdentityV2(command, _senderMock.Object, CancellationToken.None);

            _senderMock.Verify(
                x => x.Send(command, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ────────────────── POST /api/v1/facephi/validate-face ───────────────────

        [Test]
        public async Task ValidateFaceOnly_ReturnsIsValidTrue_WhenApproved()
        {
            SetupSender<ValidateFaceOnlyCmd>(FacialValidationStatus.Approved);

            var result = await Facephi.ValidateFaceOnly(
                new ValidateFaceOnlyCmd(), _senderMock.Object, CancellationToken.None);

            var ok = result as Ok<ValidateBiometricResponse>;

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.Not.Null);
                Assert.That(ok!.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(ok.Value!.IsValid, Is.True);
            });
        }

        [Test]
        public async Task ValidateFaceOnly_ReturnsIsValidFalse_WhenRejected()
        {
            SetupSender<ValidateFaceOnlyCmd>(FacialValidationStatus.Rejected);

            var result = await Facephi.ValidateFaceOnly(
                new ValidateFaceOnlyCmd(), _senderMock.Object, CancellationToken.None);

            var ok = result as Ok<ValidateBiometricResponse>;

            Assert.That(ok!.Value!.IsValid, Is.False);
        }

        [Test]
        public async Task ValidateFaceOnly_SendsTheCommandThroughMediatR()
        {
            SetupSender<ValidateFaceOnlyCmd>(FacialValidationStatus.Approved);

            var command = new ValidateFaceOnlyCmd { IdT24 = "123456789" };

            await Facephi.ValidateFaceOnly(command, _senderMock.Object, CancellationToken.None);

            _senderMock.Verify(
                x => x.Send(command, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task BothValidationEndpoints_ReturnTheSameShape()
        {
            // Si divergen, el consumidor tiene que parsear dos formatos distintos.
            _senderMock
                .Setup(x => x.Send(It.IsAny<ValidateIdentityV2Cmd>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidateIdentityV2Result(
                    FacialValidationStatus.Approved, BuildFacephiResult()));

            _senderMock
                .Setup(x => x.Send(It.IsAny<ValidateFaceOnlyCmd>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidateIdentityV2Result(
                    FacialValidationStatus.Approved, BuildFacephiResult()));

            var full = await Facephi.ValidateIdentityV2(
                new ValidateIdentityV2Cmd(), _senderMock.Object, CancellationToken.None);

            var faceOnly = await Facephi.ValidateFaceOnly(
                new ValidateFaceOnlyCmd(), _senderMock.Object, CancellationToken.None);

            Assert.That(faceOnly.GetType(), Is.EqualTo(full.GetType()));
        }

        // ──────────────── Consulta de documento almacenado ───────────────────────

        [Test]
        public async Task GetDocument_DelegatesToMediatRAndReturnsOk()
        {
            var expected = new GetFacePhiDocumentResponse
            {
                HasDocument = true,
                DocumentType = "CED"
            };

            _senderMock
                .Setup(x => x.Send(It.IsAny<GetFacePhiDocumentQry>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            var result = await Facephi.GetDocumentByIdT24(
                "123456789", _senderMock.Object, CancellationToken.None);

            var ok = result as Ok<GetFacePhiDocumentResponse>;

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.Not.Null);
                Assert.That(ok!.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(ok.Value, Is.SameAs(expected));
            });
        }

        [Test]
        public async Task GetDocument_PassesTheIdT24ToTheQuery()
        {
            _senderMock
                .Setup(x => x.Send(It.IsAny<GetFacePhiDocumentQry>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetFacePhiDocumentResponse { HasDocument = false });

            await Facephi.GetDocumentByIdT24(
                "987654321", _senderMock.Object, CancellationToken.None);

            _senderMock.Verify(
                x => x.Send(
                    It.Is<GetFacePhiDocumentQry>(qry => qry.IdT24 == "987654321"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
