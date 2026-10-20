namespace UglyToad.PdfPig.Encryption
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
    using Tokens;
    using Writer;

    /// <summary>
    /// The result of creating a standard security handler: the <c>/Encrypt</c> dictionary to write and the
    /// file encryption key to encrypt strings and streams with.
    /// </summary>
    internal sealed class StandardSecurityHandlerResult
    {
        public StandardSecurityHandlerResult(byte[] fileKey, DictionaryToken encryptionDictionary)
        {
            FileKey = fileKey;
            EncryptionDictionary = encryptionDictionary;
        }

        /// <summary>
        /// The 32 byte file encryption key, used directly for every object with revision 6.
        /// </summary>
        public byte[] FileKey { get; }

        /// <summary>
        /// The <c>/Encrypt</c> dictionary, which must be written without being encrypted itself.
        /// </summary>
        public DictionaryToken EncryptionDictionary { get; }
    }

    /// <summary>
    /// Creates the encryption dictionary of the standard security handler.
    /// Only AES-256 (revision 6, ISO 32000-2) is supported.
    /// </summary>
    internal static class StandardSecurityHandlerBuilder
    {
        /// <summary>
        /// Creates an AES-256 revision 6 encryption dictionary (algorithms 8, 9 and 10 of ISO 32000-2) with a new random file key.
        /// </summary>
        /// <param name="userPassword">The password needed to open the document, an empty password lets anyone open it.</param>
        /// <param name="ownerPassword">The password which gives full access regardless of the permissions.</param>
        /// <param name="permissions">The operations allowed for users who opened the document with the user password.</param>
        /// <param name="encryptMetadata">Whether the XMP metadata stream is encrypted too.</param>
        public static StandardSecurityHandlerResult CreateR6(string userPassword, string ownerPassword, PdfPermissions permissions, bool encryptMetadata)
        {
            var user = PreparePassword(userPassword);
            var owner = PreparePassword(ownerPassword);

            using (var random = RandomNumberGenerator.Create())
            {
                var fileKey = Random(random, 32);

                // Algorithm 8: the user password entries.
                var userValidationSalt = Random(random, 8);
                var userKeySalt = Random(random, 8);

                var userHash = StandardSecurityHandlerAlgorithms.ComputeStupidIsoHash(user, userValidationSalt, null);
                var u = Concat(userHash, userValidationSalt, userKeySalt);

                var userIntermediateKey = StandardSecurityHandlerAlgorithms.ComputeStupidIsoHash(user, userKeySalt, null);
                var ue = AesEncryptionHelper.EncryptNoIv(userIntermediateKey, fileKey);

                // Algorithm 9: the owner password entries, which also depend on the complete /U.
                var ownerValidationSalt = Random(random, 8);
                var ownerKeySalt = Random(random, 8);

                var ownerHash = StandardSecurityHandlerAlgorithms.ComputeStupidIsoHash(owner, ownerValidationSalt, u);
                var o = Concat(ownerHash, ownerValidationSalt, ownerKeySalt);

                var ownerIntermediateKey = StandardSecurityHandlerAlgorithms.ComputeStupidIsoHash(owner, ownerKeySalt, u);
                var oe = AesEncryptionHelper.EncryptNoIv(ownerIntermediateKey, fileKey);

                // Algorithm 10: /Perms.
                var p = permissions.ToPValue();
                var perms = new byte[16];
                perms[0] = (byte)p;
                perms[1] = (byte)(p >> 8);
                perms[2] = (byte)(p >> 16);
                perms[3] = (byte)(p >> 24);
                perms[4] = perms[5] = perms[6] = perms[7] = 0xFF;
                perms[8] = (byte)(encryptMetadata ? 'T' : 'F');
                perms[9] = (byte)'a';
                perms[10] = (byte)'d';
                perms[11] = (byte)'b';
                Buffer.BlockCopy(Random(random, 4), 0, perms, 12, 4);

                var encryptedPerms = AesEncryptionHelper.EncryptEcb(fileKey, perms);

                var cryptFilter = new DictionaryToken(new Dictionary<NameToken, IToken>
                {
                    { NameToken.Type, NameToken.Create("CryptFilter") },
                    { NameToken.Create("CFM"), NameToken.Create("AESV3") },
                    { NameToken.Create("AuthEvent"), NameToken.Create("DocOpen") },
                    { NameToken.Length, new NumericToken(32) }
                });

                var dictionary = new DictionaryToken(new Dictionary<NameToken, IToken>
                {
                    { NameToken.Filter, NameToken.Create("Standard") },
                    { NameToken.V, new NumericToken(5) },
                    { NameToken.R, new NumericToken(6) },
                    { NameToken.Length, new NumericToken(256) },
                    {
                        NameToken.Cf, new DictionaryToken(new Dictionary<NameToken, IToken>
                        {
                            { NameToken.Create("StdCF"), cryptFilter }
                        })
                    },
                    { NameToken.StmF, NameToken.Create("StdCF") },
                    { NameToken.StrF, NameToken.Create("StdCF") },
                    { NameToken.O, ToHex(o) },
                    { NameToken.U, ToHex(u) },
                    { NameToken.Oe, ToHex(oe) },
                    { NameToken.Ue, ToHex(ue) },
                    { NameToken.Create("Perms"), ToHex(encryptedPerms) },
                    { NameToken.P, new NumericToken(p) },
                    { NameToken.EncryptMetaData, encryptMetadata ? BooleanToken.True : BooleanToken.False }
                });

                return new StandardSecurityHandlerResult(fileKey, dictionary);
            }
        }

        /// <summary>
        /// UTF-8 bytes of the password, truncated to 127 bytes.
        /// </summary>
        /// <remarks>
        /// The specification asks for SASLprep (RFC 4013). Unicode normalization form KC covers its normalization step and so the
        /// common cases (e.g. composed versus decomposed accents), but the mapping of non-ASCII spaces and of characters commonly
        /// mapped to nothing, the prohibited characters and the bidirectional checks are not implemented.
        /// </remarks>
        internal static byte[] PreparePassword(string? password)
        {
            var normalized = (password ?? string.Empty).Normalize(NormalizationForm.FormKC);

            return StandardSecurityHandlerAlgorithms.TruncatePasswordTo127Bytes(Encoding.UTF8.GetBytes(normalized));
        }

        private static byte[] Random(RandomNumberGenerator random, int length)
        {
            var bytes = new byte[length];
            random.GetBytes(bytes);
            return bytes;
        }

        private static byte[] Concat(byte[] first, byte[] second, byte[] third)
        {
            var result = new byte[first.Length + second.Length + third.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            Buffer.BlockCopy(third, 0, result, first.Length + second.Length, third.Length);
            return result;
        }

        private static HexToken ToHex(byte[] bytes)
        {
            return new HexToken(BitConverter.ToString(bytes).Replace("-", string.Empty).AsSpan());
        }
    }
}
