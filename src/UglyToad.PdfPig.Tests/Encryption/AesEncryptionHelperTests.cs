namespace UglyToad.PdfPig.Tests.Encryption
{
    using PdfPig.Core;
    using PdfPig.Encryption;

    public class AesEncryptionHelperTests
    {
        [Fact]
        public void CanDecryptDateString()
        {
            var key = new byte[]
            {
                54, 109, 249, 186, 109, 210, 209, 44, 94, 28, 227, 232, 73, 86, 128, 186
            };

            var data = new byte[]
            {
                123, 28, 227, 85, 79, 126, 149, 28, 211, 96, 199, 192, 105, 149, 76, 231,
                8, 136, 51, 141, 139, 44, 0, 230, 228, 116, 12, 145, 132, 157, 5, 123, 235, 247, 232, 244, 36, 217, 73, 147, 157, 124, 27, 143, 255, 79, 220, 194
            };

            var output = AesEncryptionHelper.Decrypt(data, key);

            var actual = OtherEncodings.BytesAsLatin1String(output);

            Assert.Equal("D:20180808103317-07'00'", actual);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(100_000)]
        public void EncryptThenDecryptRoundTrips(int length)
        {
            var key = Enumerable.Range(0, 32).Select(x => (byte)(x * 7)).ToArray();
            var data = Enumerable.Range(0, length).Select(x => (byte)x).ToArray();

            var encrypted = AesEncryptionHelper.Encrypt(key, data);

            // IV + padded cipher text
            Assert.Equal(16 + (length / 16 + 1) * 16, encrypted.Length);
            Assert.Equal(data, AesEncryptionHelper.Decrypt(encrypted, key));
        }

        [Fact]
        public void EncryptUsesRandomIv()
        {
            var key = new byte[32];
            var data = new byte[] { 1, 2, 3 };

            var first = AesEncryptionHelper.Encrypt(key, data);
            var second = AesEncryptionHelper.Encrypt(key, data);

            Assert.NotEqual(first.Take(16), second.Take(16));
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void EncryptRejectsKeysThatAreNotAes256()
        {
            Assert.Throws<ArgumentException>(() => AesEncryptionHelper.Encrypt(new byte[16], new byte[1]));
            Assert.Throws<ArgumentException>(() => AesEncryptionHelper.EncryptNoIv(new byte[24], new byte[16]));
            Assert.Throws<ArgumentException>(() => AesEncryptionHelper.EncryptEcb(new byte[16], new byte[16]));
        }

        [Fact]
        public void EncryptNoIvMatchesZeroIvCbcDecryption()
        {
            var key = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
            var data = Enumerable.Range(0, 32).Select(x => (byte)(255 - x)).ToArray();

            var encrypted = AesEncryptionHelper.EncryptNoIv(key, data);

            Assert.Equal(32, encrypted.Length);
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = key;
            aes.IV = new byte[16];
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;
            using var decryptor = aes.CreateDecryptor();
            Assert.Equal(data, decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length));
        }

        [Fact]
        public void EncryptEcbMatchesEcbDecryption()
        {
            var key = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
            var block = Enumerable.Range(0, 16).Select(x => (byte)(x * 3)).ToArray();

            var encrypted = AesEncryptionHelper.EncryptEcb(key, block);

            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = key;
            aes.Mode = System.Security.Cryptography.CipherMode.ECB;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;
            using var decryptor = aes.CreateDecryptor();
            Assert.Equal(block, decryptor.TransformFinalBlock(encrypted, 0, 16));
            Assert.Throws<ArgumentException>(() => AesEncryptionHelper.EncryptEcb(key, new byte[15]));
        }
    }
}
