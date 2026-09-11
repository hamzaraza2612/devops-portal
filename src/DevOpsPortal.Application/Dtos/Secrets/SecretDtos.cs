using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Secrets;

/// <summary>Metadata plus non-secret structured display fields (Username/Host/
/// Port/DatabaseName — the "which login, which server" context the
/// Credentials UI shows without revealing anything) — deliberately has no
/// Value field. Only RevealedSecretDto (returned solely by
/// ISecretReferenceService.RevealAsync, gated by secrets.reveal) ever carries
/// the actual secret value out of the API.</summary>
public record SecretReferenceDto(
    Guid Id,
    string Name,
    SecretCategory Category,
    SecretScope Scope,
    Guid? ApplicationId,
    string? ApplicationName,
    Guid? EnvironmentDefinitionId,
    string? EnvironmentName,
    string? Description,
    string? Username,
    string? Host,
    int? Port,
    string? DatabaseName,
    string ProviderKey,
    bool IsActive,
    Guid CreatedByUserId,
    string? CreatedByUsername,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Value is write-only — present on the request to create the secret,
/// never echoed back on SecretReferenceDto or any other response (only
/// RevealedSecretDto, via the explicit, separately-permissioned reveal
/// action, ever returns it).</summary>
public record CreateSecretReferenceRequest(
    string Name,
    SecretCategory Category,
    SecretScope Scope,
    Guid? ApplicationId,
    Guid? EnvironmentDefinitionId,
    string? Description,
    string Value,
    string? Username = null,
    string? Host = null,
    int? Port = null,
    string? DatabaseName = null);

/// <summary>Name/Category/Scope/ApplicationId/EnvironmentDefinitionId are
/// immutable after creation — changing a secret's scope after the fact is
/// exactly the kind of silent-widening-of-access this phase's environment
/// isolation requirement exists to prevent. Value is optional: omit it to
/// change only Description/Username/Host/Port/DatabaseName/IsActive; supply
/// it to rotate the value in place (same reference, same scope, new
/// ciphertext).</summary>
public record UpdateSecretReferenceRequest(
    string? Description, bool IsActive, string? Value, string? Username = null, string? Host = null, int? Port = null, string? DatabaseName = null);

/// <summary>The one and only DTO in this codebase that carries a secret's
/// plaintext value out of the API — returned solely by
/// ISecretReferenceService.RevealAsync, gated by secrets.reveal (distinct
/// from secrets.view), and never included in any list/get response. A
/// controlled "Show password" action, not something the UI ever preloads.</summary>
public record RevealedSecretDto(string Value);
