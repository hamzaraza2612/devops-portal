// Mirrors DevOpsPortal.Domain/Constants/PermissionCodes.cs exactly. The
// backend is the sole source of authorization truth (see auth/AuthContext
// docstring) — this file only decides what the UI *offers*, never what it
// *allows*: every action a button here triggers is re-checked server-side.

export const Permissions = {
  TenantsView: 'tenants.view',
  TenantsManage: 'tenants.manage',
  UsersView: 'users.view',
  UsersManage: 'users.manage',
  RolesView: 'roles.view',
  RolesManage: 'roles.manage',
  AuditView: 'audit.view',
  ApplicationsView: 'applications.view',
  ApplicationsManage: 'applications.manage',
  RepositoriesView: 'repositories.view',
  RepositoriesManage: 'repositories.manage',
  TargetServersView: 'targetservers.view',
  TargetServersManage: 'targetservers.manage',
  EnvironmentsView: 'environments.view',
  BuildServersView: 'buildservers.view',
  BuildServersManage: 'buildservers.manage',

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
  DeploymentsRollback: 'deployments.rollback',
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

/** Whether the user holds at least one permission tied to this specific
 * environment tier (promote/approve/deploy) or the blanket deployments.view
 * read permission — used to decide whether to show that environment's
 * application URL and pipeline card. Mirrors the boundary that already
 * exists everywhere else in the read APIs (deployments.view is a global,
 * not per-environment, read permission — see PROJECT_STATE.md Phase 4 notes). */
export function hasEnvironmentAccess(permissions: readonly string[], tier: EnvironmentTier): boolean {
  if (permissions.includes(Permissions.DeploymentsView)) return true;
  const candidates = [
    PromotePermissionByEnvironment[tier],
    ApprovePermissionByEnvironment[tier],
    DeployPermissionByEnvironment[tier],
  ].filter((p): p is Permission => Boolean(p));
  return candidates.some((p) => permissions.includes(p));
}
