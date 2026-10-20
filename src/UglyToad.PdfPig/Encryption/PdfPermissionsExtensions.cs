namespace UglyToad.PdfPig.Encryption
{
    using Writer;

    internal static class PdfPermissionsExtensions
    {
        // Bits 1-2 must be 0, bits 7-8 and 13-32 are reserved and must be 1 (ISO 32000-2, table 22).
        private const uint ReservedBits = 0xFFFFF0C0;

        /// <summary>
        /// The 32-bit signed value of the <c>/P</c> entry of the encryption dictionary.
        /// </summary>
        public static int ToPValue(this PdfPermissions permissions)
        {
            return unchecked((int)(ReservedBits | (uint)(permissions & PdfPermissions.All)));
        }
    }
}
