namespace UglyToad.PdfPig.Tests.Encryption
{
    using PdfPig.Core;
    using PdfPig.CrossReference;
    using PdfPig.Encryption;
    using PdfPig.Tokens;
    using PdfPig.Writer;

    public class ObjectEncryptorTests
    {
        private static readonly IndirectReference Reference = new IndirectReference(12, 0);

        private static (ObjectEncryptor Encryptor, EncryptionHandler Handler) Create(bool encryptMetadata)
        {
            var result = StandardSecurityHandlerBuilder.CreateR6("user", "owner", PdfPermissions.All, encryptMetadata);

            var trailer = new TrailerDictionary(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Size, new NumericToken(1) },
                { NameToken.Root, new IndirectReferenceToken(new IndirectReference(1, 0)) },
            }), false);

            var handler = new EncryptionHandler(EncryptionDictionaryFactory.Read(result.EncryptionDictionary, null!), trailer, ["user"]);

            return (new ObjectEncryptor(result.FileKey, encryptMetadata), handler);
        }

        [Fact]
        public void StringsAndStreamsInAnObjectRoundTrip()
        {
            var (encryptor, handler) = Create(true);

            var original = new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Type, NameToken.Create("Thing") },
                { NameToken.Create("Title"), new StringToken("Hello åäö world") },
                { NameToken.Create("Raw"), new HexToken("0001FF".AsSpan()) },
                { NameToken.Create("Number"), new NumericToken(5) },
                { NameToken.Create("Ref"), new IndirectReferenceToken(new IndirectReference(3, 0)) },
                {
                    NameToken.Create("List"), new ArrayToken(new IToken[]
                    {
                        new StringToken("one"),
                        new DictionaryToken(new Dictionary<NameToken, IToken> { { NameToken.Create("Inner"), new StringToken("two") } })
                    })
                },
            });

            var encrypted = (DictionaryToken)encryptor.Encrypt(Reference, original);

            Assert.IsType<HexToken>(encrypted.Data["Title"]);
            Assert.NotEqual("Hello åäö world", ((IDataToken<string>)encrypted.Data["Title"]).Data);
            Assert.Equal("Thing", ((NameToken)encrypted.Data["Type"]).Data);
            Assert.Equal(5, ((NumericToken)encrypted.Data["Number"]).Int);
            Assert.Equal(new IndirectReference(3, 0), ((IndirectReferenceToken)encrypted.Data["Ref"]).Data);

            var decrypted = (DictionaryToken)handler.Decrypt(Reference, encrypted);

            Assert.Equal("Hello åäö world", ((IDataToken<string>)decrypted.Data["Title"]).Data);
            Assert.Equal(new byte[] { 0x00, 0x01, 0xFF }, ((HexToken)decrypted.Data["Raw"]).Bytes.ToArray());
            var list = (ArrayToken)decrypted.Data["List"];
            Assert.Equal("one", ((IDataToken<string>)list.Data[0]).Data);
            Assert.Equal("two", ((IDataToken<string>)((DictionaryToken)list.Data[1]).Data["Inner"]).Data);
        }

        [Fact]
        public void StreamDataAndDictionaryAreEncrypted()
        {
            var (encryptor, handler) = Create(true);

            var data = Enumerable.Range(0, 1000).Select(x => (byte)(x % 251)).ToArray();
            var stream = new StreamToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Length, new NumericToken(data.Length) },
                { NameToken.Create("Note"), new StringToken("in a stream dictionary") }
            }), data);

            var encrypted = (StreamToken)encryptor.Encrypt(Reference, stream);

            Assert.NotEqual(data, encrypted.Data.ToArray());
            Assert.Equal(encrypted.Data.Length, ((NumericToken)encrypted.StreamDictionary.Data["Length"]).Int);
            Assert.Equal(16 + ((data.Length / 16) + 1) * 16, encrypted.Data.Length);

            var decrypted = (StreamToken)handler.Decrypt(Reference, encrypted);

            Assert.Equal(data, decrypted.Data.ToArray());
            Assert.Equal("in a stream dictionary", ((IDataToken<string>)decrypted.StreamDictionary.Data["Note"]).Data);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MetadataIsOnlyEncryptedWhenRequested(bool encryptMetadata)
        {
            var (encryptor, _) = Create(encryptMetadata);
            var xml = OtherEncodings.StringAsLatin1Bytes("<x:xmpmeta>metadata</x:xmpmeta>");
            var stream = new StreamToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Type, NameToken.Metadata },
                { NameToken.Subtype, NameToken.Create("XML") }
            }), xml);

            var result = (StreamToken)encryptor.Encrypt(Reference, stream);

            Assert.Equal(!encryptMetadata, xml.AsSpan().SequenceEqual(result.Data.Span));
        }

        [Fact]
        public void StreamsWithTheirOwnCryptFilterAreLeftAlone()
        {
            var (encryptor, _) = Create(true);
            var data = new byte[] { 1, 2, 3 };
            var stream = new StreamToken(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Filter, new ArrayToken(new IToken[] { NameToken.Crypt, NameToken.FlateDecode }) }
            }), data);

            var result = (StreamToken)encryptor.Encrypt(Reference, stream);

            Assert.Same(stream, result);
        }

        [Fact]
        public void KeyMustBeAes256()
        {
            Assert.Throws<ArgumentException>(() => new ObjectEncryptor(new byte[16], true));
        }
    }
}
