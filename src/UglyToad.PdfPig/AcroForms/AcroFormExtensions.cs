namespace UglyToad.PdfPig.AcroForms
{
    using System.Collections.Generic;
    using System.Linq;
    using UglyToad.PdfPig.AcroForms.Fields;
    using UglyToad.PdfPig.Tokens;

    /// <summary>
    /// Extensions for AcroForm.
    /// </summary>
    public static class AcroFormExtensions
    {
        /// <summary>
        /// Get fields containing data in form. 
        /// </summary>
        public static IEnumerable<AcroFieldBase> GetFields(this AcroForm form)
        {
            return form.Fields.SelectMany(f => f.GetFields());
        }

        /// <summary>
        /// Get fields containing data which are children of field. 
        /// </summary>
        public static IEnumerable<AcroFieldBase> GetFields(this AcroFieldBase fieldBase)
        {
            if (fieldBase.FieldType != AcroFieldType.Unknown)
            {
                yield return fieldBase;
            }

            if (fieldBase is AcroNonTerminalField nonTerminalField)
            {
                foreach (var child in nonTerminalField.Children)
                {
                    foreach (var item in child.GetFields())
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>
        /// Get string values of field. The value is:
        /// <list type="bullet">
        /// <item><description>for a text field, its text;</description></item>
        /// <item><description>for a single checkbox, <c>True</c> or <c>False</c>;</description></item>
        /// <item><description>for a group of radio buttons or of checkboxes, the name of the selected state without the
        /// leading slash (for example <c>RadioButton01</c>), or an empty string when none is selected;</description></item>
        /// <item><description>for a list box or a combo box, the selected option. When several options are selected
        /// (a multi-select list box) they are joined with a comma, so an option which itself contains a comma can't
        /// be told apart, use <see cref="AcroListBoxField.SelectedOptions"/> for that. An empty string means nothing is selected;</description></item>
        /// <item><description>an empty string for any other field.</description></item>
        /// </list>
        /// </summary>
        public static KeyValuePair<string?, string?> GetFieldValue(this AcroFieldBase fieldBase)
        {
            return fieldBase switch
            {
                AcroTextField textField => new(textField.Information.PartialName, textField.Value),
                AcroCheckboxField checkboxField => new(checkboxField.Information.PartialName, checkboxField.IsChecked.ToString())!,
                AcroRadioButtonsField or AcroCheckboxesField => new(fieldBase.Information.PartialName, GetNameValue(fieldBase)),
                AcroListBoxField listBox => new(listBox.Information.PartialName, string.Join(",", listBox.SelectedOptions)),
                AcroComboBoxField comboBox => new(comboBox.Information.PartialName, string.Join(",", comboBox.SelectedOptions)),
                _ => new(fieldBase.Information.PartialName, ""),
            };
        }

        private static string GetNameValue(AcroFieldBase fieldBase)
        {
            return fieldBase.Dictionary.TryGet(NameToken.V, out NameToken? value) ? value.Data : string.Empty;
        }
    }
}
