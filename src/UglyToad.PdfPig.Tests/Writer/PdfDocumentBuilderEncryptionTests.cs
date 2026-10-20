namespace UglyToad.PdfPig.Tests.Writer
{
    using System.Text;
    using Content;
    using Integration;
    using PdfPig.Core;
    using PdfPig.Exceptions;
    using PdfPig.Fonts.Standard14Fonts;
    using PdfPig.Writer;
    using Tests.Fonts.TrueType;

    public class PdfDocumentBuilderEncryptionTests
    {
        private static ParsingOptions Password(string password) => new ParsingOptions { Passwords = [password], UseLenientParsing = false };

        private static byte[] BuildDocument(PdfEncryptionOptions options, PdfWriterType type = PdfWriterType.Default, double version = 1.7, byte[]? image = null)
        {
            var output = new MemoryStream();
            using (var builder = new PdfDocumentBuilder(output, options, true, type, version))
            {
                var page = builder.AddPage(PageSize.A4);
                var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);
                var roboto = builder.AddTrueTypeFont(TrueTypeTestHelper.GetFileBytes("Roboto-Regular.ttf"));

                page.AddText("Standard fourteen text", 12, new PdfPoint(25, 700), helvetica);
                page.AddText("TrueType text", 12, new PdfPoint(25, 650), roboto);

                if (image != null)
                {
                    page.AddJpeg(image, new PdfRectangle(25, 400, 200, 500));
                }

                builder.DocumentInformation.Title = "Secret title";

                return builder.Build();
            }
        }

        [Theory]
        [InlineData(PdfWriterType.Default)]
        [InlineData(PdfWriterType.ObjectInMemoryDedup)]
        public void RoundTripsWithBothPasswords(PdfWriterType type)
        {
            var image = File.ReadAllBytes(IntegrationHelpers.GetDocumentPath("smile-250-by-160.jpg", false));
            var bytes = BuildDocument(new PdfEncryptionOptions { UserPassword = "open-me", OwnerPassword = "admin", Permissions = PdfPermissions.Print }, type, image: image);

            var text = Encoding.GetEncoding("iso-8859-1").GetString(bytes);
            Assert.Contains("/Encrypt", text);
            Assert.DoesNotContain("Secret title", text);

            foreach (var password in new[] { "open-me", "admin" })
            {
                using var document = PdfDocument.Open(bytes, Password(password));

                var page = document.GetPage(1);
                Assert.Contains("Standard fourteen text", page.Text);
                Assert.Contains("TrueType text", page.Text);
                Assert.Equal(image, Assert.Single(page.GetImages()).RawMemory.ToArray());
                Assert.Equal("Secret title", document.Information.Title);
            }
        }

        [Fact]
        public void OpeningWithoutOrWithTheWrongPasswordFails()
        {
            var bytes = BuildDocument(new PdfEncryptionOptions { UserPassword = "open-me", OwnerPassword = "admin" });

            Assert.Throws<PdfDocumentEncryptedException>(() => PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false }));
            Assert.Throws<PdfDocumentEncryptedException>(() => PdfDocument.Open(bytes, Password("wrong")));
        }

        [Fact]
        public void EmptyUserPasswordOpensWithoutAPassword()
        {
            var bytes = BuildDocument(new PdfEncryptionOptions { OwnerPassword = "admin", Permissions = PdfPermissions.None });

            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            Assert.Contains("TrueType text", document.GetPage(1).Text);
        }

        [Fact]
        public void EmptyOwnerPasswordIsReplacedByARandomOne()
        {
            var bytes = BuildDocument(new PdfEncryptionOptions { UserPassword = "open-me" });

            using (var document = PdfDocument.Open(bytes, Password("open-me")))
            {
                Assert.Equal(1, document.NumberOfPages);
            }

            // An empty owner password must not be usable to open the document with owner rights.
            Assert.Throws<PdfDocumentEncryptedException>(() => PdfDocument.Open(bytes, Password("")));
        }

        [Theory]
        [InlineData(1.7, true)]
        [InlineData(2.0, false)]
        public void AdobeExtensionLevelIsWrittenUnlessThePdfVersionIs2(double version, bool expectExtension)
        {
            var bytes = BuildDocument(new PdfEncryptionOptions { OwnerPassword = "admin" }, version: version);

            var text = Encoding.GetEncoding("iso-8859-1").GetString(bytes);
            Assert.StartsWith("%PDF-" + version.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), text);

            // The catalog is not encrypted so the extension dictionary is readable.
            Assert.Equal(expectExtension, text.Contains("/ADBE"));
            if (expectExtension)
            {
                Assert.Contains("/ExtensionLevel 8", text);
                Assert.Contains("/BaseVersion /1.7", text);
            }
        }

        [Fact]
        public void VersionBelow17IsRaisedTo17()
        {
            var bytes = BuildDocument(new PdfEncryptionOptions { OwnerPassword = "admin" }, version: 1.4);

            var text = Encoding.GetEncoding("iso-8859-1").GetString(bytes);
            Assert.StartsWith("%PDF-1.7", text);
            Assert.Contains("/ExtensionLevel 8", text);
            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            Assert.Equal(1, document.NumberOfPages);
        }

        [Fact]
        public void EncryptionOptionsAreRequired()
        {
            Assert.Throws<ArgumentNullException>(() => new PdfDocumentBuilder(new MemoryStream(), null!));
        }

        [Fact]
        public void PdfAForbidsEncryption()
        {
            using var builder = new PdfDocumentBuilder(new MemoryStream(), new PdfEncryptionOptions { OwnerPassword = "admin" }, true);
            builder.ArchiveStandard = PdfAStandard.A2B;
            builder.AddPage(PageSize.A4);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Fact]
        public void CustomTokenWritersAreRejected()
        {
            Assert.Throws<NotSupportedException>(() =>
                new PdfDocumentBuilder(new MemoryStream(), new PdfEncryptionOptions(), true, tokenWriter: new TestTokenWriter()));
        }
    }
}
