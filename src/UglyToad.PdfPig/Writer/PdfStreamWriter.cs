namespace UglyToad.PdfPig.Writer
{
    using Core;
    using Encryption;
    using Graphics.Operations;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Tokens;

    /// <summary>
    /// This class would lazily flush all token. Allowing us to make changes to references without need to rewrite the whole stream
    /// </summary>
    internal class PdfStreamWriter : IPdfStreamWriter
    {
        private readonly Action<double>? recordVersion;
        protected const double DefaultVersion = 1.2;
        protected Dictionary<IndirectReference, long> offsets = new Dictionary<IndirectReference, long>();
        protected bool DisposeStream { get; set; }
        protected bool Initialized { get; set; }
        protected int CurrentNumber { get; set; } = 1;
        protected readonly ITokenWriter TokenWriter;

        /// <summary>
        /// The two parts of the trailer <c>/ID</c>, fixed when the writer is created.
        /// </summary>
        public IReadOnlyList<byte[]> DocumentId { get; }

        public Stream Stream { get; protected set; }

        /// <summary>
        /// When set every object written has its strings and streams encrypted, except through <see cref="WriteUnencryptedToken"/>.
        /// </summary>
        internal ObjectEncryptor? Encryptor { get; set; }

        private bool bypassEncryption;

        private IndirectReferenceToken? encryptionReference;

        public bool AttemptDeduplication { get; set; } = true;

        public bool WritingPageContents
        {
            get => TokenWriter.WritingPageContents;
            set => TokenWriter.WritingPageContents = value;
        }

        internal PdfStreamWriter(
            Stream baseStream,
            bool disposeStream = true,
            ITokenWriter? tokenWriter = null,
            Action<double>? recordVersion = null,
            IReadOnlyList<byte[]>? documentId = null)
        {
            Stream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));

            if (!baseStream.CanWrite)
            {
                throw new ArgumentException("Output stream must be writable");
            }

            this.recordVersion = recordVersion;
            DisposeStream = disposeStream;
            TokenWriter = tokenWriter ?? new TokenWriter();

            if (documentId != null && documentId.Count != 2)
            {
                throw new ArgumentException("The document identifier must consist of exactly two parts.", nameof(documentId));
            }

            DocumentId = documentId ?? DocumentIdentifier.Create();
        }

        public virtual IndirectReferenceToken WriteToken(IToken token)
        {
            if (!Initialized)
            {
                InitializePdf(DefaultVersion);
            }

            var ir = ReserveObjectNumber();
            offsets.Add(ir.Data, Stream.Position);
            var obj = new ObjectToken(XrefLocation.File(Stream.Position), ir.Data, EncryptIfEnabled(ir.Data, token));
            TokenWriter.WriteToken(obj, Stream);
            return ir;
        }

        public virtual IndirectReferenceToken WriteToken(IToken token, IndirectReferenceToken indirectReference)
        {
            if (!Initialized)
            {
                InitializePdf(DefaultVersion);
            }

            offsets.Add(indirectReference.Data, Stream.Position);
            var obj = new ObjectToken(XrefLocation.File(Stream.Position), indirectReference.Data, EncryptIfEnabled(indirectReference.Data, token));
            TokenWriter.WriteToken(obj, Stream);
            return indirectReference;
        }

        /// <summary>
        /// Turns on encryption for everything written from now on. Must be called before the first object is written.
        /// The encryption dictionary is written without being encrypted and referenced from the trailer.
        /// </summary>
        internal void EnableEncryption(StandardSecurityHandlerResult handler)
        {
            if (offsets.Count > 0)
            {
                throw new InvalidOperationException("Encryption must be configured before any object is written.");
            }

            if (TokenWriter is not ITrailerTokenWriter)
            {
                throw new NotSupportedException("The token writer in use cannot write the /Encrypt entry of the trailer, encryption is only supported with the default token writer.");
            }

            encryptionReference = WriteUnencryptedToken(handler.EncryptionDictionary);
            Encryptor = new ObjectEncryptor(handler.FileKey, handler.EncryptMetadata);
        }

        /// <summary>
        /// Writes a token without encrypting it, used for the encryption dictionary itself.
        /// </summary>
        internal IndirectReferenceToken WriteUnencryptedToken(IToken token)
        {
            bypassEncryption = true;
            try
            {
                return WriteToken(token);
            }
            finally
            {
                bypassEncryption = false;
            }
        }

        protected bool IsEncrypting => Encryptor != null && !bypassEncryption;

        protected IToken EncryptIfEnabled(IndirectReference reference, IToken token)
        {
            return IsEncrypting ? Encryptor!.Encrypt(reference, token) : token;
        }

        public IndirectReferenceToken ReserveObjectNumber()
        {
            return new IndirectReferenceToken(new IndirectReference(CurrentNumber++, 0));
        }

        public void InitializePdf(double version)
        {
            recordVersion?.Invoke(version);

            Stream.WriteText($"%PDF-{version.ToString("0.0", CultureInfo.InvariantCulture)}");
            Stream.WriteNewLine();
            Stream.WriteText("%"u8);
            Stream.Write([169, 205, 196, 210]);
            Stream.WriteNewLine();
            Initialized = true;
        }

        public void CompletePdf(IndirectReferenceToken catalogReference, IndirectReferenceToken? documentInformationReference = null)
        {
            if (TokenWriter is ITrailerTokenWriter trailerWriter)
            {
                trailerWriter.WriteCrossReferenceTable(offsets, catalogReference.Data, Stream, documentInformationReference?.Data, null, DocumentId, encryptionReference?.Data);
            }
            else
            {
                TokenWriter.WriteCrossReferenceTable(offsets, catalogReference.Data, Stream, documentInformationReference?.Data, null);
            }
        }

        public void Dispose()
        {
            if (DisposeStream)
            {
                Stream?.Dispose();
            }
            
            Stream = null!;
        }
    }
}
