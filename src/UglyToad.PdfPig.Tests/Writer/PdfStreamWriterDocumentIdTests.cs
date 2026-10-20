namespace UglyToad.PdfPig.Tests.Writer
{
    using System.Text;
    using PdfPig.Core;
    using PdfPig.Tokens;
    using PdfPig.Writer;

    public class PdfStreamWriterDocumentIdTests
    {
        private static string WriteMinimalPdf(PdfStreamWriter writer, MemoryStream stream)
        {
            var catalog = writer.WriteToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Type, NameToken.Catalog }
            }));
            writer.CompletePdf(catalog);
            return Encoding.ASCII.GetString(stream.ToArray());
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TrailerContainsTheGivenIdentifier(bool dedup)
        {
            var id = new[] { Enumerable.Range(1, 16).Select(x => (byte)x).ToArray(), Enumerable.Range(101, 16).Select(x => (byte)x).ToArray() };
            var stream = new MemoryStream();

            using var writer = dedup
                ? new PdfDedupStreamWriter(stream, false, documentId: id)
                : new PdfStreamWriter(stream, false, documentId: id);
            var text = WriteMinimalPdf(writer, stream);

            Assert.Same(id, writer.DocumentId);
            var trailer = text.Substring(text.IndexOf("trailer", StringComparison.Ordinal));
            var idPosition = trailer.IndexOf("/ID", StringComparison.Ordinal);
            Assert.True(idPosition >= 0);
            var first = trailer.IndexOf(Hex(id[0]), idPosition, StringComparison.OrdinalIgnoreCase);
            var second = trailer.IndexOf(Hex(id[1]), idPosition, StringComparison.OrdinalIgnoreCase);
            Assert.True(first > idPosition && second > first);
        }

        [Fact]
        public void IdentifierIsCreatedWhenNoneIsGiven()
        {
            var stream = new MemoryStream();

            using var writer = new PdfStreamWriter(stream, false);
            var first = writer.DocumentId;
            var text = WriteMinimalPdf(writer, stream);

            Assert.Equal(2, first.Count);
            Assert.Equal(16, first[0].Length);
            Assert.Contains(Hex(first[0]), text);
            Assert.Same(first, writer.DocumentId);
        }

        [Fact]
        public void IdentifierMustHaveTwoParts()
        {
            Assert.Throws<ArgumentException>(() => new PdfStreamWriter(new MemoryStream(), false, documentId: new[] { new byte[16] }));
        }
    }
}
