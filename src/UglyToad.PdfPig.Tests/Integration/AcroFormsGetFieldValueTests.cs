namespace UglyToad.PdfPig.Tests.Integration
{
    using AcroForms;
    using AcroForms.Fields;
    using PdfPig.Tokens;

    public class AcroFormsGetFieldValueTests
    {
        private static string GetFilename()
        {
            return IntegrationHelpers.GetDocumentPath("AcroFormsBasicFields");
        }

        private static string ValueOf(string name)
        {
            using (var document = PdfDocument.Open(GetFilename(), ParsingOptions.LenientParsingOff))
            {
                Assert.True(document.TryGetForm(out var form));
                var field = form.GetFields().Single(f => f.Information.PartialName == name);
                var value = field.GetFieldValue();
                Assert.Equal(name, value.Key);
                return value.Value!;
            }
        }

        [Theory]
        [InlineData("RadioButtonGroup-DefaultValue", "RadioButton01")]
        [InlineData("RadioButtonGroup", "")]
        public void RadioGroupGivesTheSelectedStateName(string name, string expected)
        {
            Assert.Equal(expected, ValueOf(name));
        }

        [Theory]
        [InlineData("CheckboxGroupChecked", "Option3")]
        [InlineData("CheckboxGroup", "")]
        public void CheckboxGroupGivesTheSelectedStateName(string name, string expected)
        {
            Assert.Equal(expected, ValueOf(name));
        }

        [Theory]
        [InlineData("ListBox-DefaultValue", "Opt01")]
        [InlineData("ListBox", "")]
        [InlineData("ComboBox-DefaultValue", "Opt01")]
        [InlineData("ComboBox", "")]
        public void ChoiceFieldsGiveTheSelectedOption(string name, string expected)
        {
            Assert.Equal(expected, ValueOf(name));
        }

        [Fact]
        public void TextFieldAndCheckboxAreUnchanged()
        {
            Assert.Equal("DefaultValue", ValueOf("TextField-DefaultValue"));
            Assert.Equal("True", ValueOf("Checkbox-DefaultValue"));
            Assert.Equal("False", ValueOf("Checkbox"));
        }

        [Fact]
        public void MultipleSelectedOptionsAreJoinedWithAComma()
        {
            var information = new AcroFieldCommonInformation(null, null, null, null);
            var dictionary = new DictionaryToken(new Dictionary<NameToken, IToken>());

            var field = new AcroListBoxField(
                dictionary,
                "Ch",
                AcroChoiceFieldFlags.MultiSelect,
                information,
                [],
                ["One", "Three"],
                null,
                null,
                null,
                null);

            Assert.Equal("One,Three", field.GetFieldValue().Value);
        }
    }
}
