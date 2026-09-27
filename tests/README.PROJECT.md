pixinit — tests (harness)
=========================

The tests in tests/pixinit.Tests are implemented as an executable harness rather than a VSTest project.

Run the harness
- From the repository root:
  dotnet run --project tests/pixinit.Tests/pixinit.Tests.csproj -- [--ui] [--hardware]

Flags
- --ui: run UI-related verification fixtures (isolated)
- --hardware: discover and run read-only hardware checks (run on a designated test machine)

Notes
- `dotnet test` may not run this harness; prefer `dotnet run` with the project file.
- Review tests/Program.cs for additional runtime options.

If you want, I can add example commands, CI configuration for running the harness in GitHub Actions, or convert the harness into standard unit tests for easier CI integration.
