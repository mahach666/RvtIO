# Changelog

All notable changes are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html); versions before 1.0 may change the
API between minor releases.

## [Unreleased]

## [0.1.0] - first public preview

- Reads Revit projects (`.rvt`) and families (`.rfa`) saved by Revit 2020 to 2026 without Revit; Revit 2027 files open
  with partial support.
- Writes IFC2X3 (coordination view) with optional Revit parameters as property sets, compact JSON and a full JSON dump.
- `RvtDocument`, `FilteredElementCollector` and the element model for reading; `IfcExporter` and `CompactJsonExporter`
  for export; the `rvtio` command line tool.
- Targets .NET 8 and .NET 10; the library is silent by default and reports through `DiagnosticOutput`.
- Excludes resolved non-primary design options from IFC while retaining all options in the document API.
- Reads additional user parameters on materials, curve center lines and view markers.
- Corrects element identifiers, material layers and compressed data handling in newer project layouts.
