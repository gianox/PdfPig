namespace UglyToad.PdfPig.Tests.Writer
{
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;
    using Content;
    using PdfPig.Core;
    using PdfPig.Encryption;
    using PdfPig.Fonts.Standard14Fonts;
    using PdfPig.Writer;

    /// <summary>
    /// The common "can't print or copy" document: opens without a password, but the permissions are recorded.
    /// </summary>
    public class PdfDocumentBuilderEncryptionPermissionsTests
    {
        private static byte[] Build(PdfEncryptionOptions options, PdfWriterType type)
        {
            var output = new MemoryStream();
            using (var builder = new PdfDocumentBuilder(output, options, true, type))
            {
                var page = builder.AddPage(PageSize.A4);
                var font = builder.AddStandard14Font(Standard14Font.Helvetica);
                page.AddText("Restricted but readable", 12, new PdfPoint(25, 700), font);
                builder.Build();
            }

            return output.ToArray();
        }

        // The /Encrypt dictionary itself is never encrypted so /P can be read from the raw bytes.
        private static UserAccessPermissions ReadPermissions(byte[] bytes)
        {
            var raw = Encoding.GetEncoding("iso-8859-1").GetString(bytes);
            var match = Regex.Match(raw, @"/P (-?\d+)");
            Assert.True(match.Success);

            return (UserAccessPermissions)(uint)int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        [Theory]
        [InlineData(PdfWriterType.Default)]
        [InlineData(PdfWriterType.ObjectInMemoryDedup)]
        public void OpensWithoutPasswordAndRecordsTheRestrictions(PdfWriterType type)
        {
            var allowed = PdfPermissions.All & ~(PdfPermissions.Print | PdfPermissions.PrintHighQuality | PdfPermissions.CopyContent);
            var bytes = Build(new PdfEncryptionOptions { OwnerPassword = "owner", Permissions = allowed }, type);

            using (var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false }))
            {
                Assert.True(document.IsEncrypted);
                Assert.Contains("Restricted but readable", document.GetPage(1).Text);
            }

            var permissions = ReadPermissions(bytes);
            Assert.False(permissions.HasFlag(UserAccessPermissions.Print));
            Assert.False(permissions.HasFlag(UserAccessPermissions.PrintHighQuality));
            Assert.False(permissions.HasFlag(UserAccessPermissions.CopyTextAndGraphics));
            Assert.True(permissions.HasFlag(UserAccessPermissions.Modify));
            Assert.True(permissions.HasFlag(UserAccessPermissions.AddOrModifyTextAnnotationsAndFillFormFields));
            Assert.True(permissions.HasFlag(UserAccessPermissions.FillExistingFormFields));
            Assert.True(permissions.HasFlag(UserAccessPermissions.ExtractTextAndGraphics));
            Assert.True(permissions.HasFlag(UserAccessPermissions.AssembleDocument));
        }

        [Fact]
        public void NoPermissionsRecordsNoFlags()
        {
            var bytes = Build(new PdfEncryptionOptions { OwnerPassword = "owner", Permissions = PdfPermissions.None }, PdfWriterType.Default);

            var permissions = ReadPermissions(bytes);
            Assert.False(permissions.HasFlag(UserAccessPermissions.Print));
            Assert.False(permissions.HasFlag(UserAccessPermissions.Modify));
            Assert.False(permissions.HasFlag(UserAccessPermissions.CopyTextAndGraphics));
            Assert.False(permissions.HasFlag(UserAccessPermissions.AddOrModifyTextAnnotationsAndFillFormFields));
            Assert.False(permissions.HasFlag(UserAccessPermissions.FillExistingFormFields));
            Assert.False(permissions.HasFlag(UserAccessPermissions.ExtractTextAndGraphics));
            Assert.False(permissions.HasFlag(UserAccessPermissions.AssembleDocument));
            Assert.False(permissions.HasFlag(UserAccessPermissions.PrintHighQuality));

            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            Assert.Contains("Restricted but readable", document.GetPage(1).Text);
        }
    }
}
