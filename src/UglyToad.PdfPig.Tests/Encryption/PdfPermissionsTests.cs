namespace UglyToad.PdfPig.Tests.Encryption
{
    using PdfPig.Encryption;
    using PdfPig.Writer;

    public class PdfPermissionsTests
    {
        [Fact]
        public void NoneOnlySetsReservedBits()
        {
            var p = PdfPermissions.None.ToPValue();

            Assert.Equal(unchecked((int)0xFFFFF0C0), p);
            Assert.True(p < 0);
        }

        [Fact]
        public void AllSetsEveryBitExceptTheFirstTwo()
        {
            var p = PdfPermissions.All.ToPValue();

            Assert.Equal(-4, p);
        }

        [Theory]
        [InlineData(PdfPermissions.Print, 3)]
        [InlineData(PdfPermissions.Modify, 4)]
        [InlineData(PdfPermissions.CopyContent, 5)]
        [InlineData(PdfPermissions.ModifyAnnotations, 6)]
        [InlineData(PdfPermissions.FillForms, 9)]
        [InlineData(PdfPermissions.ExtractForAccessibility, 10)]
        [InlineData(PdfPermissions.Assemble, 11)]
        [InlineData(PdfPermissions.PrintHighQuality, 12)]
        public void SingleFlagSetsItsBitOnly(PdfPermissions permission, int bitPosition)
        {
            var p = (uint)permission.ToPValue();

            Assert.Equal(0xFFFFF0C0 | (1u << (bitPosition - 1)), p);
            Assert.Equal(0u, p & 0b11);
        }

        [Fact]
        public void UndefinedBitsAreIgnored()
        {
            var p = ((PdfPermissions)(1 << 1 | 1 << 20)).ToPValue();

            Assert.Equal(PdfPermissions.None.ToPValue(), p);
        }
    }
}
