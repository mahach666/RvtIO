# Frozen resource fixtures

The library embeds tables whose content comes from the user interface resources of Revit 2022: the captions of the
builtin parameters (`compact_builtin_parameters.tsv`), the parameter group labels (`compact_parameter_groups.tsv`) and the names of
the system families (`revit_ui_names.tsv`). The original resource files cannot be published, so a test that compares an
embedded table with them cannot run from this repository alone.

The fixtures in `tests/RvtIO.Tests/Fixtures/Frozen/` close that gap. They hold only identifiers, numbers and
SHA-256 hashes of the original strings. The correspondence with the originals was checked once, privately, when the
fixtures were derived; the repository tests then check the embedded tables against the fixtures, and the optional tests
described below re-check the fixtures against the originals.

## The fixtures

| File | Rows | Content | Checked by |
|---|---|---|---|
| `parameter_table.tsv` | 3352 | builtin parameter id, group id, string id, plain-label flag, SHA-256 of the English caption | `CompactLabelsTests.ParameterTableEqualsTheFrozenRevit2022Table` |
| `caption_keys.tsv` | 3351 | resource symbol, string id, SHA-256 of the comparison key (spaces removed, upper case) of the English and of the Russian caption | `BuiltInParameterCaptionResourceTests.FrozenCaptionKeysOfAll3351ParametersDifferFromIfcNameOverrides`, `EmbeddedLabelsDifferFromIfcNameOverridesAndHaveTheOriginalKeys`, `FrozenSymbolIdsAgreeWithNativeInitializerForSixOwnFamilyParameters` |
| `own_family_captions.tsv` | 12 | SHA-256 of the exact captions of the six electrical panel family parameters, English and Russian | `FrozenCaptionOfAnOwnFamilyParameterIsThatOfTheOriginal`, `EnumIdentifierIsNotTheFrozenNativeDefinitionCaption` |
| `group_labels.tsv` | 120 | symbol, string id, SHA-256 of each parameter group label | `CompactLabelsTests.GroupLabelsAreFrozenGeneratedResStringsAndAgreeWithTheEnumExtension` |
| `ui_names.tsv` | 114 | culture, resource file, string id, SHA-256 of every string the loader can request, both languages | `SystemFamilyNameTests.EmbeddedTableEqualsTheFrozenHashesOfTheOriginalStrings` |
| `label_vectors.tsv` | 362 | builtin parameter id, SHA-256 of the label and of the group label as an independent export prints them | the label vector theory of `CompactLabelsTests` (362 rows) |
| `original_pins.tsv` | 13 | length and SHA-256 of the 12 resource files and of the identifier list, and the line count of the list | `FrozenPinsRecordTheOriginalFiles` |

The hash of a string is the SHA-256 of its UTF-8 bytes. No caption, translation or other text of the originals is stored,
so a failing row says that a value differs but not what it should be; compare with the originals to find out.

A change to any row of an embedded table, to a group, to an identifier or to a label makes a repository test fail.

## What stays private

* The Russian captions of the builtin parameters have no embedded counterpart. They are frozen as hashes and only the
  optional tests below can tie them to the originals.
* Reading a real resource file (the PE string table reader) is tested from the repository with images built by the tests
  (`SyntheticPe`). Reading the original files is tested only with the optional tests.
* The original-resource tests are skipped without `RVTIO_REVIT_RESOURCES`, so that a maintainer
  can run them in private: `ReadsOriginalCaptionInRequestedLanguage`, `CopiesRetainOriginalBytes`,
  `DoesNotSubstituteOtherLanguage`, `TruncatedResourceImageIsRejected`, `EnumIdentifierIsNotNativeDefinitionCaption`,
  `AllOriginalBuiltinCaptionKeysDifferFromIfcNameOverrides`, `OriginalSymbolIdsAgreeWithNativeInitializerForSixOwnFamilyParameters`,
  `GroupLabelsAreGeneratedResStringsAndAgreeWithTheEnumExtension`, `ParameterTableEqualsTheRevit2022Sources`,
  `PeStringTable_ReadsExactResourceEntries`, `InstalledResourceDllsGiveTheSameStringsAsTheEmbeddedTable`,
  `NamesAreExactStringsOfTheInstalledResourceDlls`.

## Deriving and re-checking

Environment variables (all optional; without the first one the tests below are skipped):

| Variable | Meaning |
|---|---|
| `RVTIO_REVIT_RESOURCES` | Folder laid out as `2022/<culture>/*.dll` (cultures `en-US` and `ru-RU`: `BuildingSystemsRes.dll`, `EssentialsRes.dll`, `GeneratedRes.dll`, `HostObjRes.dll`, `Revit.dll`, `StairRampRes.dll`) plus `2022/GeneratedResourceIds.inl`, copied from an installed Revit 2022. |
| `RVTIO_NATIVE_TABLE_DIR` | Folder with `177-native-builtin-definition-table.json`, the table of the 3352 native builtin parameter definitions (columns `id`, `groupId`, `originalResourceId`). Needed only for `parameter_table.tsv`. |
| `RVTIO_TEST_FILES` | The private test file folder (see `docs/test-fixtures.md`); `TestData/compact_label_vectors.tsv` there (tab separated: id, label, group label) is needed only for `label_vectors.tsv`. |
| `RVTIO_FROZEN_OUT` | Folder the test `RegenerateFrozenFixtures` writes the derived fixtures to. |

* Check: `dotnet test --filter FullyQualifiedName~Frozen` with the variables set. `FrozenFixturesEqualWhatTheOriginalResourcesGive`
  rebuilds every fixture it can from the originals and compares it with the committed file byte for byte;
  `FrozenParameterTableEqualsTheNativeDefinitionTable` and `FrozenLabelVectorsEqualThePrivateVectors` do the same for the
  two fixtures that need the extra inputs, and are reported as skipped when those are missing.
* Re-derive: set `RVTIO_FROZEN_OUT` and run `dotnet test --filter FullyQualifiedName~RegenerateFrozenFixtures`, then copy the
  files over `tests/RvtIO.Tests/Fixtures/Frozen/`. The derivation code is `FrozenFixtureBuilder` in the test project;
  while deriving, it also fails when a caption is blank, contains a NUL character or equals `IfcName` or `NameOverride`
  once spaces are removed, which are the facts `caption_keys.tsv` records.

## Derivation record

The fixtures in this repository were derived and verified against the English and Russian Revit 2022 resource files whose
lengths and SHA-256 hashes are in `original_pins.tsv`, and against the table of 3352 native builtin parameter definitions
(3342 plain labels, 10 without one). With all four variables set, the whole group above passes: the committed fixtures
equal what the originals give, the embedded tables equal the fixtures, and the kept original-resource tests pass.
