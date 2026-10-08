Pixinit - SDE2 System Design Architecture
1. Goal
Windows WPF app for SATA and NVMe diagnostics.
Must support: Sequential R/W, Random 4K IOPS, TBW, Sustained Write, Temperature.

2. Architecture Style
Clean Architecture + MVVM
4 Layers: Presentation, Application, Domain, Infrastructure

3. Layer Diagram
Presentation (Views, ViewModels)
      |
Application (Coordinators, UseCases, Policies)
      |
Domain (Models, Interfaces, Policies)
      |
Infrastructure (Windows Interop, Benchmark Engine, SQLite)
4. Folder Structure (Proposed)
pixinit/
Core/
Abstractions/ IDeviceDiscovery, ISataProvider, INvmeProvider, IBenchmarkEngine
Devices/ StorageDevice, DeviceType
Benchmark/ BenchmarkRequest, BenchmarkResult, BenchmarkPolicy
Diagnostics/ SmartData, NvmeHealth, TbWInfo, ThermalInfo
Application/
Discovery/ DiscoveryCoordinator
Benchmarking/ BenchmarkOrchestrator, BenchmarkTargetResolver, WriteConsent
Diagnostics/ SataDiagnosticsUseCase, NvmeDiagnosticsUseCase
Assessment/ HealthAssessor
Infrastructure/
Windows/ Discovery/ WindowsDiskDiscovery
Windows/ Sata/ WindowsAtaTransport, WindowsSataProvider
Windows/ Nvme/ WindowsNvmeTransport, WindowsNvmeProvider
Benchmarking/ FileBenchmarkEngine, RandomIoEngine, SustainedWriteEngine, OwnedBenchmarkFile
History/ SqliteHistoryStore
Logging/ DiscoveryLog
Presentation/
ViewModels/ ShellVM, BenchmarkVM, SataVM, NvmeVM, HistoryVM
Views/ XAML
Composition/
ServiceRegistry.cs (DI container)

5. Key Interfaces (Core)
interface IDeviceDiscovery { Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken ct); }
interface IBenchmarkEngine {
  Task<BenchmarkResult> RunAsync(BenchmarkRequest request, IProgress<BenchmarkProgress> progress, CancellationToken ct);
}
interface ISataDiagnosticsProvider {
  Task<SataDiagnosticResult> GetDiagnosticsAsync(string devicePath, CancellationToken ct);
}
interface INvmeDiagnosticsProvider {
  Task<NvmeDiagnosticResult> GetDiagnosticsAsync(string devicePath, CancellationToken ct);
}
interface IHistoryStore {
  Task SaveAsync(BenchmarkSession session);
  Task<IReadOnlyList<BenchmarkSession>> ListAsync();
}
6. Benchmark Engine Design (Core Need)
6.1 Request Model
BenchmarkRequest {
  string targetDirectory
  long fileSize = 1GB
  bool includeRandom
  bool includeSustained
  int sustainedDurationSec = 30
}
6.2 Engine Split
FileBenchmarkEngine: base file create, delete, buffer management
SequentialEngine: uses 1MB blocks, measures total time
RandomEngine: uses 4K blocks, random offsets, measures latency per IO, calculates IOPS
Formula: IOPS = 1000 / avgLatencyMs
Also: MB/s = (IOPS * 4K) / 1024 / 1024
SustainedEngine: writes 1GB in loop for 30 sec, samples speed every 1 sec, detects SLC cache drop
6.3 Result Model
BenchmarkResult {
  double seqReadMBs, seqWriteMBs
  double rndReadMBs, rndWriteMBs
  long rndReadIOPS, rndWriteIOPS
  double sustainedWriteMBs
  List<SpeedSample> sustainedGraph
  TimeSpan duration
}
6.4 Progress Model
BenchmarkProgress { string phase, double percent, double currentMBs }
7. Diagnostics Design (TBW, Temp)
7.1 SATA Path
WindowsAtaTransport -> AtaIdentifyParser + SmartParser -> SataResultMapper
Extract:

Temperature from SMART 194
Total LBAs Written -> TBW = LBAs * 512 / 1TB
Power On Hours, Wear Level
7.2 NVMe Path
WindowsNvmeTransport -> NvmeParser -> NvmeResultMapper
Extract from SMART Log:

Temperature (Composite Temp)
Data Units Written -> TBW = DataUnits * 512 * 1000 / 1TB
Percentage Used -> Health
7.3 Health Assessor
TbwInfo { double writtenTB, double ratedTB, double remainingPercent }
ThermalInfo { int idleC, int loadC, ThermalState Green/Yellow/Red }
Rules:

Temp > 80C => Red, > 75C => Yellow
TBW remaining < 10% => Warning
8. Concurrency and Safety
StorageOperationGate: only one operation per device at a time, using SemaphoreSlim per devicePath
CancellationTokenSource + generation counter in ViewModel to cancel stale runs
OwnedBenchmarkFile: creates file with FileOptions.DeleteOnClose and try/finally cleanup
9. Dependency Injection
Use Microsoft.Extensions.DependencyInjection
Composition Root in App.OnStartup:

services.AddSingleton<IDeviceDiscovery, WindowsDiskDiscovery>();
services.AddSingleton<ISataDiagnosticsProvider, WindowsSataDiagnosticsProvider>();
services.AddSingleton<INvmeDiagnosticsProvider, WindowsNvmeDiagnosticsProvider>();
services.AddTransient<IBenchmarkEngine, FileBenchmarkEngine>();
services.AddSingleton<IHistoryStore, SqliteHistoryStore>();
services.AddSingleton<ShellViewModel>();
ViewModels get dependencies via constructor, easy to mock.

10. History and Export
SqliteHistoryStore: table BenchmarkSessions (id, deviceId, resultJson, timestamp)
Exporters: BenchmarkReportExporter (JSON + TXT), DiagnosticReportExporter (redacted serial)
11. UI Flow (MVVM)
ShellVM.StartInitialScanAsync()
-> DiscoveryCoordinator -> IDeviceDiscovery
-> Device list -> user selects
-> SataVM / NvmeVM -> IDiagnosticsProvider
-> BenchmarkVM -> IBenchmarkEngine -> Progress -> Result -> IHistoryStore

12. Testing Strategy
Core: pure unit tests for parsers, mappers, policy
Infrastructure: integration tests with --hardware flag
Application: mock providers to test orchestrators
Convert tests/pixinit.Tests to xUnit for CI
13. CI
GitHub Actions:

Build on windows-latest
dotnet build
dotnet test
dotnet run --project tests/pixinit.Tests
14. Future Extensibility
Add USB detection: new provider implementing IDeviceDiscovery
Add new benchmark type: new IBenchmarkEngine implementation
No change in ViewModel needed due to interface abstraction