namespace UglyToad.PdfPig.Tests.Writer
{
    using PdfPig.Actions;
    using Content;
    using Integration;
    using PdfPig.Outline;
    using PdfPig.Outline.Destinations;
    using PdfPig.Core;
    using PdfPig.Fonts.Standard14Fonts;
    using PdfPig.Writer;
    using Tests.Fonts.TrueType;
    using TestImages;

    /// <summary>
    /// Round trips for the different kinds of content the builder writes, with encryption on.
    /// </summary>
    public class PdfDocumentBuilderEncryptionContentTests
    {
        private const string UserPassword = "open-me";

        private static PdfEncryptionOptions Options() => new PdfEncryptionOptions { UserPassword = UserPassword, OwnerPassword = "admin" };

        private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(bytes, new ParsingOptions { Password = UserPassword, UseLenientParsing = false });

        private static PdfDocumentBuilder NewBuilder(MemoryStream output, PdfWriterType type) => new PdfDocumentBuilder(output, Options(), true, type);

        public static IEnumerable<object[]> WriterTypes() => new[]
        {
            new object[] { PdfWriterType.Default },
            new object[] { PdfWriterType.ObjectInMemoryDedup }
        };

        [Theory]
        [MemberData(nameof(WriterTypes))]
        public void Standard14AndSimpleTrueTypeText(PdfWriterType type)
        {
            var output = new MemoryStream();
            using (var builder = NewBuilder(output, type))
            {
                var page = builder.AddPage(PageSize.A4);
                var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);
                var roboto = builder.AddTrueTypeFont(TrueTypeTestHelper.GetFileBytes("Roboto-Regular.ttf"));
                page.AddText("Helvetica line", 12, new PdfPoint(25, 700), helvetica);
                page.AddText("Roboto line", 12, new PdfPoint(25, 650), roboto);
                builder.Build();
            }

            using var document = Open(output.ToArray());
            var text = document.GetPage(1).Text;
            Assert.Contains("Helvetica line", text);
            Assert.Contains("Roboto line", text);
        }

        [Theory]
        [MemberData(nameof(WriterTypes))]
        public void CompositeTrueTypeText(PdfWriterType type)
        {
            // Use more than 255 distinct characters to make sure the font really is written as a composite font.
            var many = new string(Enumerable.Range(0x100, 300).Select(i => (char)i).ToArray());

            var output = new MemoryStream();
            using (var builder = NewBuilder(output, type))
            {
                var page = builder.AddPage(PageSize.A4);
                var font = builder.AddCompositeTrueTypeFont(TrueTypeTestHelper.GetFileBytes("Roboto-Regular.ttf"));
                page.AddText("Composite text", 12, new PdfPoint(25, 700), font);
                page.AddText(many.Substring(0, 60), 8, new PdfPoint(25, 650), font);
                builder.Build();
            }

            using var document = Open(output.ToArray());
            Assert.Contains("Composite text", document.GetPage(1).Text);
        }

        [Theory]
        [MemberData(nameof(WriterTypes))]
        public void JpegPngAndCcittImages(PdfWriterType type)
        {
            var jpeg = File.ReadAllBytes(IntegrationHelpers.GetDocumentPath("smile-250-by-160.jpg", false));
            var png = File.ReadAllBytes(IntegrationHelpers.GetDocumentPath("pdfpig.png", false));
            var fax = CcittG4TestImage.Load();

            var output = new MemoryStream();
            using (var builder = NewBuilder(output, type))
            {
                var page = builder.AddPage(PageSize.A4);
                page.AddJpeg(jpeg, new PdfRectangle(25, 600, 200, 700));
                page.AddPng(png, new PdfRectangle(25, 450, 200, 550));
                page.AddCcittG4(fax.RawCcittData, fax.Width, fax.Height, new PdfRectangle(25, 250, 200, 400), fax.BlackIs1);
                builder.Build();
            }

            using var document = Open(output.ToArray());
            var images = document.GetPage(1).GetImages().ToList();
            Assert.Equal(3, images.Count);

            Assert.Equal(jpeg, images[0].RawMemory.ToArray());

            Assert.True(images[1].TryGetPng(out var decodedPng));
            Assert.NotEmpty(decodedPng!);

            Assert.Equal(fax.Width, images[2].WidthInSamples);
            Assert.Equal(fax.Height, images[2].HeightInSamples);
            Assert.Equal(1, images[2].BitsPerComponent);
        }

        [Theory]
        [MemberData(nameof(WriterTypes))]
        public void BookmarksWithUnicodeTitles(PdfWriterType type)
        {
            var titles = new[] { "Kapitel æøå", "日本語", "Emoji 😀", "Plain" };

            var output = new MemoryStream();
            using (var builder = NewBuilder(output, type))
            {
                var nodes = new List<BookmarkNode>();
                var font = builder.AddStandard14Font(Standard14Font.Helvetica);
                for (var i = 0; i < titles.Length; i++)
                {
                    builder.AddPage(PageSize.A4).AddText("Page " + (i + 1), 12, new PdfPoint(25, 700), font);
                    nodes.Add(new DocumentBookmarkNode(titles[i], 0,
                        new ExplicitDestination(i + 1, ExplicitDestinationType.FitPage, ExplicitDestinationCoordinates.Empty),
                        Array.Empty<BookmarkNode>()));
                }

                builder.Bookmarks = new Bookmarks(nodes);
                builder.Build();
            }

            using var document = Open(output.ToArray());
            Assert.True(document.TryGetBookmarks(out var bookmarks));
            Assert.Equal(titles, bookmarks.GetNodes().Select(n => n.Title));
            Assert.Equal(new[] { 1, 2, 3, 4 }, bookmarks.GetNodes().OfType<DocumentBookmarkNode>().Select(n => n.PageNumber));
        }

        [Theory]
        [MemberData(nameof(WriterTypes))]
        public void LinkAnnotations(PdfWriterType type)
        {
            var area = new PdfRectangle(25, 690, 200, 720);

            var output = new MemoryStream();
            using (var builder = NewBuilder(output, type))
            {
                builder.AddPage(PageSize.A4).AddLink("https://example.com/æøå", area);
                builder.Build();
            }

            using var document = Open(output.ToArray());
            var annotation = Assert.Single(document.GetPage(1).GetAnnotations());
            Assert.Equal(area, annotation.Rectangle);
            Assert.Equal("https://example.com/æøå", Assert.IsType<UriAction>(annotation.Action).Uri);
        }

        [Theory]
        [MemberData(nameof(WriterTypes))]
        public void DocumentInformationStringsAreEncrypted(PdfWriterType type)
        {
            var output = new MemoryStream();
            using (var builder = NewBuilder(output, type))
            {
                builder.AddPage(PageSize.A4);
                builder.DocumentInformation.Title = "Titel æøå";
                builder.DocumentInformation.Author = "Forfatter";
                builder.DocumentInformation.Subject = "Emne";
                builder.DocumentInformation.Keywords = "nøgleord";
                builder.DocumentInformation.CustomMetadata["Custom"] = "Værdi";
                builder.Build();
            }

            var bytes = output.ToArray();
            var raw = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(bytes);
            Assert.DoesNotContain("Forfatter", raw);
            Assert.DoesNotContain("Emne", raw);

            using var document = Open(bytes);
            var info = document.Information;
            Assert.Equal("Titel æøå", info.Title);
            Assert.Equal("Forfatter", info.Author);
            Assert.Equal("Emne", info.Subject);
            Assert.Equal("nøgleord", info.Keywords);
            Assert.Equal("Værdi", ((PdfPig.Tokens.IDataToken<string>)info.DocumentInformationDictionary!.Data["Custom"]).Data);
        }
    }
}
