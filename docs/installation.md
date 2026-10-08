# PIXINIT installation and release guide

Current release candidate: 1.0.0, Windows x64, 2026-10-08. Qualification: PARTIAL.

## Package and requirements
- Installer: `artifacts/installer/PIXINIT-Setup-1.0.0-win-x64.exe`.
- Checksum: `artifacts/installer/SHA256SUMS.txt`. A matching checksum verifies file identity; it does not establish publisher trust.
- The Windows x64 package includes its .NET runtime. No separate .NET installation is required to run the packaged application.
- The installer and application request administrator elevation under the existing manifest. Windows may ask for administrator credentials.
- The installer and application are unsigned. No self-signed trust certificate is installed.
- The optional HTML overview uses an installed Microsoft Edge WebView2 Runtime. Native WPF presentation remains the fallback when WebView2 is unavailable. This package does not install the WebView2 Runtime.
- Windows 11, other controllers and enterprise application-control policies remain unverified. See [compatibility](compatibility.md).

## Install
Close PIXINIT first. From the repository root, run in PowerShell:

```powershell
Get-FileHash '.\artifacts\installer\PIXINIT-Setup-1.0.0-win-x64.exe' -Algorithm SHA256
& '.\artifacts\installer\PIXINIT-Setup-1.0.0-win-x64.exe'
```

Compare the hash against `SHA256SUMS.txt`, approve the administrator prompt and follow Setup. Choose the optional desktop shortcut if wanted. The default destination is `C:\Program Files\PIXINIT`; the Start menu entry is PIXINIT. Finish Setup to launch, or use the Start menu shortcut.

The current navigation is Diagnostics, Benchmark, Other / Unidentified. SATA and NVMe each provide Overview, SMART and Details. Raw Data navigation has been removed; stored raw evidence and exports remain available through existing reporting options.

## Upgrade and uninstall
- Close PIXINIT and run the new installer. The application uses a stable Inno Setup AppId. Upgrades from earlier per-user packages still need isolated validation; automatic migration is not claimed.
- Writable history and WebView2 data stay under `%LocalAppData%\PIXINIT`, in the profile used to run the application. Supplying credentials for another administrator may use that administrator's profile.
- Uninstall through Windows Settings > Apps > PIXINIT > Uninstall. User history/settings are preserved by default; uninstall does not deliberately delete the profile data directory.
- This build has not been installed, upgraded or uninstalled in an isolated machine. Existing installation evidence belongs to earlier builds and is not qualification for this package.

## Reproduce the package
Build prerequisites: .NET 10 SDK and Inno Setup 6. From the repository root:

```powershell
.\build\release.ps1
```

For a compiler outside the standard locations:

```powershell
.\build\release.ps1 -InnoSetupPath 'C:\path\to\ISCC.exe'
```

The script runs Release and UI verification, treats build warnings as errors, publishes self-contained x64 output, rejects development files, compiles Setup and writes SHA256SUMS.txt. Build intermediates use `artifacts/diagnostics-build` to avoid the application's ordinary output directory. It stops on failed checks. Packaging does not install or distribute the application.

`build/verify.ps1 -Ui` runs the bounded automated harness. Do not pass `-Hardware` for UI verification: that separate opt-in hardware workflow includes physical diagnostic and write operations. No full physical benchmark is needed to assess layout.

## DPI and accessibility acceptance procedure
Current status: 100 percent / 96 DPI WPF fixture layouts inspected at 1366 x 768 and 1800 x 1000. Actual 125 percent, 150 percent, mixed DPI and screen-reader workflows are NOT RUN. PerMonitorV2 in the manifest is a declaration, not validation evidence.

1. Record Windows build, display resolution, scaling, monitor arrangement and installer SHA-256. Keep original display settings for restoration.
2. Install the candidate on a disposable Windows machine or VM. Start at 100 percent scale, then use Settings > System > Display > Scale to select 125 percent and 150 percent where offered. Follow Windows sign-out/relaunch requirements.
3. At each scale, launch at the laptop and desktop window sizes supported by that screen. Record actual window size and scale; do not imply a desktop-size window fits a smaller work area.
4. Inspect Diagnostics > SATA/NVMe > Overview, SMART, Details; Benchmark; Other / Unidentified; and history/export dialogs. Check full serial wrapping, long counters, units, buttons, table headers, visible rows, empty states, error messages, and vertical/horizontal scrolling.
5. Use Tab, Shift+Tab, arrows, Space and Enter to navigate. Focus must remain visible and actions reachable. Check Windows Narrator separately. Do not run a write benchmark solely to validate controls.
6. If two monitors with different scales are available, move the app between them in both directions, maximize/restore and reopen dialogs. Check text sharpness, clipping, retained selection and scroll position.
7. Restore original settings. Save actual screenshots and log each configuration as PASS, FAIL or NOT RUN, with defect details. Screenshot resizing is not DPI testing.

Release DPI acceptance requires no inaccessible controls, clipped essential text, missing table rows or lost focus at each tested configuration. Unavailable setups stay NOT RUN.