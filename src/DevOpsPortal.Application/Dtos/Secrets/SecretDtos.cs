using DevOpsPortal.Domain.Enums;

namespace DevOpsPortal.Application.Dtos.Secrets;

/// <summary>Metadata only — deliberately has no Value field. There is no DTO,
/// anywhere in this codebase, that carries a secret's plaintext value out of
/// the API (master requirements §3: "Never return secret values in API
/// responses").</summary>
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
    string ProviderKey,
    bool IsActive,
    Guid CreatedByUserId,
    string? CreatedByUsername,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Value is write-only — present on the request to create the secret,
/// never echoed back on SecretReferenceDto or any other response.</summary>
public record CreateSecretReferenceRequest(
    string Name,
    SecretCategory Category,
    SecretScope Scope,
    Guid? ApplicationId,
    Guid? EnvironmentDefinitionId,
    string? Description,
    string Value);

/// <summary>Name/Category/Scope/ApplicationId/EnvironmentDefinitionId are
/// immutable after creation — changing a secret's scope after the fact is
/// exactly the kind of silent-widening-of-access this phase's environment
/// isolation requirement exists to prevent. Value is optional: omit it to
/// change only Description/IsActive; supply it to rotate the value in place
/// (same reference, same scope, new ciphertext).</summary>
public record UpdateSecretReferenceRequest(string? Description, bool IsActive, string? Value);
