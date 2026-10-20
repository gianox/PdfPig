namespace UglyToad.PdfPig.Writer
{
    using System.Collections.Generic;
    using System.IO;
    using Core;

    /// <summary>
    /// Implemented by token writers which can write a trailer with a document identifier that was fixed before the first
    /// object was written. Separate from <see cref="ITokenWriter"/> so that custom implementations of that interface keep working,
    /// they get a random identifier generated when the trailer is written.
    /// </summary>
    internal interface ITrailerTokenWriter
    {
        /// <summary>
        /// Writes the cross-reference table and trailer, see <see cref="ITokenWriter.WriteCrossReferenceTable"/>.
        /// </summary>
        /// <param name="objectOffsets">The byte offset from the start of the document for each object in the document.</param>
        /// <param name="catalogToken">The object representing the catalog dictionary.</param>
        /// <param name="outputStream">The output stream to write to.</param>
        /// <param name="documentInformationReference">The document information dictionary if present.</param>
        /// <param name="prevTableLocation">The offset to the previous xref table if present.</param>
        /// <param name="documentId">The two parts of the trailer <c>/ID</c>.</param>
        /// <param name="encryptionReference">The encryption dictionary if the document is encrypted.</param>
        void WriteCrossReferenceTable(
            IReadOnlyDictionary<IndirectReference, long> objectOffsets,
            IndirectReference catalogToken,
            Stream outputStream,
            IndirectReference? documentInformationReference,
            long? prevTableLocation,
            IReadOnlyList<byte[]> documentId,
            IndirectReference? encryptionReference);
    }
}
