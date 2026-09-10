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

// --- Auth / Users ---

export interface UserDto {
  id: string;
  username: string;
  email: string;
  fullName: string;
  isActive: boolean;
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

// --- Roles ---

export interface RoleDto {
  id: string;
  name: string;
  description: string;
  isSystem: boolean;
  permissions: string[];
}

export interface PermissionDto {
  id: string;
  code: string;
  description: string;
}

// --- Environments (reference data) ---

export interface EnvironmentDefinitionDto {
  id: string;
  name: string;
  sortOrder: number;
  isProductionLike: boolean;
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

// --- Repositories / Target servers ---

export interface RepositoryDto {
  id: string;
  name: string;
  url: string;
  provider: RepositoryProvider;
  description: string | null;
  accessTokenEnvVarName: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface TargetServerDto {
  id: string;
  name: string;
  description: string | null;
  hostname: string;
  isActive: boolean;
  createdAt: string;
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
  commitSha: string;
  commitMessage: string | null;
  commitAuthor: string | null;
  branch: string | null;
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

// --- Build configuration ---

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
