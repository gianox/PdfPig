namespace UglyToad.PdfPig.Tests.Writer
{
    using Content;
    using Integration;
    using PdfPig.Core;
    using PdfPig.Fonts.Standard14Fonts;
    using PdfPig.Writer;
    using Tests.Fonts.TrueType;

    /// <summary>
    /// Writes sample encrypted documents to open in real viewers. Not part of the regular run:
    /// remove the Skip to generate the files in <see cref="OutputFolder"/>, outside of the repository.
    /// </summary>
    public class EncryptedSampleGenerator
    {
        private const string OutputFolder = @"C:\Users\ffb\source\repos\gianox\encryption-samples";

        private const string UnicodeTitle = "Pr\u00f8ve \u00e6\u00f8\u00e5";

        [Fact(Skip = "Generates sample files for a manual viewer check")]
        public void GenerateSamples()
        {
            Directory.CreateDirectory(OutputFolder);

            File.WriteAllBytes(
                Path.Combine(OutputFolder, "01-user-and-owner-password.pdf"),
                BuildMixedDocument(new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner", Permissions = PdfPermissions.All }));

            File.WriteAllBytes(
                Path.Combine(OutputFolder, "02-no-user-password-no-print-no-copy.pdf"),
                BuildMixedDocument(new PdfEncryptionOptions
                {
                    UserPassword = string.Empty,
                    OwnerPassword = "owner",
                    Permissions = PdfPermissions.All & ~(PdfPermissions.Print | PdfPermissions.PrintHighQuality | PdfPermissions.CopyContent)
                }));

            File.WriteAllBytes(Path.Combine(OutputFolder, "03-copied-pages-encrypted.pdf"), BuildCopiedDocument());

            File.WriteAllText(Path.Combine(OutputFolder, "README.txt"), Readme);
        }

        private static byte[] BuildMixedDocument(PdfEncryptionOptions options)
        {
            using var builder = new PdfDocumentBuilder(new MemoryStream(), options, true);

            var page = builder.AddPage(PageSize.A4);
            var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);
            var roboto = builder.AddTrueTypeFont(TrueTypeTestHelper.GetFileBytes("Roboto-Regular.ttf"));

            page.AddText("Standard 14 text (Helvetica)", 16, new PdfPoint(40, 780), helvetica);
            page.AddText("TrueType text (Roboto) - " + UnicodeTitle, 16, new PdfPoint(40, 740), roboto);

            var jpeg = File.ReadAllBytes(IntegrationHelpers.GetDocumentPath("smile-250-by-160.jpg", false));
            page.AddJpeg(jpeg, new PdfRectangle(40, 540, 290, 700));

            builder.DocumentInformation.Title = UnicodeTitle;
            builder.DocumentInformation.Author = "PdfPig encryption sample";

            return builder.Build();
        }

        private static byte[] BuildCopiedDocument()
        {
            using var source = PdfDocument.Open(IntegrationHelpers.GetDocumentPath("Multiple Page - from Mortality Statistics.pdf"));
            using var builder = new PdfDocumentBuilder(new MemoryStream(), new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner" }, true);

            builder.AddPage(source, 1);
            builder.AddPage(source, 2);
            builder.DocumentInformation.Title = UnicodeTitle;

            return builder.Build();
        }

        private const string Readme = @"Sample AES-256 encrypted PDFs written by PdfPig (fork, branch feature/pdf-encryption).
All files use the revision 6 standard security handler (PDF 1.7 header plus Adobe extension level 8).
Regenerate with the EncryptedSampleGenerator test (remove its Skip first).

01-user-and-owner-password.pdf
  User password:  user
  Owner password: owner
  Permissions:    all
  Content:        Helvetica text, Roboto (TrueType) text, a JPEG smiley, document title ""Prøve æøå"".
  Expected:       Asks for a password on open. ""user"" opens it, ""owner"" opens it (Acrobat: no restrictions
                  either way). A wrong password is rejected. Title shows ""Prøve æøå"" in Document Properties.

02-no-user-password-no-print-no-copy.pdf
  User password:  (none)
  Owner password: owner
  Permissions:    everything except printing (normal and high quality) and copying content
  Content:        same as file 1.
  Expected:       Opens without a password prompt. Printing is disabled, copying text is disabled
                  (Acrobat: Document Properties > Security shows Printing: Not Allowed and Content Copying:
                  Not Allowed; Chrome's print button may still print, Chrome does not enforce print permissions
                  in every case). Opening with ""owner"" in Acrobat lifts the restrictions.

03-copied-pages-encrypted.pdf
  User password:  user
  Owner password: owner
  Permissions:    all
  Content:        pages 1 and 2 copied from the PdfPig test file ""Multiple Page - from Mortality Statistics.pdf"",
                  then encrypted. Title ""Prøve æøå"".
  Expected:       Asks for a password, ""user"" or ""owner"" opens it. Both pages show the original text and layout.
";
    }
}
