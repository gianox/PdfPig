namespace UglyToad.PdfPig.Writer
{
    using System;

    /// <summary>
    /// Operations a viewer should allow on an encrypted document opened with the user password.
    /// Permissions are honoured by well-behaved viewers but are not real protection unless the document also needs a user password.
    /// </summary>
    [Flags]
    public enum PdfPermissions
    {
        /// <summary>
        /// None of the operations are allowed.
        /// </summary>
        None = 0,

        /// <summary>
        /// Print the document, possibly not at the highest quality level (see <see cref="PrintHighQuality"/>).
        /// </summary>
        Print = 1 << 2,

        /// <summary>
        /// Modify the contents of the document by operations other than those controlled by
        /// <see cref="ModifyAnnotations"/>, <see cref="FillForms"/> and <see cref="Assemble"/>.
        /// </summary>
        Modify = 1 << 3,

        /// <summary>
        /// Copy or otherwise extract text and graphics from the document.
        /// </summary>
        CopyContent = 1 << 4,

        /// <summary>
        /// Add or modify text annotations, fill in interactive form fields and, if <see cref="Modify"/> is also set,
        /// create or modify interactive form fields.
        /// </summary>
        ModifyAnnotations = 1 << 5,

        /// <summary>
        /// Fill in existing interactive form fields, even if <see cref="ModifyAnnotations"/> is not set.
        /// </summary>
        FillForms = 1 << 8,

        /// <summary>
        /// Extract text and graphics in support of accessibility to users with disabilities.
        /// </summary>
        ExtractForAccessibility = 1 << 9,

        /// <summary>
        /// Assemble the document (insert, rotate or delete pages and create bookmarks or thumbnails), even if <see cref="Modify"/> is not set.
        /// </summary>
        Assemble = 1 << 10,

        /// <summary>
        /// Print the document to a representation from which a faithful digital copy could be generated.
        /// Without this (and with <see cref="Print"/> set) printing is limited to a low quality representation.
        /// </summary>
        PrintHighQuality = 1 << 11,

        /// <summary>
        /// All of the operations are allowed.
        /// </summary>
        All = Print | Modify | CopyContent | ModifyAnnotations | FillForms | ExtractForAccessibility | Assemble | PrintHighQuality
    }
}
