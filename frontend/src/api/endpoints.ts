import { api } from './client';
import type {
  ApplicationDto,
  ApplicationEnvironmentDto,
  AuditLogDto,
  BuildConfigurationDto,
  CreateApplicationRequest,
  CreateDevDeploymentRequest,
  CreatePromotionRequest,
  DecidePromotionRequest,
  DeploymentDto,
  DeploymentLogEntryDto,
  DeploymentStatus,
  DeploymentStatusSummaryDto,
  EnvironmentDefinitionDto,
  GitCommitInfoDto,
  GitProviderResultDto,
  LoginRequest,
  LoginResponse,
  PagedResult,
  PromotionRequestDto,
  RepositoryDto,
  RoleDto,
  RollbackRequest,
  TargetServerDto,
  UpdateApplicationRequest,
  UpsertApplicationEnvironmentRequest,
  UserDto,
} from '../types/api';

export const AuthApi = {
  login: (body: LoginRequest) => api.post<LoginResponse>('/auth/login', body),
  me: () => api.get<UserDto>('/auth/me'),
};

export const ApplicationsApi = {
  list: () => api.get<ApplicationDto[]>('/applications'),
  get: (id: string) => api.get<ApplicationDto>(`/applications/${id}`),
  create: (body: CreateApplicationRequest) => api.post<ApplicationDto>('/applications', body),
  update: (id: string, body: UpdateApplicationRequest) => api.put<ApplicationDto>(`/applications/${id}`, body),

  environments: (applicationId: string) =>
    api.get<ApplicationEnvironmentDto[]>(`/applications/${applicationId}/environments`),
  environment: (applicationId: string, environmentDefinitionId: string) =>
    api.get<ApplicationEnvironmentDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}`),
  upsertEnvironment: (applicationId: string, environmentDefinitionId: string, body: UpsertApplicationEnvironmentRequest) =>
    api.put<ApplicationEnvironmentDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}`, body),

  latestCommit: (applicationId: string, environmentDefinitionId: string) =>
    api.get<GitProviderResultDto<GitCommitInfoDto>>(
      `/applications/${applicationId}/environments/${environmentDefinitionId}/commits/latest`,
    ),
  recentCommits: (applicationId: string, environmentDefinitionId: string, count = 20) =>
    api.get<GitProviderResultDto<GitCommitInfoDto[]>>(
      `/applications/${applicationId}/environments/${environmentDefinitionId}/commits/recent?count=${count}`,
    ),

  status: (applicationId: string, environmentDefinitionId: string) =>
    api.get<DeploymentStatusSummaryDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/status`),

  deployToDev: (applicationId: string, body: CreateDevDeploymentRequest) =>
    api.post<DeploymentDto>(`/applications/${applicationId}/deployments/dev`, body),

  requestPromotion: (applicationId: string, environmentDefinitionId: string, body: CreatePromotionRequest) =>
    api.post<PromotionRequestDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/promotions`, body),

  rollback: (applicationId: string, environmentDefinitionId: string, body: RollbackRequest) =>
    api.post<DeploymentDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/rollback`, body),

  buildConfiguration: (applicationId: string) =>
    api.get<BuildConfigurationDto | null>(`/applications/${applicationId}/build-configuration`),
};

export const DeploymentsApi = {
  list: (filter?: { applicationId?: string; environmentDefinitionId?: string; status?: DeploymentStatus }) => {
    const params = new URLSearchParams();
    if (filter?.applicationId) params.set('applicationId', filter.applicationId);
    if (filter?.environmentDefinitionId) params.set('environmentDefinitionId', filter.environmentDefinitionId);
    if (filter?.status !== undefined) params.set('status', String(filter.status));
    const qs = params.toString();
    return api.get<DeploymentDto[]>(`/deployments${qs ? `?${qs}` : ''}`);
  },
  get: (id: string) => api.get<DeploymentDto>(`/deployments/${id}`),
  logs: (id: string) => api.get<DeploymentLogEntryDto[]>(`/deployments/${id}/logs`),
};

export const PromotionsApi = {
  /** By default only Status===PendingApproval. Pass includeApprovedAwaitingDeploy
   * to also surface promotions that were approved but have no Deployment row yet
   * — otherwise an approved-not-yet-deployed promotion would be undiscoverable
   * through this endpoint (approving never creates the Deployment row itself). */
  listPending: (filter?: { applicationId?: string; toEnvironmentDefinitionId?: string; includeApprovedAwaitingDeploy?: boolean }) => {
    const params = new URLSearchParams();
    if (filter?.applicationId) params.set('applicationId', filter.applicationId);
    if (filter?.toEnvironmentDefinitionId) params.set('toEnvironmentDefinitionId', filter.toEnvironmentDefinitionId);
    if (filter?.includeApprovedAwaitingDeploy) params.set('includeApprovedAwaitingDeploy', 'true');
    const qs = params.toString();
    return api.get<PromotionRequestDto[]>(`/promotions${qs ? `?${qs}` : ''}`);
  },
  get: (id: string) => api.get<PromotionRequestDto>(`/promotions/${id}`),
  approve: (id: string, body: DecidePromotionRequest) => api.post<PromotionRequestDto>(`/promotions/${id}/approve`, body),
  reject: (id: string, body: DecidePromotionRequest) => api.post<PromotionRequestDto>(`/promotions/${id}/reject`, body),
  deploy: (id: string) => api.post<DeploymentDto>(`/promotions/${id}/deploy`),
};

export const EnvironmentsApi = {
  list: () => api.get<EnvironmentDefinitionDto[]>('/environments'),
};

export const RolesApi = {
  list: () => api.get<RoleDto[]>('/roles'),
};

export const UsersApi = {
  list: () => api.get<UserDto[]>('/users'),
};

export const RepositoriesApi = {
  list: () => api.get<RepositoryDto[]>('/repositories'),
};

export const TargetServersApi = {
  list: () => api.get<TargetServerDto[]>('/target-servers'),
};

export const AuditApi = {
  query: (params: { userId?: string; action?: string; from?: string; to?: string; page?: number; pageSize?: number }) => {
    const qs = new URLSearchParams();
    if (params.userId) qs.set('userId', params.userId);
    if (params.action) qs.set('action', params.action);
    if (params.from) qs.set('from', params.from);
    if (params.to) qs.set('to', params.to);
    if (params.page) qs.set('page', String(params.page));
    if (params.pageSize) qs.set('pageSize', String(params.pageSize));
    return api.get<PagedResult<AuditLogDto>>(`/audit?${qs.toString()}`);
  },
};

// Re-exported so callers importing from endpoints.ts don't also need a
// separate import from types/api for this one request-shape type.
export type { UpsertApplicationEnvironmentRequest } from '../types/api';
