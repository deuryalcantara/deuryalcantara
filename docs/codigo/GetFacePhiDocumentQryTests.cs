// Ruta destino:
// tests/UnitTests/Application/Facephi/GetFacePhiDocumentQryTests.cs
//
// Sólo aplica si convertiste el GET a Query de MediatR.
// Si el endpoint sigue llamando al repositorio directo, no uses este archivo.

using Microsoft.Extensions.Logging;
using onboarding_micro_person.Application.Facephi.Queries;
using onboarding_micro_person.Common.Entities;
using onboarding_micro_person.Common.Interfaces.Biometric;

namespace onboarding_micro_person.Tests.UnitTests.Application.Facephi
{
    [TestFixture]
    [Category("Facephi")]
    public class GetFacePhiDocumentQryTests
    {
        private Mock<IFacePhiBiometricRepository> _repositoryMock = default!;

        [SetUp]
        public void Setup()
        {
            _repositoryMock = new Mock<IFacePhiBiometricRepository>();
        }

        private GetFacePhiDocumentQryHandler BuildHandler() => new(
            _repositoryMock.Object,
            Mock.Of<ILogger<GetFacePhiDocumentQryHandler>>());

        private void SetupStoredDocument(string token1, string documentType = "CED")
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FacePhiBiometricDocument
                {
                    IdT24 = "123456789",
                    DocumentType = documentType,
                    Token1 = token1,
                    Token2 = "stored-best-image",
                    Method = 5
                });
        }

        // ─────────────────────────────── Validador ───────────────────────────────

        [Test]
        public void Validator_RequiresIdT24()
        {
            var result = new GetFacePhiDocumentQryValidator().Validate(new GetFacePhiDocumentQry());

            Assert.That(result.IsValid, Is.False);
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Validator_RejectsBlankIdT24(string idT24)
        {
            var result = new GetFacePhiDocumentQryValidator()
                .Validate(new GetFacePhiDocumentQry { IdT24 = idT24 });

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Validator_Succeeds_WithAnIdT24()
        {
            var result = new GetFacePhiDocumentQryValidator()
                .Validate(new GetFacePhiDocumentQry { IdT24 = "123456789" });

            Assert.That(result.IsValid, Is.True);
        }

        // ──────────────────────────────── Handler ────────────────────────────────

        [Test]
        public async Task Handler_ReturnsFalse_WhenThereIsNoRecord()
        {
            _repositoryMock
                .Setup(x => x.GetByIdT24Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FacePhiBiometricDocument)null!);

            var result = await BuildHandler().Handle(
                new GetFacePhiDocumentQry { IdT24 = "123456789" }, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.HasDocument, Is.False);
                Assert.That(result.DocumentType, Is.Null);
            });
        }

        [Test]
        public async Task Handler_ReturnsTrue_WhenTheDocumentHasToken1()
        {
            SetupStoredDocument("stored-document-token", "PASSPORT");

            var result = await BuildHandler().Handle(
                new GetFacePhiDocumentQry { IdT24 = "123456789" }, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.HasDocument, Is.True);
                Assert.That(result.DocumentType, Is.EqualTo("PASSPORT"));
            });
        }

        [TestCase("")]
        [TestCase("   ")]
        public async Task Handler_ReturnsFalse_WhenToken1IsMissing(string token1)
        {
            // El front usa este resultado para decidir si puede llamar a
            // validate-face. Un registro sin token1 no habilita ese flujo.
            SetupStoredDocument(token1);

            var result = await BuildHandler().Handle(
                new GetFacePhiDocumentQry { IdT24 = "123456789" }, CancellationToken.None);

            Assert.That(result.HasDocument, Is.False);
        }

        [Test]
        public async Task Handler_QueriesTheRepositoryWithTheGivenIdT24()
        {
            SetupStoredDocument("stored-document-token");

            await BuildHandler().Handle(
                new GetFacePhiDocumentQry { IdT24 = "987654321" }, CancellationToken.None);

            _repositoryMock.Verify(
                x => x.GetByIdT24Async("987654321", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task Handler_NeverExposesTheStoredTokens()
        {
            SetupStoredDocument("stored-document-token");

            var result = await BuildHandler().Handle(
                new GetFacePhiDocumentQry { IdT24 = "123456789" }, CancellationToken.None);

            var serialized = System.Text.Json.JsonSerializer.Serialize(result);

            Assert.Multiple(() =>
            {
                Assert.That(serialized, Does.Not.Contain("stored-document-token"));
                Assert.That(serialized, Does.Not.Contain("stored-best-image"));
            });
        }
    }
}
