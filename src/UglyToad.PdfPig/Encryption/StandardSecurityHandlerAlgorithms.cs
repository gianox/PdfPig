#nullable disable

namespace UglyToad.PdfPig.Encryption
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using Exceptions;

    /// <summary>
    /// Key derivation and hashing algorithms of the standard security handler, shared by reading and writing encrypted documents.
    /// </summary>
    internal static class StandardSecurityHandlerAlgorithms
    {
        internal static byte[] ComputeSha256Hash(byte[] input1, byte[] input2, byte[] input3 = null)
        {
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(input1, 0, input1.Length, null, 0);
                sha.TransformBlock(input2, 0, input2.Length, null, 0);

                if (input3 != null)
                {
                    sha.TransformFinalBlock(input3, 0, input3.Length);
                }
                else
                {
                    sha.TransformFinalBlock([], 0, 0);
                }

                return sha.Hash;
            }
        }

        internal static byte[] TruncatePasswordTo127Bytes(byte[] password)
        {
            if (password.Length <= 127)
            {
                return password;
            }

            var result = new byte[127];
            Array.Copy(password, result, 127);
            return result;
        }

        /// <summary>
        /// This is the revision 6 algorithm the specification for which can't be viewed unless you pay the ISO a lot of money (which makes me extremely angry).
        /// This is an attempt to port directly from PDFBox code.
        /// </summary>
        /// <param name="password">The truncated user or owner password.</param>
        /// <param name="salt">The 8 byte user or owner key validation salt.</param>
        /// <param name="vector">Used when hashing owner password in which case it is the 48 byte /U key.</param>
        internal static byte[] ComputeStupidIsoHash(byte[] password, byte[] salt, byte[] vector)
        {
            // There are some details here https://web.archive.org/web/20180311160224/esec-lab.sogeti.com/posts/2011/09/14/the-undocumented-password-validation-algorithm-of-adobe-reader-x.html
            if (vector is null)
            {
                vector = [];
            }
            else if (vector.Length > 0 && vector.Length < 48)
            {
                throw new PdfDocumentEncryptedException($"Vector for revision 6 owner password check (/U) is the wrong length, expected 48 bytes got {vector.Length} bytes.");
            }
            else if (vector.Length > 48)
            {
                var temp = new byte[48];
                Array.Copy(vector, temp, temp.Length);
                vector = temp;
            }

            password = TruncatePasswordTo127Bytes(password);

            byte[] input;
            using (var sha256 = SHA256.Create())
            {
                sha256.TransformBlock(password, 0, password.Length, null, 0);
                sha256.TransformBlock(salt, 0, salt.Length, null, 0);
                sha256.TransformBlock(vector, 0, vector.Length, null, 0);
                sha256.TransformFinalBlock([], 0, 0);
                input = sha256.Hash;
            }

            var key = new byte[16];
            Array.Copy(input, key, 16);

            var iv = new byte[16];
            Array.Copy(input, 16, iv, 0, iv.Length);

            byte[] x = null;
            var i = 0;
            while (i < 64 || i < x[x.Length - 1] + 32)
            {
                var roundResult = new byte[64 * (password.Length + input.Length + vector.Length)];

                var position = 0;
                for (var j = 0; j < 64; j++)
                {
                    Array.Copy(password, 0, roundResult, position, password.Length);
                    position += password.Length;
                    Array.Copy(input, 0, roundResult, position, input.Length);
                    position += input.Length;
                    if (vector.Length > 0)
                    {
                        Array.Copy(vector, 0, roundResult, position, vector.Length);
                        position += vector.Length;
                    }
                }

                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.None;

                    using (var decryptor = aes.CreateEncryptor())
                    {
                        x = decryptor.TransformFinalBlock(roundResult, 0, roundResult.Length);
                    }
                }

                var sumOfFirstSixteenBytesOfX = x.Take(16).Sum(v => (long)v);
                var mod3 = sumOfFirstSixteenBytesOfX % 3;
                HashAlgorithm nextHash = mod3 switch {
                    0 => SHA256.Create(),
                    1 => SHA384.Create(),
                    2 => SHA512.Create(),
                    _ => throw new PdfDocumentEncryptedException("Invalid remainder from summing first sixteen bytes of this round's hash.")
                };
                input = nextHash.ComputeHash(x);
                Array.Copy(input, key, 16);
                Array.Copy(input, 16, iv, 0, 16);

                i++;
            }

            if (input.Length > 32)
            {
                var result = new byte[32];
                Array.Copy(input, result, result.Length);
                return result;
            }

            return input;
        }
    }
}
