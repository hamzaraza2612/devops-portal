using System.Runtime.CompilerServices;

// Lets the test project exercise internal-only helpers (PosixShellEscaper,
// ComposeOperationArgs) directly rather than only through their public
// callers — those are pure, deterministic logic worth testing in isolation.
[assembly: InternalsVisibleTo("DevOpsPortal.Tests")]
