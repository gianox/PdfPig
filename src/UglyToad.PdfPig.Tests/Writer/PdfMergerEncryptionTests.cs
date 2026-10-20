namespace UglyToad.PdfPig.Tests.Writer
{
    using System.Text;
    using Integration;
    using PdfPig.Exceptions;
    using PdfPig.Writer;

    public class PdfMergerEncryptionTests
    {
        private static readonly string[] Files =
        {
            "Single Page Simple - from inkscape.pdf",
            "Single Page Simple - from open office.pdf"
        };

        private static List<byte[]> ReadFiles() => Files.Select(f => File.ReadAllBytes(IntegrationHelpers.GetDocumentPath(f))).ToList();

        private static PdfEncryptionOptions Options() => new PdfEncryptionOptions { UserPassword = "open-me", OwnerPassword = "admin" };

        private static void AssertMergedAndEncrypted(byte[] merged)
        {
            Assert.Throws<PdfDocumentEncryptedException>(() => PdfDocument.Open(merged, new ParsingOptions { UseLenientParsing = false }));

            foreach (var password in new[] { "open-me", "admin" })
            {
                using var document = PdfDocument.Open(merged, new ParsingOptions { Password = password, UseLenientParsing = false });
                Assert.True(document.IsEncrypted);
                Assert.Equal(2, document.NumberOfPages);
                Assert.Equal("Write something inInkscape", document.GetPage(1).Text);
                Assert.Equal("I am a simple pdf.", document.GetPage(2).Text);
            }
        }

        [Fact]
        public void MergesBytesIntoAnEncryptedDocument()
        {
            var merged = PdfMerger.Merge(Options(), ReadFiles());

            AssertMergedAndEncrypted(merged);
        }

        [Fact]
        public void MergesStreamsIntoAnEncryptedDocument()
        {
            var streams = ReadFiles().Select(b => (Stream)new MemoryStream(b)).ToList();
            var output = new MemoryStream();

            PdfMerger.Merge(Options(), streams, output);

            AssertMergedAndEncrypted(output.ToArray());
        }

        [Fact]
        public void RespectsPageSelectionAndDocumentInformation()
        {
            var info = new PdfDocumentBuilder.DocumentInformationBuilder { Title = "Merged secret" };

            var merged = PdfMerger.Merge(Options(), ReadFiles(), new[] { new[] { 1 }, new[] { 1 } }, info);

            Assert.DoesNotContain("Merged secret", Encoding.GetEncoding("iso-8859-1").GetString(merged));
            using var document = PdfDocument.Open(merged, new ParsingOptions { Password = "open-me", UseLenientParsing = false });
            Assert.Equal(2, document.NumberOfPages);
            Assert.Equal("Merged secret", document.Information.Title);
        }

        [Fact]
        public void EncryptionOptionsAreRequired()
        {
            Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge(null!, ReadFiles()));
            Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge(null!, new List<Stream>(), new MemoryStream()));
        }

        [Fact]
        public void UnencryptedMergeIsUnchanged()
        {
            var merged = PdfMerger.Merge(ReadFiles());

            using var document = PdfDocument.Open(merged, new ParsingOptions { UseLenientParsing = false });
            Assert.False(document.IsEncrypted);
        }
    }
}
