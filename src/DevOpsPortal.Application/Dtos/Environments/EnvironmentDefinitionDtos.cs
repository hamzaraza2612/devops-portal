namespace DevOpsPortal.Application.Dtos.Environments;

public record EnvironmentDefinitionDto(Guid Id, string Name, int SortOrder, bool IsProductionLike, bool IsActive);

/// <summary>Deliberately narrow: Name and SortOrder are immutable via this
/// request — see EnvironmentDefinitionService.UpdateAsync's doc comment for
/// why. IsActive doubles as this entity's "remove" affordance (a hard delete
/// would cascade-remove every UserEnvironmentAccess grant referencing it —
/// see AppDbContext — which is far too destructive for a routine admin
/// action); IsProductionLike controls whether promoting into this
/// environment requires a separate CTO approval.</summary>
public record UpdateEnvironmentDefinitionRequest(bool IsProductionLike, bool IsActive);
