namespace UglyToad.PdfPig.Tests.Writer
{
    using System.Text;
    using PdfPig.Encryption;
    using PdfPig.Tokens;
    using PdfPig.Writer;

    public class PdfStreamWriterEncryptionTests
    {
        private const string Marker = "PLAIN-MARKER-TEXT";

        private static ObjectEncryptor CreateEncryptor()
        {
            var result = StandardSecurityHandlerBuilder.CreateR6("user", "owner", PdfPermissions.All, true);
            return new ObjectEncryptor(result.FileKey, true);
        }

        private static StreamToken MarkerStream()
        {
            var data = Encoding.ASCII.GetBytes(Marker + Marker + Marker);
            return new StreamToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Length, new NumericToken(data.Length) }
            }), data);
        }

        private static DictionaryToken MarkerDictionary()
        {
            return new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Create("Title"), new StringToken(Marker) }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ObjectsAreEncryptedWhenAnEncryptorIsSet(bool dedup)
        {
            var stream = new MemoryStream();
            using var writer = dedup ? new PdfDedupStreamWriter(stream, false) : new PdfStreamWriter(stream, false);
            writer.Encryptor = CreateEncryptor();

            writer.WriteToken(MarkerStream());
            writer.WriteToken(MarkerDictionary());
            writer.WriteToken(new StreamToken(new DictionaryToken(new Dictionary<NameToken, IToken>()), Encoding.ASCII.GetBytes(Marker)), writer.ReserveObjectNumber());

            Assert.DoesNotContain("PLAIN-MARKER", Encoding.ASCII.GetString(stream.ToArray()));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ObjectsAreWrittenPlainWithoutAnEncryptor(bool dedup)
        {
            var stream = new MemoryStream();
            using var writer = dedup ? new PdfDedupStreamWriter(stream, false) : new PdfStreamWriter(stream, false);

            writer.WriteToken(MarkerStream());
            writer.WriteToken(MarkerDictionary());

            var text = Encoding.ASCII.GetString(stream.ToArray());
            Assert.Contains(Marker + Marker + Marker, text);
            Assert.Contains(Marker, text);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnencryptedTokensBypassTheEncryptor(bool dedup)
        {
            var stream = new MemoryStream();
            using var writer = dedup ? new PdfDedupStreamWriter(stream, false) : new PdfStreamWriter(stream, false);
            writer.Encryptor = CreateEncryptor();

            writer.WriteUnencryptedToken(MarkerDictionary());
            Assert.Contains(Marker, Encoding.ASCII.GetString(stream.ToArray()));

            // Only the one call is exempt.
            var length = stream.Length;
            writer.WriteToken(MarkerDictionary());
            Assert.DoesNotContain(Marker, Encoding.ASCII.GetString(stream.ToArray(), (int)length, (int)(stream.Length - length)));
        }

        [Fact]
        public void IdenticalObjectsAreStillDeduplicatedWhenEncrypting()
        {
            var stream = new MemoryStream();
            using var writer = new PdfDedupStreamWriter(stream, false);
            writer.Encryptor = CreateEncryptor();

            var first = writer.WriteToken(MarkerStream());
            var second = writer.WriteToken(MarkerStream());
            var other = writer.WriteToken(MarkerDictionary());

            Assert.Equal(first.Data, second.Data);
            Assert.NotEqual(first.Data, other.Data);
        }
    }
}
