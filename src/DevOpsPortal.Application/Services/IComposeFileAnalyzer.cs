using DevOpsPortal.Application.Dtos.Discovery;

namespace DevOpsPortal.Application.Services;

/// <summary>
/// Read-only analysis of a docker-compose.yml an admin supplies (pasted/uploaded
/// content — never fetched from a live server). Purely computational: no
/// filesystem, network, shell, or Docker access — discovery must never
/// auto-deploy anything.
/// </summary>
public interface IComposeFileAnalyzer
{
    ComposeAnalysisResult Analyze(string content);
}
