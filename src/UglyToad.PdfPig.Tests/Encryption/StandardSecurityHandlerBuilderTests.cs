namespace UglyToad.PdfPig.Tests.Encryption
{
    using System.Security.Cryptography;
    using PdfPig.Core;
    using PdfPig.CrossReference;
    using PdfPig.Encryption;
    using PdfPig.Exceptions;
    using PdfPig.Tokens;
    using PdfPig.Writer;

    public class StandardSecurityHandlerBuilderTests
    {
        private static EncryptionHandler OpenWith(DictionaryToken encrypt, string password)
        {
            var trailer = new TrailerDictionary(new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                { NameToken.Size, new NumericToken(1) },
                { NameToken.Root, new IndirectReferenceToken(new IndirectReference(1, 0)) },
            }), false);

            var dictionary = EncryptionDictionaryFactory.Read(encrypt, null!);

            return new EncryptionHandler(dictionary, trailer, [password]);
        }

        // Encrypts a string with the file key and checks the handler opened with a password decrypts it, so the
        // handler must have derived the same file key.
        private static void AssertHandlerUsesKey(EncryptionHandler handler, byte[] fileKey)
        {
            var reference = new IndirectReference(7, 0);
            var encrypted = AesEncryptionHelper.Encrypt(fileKey, OtherEncodings.StringAsLatin1Bytes("secret text"));
            var hex = new HexToken(BitConverter.ToString(encrypted).Replace("-", string.Empty).AsSpan());

            var decrypted = handler.Decrypt(reference, hex);

            Assert.Equal("secret text", Assert.IsAssignableFrom<IDataToken<string>>(decrypted).Data);
        }

        [Fact]
        public void BothPasswordsAreAcceptedByTheReadSide()
        {
            var result = StandardSecurityHandlerBuilder.CreateR6("user pw", "owner pw", PdfPermissions.Print | PdfPermissions.FillForms, true);

            AssertHandlerUsesKey(OpenWith(result.EncryptionDictionary, "user pw"), result.FileKey);
            AssertHandlerUsesKey(OpenWith(result.EncryptionDictionary, "owner pw"), result.FileKey);
        }

        [Fact]
        public void WrongPasswordIsRejected()
        {
            var result = StandardSecurityHandlerBuilder.CreateR6("user pw", "owner pw", PdfPermissions.All, true);

            Assert.Throws<PdfDocumentEncryptedException>(() => OpenWith(result.EncryptionDictionary, "wrong"));
            Assert.Throws<PdfDocumentEncryptedException>(() => OpenWith(result.EncryptionDictionary, ""));
        }

        [Fact]
        public void EmptyUserPasswordOpensWithoutAPassword()
        {
            var result = StandardSecurityHandlerBuilder.CreateR6("", "owner pw", PdfPermissions.Print, true);

            AssertHandlerUsesKey(OpenWith(result.EncryptionDictionary, ""), result.FileKey);
            AssertHandlerUsesKey(OpenWith(result.EncryptionDictionary, "owner pw"), result.FileKey);
        }

        [Fact]
        public void NonAsciiAndOverlongPasswordsWork()
        {
            // Passwords are normalized (NFKC) before use, so both spellings produce the same key material.
            var composed = "påsswørd-é";
            var decomposed = "påsswørd-é";
            var longPassword = new string('x', 200);

            var result = StandardSecurityHandlerBuilder.CreateR6(composed, longPassword, PdfPermissions.None, true);

            Assert.Equal(StandardSecurityHandlerBuilder.PreparePassword(composed), StandardSecurityHandlerBuilder.PreparePassword(decomposed));
            // The read side normalizes too, so either spelling opens the document.
            AssertHandlerUsesKey(OpenWith(result.EncryptionDictionary, composed), result.FileKey);
            AssertHandlerUsesKey(OpenWith(result.EncryptionDictionary, decomposed), result.FileKey);
            // Only the first 127 bytes count.
            AssertHandlerUsesKey(OpenWith(result.EncryptionDictionary, new string('x', 127) + "ignored"), result.FileKey);
        }

        [Fact]
        public void DictionaryHasTheExpectedEntries()
        {
            var result = StandardSecurityHandlerBuilder.CreateR6("a", "b", PdfPermissions.None, false);
            var dictionary = result.EncryptionDictionary;

            Assert.Equal(32, result.FileKey.Length);
            Assert.Equal("Standard", ((NameToken)dictionary.Data["Filter"]).Data);
            Assert.Equal(5, ((NumericToken)dictionary.Data["V"]).Int);
            Assert.Equal(6, ((NumericToken)dictionary.Data["R"]).Int);
            Assert.Equal(256, ((NumericToken)dictionary.Data["Length"]).Int);
            Assert.Equal("StdCF", ((NameToken)dictionary.Data["StmF"]).Data);
            Assert.Equal("StdCF", ((NameToken)dictionary.Data["StrF"]).Data);
            Assert.False(((BooleanToken)dictionary.Data["EncryptMetadata"]).Data);

            var filter = (DictionaryToken)((DictionaryToken)dictionary.Data["CF"]).Data["StdCF"];
            Assert.Equal("AESV3", ((NameToken)filter.Data["CFM"]).Data);
            Assert.Equal("DocOpen", ((NameToken)filter.Data["AuthEvent"]).Data);
            Assert.Equal(32, ((NumericToken)filter.Data["Length"]).Int);

            Assert.Equal(48, ((HexToken)dictionary.Data["U"]).Bytes.Length);
            Assert.Equal(48, ((HexToken)dictionary.Data["O"]).Bytes.Length);
            Assert.Equal(32, ((HexToken)dictionary.Data["UE"]).Bytes.Length);
            Assert.Equal(32, ((HexToken)dictionary.Data["OE"]).Bytes.Length);
            Assert.Equal(16, ((HexToken)dictionary.Data["Perms"]).Bytes.Length);
            Assert.Equal(PdfPermissions.None.ToPValue(), ((NumericToken)dictionary.Data["P"]).Int);
        }

        [Theory]
        [InlineData(true, 'T')]
        [InlineData(false, 'F')]
        public void PermsDecryptsToThePermissions(bool encryptMetadata, char flag)
        {
            var permissions = PdfPermissions.Print | PdfPermissions.CopyContent;
            var result = StandardSecurityHandlerBuilder.CreateR6("a", "b", permissions, encryptMetadata);

            var encrypted = ((HexToken)result.EncryptionDictionary.Data["Perms"]).Bytes.ToArray();
            using var aes = Aes.Create();
            aes.Key = result.FileKey;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using var decryptor = aes.CreateDecryptor();
            var perms = decryptor.TransformFinalBlock(encrypted, 0, 16);

            Assert.Equal(permissions.ToPValue(), BitConverter.ToInt32(perms, 0));
            Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, perms.Skip(4).Take(4));
            Assert.Equal((byte)flag, perms[8]);
            Assert.Equal("adb", OtherEncodings.BytesAsLatin1String(perms.Skip(9).Take(3).ToArray()));
        }

        [Fact]
        public void EveryCallUsesAFreshFileKey()
        {
            var first = StandardSecurityHandlerBuilder.CreateR6("a", "b", PdfPermissions.All, true);
            var second = StandardSecurityHandlerBuilder.CreateR6("a", "b", PdfPermissions.All, true);

            Assert.NotEqual(first.FileKey, second.FileKey);
        }
    }
}
