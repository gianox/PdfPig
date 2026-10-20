# Plan: Writing encrypted (password-protected) PDFs with `PdfDocumentBuilder`

Goal: let `PdfDocumentBuilder` produce password-protected PDFs using the standard security handler,
which Acrobat and every mainstream viewer can open:

```cs
var builder = new PdfDocumentBuilder(stream, encryption: new PdfEncryptionOptions
{
    UserPassword = "open-me",          // needed to open the file ("" = opens without a password)
    OwnerPassword = "admin",           // needed to change permissions
    Permissions = PdfPermissions.Print | PdfPermissions.FillForms,
});
```

Each step below is meant to be one small PR/commit: it builds, all tests pass, and it leaves the
library in a releasable state. Sizes: **S** ≈ < 150 lines, **M** ≈ 150–400 lines (incl. tests).

Separate from, and independent of, `forms-writing-plan.md`. The two meet in only one place:
form field values (strings) must be encrypted like any other string, and they will be, because
encryption hooks in at the object-writing level.

**Requirement: AES-256 (revision 6, ISO 32000-2) is the minimum.** It's the only algorithm written.
It's supported by Acrobat X and later and by all current viewers.

Out of scope: AES-128 and RC4 (below the required strength; RC4 is also deprecated), revision 5
(non-standard Adobe extension),
certificate/public-key encryption (`/Adobe.PubSec`), encrypting existing files in place
(incremental save), keeping signatures valid.

---

## Background: how things work today

| Area | Where | State |
|---|---|---|
| Decryption, R2–R6, RC4/AES-128/AES-256 | `src/UglyToad.PdfPig/Encryption/EncryptionHandler.cs` | Complete, read side only |
| Key derivation helpers | `EncryptionHandler`: `CalculateKeyRevisions2To4`, `CalculateKeyRevisions5And6`, `ComputeStupidIsoHash` (algorithm 2.B), `TruncatePasswordTo127Bytes` | `private static`, reusable once extracted |
| RC4 / AES helpers | `RC4.cs`, `AesEncryptionHelper.cs` | `AesEncryptionHelper.Encrypt256()` is a `NotImplementedException` stub |
| Permissions | `UserAccessPermissions.cs` | `internal enum` |
| Object writing | `PdfStreamWriter.WriteToken(...)` → `ITokenWriter.WriteToken(ObjectToken, ...)` | Each object is written **immediately** when it is added |
| Dedup writer | `PdfDedupStreamWriter` | Serialises each object and dedups by a hash of its bytes |
| Trailer `/ID` | `TokenWriter.WriteCrossReferenceTable` | Random GUIDs generated **at the very end** |
| Tests | `Integration/EncryptedDocumentTests.cs`, `Encryption/AesEncryptionHelperTests.cs` | Read side only |

Things the design has to handle:

1. **`/ID` should be fixed before the first object is written.** R6 doesn't derive its key from
   `/ID`, but the ID is still required in an encrypted file's trailer. Generating it up front keeps the
   trailer deterministic and leaves the door open for other handlers later.
2. **Encryption must be configured before anything is written.** `AddPage(PdfDocument, …)`, images and
   reserved fonts write objects straight away. Encryption options therefore go in the
   constructor, or the setter throws once the first object has been written.
3. **Dedup has to compare unencrypted objects.** AES uses a random IV, so two identical objects
   encrypt differently. The dedup writer must hash the plain object and encrypt only what it actually writes.
4. **Not everything is encrypted:** the `/Encrypt` dictionary itself, the trailer `/ID` strings, and the
   xref table. If `EncryptMetadata = false`, `/Type /Metadata` streams are also left alone.
5. **PDF/A forbids encryption.** If `ArchiveStandard != None`, throw.

---

## Phase 1: Crypto building blocks (no writer changes)

End state: everything needed to compute an `/Encrypt` dictionary and encrypt bytes exists and is
unit tested, and the read side is unchanged.

### 1.1 Extract the key derivation helpers (S)
- Move `ComputeStupidIsoHash`, `TruncatePasswordTo127Bytes` and `ComputeSha256Hash` out of `EncryptionHandler` into `internal static class StandardSecurityHandlerAlgorithms`
  (same folder). `EncryptionHandler` calls the new class. Pure refactor.
- **Done when:** all existing encryption tests pass unchanged.

### 1.2 AES encryption helper (S)
- Replace the `AesEncryptionHelper.Encrypt256()` stub with `Encrypt(byte[] key, ReadOnlySpan<byte> data)`:
  a random 16-byte IV, AES-256-CBC with PKCS#7 padding, output `IV ‖ ciphertext`. With R6 the 32-byte
  file key is used directly for every object (no per-object key, unlike older revisions).
- Also add `EncryptNoIv(key, data)` (CBC, zero IV, no padding, for `/UE` and `/OE`) and
  `EncryptEcb(key, block)` (for `/Perms`).
- **Done when:** `Decrypt(Encrypt(x)) == x` for empty, 1-byte, 16-byte and large inputs.

### 1.3 Permissions type (S)
- Public `[Flags] enum PdfPermissions`: `Print`, `Modify`, `CopyContent`, `ModifyAnnotations`,
  `FillForms`, `ExtractForAccessibility`, `Assemble`, `PrintHighQuality`, `All`, `None`.
- Map to the 32-bit `/P` value. Reserved bits 7–8 and 13–32 must be 1, bits 1–2 must be 0.
  Result is a negative `int`.
- **Done when:** unit tests cover `None`, `All` and single flags.

### 1.4 `/Encrypt` dictionary for AES-256 R6 (M)
- `internal static class StandardSecurityHandlerBuilder.CreateR6(userPwd, ownerPwd, permissions, encryptMetadata)`
  returns the file key plus `/Filter /Standard /V 5 /R 6 /Length 256 /CF << /StdCF << /CFM /AESV3
  /AuthEvent /DocOpen /Length 32 >> >> /StmF /StdCF /StrF /StdCF /O /U /OE /UE /Perms /P /EncryptMetadata`.
- Algorithms (ISO 32000-2): random 32-byte file key; 8. `/U` + `/UE`; 9. `/O` + `/OE` (hashed with `/U`);
  10. `/Perms` (P ‖ 0xFFFFFFFF ‖ 'T'/'F' ‖ "adb" ‖ 4 random bytes, AES-256-ECB).
- Passwords: UTF-8, run through SASLprep (`string.Normalize(NormalizationForm.FormKC)` covers the
  common cases; note the gap in a comment), then truncated to 127 bytes.
- **Done when:** the read side's `EncryptionHandler` accepts both passwords against the generated
  dictionary (unit test, no PDF file needed). A wrong password is rejected, and the
  `/Perms` check succeeds and returns the original permissions.

---

## Phase 2: Hooking encryption into the writer

End state: `PdfDocumentBuilder` can write an AES-256 encrypted document that PdfPig itself opens.
Nothing weaker than AES-256 can be produced.

### 2.1 Generate `/ID` up front (S)
- `PdfStreamWriter` gets the document ID (two byte arrays) at construction, or creates it if none is
  given. Add a `documentId` parameter to `ITokenWriter.WriteCrossReferenceTable`, or a new overload so
  custom `ITokenWriter` implementations keep working, instead of generating GUIDs there.
- No behaviour change apart from when the ID is generated.
- **Done when:** existing tests pass and the trailer `/ID` equals the ID the writer was given.

### 2.2 Encryptor that rewrites an object before it is written (M)
- `internal sealed class ObjectEncryptor` with `IToken Encrypt(IndirectReference obj, IToken token)`.
  It walks the token tree and returns a new tree where:
  - `StringToken`/`HexToken` → `HexToken` of the encrypted bytes (hex avoids escaping questions)
  - `StreamToken` → the same dictionary with encrypted data and `/Length` updated (encrypt the
    already-filtered bytes)
  - dictionaries/arrays: recurse; everything else unchanged
  - skipped: `/Type /Metadata` streams when `EncryptMetadata` is false, and streams that already
    have a `/Crypt` filter
- Done as a token transform, so `TokenWriter`, `NoTextTokenWriter` and user-supplied `ITokenWriter`s
  need no changes.
- **Done when:** unit tests encrypt a sample object, decrypt it with `EncryptionHandler.Decrypt`
  for the same reference, and get the original back.

### 2.3 Plug the encryptor into the stream writers (S)
- `PdfStreamWriter.WriteToken(...)` (both overloads): if an encryptor is set, apply it to the token
  using the object's reference before passing it to `ITokenWriter`.
- `PdfDedupStreamWriter`: hash the **plain** token, encrypt only when actually writing.
- The `/Encrypt` dictionary is written with encryption bypassed.
- **Done when:** unit tests confirm that two identical images are still deduplicated with encryption on.

### 2.4 Trailer `/Encrypt` (S)
- `CompletePdf` passes the `/Encrypt` reference to the trailer, again with an overload rather than a
  breaking change to `ITokenWriter`.
- **Done when:** the trailer contains `/Encrypt n 0 R` and `/ID`.

### 2.5 Public options and builder wiring (S)
- Public `PdfEncryptionOptions` (`UserPassword`, `OwnerPassword`, `Permissions`,
  `EncryptMetadata = true`). No algorithm choice: always AES-256 R6. If a stronger handler ever arrives,
  an `Algorithm` property can be added with AES-256 as the floor.
- New `PdfDocumentBuilder` constructor overloads that take `PdfEncryptionOptions`. Validation:
  - owner password empty → generate a random one (common practice; document it)
  - `ArchiveStandard != None` → throw when `Build()` runs
- Output version: AES-256 R6 belongs to PDF 2.0. Write 1.7 plus the Adobe extension level 8 entry
  `/Extensions << /ADBE << /BaseVersion /1.7 /ExtensionLevel 8 >> >>` in the catalog (what Acrobat
  does for R6), unless the builder is already set to 2.0. This is the most compatible combination.
- **Done when:** a round-trip test passes: build, `PdfDocument.Open` with the user password, then the
  owner password; text, images and fonts all come back; opening without a password throws.

---

## Phase 3: Edge cases and the full builder surface

### 3.1 Copied pages from an encrypted source (S)
- Test: open an encrypted file (`encrypted-password-is-password.pdf`) with its password, copy its pages,
  build (a) without encryption and (b) with new encryption and a new password.
- Confirms "remove protection" and "change password" both work. Nothing in the copied output should
  still be encrypted with the source's key.
- **Done when:** both tests pass.

### 3.2 Everything the builder writes (S)
- Round-trip tests with encryption on for: Standard 14 text, TrueType simple + composite fonts
  (font file streams), JPEG/PNG/CCITT images, bookmarks with Unicode titles, links, document
  information (`/Info` strings are encrypted too), XMP metadata with `EncryptMetadata` both on and off.
- **Done when:** all pass.

### 3.3 Empty user password (S)
- `UserPassword = ""` means the file opens without a password, but the permissions still apply.
  This is the common "can't print/copy" PDF.
- **Done when:** the round-trip test opens without a password, and `UserAccessPermissions` reads back the set flags.

### 3.4 Forms interaction (moved)
- Moved to the forms plan as step 2.8, because it needs forms Phase 2. Decided 2026-10-20.

Note on 3.2: the builder only writes XMP metadata for PDF/A, and PDF/A can't be encrypted, so
`EncryptMetadata` has nothing to act on for builder-written documents. It still applies to `/Metadata`
streams on copied pages, and `ObjectEncryptorTests` cover it. Builder-written XMP without PDF/A is out of scope.

### 3.5 `PdfMerger` (optional, S)
- If `PdfMerger` builds on `PdfDocumentBuilder`, expose `PdfEncryptionOptions` there as well.
- **Done when:** the merge round-trip test passes with encryption on.

---

## Phase 4: External validation and docs

### 4.1 External validation (no code, or an opt-in test)
- With and without a user password:
  - `qpdf --check --password=... out.pdf` and `qpdf --show-encryption --password=... out.pdf`
  - Open in Adobe Acrobat Reader, Chrome, Firefox (pdf.js), Edge, macOS Preview. Check the password
    prompt and that the permissions are enforced (Acrobat shows them under Document Properties → Security).
- Record the results in the matrix below.

### 4.2 Docs and public API (S)
- README section "Create an encrypted PDF", plus a short note that permissions are honoured by
  viewers but aren't real protection without a user password.
- Update `PublicApiScannerTests` for `PdfEncryptionOptions` and `PdfPermissions`.
- XML docs on all new public members.
- **Done when:** the docs are merged and the public API test passes.

---

## Cross-cutting rules

- **Never log or keep passwords** beyond deriving the key. Don't put them in exception messages.
- **Crypto via `System.Security.Cryptography`** only (`Aes`, `SHA256/384/512`, `MD5`,
  `RandomNumberGenerator`). Check that this works on every target framework the project builds for.
- **Tests:** every step adds tests. The read side (`EncryptionHandler`) is the main oracle, with
  qpdf as a second, independent one.
- **Upstream friendliness:** new code goes in `Encryption/` (algorithms) and a small hook in
  `Writer/`. No breaking changes to `ITokenWriter` (overloads or default-implemented members only).

## Suggested order and milestones

| Milestone | Steps | Result |
|---|---|---|
| E1 | 1.1–1.4 | Crypto pieces tested against the read side |
| E2 | 2.1–2.5 | AES-256 encrypted output from `PdfDocumentBuilder` |
| E3 | 3.1–3.3, 3.5 | Works with copied pages, every builder feature and `PdfMerger` (forms: see forms plan 2.8) |
| E4 | 4.1–4.2 | Validated externally and documented |

## Viewer matrix

| Viewer | With user password | No user password (permissions only) |
|---|---|---|
| Adobe Acrobat Reader | OK (2026-10-20, samples 01, 03) | OK (2026-10-20, sample 02) |
| Chrome | OK (2026-10-20, samples 01, 03) | OK (2026-10-20, sample 02) |
| Firefox (pdf.js) | | |
| Edge | | |
| macOS Preview | | |
| qpdf --check | not run (qpdf not installed) | not run |
