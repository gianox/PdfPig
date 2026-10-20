namespace UglyToad.PdfPig.Tests.Writer
{
    using PdfPig.Tokens;
    using PdfPig.Writer;

    public class PdfDedupStreamWriterTests
    {
        private static DictionaryToken Dictionary() => new DictionaryToken(new Dictionary<NameToken, IToken>
        {
            { NameToken.Type, NameToken.Create("Same") }
        });

        [Fact]
        public void SameContentsCanBeWrittenToSeveralReservedReferences()
        {
            using var writer = new PdfDedupStreamWriter(new MemoryStream(), false);

            var first = writer.ReserveObjectNumber();
            var second = writer.ReserveObjectNumber();

            writer.WriteToken(Dictionary(), first);
            writer.WriteToken(Dictionary(), second);

            Assert.NotEqual(first.Data, second.Data);
        }

        [Fact]
        public void AnObjectWrittenToAReservedReferenceIsStillTheDeduplicationTarget()
        {
            using var writer = new PdfDedupStreamWriter(new MemoryStream(), false);

            var reserved = writer.ReserveObjectNumber();
            writer.WriteToken(Dictionary(), reserved);

            Assert.Equal(reserved.Data, writer.WriteToken(Dictionary()).Data);
        }
    }
}
