namespace UglyToad.PdfPig.Encryption
{
    using System;
    using System.Collections.Generic;
    using Core;
    using Tokens;
    using Writer;

    /// <summary>
    /// Rewrites an object so that all of its strings and streams are encrypted with AES-256 (revision 6) before it is written.
    /// This is a transform of the token tree, so token writers do not need to know about encryption.
    /// </summary>
    internal sealed class ObjectEncryptor
    {
        private readonly byte[] fileKey;
        private readonly bool encryptMetadata;

        public ObjectEncryptor(byte[] fileKey, bool encryptMetadata)
        {
            if (fileKey is null || fileKey.Length != 32)
            {
                throw new ArgumentException("Only AES-256 is supported for encryption, the key must be 32 bytes.", nameof(fileKey));
            }

            this.fileKey = fileKey;
            this.encryptMetadata = encryptMetadata;
        }

        /// <summary>
        /// Returns a copy of the token with strings and streams encrypted. Tokens without strings or streams are returned as they are.
        /// </summary>
        /// <param name="reference">The object the token is written as. Unused for revision 6, where all objects share the file key.</param>
        /// <param name="token">The object contents.</param>
        public IToken Encrypt(IndirectReference reference, IToken token)
        {
            return EncryptToken(token);
        }

        private IToken EncryptToken(IToken token)
        {
            switch (token)
            {
                case StringToken stringToken:
                    return EncryptBytes(stringToken.GetBytes());
                case HexToken hexToken:
                    return EncryptBytes(hexToken.Bytes.ToArray());
                case ArrayToken array:
                    {
                        var items = new List<IToken>(array.Data.Count);
                        foreach (var item in array.Data)
                        {
                            items.Add(EncryptToken(item));
                        }

                        return new ArrayToken(items);
                    }
                case DictionaryToken dictionary:
                    return EncryptDictionary(dictionary);
                case StreamToken stream:
                    return EncryptStream(stream);
                default:
                    return token;
            }
        }

        private DictionaryToken EncryptDictionary(DictionaryToken dictionary)
        {
            var entries = new Dictionary<NameToken, IToken>(dictionary.Data.Count);
            foreach (var pair in dictionary.Data)
            {
                entries[NameToken.Create(pair.Key)] = EncryptToken(pair.Value);
            }

            return new DictionaryToken(entries);
        }

        private IToken EncryptStream(StreamToken stream)
        {
            var dictionary = stream.StreamDictionary;

            if (dictionary.TryGet(NameToken.Type, out NameToken type)
                && (NameToken.Xref.Equals(type) || (NameToken.Metadata.Equals(type) && !encryptMetadata)))
            {
                return stream;
            }

            if (HasCryptFilter(dictionary))
            {
                // The stream chooses how it is encrypted itself.
                return stream;
            }

            var encrypted = AesEncryptionHelper.Encrypt(fileKey, stream.Data.Span);

            var encryptedDictionary = EncryptDictionary(dictionary)
                .With(NameToken.Length, new NumericToken(encrypted.Length));

            return new StreamToken(encryptedDictionary, encrypted);
        }

        private static bool HasCryptFilter(DictionaryToken dictionary)
        {
            if (!dictionary.TryGet(NameToken.Filter, out IToken filter))
            {
                return false;
            }

            if (filter is NameToken name)
            {
                return NameToken.Crypt.Equals(name);
            }

            if (filter is ArrayToken array)
            {
                foreach (var item in array.Data)
                {
                    if (item is NameToken itemName && NameToken.Crypt.Equals(itemName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Hex avoids any questions about escaping binary data in literal strings.
        private HexToken EncryptBytes(byte[] bytes) => DocumentIdentifier.ToToken(AesEncryptionHelper.Encrypt(fileKey, bytes));
    }
}
