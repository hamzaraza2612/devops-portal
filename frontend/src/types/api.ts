// Mirrors DevOpsPortal.Application/Dtos/**/*.cs and Domain/Enums/*.cs exactly
// (property names, casing, numeric enum values) so this file stays the single
// source of truth for what the backend actually returns. Update alongside any
// backend DTO change — do not let it drift.

// TS enums emit runtime code that `erasableSyntaxOnly` (this project's
// tsconfig) rejects, so every backend enum is a `const` object + derived
// union type instead. Usage (`DeploymentStatus.Pending`, `Record<DeploymentStatus, ...>`)
// is identical to a real enum; only the declaration differs.

export const DeploymentStatus = {
  Pending: 0,
  Queued: 1,
  Running: 2,
  Succeeded: 3,
  Failed: 4,
  Cancelled: 5,
} as const;
export type DeploymentStatus = (typeof DeploymentStatus)[keyof typeof DeploymentStatus];

export const ApprovalStatus = {
  PendingApproval: 0,
  Approved: 1,
  Rejected: 2,
} as const;
export type ApprovalStatus = (typeof ApprovalStatus)[keyof typeof ApprovalStatus];

export const DeploymentLogLevel = {
  Info: 0,
  Warning: 1,
  Error: 2,
} as const;
export type DeploymentLogLevel = (typeof DeploymentLogLevel)[keyof typeof DeploymentLogLevel];

export const HealthCheckType = {
  None: 0,
  Http: 1,
  TcpPort: 2,
} as const;
export type HealthCheckType = (typeof HealthCheckType)[keyof typeof HealthCheckType];

export const DeploymentMode = {
  LegacyFilesystem: 0,
  ContainerImage: 1,
} as const;
export type DeploymentMode = (typeof DeploymentMode)[keyof typeof DeploymentMode];

export const ImageTagStrategy = {
  CommitSha: 0,
  BuildNumber: 1,
  SemVer: 2,
} as const;
export type ImageTagStrategy = (typeof ImageTagStrategy)[keyof typeof ImageTagStrategy];

export const RepositoryProvider = {
  GitLab: 0,
} as const;
export type RepositoryProvider = (typeof RepositoryProvider)[keyof typeof RepositoryProvider];

export const SshAuthMethod = {
  PrivateKey: 0,
  Password: 1,
} as const;
export type SshAuthMethod = (typeof SshAuthMethod)[keyof typeof SshAuthMethod];

export const ContainerState = {
  Unknown: 0,
  Running: 1,
  Exited: 2,
  Restarting: 3,
  Paused: 4,
  Created: 5,
  Unhealthy: 6,
} as const;
export type ContainerState = (typeof ContainerState)[keyof typeof ContainerState];

// --- Environments (reference data) ---

export interface EnvironmentDefinitionDto {
  id: string;
  name: string;
  sortOrder: number;
  isProductionLike: boolean;
  isActive: boolean;
}

// --- Auth / Users ---
// Phase 12 replaced the Role/Permission catalog with a simplified
// User.IsAdmin / User.CanApproveProduction / per-environment access model
// (see PROJECT_STATE.md) — `roles`/`permissions` below are still present
// (synthesized server-side) so every existing permission-gated UI check
// keeps working unchanged.

export interface UserDto {
  id: string;
  username: string;
  email: string;
  fullName: string;
  isActive: boolean;
  isAdmin: boolean;
  canApproveProduction: boolean;
  environmentAccess: EnvironmentDefinitionDto[];
  createdAt: string;
  lastLoginAt: string | null;
  roles: string[];
  permissions: string[];
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  user: UserDto;
}

// --- Users (admin management) ---

export interface CreateUserRequest {
  username: string;
  email: string;
  fullName: string;
  password: string;
  isAdmin: boolean;
  canApproveProduction: boolean;
  environmentDefinitionIds: string[];
}

export interface UpdateUserRequest {
  email: string;
  fullName: string;
  isActive: boolean;
  isAdmin: boolean;
  canApproveProduction: boolean;
  environmentDefinitionIds: string[];
}

export interface AdminResetPasswordRequest {
  newPassword: string;
}

export interface ChangeOwnPasswordRequest {
  currentPassword: string;
  newPassword: string;
}

// --- Build servers (integrations) ---

export const BuildProviderType = {
  Jenkins: 0,
} as const;
export type BuildProviderType = (typeof BuildProviderType)[keyof typeof BuildProviderType];

export interface BuildServerDto {
  id: string;
  name: string;
  description: string | null;
  providerType: BuildProviderType;
  baseUrl: string;
  username: string | null;
  apiTokenEnvVarName: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateBuildServerRequest {
  name: string;
  description: string | null;
  providerType: BuildProviderType;
  baseUrl: string;
  username: string | null;
  apiTokenEnvVarName: string | null;
}

export interface UpdateBuildServerRequest {
  name: string;
  description: string | null;
  baseUrl: string;
  username: string | null;
  apiTokenEnvVarName: string | null;
  isActive: boolean;
}

// --- Applications ---

export interface ApplicationDto {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  deploymentMode: DeploymentMode;
  repositoryId: string | null;
  repositoryName: string | null;
  sourcePath: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateApplicationRequest {
  name: string;
  slug: string;
  description: string | null;
  deploymentMode: DeploymentMode;
  repositoryId: string | null;
  sourcePath: string | null;
}

export interface UpdateApplicationRequest {
  name: string;
  description: string | null;
  deploymentMode: DeploymentMode;
  repositoryId: string | null;
  sourcePath: string | null;
  isActive: boolean;
}

export interface ApplicationEnvironmentDto {
  id: string;
  applicationId: string;
  environmentDefinitionId: string;
  environmentName: string;
  targetServerId: string;
  targetServerName: string;
  branchName: string | null;
  deploymentRootPath: string | null;
  publishSubPath: string;
  backupSubPath: string;
  backupRetentionCount: number | null;
  composeFilePath: string;
  composeProjectName: string | null;
  serviceName: string | null;
  containerName: string | null;
  externalNetworkName: string | null;
  useDownWithVolumesOnDeploy: boolean;
  healthCheckType: HealthCheckType;
  healthCheckEndpoint: string | null;
  healthCheckIntervalSeconds: number;
  healthCheckTimeoutSeconds: number;
  applicationUrl: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface UpsertApplicationEnvironmentRequest {
  targetServerId: string;
  branchName: string | null;
  deploymentRootPath: string | null;
  publishSubPath: string;
  backupSubPath: string;
  backupRetentionCount: number | null;
  composeFilePath: string;
  composeProjectName: string | null;
  serviceName: string | null;
  containerName: string | null;
  externalNetworkName: string | null;
  useDownWithVolumesOnDeploy: boolean;
  healthCheckType: HealthCheckType;
  healthCheckEndpoint: string | null;
  healthCheckIntervalSeconds: number;
  healthCheckTimeoutSeconds: number;
  applicationUrl: string | null;
  isActive: boolean;
}

// --- Repositories (GitLab) ---

export interface RepositoryDto {
  id: string;
  name: string;
  url: string;
  provider: RepositoryProvider;
  description: string | null;
  defaultBranch: string | null;
  username: string | null;
  hasAccessToken: boolean;
  accessTokenEnvVarName: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface CreateRepositoryRequest {
  name: string;
  url: string;
  provider: RepositoryProvider;
  description: string | null;
  defaultBranch: string | null;
  username: string | null;
  accessTokenEnvVarName: string | null;
}

export interface UpdateRepositoryRequest {
  name: string;
  url: string;
  provider: RepositoryProvider;
  description: string | null;
  defaultBranch: string | null;
  username: string | null;
  accessTokenEnvVarName: string | null;
  isActive: boolean;
}

export interface SetRepositoryAccessTokenRequest {
  value: string;
}

export interface RepositoryConnectionTestResultDto {
  connected: boolean;
  authenticatedAs: string | null;
  projectName: string | null;
  errorMessage: string | null;
  testedAt: string;
}

// --- Target servers (SSH) ---

export interface AllowedDeploymentRootDto {
  id: string;
  targetServerId: string;
  rootPath: string;
  description: string | null;
  isActive: boolean;
}

export interface TargetServerDto {
  id: string;
  name: string;
  description: string | null;
  hostname: string | null;
  sshPort: number;
  sshUsername: string | null;
  sshAuthMethod: SshAuthMethod;
  hasSshCredential: boolean;
  hasSshPassphrase: boolean;
  isActive: boolean;
  createdAt: string;
  allowedDeploymentRoots: AllowedDeploymentRootDto[];
}

export interface CreateTargetServerRequest {
  name: string;
  description: string | null;
  hostname: string | null;
  sshPort: number;
  sshUsername: string | null;
  sshAuthMethod: SshAuthMethod;
}

export interface UpdateTargetServerRequest {
  name: string;
  description: string | null;
  hostname: string | null;
  sshPort: number;
  sshUsername: string | null;
  sshAuthMethod: SshAuthMethod;
  isActive: boolean;
}

export interface SetSshCredentialRequest {
  value: string;
}

export interface SetSshPassphraseRequest {
  value: string | null;
}

export interface TargetServerConnectionTestResultDto {
  sshConnected: boolean;
  authenticatedUser: string | null;
  osInfo: string | null;
  dockerAvailable: boolean;
  dockerVersion: string | null;
  composeAvailable: boolean;
  composeVersion: string | null;
  errorMessage: string | null;
  testedAt: string;
}

export interface CreateAllowedDeploymentRootRequest {
  rootPath: string;
  description: string | null;
}

// --- Deployments ---

export interface DeploymentDto {
  id: string;
  applicationId: string;
  applicationName: string;
  environmentDefinitionId: string;
  environmentName: string;
  commitSha: string;
  commitMessage: string | null;
  commitAuthor: string | null;
  branch: string | null;
  imageReference: string | null;
  versionLabel: string | null;
  status: DeploymentStatus;
  isRollback: boolean;
  rollbackOfDeploymentId: string | null;
  promotionRequestId: string | null;
  requestedByUserId: string;
  requestedByUsername: string | null;
  requestedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
  healthCheckPassed: boolean | null;
  healthCheckDetail: string | null;
}

export interface DeploymentLogEntryDto {
  sequence: number;
  timestamp: string;
  level: DeploymentLogLevel;
  message: string;
}

export interface CreateDevDeploymentRequest {
  commitSha: string | null;
  commitMessage: string | null;
  commitAuthor: string | null;
  branch: string | null;
  releaseId: string | null;
}

export interface RollbackRequest {
  targetDeploymentId: string;
}

export interface DeploymentStatusSummaryDto {
  applicationId: string;
  environmentDefinitionId: string;
  environmentName: string;
  currentDeployment: DeploymentDto | null;
  latestAttempt: DeploymentDto | null;
  pendingPromotionRequestId: string | null;
}

// --- Promotions ---

export interface PromotionRequestDto {
  id: string;
  applicationId: string;
  applicationName: string;
  fromEnvironmentDefinitionId: string;
  fromEnvironmentName: string;
  toEnvironmentDefinitionId: string;
  toEnvironmentName: string;
  sourceDeploymentId: string;
  commitSha: string;
  fromBranch: string | null;
  toBranch: string | null;
  branchPromotionSucceeded: boolean | null;
  branchPromotionDetail: string | null;
  status: ApprovalStatus;
  requestedByUserId: string;
  requestedByUsername: string | null;
  requestedAt: string;
  decidedByUserId: string | null;
  decidedByUsername: string | null;
  decidedAt: string | null;
  decisionNotes: string | null;
  notifiedAt: string | null;
  requiresCtoApproval: boolean;
  ctoApprovalStatus: ApprovalStatus | null;
  ctoDecidedByUserId: string | null;
  ctoDecidedByUsername: string | null;
  ctoDecidedAt: string | null;
  ctoNotifiedAt: string | null;
  linkedDeploymentId: string | null;
  linkedDeploymentStatus: DeploymentStatus | null;
}

export interface CreatePromotionRequest {
  sourceDeploymentId: string;
}

export interface DecidePromotionRequest {
  notes: string | null;
}

// --- Build configuration / Builds / Releases ---

export interface BuildConfigurationDto {
  id: string;
  applicationId: string;
  projectOrSolutionPath: string;
  publishConfiguration: string;
  dockerfilePath: string | null;
  imageRegistry: string | null;
  imageName: string | null;
  imageTagStrategy: ImageTagStrategy;
}

export const BuildStatus = {
  Queued: 0,
  Running: 1,
  Succeeded: 2,
  Failed: 3,
} as const;
export type BuildStatus = (typeof BuildStatus)[keyof typeof BuildStatus];

export interface ReleaseDto {
  id: string;
  applicationId: string;
  buildRequestId: string;
  commitSha: string;
  branch: string | null;
  buildNumber: number;
  imageReference: string;
  buildStatus: BuildStatus;
  createdAt: string;
}

// --- Git commit lookup ---
// GitProviderResult<T> never throws for expected failures (unreachable host,
// missing branch, unsupported provider) — Success=false + ErrorMessage instead.

export interface GitCommitInfoDto {
  sha: string;
  message: string;
  authorName: string | null;
  authorEmail: string | null;
  committedAt: string | null;
}

export interface GitProviderResultDto<T> {
  success: boolean;
  data: T | null;
  errorMessage: string | null;
}

// --- Audit ---

export const AuditResult = {
  Success: 0,
  Failure: 1,
} as const;
export type AuditResult = (typeof AuditResult)[keyof typeof AuditResult];

export interface AuditLogDto {
  id: string;
  timestamp: string;
  userId: string | null;
  username: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  ipAddress: string | null;
  result: AuditResult;
  details: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// --- Containers (live status/control — master requirements §8/§9/§10) ---

/** A `docker stats --no-stream` snapshot for one container. Memory usage/
 * limit and network/block I/O are Docker's own human-readable formatted
 * strings (e.g. "128MiB", "1.2kB / 3.4kB"), not exact byte counts. */
export interface ContainerStatsDto {
  cpuPercent: number | null;
  memoryUsage: string | null;
  memoryLimit: string | null;
  memoryPercent: number | null;
  networkIO: string | null;
  blockIO: string | null;
  pidCount: number | null;
}

export interface ContainerInfoDto {
  serviceName: string;
  containerName: string;
  image: string;
  imageTag: string | null;
  state: ContainerState;
  dockerHealthStatus: string | null;
  startedAt: string | null;
  uptime: string | null;
  restartCount: number;
  ports: string[];
  stats: ContainerStatsDto | null;
}

/** Recent `docker logs --tail N` output for one container, fetched on demand. */
export interface ContainerLogsDto {
  containerName: string;
  success: boolean;
  logs: string;
}

export interface HealthCheckStatusDto {
  type: HealthCheckType;
  lastProbePassed: boolean | null;
  lastProbeDetail: string | null;
  lastProbeAt: string;
  lastSuccessfulCheckAt: string | null;
}

export interface ContainerEnvironmentStatusDto {
  applicationId: string;
  environmentDefinitionId: string;
  environmentName: string;
  isConfigured: boolean;
  isReachable: boolean;
  unreachableReason: string | null;
  targetServerName: string | null;
  expectedServiceName: string | null;
  expectedContainerName: string | null;
  containers: ContainerInfoDto[];
  healthCheck: HealthCheckStatusDto | null;
  currentImageOrVersion: string | null;
  lastRestartAt: string | null;
  latestDeploymentId: string | null;
  latestDeploymentStatus: DeploymentStatus | null;
}

export interface ContainerActionResultDto {
  success: boolean;
  message: string;
  performedAt: string;
}

export interface RecreateWithVolumesRequest {
  confirm: boolean;
}

// --- Secrets / Credentials ---
// A "Credential" in the UI is just a SecretReference with the structured
// display fields (Username/Host/Port/DatabaseName) filled in — same
// backend model, no separate entity. The actual value is never present on
// SecretReferenceDto; only RevealAsync/RevealedSecretDto ever returns it,
// via the explicit, separately-permissioned "Show password" action.

export const SecretCategory = {
  Database: 0,
  Api: 1,
  Registry: 2,
  GitLab: 3,
  Jenkins: 4,
  Smtp: 5,
  Server: 6,
  Other: 7,
} as const;
export type SecretCategory = (typeof SecretCategory)[keyof typeof SecretCategory];

export const SecretScope = {
  Global: 0,
  Application: 1,
  ApplicationEnvironment: 2,
} as const;
export type SecretScope = (typeof SecretScope)[keyof typeof SecretScope];

export interface SecretReferenceDto {
  id: string;
  name: string;
  category: SecretCategory;
  scope: SecretScope;
  applicationId: string | null;
  applicationName: string | null;
  environmentDefinitionId: string | null;
  environmentName: string | null;
  description: string | null;
  username: string | null;
  host: string | null;
  port: number | null;
  databaseName: string | null;
  providerKey: string;
  isActive: boolean;
  createdByUserId: string;
  createdByUsername: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateSecretReferenceRequest {
  name: string;
  category: SecretCategory;
  scope: SecretScope;
  applicationId: string | null;
  environmentDefinitionId: string | null;
  description: string | null;
  value: string;
  username: string | null;
  host: string | null;
  port: number | null;
  databaseName: string | null;
}

export interface UpdateSecretReferenceRequest {
  description: string | null;
  isActive: boolean;
  value: string | null;
  username: string | null;
  host: string | null;
  port: number | null;
  databaseName: string | null;
}

export interface RevealedSecretDto {
  value: string;
}
