namespace UglyToad.PdfPig.Writer
{
    using System;
    using System.Collections.Generic;
    using Tokens;

    /// <summary>
    /// The two byte strings of the trailer <c>/ID</c> entry.
    /// </summary>
    internal static class DocumentIdentifier
    {
        /// <summary>
        /// Creates a new random identifier, both parts are equal for a newly created document.
        /// </summary>
        public static IReadOnlyList<byte[]> Create()
        {
            var id = Guid.NewGuid().ToByteArray();
            return [id, (byte[])id.Clone()];
        }

        public static HexToken ToToken(byte[] bytes)
        {
            return new HexToken(BitConverter.ToString(bytes).Replace("-", string.Empty).AsSpan());
        }
    }
}
