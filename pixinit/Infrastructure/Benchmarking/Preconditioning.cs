namespace pixinit.Infrastructure.Benchmarking;
internal static class Preconditioning
{
    internal const string TrimStatus = "Skipped: owned file retained between passes for read-after-write. No file-level TRIM or volume flush issued. Normal deletion at final cleanup leaves deallocation/TRIM to Windows; completion not verified.";
    internal static Task RecoverAsync(CancellationToken token) => Task.Delay(2000, token);
}
