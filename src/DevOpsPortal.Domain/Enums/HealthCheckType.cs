namespace DevOpsPortal.Domain.Enums;

/// <summary>Configuration only — Phase 2 does not execute health probes.</summary>
public enum HealthCheckType
{
    None = 0,
    Http = 1,
    TcpPort = 2,
}
