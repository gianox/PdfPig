namespace UglyToad.PdfPig.Tests.Writer
{
    using System.Text;
    using Integration;
    using PdfPig.Exceptions;
    using PdfPig.Writer;

    public class PdfDocumentBuilderEncryptedSourceTests
    {
        private const string SourceName = "encrypted-password-is-password.pdf";
        private const string SourcePassword = "password";

        private static PdfDocument OpenSource() => PdfDocument.Open(
            IntegrationHelpers.GetSpecificTestDocumentPath(SourceName),
            new ParsingOptions { Password = SourcePassword });

        private static string[] SourceTexts()
        {
            using var source = OpenSource();
            return source.GetPages().Select(p => p.Text).ToArray();
        }

        private static byte[] CopyAllPages(PdfEncryptionOptions? options)
        {
            using var source = OpenSource();
            using var builder = options is null
                ? new PdfDocumentBuilder(new MemoryStream(), true)
                : new PdfDocumentBuilder(new MemoryStream(), options, true);

            for (var i = 1; i <= source.NumberOfPages; i++)
            {
                builder.AddPage(source, i);
            }

            return builder.Build();
        }

        [Fact]
        public void SourceHasText()
        {
            // Guards the other tests: comparing empty strings would prove nothing.
            Assert.Contains(SourceTexts(), t => !string.IsNullOrWhiteSpace(t));
        }

        [Fact]
        public void CopyingWithoutEncryptionRemovesTheProtection()
        {
            var bytes = CopyAllPages(null);

            var raw = Encoding.GetEncoding("iso-8859-1").GetString(bytes);
            Assert.DoesNotContain("/Encrypt", raw);

            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            Assert.False(document.IsEncrypted);
            Assert.Equal(SourceTexts(), document.GetPages().Select(p => p.Text).ToArray());
        }

        [Fact]
        public void CopyingWithNewEncryptionChangesThePassword()
        {
            var bytes = CopyAllPages(new PdfEncryptionOptions { UserPassword = "new-user", OwnerPassword = "new-owner" });

            // The source's password must no longer open the copy, nor may no password at all.
            Assert.Throws<PdfDocumentEncryptedException>(() => PdfDocument.Open(bytes, new ParsingOptions { Password = SourcePassword, UseLenientParsing = false }));
            Assert.Throws<PdfDocumentEncryptedException>(() => PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false }));

            foreach (var password in new[] { "new-user", "new-owner" })
            {
                using var document = PdfDocument.Open(bytes, new ParsingOptions { Password = password, UseLenientParsing = false });
                Assert.True(document.IsEncrypted);
                Assert.Equal(SourceTexts(), document.GetPages().Select(p => p.Text).ToArray());
            }
        }

        [Fact]
        public void CopyingWithNewEncryptionUsesTheNewHandlerForEveryObject()
        {
            // If a copied stream were left encrypted with the source key it would come back as garbage text.
            var bytes = CopyAllPages(new PdfEncryptionOptions { OwnerPassword = "new-owner" });

            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            Assert.Equal(SourceTexts(), document.GetPages().Select(p => p.Text).ToArray());
        }
    }
}
