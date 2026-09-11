import { api } from './client';
import type {
  AdminResetPasswordRequest,
  AllowedDeploymentRootDto,
  ApplicationDto,
  ApplicationEnvironmentDto,
  AuditLogDto,
  BuildConfigurationDto,
  BuildServerDto,
  ChangeOwnPasswordRequest,
  ContainerActionResultDto,
  ContainerEnvironmentStatusDto,
  CreateAllowedDeploymentRootRequest,
  CreateApplicationRequest,
  CreateBuildServerRequest,
  CreateDevDeploymentRequest,
  CreatePromotionRequest,
  CreateRepositoryRequest,
  CreateSecretReferenceRequest,
  CreateTargetServerRequest,
  CreateUserRequest,
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
  RecreateWithVolumesRequest,
  ReleaseDto,
  RepositoryConnectionTestResultDto,
  RepositoryDto,
  RevealedSecretDto,
  RollbackRequest,
  SecretCategory,
  SecretReferenceDto,
  SetRepositoryAccessTokenRequest,
  SetSshCredentialRequest,
  SetSshPassphraseRequest,
  TargetServerConnectionTestResultDto,
  TargetServerDto,
  UpdateApplicationRequest,
  UpdateBuildServerRequest,
  UpdateRepositoryRequest,
  UpdateSecretReferenceRequest,
  UpdateTargetServerRequest,
  UpdateUserRequest,
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

  releases: (applicationId: string) => api.get<ReleaseDto[]>(`/applications/${applicationId}/releases`),

  // --- Live container status/control (master requirements §8/§9/§10) ---
  containerStatus: (applicationId: string, environmentDefinitionId: string) =>
    api.get<ContainerEnvironmentStatusDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/containers`),
  restartContainers: (applicationId: string, environmentDefinitionId: string) =>
    api.post<ContainerActionResultDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/containers/restart`),
  startContainers: (applicationId: string, environmentDefinitionId: string) =>
    api.post<ContainerActionResultDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/containers/start`),
  stopContainers: (applicationId: string, environmentDefinitionId: string) =>
    api.post<ContainerActionResultDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/containers/stop`),
  /** docker compose down -v / up -d — destroys volumes; requires an explicit Confirm: true. */
  recreateContainers: (applicationId: string, environmentDefinitionId: string, body: RecreateWithVolumesRequest) =>
    api.post<ContainerActionResultDto>(`/applications/${applicationId}/environments/${environmentDefinitionId}/containers/recreate`, body),
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

export const UsersApi = {
  list: () => api.get<UserDto[]>('/users'),
  get: (id: string) => api.get<UserDto>(`/users/${id}`),
  create: (body: CreateUserRequest) => api.post<UserDto>('/users', body),
  update: (id: string, body: UpdateUserRequest) => api.put<UserDto>(`/users/${id}`, body),
  resetPassword: (id: string, body: AdminResetPasswordRequest) => api.post<void>(`/users/${id}/reset-password`, body),
  changeOwnPassword: (body: ChangeOwnPasswordRequest) => api.post<void>('/users/me/change-password', body),
};

export const RepositoriesApi = {
  list: () => api.get<RepositoryDto[]>('/repositories'),
  get: (id: string) => api.get<RepositoryDto>(`/repositories/${id}`),
  create: (body: CreateRepositoryRequest) => api.post<RepositoryDto>('/repositories', body),
  update: (id: string, body: UpdateRepositoryRequest) => api.put<RepositoryDto>(`/repositories/${id}`, body),
  setAccessToken: (id: string, body: SetRepositoryAccessTokenRequest) =>
    api.put<RepositoryDto>(`/repositories/${id}/access-token`, body),
  /** Master requirements §3 "Test GitLab Connection" — actually reaches GitLab; never fabricated. */
  testConnection: (id: string) => api.post<RepositoryConnectionTestResultDto>(`/repositories/${id}/test-connection`),
};

export const TargetServersApi = {
  list: () => api.get<TargetServerDto[]>('/target-servers'),
  get: (id: string) => api.get<TargetServerDto>(`/target-servers/${id}`),
  create: (body: CreateTargetServerRequest) => api.post<TargetServerDto>('/target-servers', body),
  update: (id: string, body: UpdateTargetServerRequest) => api.put<TargetServerDto>(`/target-servers/${id}`, body),
  addAllowedRoot: (targetServerId: string, body: CreateAllowedDeploymentRootRequest) =>
    api.post<AllowedDeploymentRootDto>(`/target-servers/${targetServerId}/allowed-roots`, body),
  setSshCredential: (id: string, body: SetSshCredentialRequest) => api.put<TargetServerDto>(`/target-servers/${id}/ssh-credential`, body),
  setSshPassphrase: (id: string, body: SetSshPassphraseRequest) => api.put<TargetServerDto>(`/target-servers/${id}/ssh-passphrase`, body),
  /** Master requirements §6 "Test Connection" — actually connects over SSH; never fabricated. */
  testConnection: (id: string) => api.post<TargetServerConnectionTestResultDto>(`/target-servers/${id}/test-connection`),
};

export const BuildServersApi = {
  list: () => api.get<BuildServerDto[]>('/build-servers'),
  get: (id: string) => api.get<BuildServerDto>(`/build-servers/${id}`),
  create: (body: CreateBuildServerRequest) => api.post<BuildServerDto>('/build-servers', body),
  update: (id: string, body: UpdateBuildServerRequest) => api.put<BuildServerDto>(`/build-servers/${id}`, body),
};

export const SecretsApi = {
  list: (filter?: { applicationId?: string; environmentDefinitionId?: string; category?: SecretCategory }) => {
    const params = new URLSearchParams();
    if (filter?.applicationId) params.set('applicationId', filter.applicationId);
    if (filter?.environmentDefinitionId) params.set('environmentDefinitionId', filter.environmentDefinitionId);
    if (filter?.category !== undefined) params.set('category', String(filter.category));
    const qs = params.toString();
    return api.get<SecretReferenceDto[]>(`/secrets${qs ? `?${qs}` : ''}`);
  },
  get: (id: string) => api.get<SecretReferenceDto>(`/secrets/${id}`),
  create: (body: CreateSecretReferenceRequest) => api.post<SecretReferenceDto>('/secrets', body),
  update: (id: string, body: UpdateSecretReferenceRequest) => api.put<SecretReferenceDto>(`/secrets/${id}`, body),
  delete: (id: string) => api.del<void>(`/secrets/${id}`),
  /** The controlled "Show password" action — only called when the user explicitly clicks it. */
  reveal: (id: string) => api.post<RevealedSecretDto>(`/secrets/${id}/reveal`),
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
