namespace DevOpsPortal.Application.Dtos.Environments;

public record EnvironmentDefinitionDto(Guid Id, string Name, int SortOrder, bool IsProductionLike, bool IsActive);
