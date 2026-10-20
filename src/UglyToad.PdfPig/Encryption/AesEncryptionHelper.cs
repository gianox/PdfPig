namespace UglyToad.PdfPig.Encryption
{
    using System;
    using System.Security.Cryptography;

    internal static class AesEncryptionHelper
    {
        /// <summary>
        /// Encrypts data for a revision 6 stream or string: AES-256-CBC with PKCS#7 padding and a random IV, which is
        /// written in front of the cipher text. With revision 6 the file encryption key is used directly for every object.
        /// </summary>
        public static byte[] Encrypt(byte[] key, ReadOnlySpan<byte> data)
        {
            ValidateKey(key);

            var iv = new byte[16];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(iv);
            }

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var input = data.ToArray();
                using (var encryptor = aes.CreateEncryptor())
                {
                    var cipherText = encryptor.TransformFinalBlock(input, 0, input.Length);
                    var result = new byte[iv.Length + cipherText.Length];
                    Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                    Buffer.BlockCopy(cipherText, 0, result, iv.Length, cipherText.Length);
                    return result;
                }
            }
        }

        /// <summary>
        /// AES-256-CBC with a zero IV and no padding, used for the <c>/UE</c> and <c>/OE</c> entries.
        /// The data length must be a multiple of 16.
        /// </summary>
        public static byte[] EncryptNoIv(byte[] key, byte[] data)
        {
            ValidateKey(key);

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = new byte[16];
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;

                using (var encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        /// <summary>
        /// AES-256-ECB of a single 16 byte block, used for the <c>/Perms</c> entry.
        /// </summary>
        public static byte[] EncryptEcb(byte[] key, byte[] block)
        {
            ValidateKey(key);

            if (block.Length != 16)
            {
                throw new ArgumentException("The block to encrypt in ECB mode must be exactly 16 bytes.", nameof(block));
            }

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (var encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(block, 0, block.Length);
                }
            }
        }

        private static void ValidateKey(byte[] key)
        {
            if (key is null || key.Length != 32)
            {
                throw new ArgumentException("Only AES-256 is supported for encryption, the key must be 32 bytes.", nameof(key));
            }
        }

        public static byte[] Decrypt(byte[] data, byte[] finalKey)
        {
            if (data.Length == 0)
            {
                return data;
            }

            var iv = new byte[16];
            Array.Copy(data, iv, iv.Length);

            using (var aes = Aes.Create())
            {
                aes.Key = finalKey;
                aes.IV = iv;

#if NET8_0_OR_GREATER
                if (data.Length <= iv.Length)
                {
                    aes.Clear();
                    return [];
                }

                var encryptedData = data.AsSpan(iv.Length);
                var output = aes.DecryptCbc(encryptedData, iv, PaddingMode.PKCS7);
                aes.Clear();
                return output;
#else
                if (data.Length <= iv.Length)
                {
                    aes.Clear();
                    return [];
                }

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                {
                    var output = decryptor.TransformFinalBlock(data, iv.Length, data.Length - iv.Length);
                    aes.Clear();
                    return output;
                }
#endif
            }
        }
    }
}
