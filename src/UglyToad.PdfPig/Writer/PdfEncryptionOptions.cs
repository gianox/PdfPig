namespace UglyToad.PdfPig.Writer
{
    /// <summary>
    /// Options for writing a password protected document with <see cref="PdfDocumentBuilder"/>.
    /// The document is always encrypted with AES-256 (the revision 6 standard security handler of ISO 32000-2), which is
    /// supported by Acrobat X and later and by all current viewers. Weaker algorithms are not available.
    /// </summary>
    public sealed class PdfEncryptionOptions
    {
        /// <summary>
        /// The password needed to open the document. An empty password (the default) lets anyone open the document,
        /// while the <see cref="Permissions"/> still apply to viewers that honour them.
        /// </summary>
        public string UserPassword { get; set; } = string.Empty;

        /// <summary>
        /// The password which gives full access regardless of the <see cref="Permissions"/>, for example to change them.
        /// When empty (the default) a random password is generated, which nobody knows, so the permissions can't be lifted
        /// by supplying a password.
        /// </summary>
        public string OwnerPassword { get; set; } = string.Empty;

        /// <summary>
        /// The operations allowed for users who opened the document with the <see cref="UserPassword"/>. Defaults to <see cref="PdfPermissions.All"/>.
        /// Permissions are honoured by well-behaved viewers but are not real protection unless a user password is also required.
        /// </summary>
        public PdfPermissions Permissions { get; set; } = PdfPermissions.All;

        /// <summary>
        /// Whether the XMP metadata stream is encrypted too. Defaults to <see langword="true"/>. When <see langword="false"/>
        /// search tools which only read the metadata can still see it.
        /// </summary>
        public bool EncryptMetadata { get; set; } = true;
    }
}
