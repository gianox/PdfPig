namespace UglyToad.PdfPig.Tests.Writer
{
    using System.Text;
    using PdfPig.Encryption;
    using PdfPig.Exceptions;
    using PdfPig.Tokens;
    using PdfPig.Writer;

    public class PdfStreamWriterTrailerEncryptTests
    {
        private static byte[] WriteDocument(PdfStreamWriter writer, MemoryStream stream)
        {
            var pagesReference = writer.ReserveObjectNumber();
            var page = writer.WriteToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Type, NameToken.Page },
                { NameToken.Parent, pagesReference },
                { NameToken.MediaBox, new ArrayToken(new IToken[] { new NumericToken(0), new NumericToken(0), new NumericToken(100), new NumericToken(100) }) },
            }));
            writer.WriteToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Type, NameToken.Pages },
                { NameToken.Kids, new ArrayToken(new IToken[] { page }) },
                { NameToken.Count, new NumericToken(1) },
            }), pagesReference);
            var catalog = writer.WriteToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Type, NameToken.Catalog },
                { NameToken.Pages, pagesReference },
            }));
            var info = writer.WriteToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Create("Title"), new StringToken("A secret title") },
            }));

            writer.CompletePdf(catalog, info);

            return stream.ToArray();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TrailerReferencesTheEncryptionDictionaryAndTheDocumentOpensWithBothPasswords(bool dedup)
        {
            var handler = StandardSecurityHandlerBuilder.CreateR6("user", "owner", PdfPermissions.Print, true);
            var stream = new MemoryStream();
            using var writer = dedup ? new PdfDedupStreamWriter(stream, false) : new PdfStreamWriter(stream, false);
            writer.EnableEncryption(handler);

            var bytes = WriteDocument(writer, stream);

            var text = Encoding.ASCII.GetString(bytes);
            var trailer = text.Substring(text.IndexOf("trailer", StringComparison.Ordinal));
            Assert.Contains("/Encrypt", trailer);
            Assert.Contains("/ID", trailer);
            Assert.DoesNotContain("A secret title", text);

            foreach (var password in new[] { "user", "owner" })
            {
                using var document = PdfDocument.Open(bytes, new ParsingOptions { Passwords = [password], UseLenientParsing = false });
                Assert.Equal("A secret title", document.Information.Title);
                Assert.Equal(1, document.NumberOfPages);
            }

            Assert.Throws<PdfDocumentEncryptedException>(() => PdfDocument.Open(bytes, new ParsingOptions { Passwords = ["wrong"], UseLenientParsing = false }));
        }

        [Fact]
        public void EncryptionCannotBeEnabledAfterAnObjectWasWritten()
        {
            var stream = new MemoryStream();
            using var writer = new PdfStreamWriter(stream, false);
            writer.WriteToken(NullToken.Instance);

            Assert.Throws<InvalidOperationException>(() =>
                writer.EnableEncryption(StandardSecurityHandlerBuilder.CreateR6("a", "b", PdfPermissions.All, true)));
        }

        [Fact]
        public void EncryptionNeedsATokenWriterWhichCanWriteTheTrailer()
        {
            var stream = new MemoryStream();
            using var writer = new PdfStreamWriter(stream, false, new TestTokenWriter());

            Assert.Throws<NotSupportedException>(() =>
                writer.EnableEncryption(StandardSecurityHandlerBuilder.CreateR6("a", "b", PdfPermissions.All, true)));
        }
    }
}
