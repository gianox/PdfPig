# Plan: Writing and editing forms (AcroForms) with `PdfDocumentBuilder`

Goal of the fork: let `PdfDocumentBuilder` produce PDFs with working, fillable forms. That means:
(1) form fields survive when pages are copied, (2) their values can be filled in, (3) PdfPig draws
the field appearances itself, and (4) new fields can be created from scratch.

Each step below is meant to be one small PR/commit: it builds, all tests pass, and it leaves the
library in a releasable state. Sizes: **S** ≈ < 150 lines, **M** ≈ 150–400 lines (incl. tests).

Out of scope: XFA forms, JavaScript actions/calculations, keeping digital signatures valid
(needs incremental save), form flattening (could be a later phase), FDF/XFDF import/export.

---

## Background: how things work today

| Area | Where | State |
|---|---|---|
| Reading forms | `src/UglyToad.PdfPig/AcroForms/` (`AcroFormFactory`, `AcroFieldBase` + typed fields) | Complete, read-only. `PdfDocument.TryGetForm` |
| Copying pages | `PdfDocumentBuilder.AddPage(PdfDocument, int, AddPageOptions)` | Pages and their objects are written to the output **immediately** |
| Copying annotations | `PdfDocumentBuilder.CopyAnnotationsFromPageSource` | Widgets are copied; parentless widgets with clashing `/T` are renamed (`fieldNames` set) |
| Catalog | `PdfDocumentBuilder.CompleteDocument` | Never writes `/AcroForm` |
| Writing fonts | `IWritingFont`, `Standard14WritingFont`, TrueType simple + composite | Has width/bbox metrics usable for text layout |

Problems in the current copy path (from reading the code; step 1.1 confirms them with tests):

1. **No `/AcroForm` in the output catalog**, so viewers don't treat widgets as form fields.
2. **Widgets are written inline.** `CopyWithSkippedKeys` + `WriterUtil.CopyToken` write the widget
   dictionary directly into the `/Annots` array. `/Fields` and `/Kids` must point to indirect
   objects, so these widgets can't be part of a field tree.
3. **`/Parent` pulls in too much.** For hierarchical fields (widget → `/Parent` field → `/Kids`),
   `CopyToken` follows `/Parent` and copies the parent's `/Kids`. That includes widgets on pages that
   were never copied, and through their `/P` it can copy whole source pages as orphan objects.
4. **Renaming only handles parentless widgets.** Two copies of the same hierarchical form still clash.

---

## Phase 1: Keep copied forms working

End state: copying pages from a fillable PDF gives a fillable PDF. `PdfDocument.TryGetForm` on the
output returns the same fields (renamed where they clash), and Acrobat, Chrome and pdf.js let you fill them.

### 1.1 Characterization tests (S)
- Add `src/UglyToad.PdfPig.Tests/Writer/Forms/FormCopyTests.cs`.
- Tests that copy all pages of `AcroFormsBasicFields.pdf` and `Motor Insurance claim form.pdf`, then
  open the output with `TryGetForm`. Assert field count, names, types and values match the source.
- Hand-built source documents (as in the existing `AddPageRenamesClashingFieldNames` test) covering:
  a hierarchical field whose widgets sit on two pages, with only one page copied; a radio group; a
  field and widget merged into one dictionary.
- Mark the tests that fail today with `Skip = "Phase 1.x"` so the suite stays green. Each later
  step removes its skips.
- **Done when:** the gaps listed above are pinned down by (skipped) tests.

### 1.2 Internal form registry (S)
- New folder `src/UglyToad.PdfPig/Writer/Forms/`. Put new code there to keep merges from upstream simple.
- `internal sealed class FormRegistry` owned by `PdfDocumentBuilder`. It holds the field nodes
  (source dictionary, reserved output reference, parent, kids, widgets, fully qualified name),
  form-level `/DR`, `/DA`, `/Q`, `NeedAppearances` and `SigFlags`.
- `internal sealed class FieldNode`: one per field dictionary (terminal or not). A merged
  field+widget dictionary is a single node with `IsWidget = true`.
- Not used yet. Unit tests for building the tree and computing qualified names (`a.b.c`).
- **Done when:** the registry exists and is unit tested, with no behaviour change.

### 1.3 Write widgets as indirect objects (S)
- In `CopyAnnotationsFromPageSource`, a widget gets a **reserved** object number
  (`context.ReserveObjectNumber()`). The `/Annots` entry is that reference, and the widget is
  registered in the `FormRegistry`. The dictionary itself is written in `CompleteDocument`
  (deferred, so Phase 2 can change values after `AddPage`).
- Copying the same page twice must give two separate widget objects. Reserve per page copy, not per
  source reference.
- **Done when:** the output's `/Annots` holds indirect references to widgets, and all existing tests pass.

### 1.4 Emit `/AcroForm` for parentless fields (S)
- In `CompleteDocument`: when the registry has fields, write each deferred field object and add
  `/AcroForm << /Fields [...] >>` to `catalogDictionary`.
- Move the existing rename logic (`fieldNames`) into the registry.
- **Done when:** the 1.1 tests for flat forms (no `/Parent`) pass without skips, and
  `AcroFormsBasicFields.pdf` round-trips.

### 1.5 Hierarchical fields with controlled parent copying (M)
- While copying a widget with `/Parent`, walk up to the root field. For each ancestor, reserve one
  output reference per source reference (per source document) and register the node. **Do not** let
  `CopyToken` follow `/Parent` or `/Kids`: strip them before copying and rebuild them from the registry.
- In `CompleteDocument`, write each non-terminal node with `/Kids` = only the copied children
  and `/Parent` = the reserved parent reference.
- Copy inheritable entries (`/FT`, `/Ff`, `/V`, `/DV`, `/DA`, `/Q`) from the ancestors as they are.
  Don't flatten them, since the tree is preserved.
- **Done when:** the 1.1 "widgets on two pages, copy one" test passes, and no source pages leak into
  the output (assert the object count, or check that no stray `/Type /Page` objects appear).

### 1.6 Name clashes across documents (S)
- Rename at the **root** node level: when a root's partial name is already taken by another root from a
  different source (or a second copy of the same page), add `+1`, `+2`, … to the root's `/T`.
  This matches the existing suffix scheme.
- Two pages from the same source sharing one root field stay merged under that root.
- **Done when:** copying the same form twice gives two independent sets of fields, verified with `TryGetForm`.

### 1.7 Form-level resources and settings (S)
- Merge `/DR` from each source `/AcroForm` into the output `/DR`. Resolve font name clashes the same
  way page resources do (see `NameConflictSolver` in `PdfPageBuilder.cs`). Rewrite `/DA` strings that
  refer to a renamed font.
- Copy `/DA` and `/Q` (the first source wins; fields that relied on a different source's defaults get
  their own explicit `/DA`/`/Q`).
- `NeedAppearances`: true in the output if any source had it.
- Signatures: keep `/Sig` fields but drop their `/V` (the signature can't be valid after copying),
  and don't set `SigFlags`. Document this.
- **Done when:** fonts referenced by copied `/DA` strings resolve in the output.

### 1.8 Options and docs (S)
- `AddPageOptions.KeepFormFields` (default `true`; only applies when `KeepAnnotations` is true).
  When false, widgets are dropped as before step 1.3.
- Update the README "Create PDF Document" limitations list.
- Update `PublicApiScannerTests` for any new public types.
- **Done when:** the docs are updated and the public API test passes.

### 1.9 Manual viewer check (no code)
- Open the 1.1 output files in Adobe Acrobat Reader, Chrome, Firefox (pdf.js) and Edge. Fill a field,
  save, reopen. Run `qpdf --check` on each.
- Record the results in this file under "Viewer matrix" (below).

---

## Phase 2: Fill fields

End state: `builder.Form.SetText("Name", "Frederik")` (etc.) after copying pages. Values show up in
viewers through `NeedAppearances` until Phase 3 replaces that with real appearances.

### 2.1 Public read view of the form being built (S)
- `PdfDocumentBuilder.Form` → `public sealed class FormBuilder` (in `Writer/Forms/`).
- `IReadOnlyList<FormFieldInfo> Fields`: fully qualified name, field type, flags, current value and
  options/states. This is a small immutable snapshot, not the reader's `AcroFieldBase`.
- **Done when:** a test copies a form and lists its fields with the expected names and types.

### 2.2 Set text field values (S)
- `FormBuilder.SetText(string name, string value)`. Throws on an unknown name, a non-text field,
  `MaxLen` overflow, or a read-only field (unless `force: true`).
- Write `/V` on the field node. Strings outside PDFDocEncoding are written as UTF-16BE with a BOM.
  Check that `TokenWriter` does this (see `TokenWriterStringTests`) and add a test if it doesn't.
- Until Phase 3: remove `/AP` from that field's widgets and set `NeedAppearances true`, so viewers
  don't keep showing the old value.
- **Done when:** round-trip `TryGetForm` shows the new value, and so does Chrome/Acrobat.

### 2.3 Checkboxes (S)
- `SetChecked(string name, bool isChecked)`. Read the "on" state name from the widget's `/AP /N` keys
  (anything other than `/Off`). Set `/V` on the field and `/AS` on each widget.
- Existing appearances stay valid, so `/AP` is kept.
- **Done when:** a round-trip test passes, including a checkbox whose on state isn't `/Yes`.

### 2.4 Radio buttons (S)
- `SelectRadio(string name, string exportValue)`. Each kid widget has its own on-state name. Set
  `/AS` to the matching state on one widget and `/Off` on the others; `/V` on the parent.
- Handle `/Opt` (export values stored by index) and `RadiosInUnison`.
- **Done when:** the round-trip test passes, and selecting a value that doesn't exist throws.

### 2.5 Choice fields (S)
- `SelectOption(string name, string value)` for combo boxes. Check the value against `/Opt` unless the
  field is editable (`Edit` flag).
- `SelectOptions(string name, IEnumerable<string> values)` for multi-select list boxes. Write `/V`
  (array) and `/I` (sorted indices).
- Remove `/AP` + set `NeedAppearances` (same as 2.2) until Phase 3.
- **Done when:** round-trip tests pass for a combo box, a single-select list and a multi-select list.

### 2.6 Field flags (S)
- `SetReadOnly(name, bool)`, `SetRequired(name, bool)`: bits 1 and 2 of `/Ff` on the field node.
- **Done when:** round-trip tests pass.

### 2.7 Docs and sample (S)
- README section "Fill a form", with a short example: open a source PDF, copy pages, fill, save.
- **Done when:** the docs are merged.

### 2.8 Forms with encryption (S, moved from encryption plan step 3.4)
- With `PdfEncryptionOptions` on: filled values (`/V`), `/DA` strings and other `/AcroForm` strings
  are encrypted, and field values round-trip (read back through `AcroFormFactory` with the password).
  Cover both writer types and an empty user password with `FillForms` on.
- **Done when:** the round-trip tests pass.

---

## Phase 3: Generate appearance streams

End state: filled fields look right in every viewer without `NeedAppearances`. Needed for PDF/A and
for viewers or printers that ignore that flag.

### 3.1 Form XObject writing helper (S)
- `internal static class AppearanceStreamWriter`: given a bbox, a resources dictionary and a list of
  `IGraphicsStateOperation`s, write `/Type /XObject /Subtype /Form /BBox [...] /Resources ...` and
  return the reference. Reuse the operation serialisation used by `PdfPageBuilder.DefaultContentStream`.
- Handle widget rotation through `/MK /R` → `/Matrix`.
- **Done when:** unit tests check the written stream's dictionary and decoded content.

### 3.2 `/DA` parser (S)
- Parse default appearance strings such as `/Helv 0 Tf 0 g`, `0.2 0.3 0.4 rg`, `1 0 0 0 k` into
  font name, size (0 = auto) and colour operations. Inheritance order: widget/field → ancestors →
  form `/DA`.
- **Done when:** unit tests cover the common variants and malformed input (fall back to `Helv 0 Tf 0 g`).

### 3.3 Font resolution and text measuring (M)
- `internal interface IAppearanceFont`: width of a string at a size, ascent/descent, encode to bytes.
- Implementations:
  - Standard 14 fonts referenced from `/DR` by `/BaseFont` (Helvetica, Times, Courier, ZapfDingbats, …),
    using the AFM metrics `Standard14WritingFont` already loads.
  - Fonts added to the builder (`AddedFont`), including composite TrueType for non-Latin text.
- Fonts embedded in the source `/DR` that are neither: not supported for generation. Fall back to
  `NeedAppearances` for that field and log a reason.
- `FormBuilder.SetDefaultFont(AddedFont font)` registers a builder font in `/DR` for filled and new fields.
- **Done when:** unit tests measure strings for Helvetica and for a TrueType test font.

### 3.4 Single-line text appearance (M)
- Background `/MK /BG` and border `/MK /BC` with `/BS` width/style (solid, dashed, beveled/inset can
  start out as solid). 2 pt padding, clipping rectangle, `/Tx BMC … EMC`, vertical centring,
  alignment from `/Q`, and auto font size when the size is 0 (fit height, then shrink to fit width).
- **Done when:** a rendered output matches expected content-stream snapshots (golden-file tests), and
  it looks right in Chrome with `NeedAppearances` off.

### 3.5 Multiline text appearance (S)
- Word wrap using the font metrics, honour line breaks, auto size for multiline (Acrobat uses 12 pt max).
- **Done when:** snapshot tests cover wrapping, long words and explicit newlines.

### 3.6 Comb and password fields (S)
- Comb: split the width into `MaxLen` cells, one character centred per cell. Password: draw `*` or `•`.
- **Done when:** snapshot tests pass.

### 3.7 Choice field appearances (S)
- Combo box: like single-line text, showing the selected value.
- List box: visible options from `/TI`, with the selection highlighted (`0.6 0.75 0.85 rg`, as Acrobat does).
- **Done when:** snapshot tests pass.

### 3.8 Use generated appearances when filling (S)
- `SetText`/`SelectOption` now generate `/AP /N` instead of removing it. Fall back to
  `NeedAppearances` only when 3.3 can't resolve the font.
- `FormBuilder.NeedAppearances` becomes an explicit opt-in.
- If `ArchiveStandard != None`: never set `NeedAppearances` (PDF/A forbids it), and throw if an
  appearance can't be generated.
- **Done when:** the Phase 2 tests run with `NeedAppearances` off and still look right in viewers.

---

## Phase 4: Create new fields

End state: you can build a form from scratch with `PdfPageBuilder`.

### 4.1 Field creation infrastructure (S)
- `FormRegistry` can add *new* nodes (no source dictionary). Dotted names (`address.street`) create
  or reuse non-terminal parent nodes. A fully qualified name that already exists throws (radio
  groups are the exception, see 4.4).
- Set `/F 4` (print) on widgets, `/P` to the page reference, and `/Tabs /R` on pages that contain fields.
- **Done when:** unit tests cover name handling.

### 4.2 Text fields (S)
- `PdfPageBuilder.AddTextField(string name, PdfRectangle rect, TextFieldOptions? options = null)`.
- `TextFieldOptions`: `Value`, `Font`, `FontSize` (0 = auto), `Alignment`, `Multiline`, `MaxLength`,
  `Comb`, `Password`, `ReadOnly`, `Required`, `Tooltip` (`/TU`), `BorderColor`, `BackgroundColor`,
  `BorderWidth`.
- Uses the Phase 3 appearance generation.
- **Done when:** round-trip tests pass and the field is fillable in Acrobat and Chrome.

### 4.3 Checkboxes (S)
- `AddCheckBox(string name, PdfRectangle rect, bool isChecked = false, CheckBoxOptions? options = null)`.
  Export value defaults to `Yes`; the style is a ZapfDingbats glyph (`/MK /CA`, default `4` = ✔).
- Writes both the `/Yes` and `/Off` appearances (plus `/D` down appearances, optional).
- **Done when:** round-trip tests pass and the box toggles in viewers.

### 4.4 Radio groups (M)
- The group lives on the document, because its buttons can sit on several pages:
  `var group = builder.Form.AddRadioGroup("Gender");`
  `page.AddRadioButton(group, rect, exportValue: "M");`
- Options: `NoToggleToOff`, `RadiosInUnison`, initial selection, style (circle/check).
- **Done when:** round-trip tests pass, and only one button is selected at a time in viewers.

### 4.5 Combo boxes and list boxes (S)
- `AddComboBox(name, rect, IReadOnlyList<string> options, string? selected, ComboBoxOptions?)`
  (`Editable`, `Sort`).
- `AddListBox(name, rect, options, IReadOnlyList<string>? selected, ListBoxOptions?)` (`MultiSelect`).
- Options can be `(exportValue, displayText)` pairs.
- **Done when:** round-trip tests pass.

### 4.6 Push buttons and empty signature fields (optional, S)
- Push button with a caption (no actions in scope). An empty `/FT /Sig` field so other tools can sign it later.
- **Done when:** both appear in viewers.

### 4.7 Docs, sample and API review (S)
- README section "Create a form". Update `PublicApiScannerTests`. XML docs on all new public types.
- **Done when:** the docs are merged.

---

## Cross-cutting rules

- **Tests:** every step adds tests. Round-trip through `PdfDocument.Open` + `TryGetForm` is the main
  check. Appearance output gets golden-file tests on the decoded content stream.
- **External validation:** run `qpdf --check` on generated files during development (manual or opt-in
  test). For PDF/A, check with veraPDF before 3.8 is considered done.
- **Upstream friendliness:** keep new code in `Writer/Forms/`. Keep edits to `PdfDocumentBuilder` to
  small hooks, and follow the existing code style and XML doc conventions.
- **Public API:** each new public type needs an entry in `PublicApiScannerTests`.

## Suggested order and milestones

| Milestone | Steps | Result |
|---|---|---|
| M1 | 1.1–1.4 | Flat forms survive page copying |
| M2 | 1.5–1.9 | All copied forms work |
| M3 | 2.1–2.8 | Fill forms (viewer-drawn), also in encrypted documents |
| M4 | 3.1–3.8 | Fill forms with PdfPig-drawn appearances, PDF/A-safe |
| M5 | 4.1–4.7 | Create forms from scratch |

## Viewer matrix

_Filled in during step 1.9 and updated at each milestone._

| Viewer | M1 | M2 | M3 | M4 | M5 |
|---|---|---|---|---|---|
| Adobe Acrobat Reader | | | | | |
| Chrome | | | | | |
| Firefox (pdf.js) | | | | | |
| Edge | | | | | |
| macOS Preview / iOS | | | | | |
