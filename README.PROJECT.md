pixinit — project README (generated)
=====================================

This file contains an explicit, up-to-date README you can copy into README.md if you prefer.

Overview
- Name: pixinit
- Purpose: Storage diagnostics and benchmarking (WPF, Windows)
- Target: .NET 10 (net10.0-windows)

Quick start (CLI)
1. Restore and build solution:
   dotnet restore
   dotnet build pixinit.slnx -c Release

2. Run the app (recommended to use Visual Studio for WPF):
   dotnet run --project pixinit/pixinit.csproj -c Release

Running tests (harness)
- The tests are provided as an executable harness. Use:
  dotnet run --project tests/pixinit.Tests/pixinit.Tests.csproj -- [--ui] [--hardware]

Notes
- Ensure .gitignore is applied. Remove already-tracked ignored files locally with:
  git rm -r --cached .vs **/bin **/obj
  git add . && git commit -m "Apply .gitignore"

- If you want me to replace the existing README.md files in-place (they contain some embedded null characters), grant me permission and I'll attempt an in-place replacement. Otherwise copy the content from this file into README.md manually.

Next steps I can take
- Overwrite the existing README.md files with this content (requires permission).
- Create CONTRIBUTING.md or LICENSE files.
- Add per-project README files and link them from root README.md.

