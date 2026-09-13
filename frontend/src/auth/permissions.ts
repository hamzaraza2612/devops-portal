// Mirrors DevOpsPortal.Domain/Constants/PermissionCodes.cs exactly. The
// backend is the sole source of authorization truth (see auth/AuthContext
// docstring) — this file only decides what the UI *offers*, never what it
// *allows*: every action a button here triggers is re-checked server-side.

export const Permissions = {
  UsersView: 'users.view',
  UsersManage: 'users.manage',
  AuditView: 'audit.view',
  ApplicationsView: 'applications.view',
  ApplicationsManage: 'applications.manage',
  RepositoriesView: 'repositories.view',
  RepositoriesManage: 'repositories.manage',
  TargetServersView: 'targetservers.view',
  TargetServersManage: 'targetservers.manage',
  EnvironmentsView: 'environments.view',
  EnvironmentsManage: 'environments.manage',
  BuildServersView: 'buildservers.view',
  BuildServersManage: 'buildservers.manage',
  BuildsView: 'builds.view',
  BuildsRequest: 'builds.request',
  ContainersView: 'containers.view',
  ContainersControl: 'containers.control',
  ContainersRecreate: 'containers.recreate',
  SecretsView: 'secrets.view',
  SecretsReveal: 'secrets.reveal',
  SecretsManage: 'secrets.manage',

  DeploymentsView: 'deployments.view',
  DeploymentsDeployDev: 'deployments.deploy.dev',
  DeploymentsPromoteQa: 'deployments.promote.qa',
  DeploymentsApproveQa: 'deployments.approve.qa',
  DeploymentsDeployQa: 'deployments.deploy.qa',
  DeploymentsPromoteUat: 'deployments.promote.uat',
  DeploymentsApproveUat: 'deployments.approve.uat',
  DeploymentsDeployUat: 'deployments.deploy.uat',
  DeploymentsPromoteProduction: 'deployments.promote.production',
  DeploymentsApproveProduction: 'deployments.approve.production',
  DeploymentsDeployProduction: 'deployments.deploy.production',
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];

/** The four pipeline stages, in fixed pipeline order — matches the seeded
 * EnvironmentDefinitions (DEV=0, QA=1, UAT=2, PRODUCTION=3). */
export const EnvironmentTiers = ['DEV', 'QA', 'UAT', 'PRODUCTION'] as const;
export type EnvironmentTier = (typeof EnvironmentTiers)[number];

/** Per-environment permission triples for QA/UAT/PRODUCTION — the same
 * dictionaries DeploymentService uses server-side (promote/approve/deploy
 * differ per environment). DEV has no promote/approve step, only deploy. */
export const PromotePermissionByEnvironment: Partial<Record<EnvironmentTier, Permission>> = {
  QA: Permissions.DeploymentsPromoteQa,
  UAT: Permissions.DeploymentsPromoteUat,
  PRODUCTION: Permissions.DeploymentsPromoteProduction,
};

export const ApprovePermissionByEnvironment: Partial<Record<EnvironmentTier, Permission>> = {
  QA: Permissions.DeploymentsApproveQa,
  UAT: Permissions.DeploymentsApproveUat,
  PRODUCTION: Permissions.DeploymentsApproveProduction,
};

export const DeployPermissionByEnvironment: Record<EnvironmentTier, Permission> = {
  DEV: Permissions.DeploymentsDeployDev,
  QA: Permissions.DeploymentsDeployQa,
  UAT: Permissions.DeploymentsDeployUat,
  PRODUCTION: Permissions.DeploymentsDeployProduction,
};

/** Whether the user holds at least one permission tied to this SPECIFIC
 * environment tier (promote/approve/deploy) — used to decide whether to show
 * that environment's nav link, pipeline card, application URL, and pending-
 * request tab. Deliberately does NOT treat the blanket deployments.view
 * permission as "access to everything": deployments.view is granted to
 * anyone with access to *any* environment (see AppDbContextExtensions.cs's
 * "base tier" bundle), so treating it as universal access here would show
 * every environment tab to every user regardless of which one(s) they were
 * actually granted — exactly the bug this function exists to prevent. A
 * Production-approval-only user (CanApproveProduction, no PRODUCTION
 * UserEnvironmentAccess row) still correctly sees PRODUCTION via
 * ApprovePermissionByEnvironment, since that's the one legitimate case of
 * environment-specific access not backed by a UserEnvironmentAccess row. */
export function hasEnvironmentAccess(permissions: readonly string[], tier: EnvironmentTier): boolean {
  const candidates = [
    PromotePermissionByEnvironment[tier],
    ApprovePermissionByEnvironment[tier],
    DeployPermissionByEnvironment[tier],
  ].filter((p): p is Permission => Boolean(p));
  return candidates.some((p) => permissions.includes(p));
}
