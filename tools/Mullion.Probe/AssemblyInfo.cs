using System.Runtime.Versioning;

// The probe talks directly to Win32, so it is genuinely Windows-only. Declaring
// that satisfies CA1416 honestly rather than suppressing it - the analyzer
// firing here is the Core/Platform boundary doing its job.
[assembly: SupportedOSPlatform("windows")]
