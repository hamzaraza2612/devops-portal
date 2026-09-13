import { describe, expect, it } from 'vitest';
import { Permissions, hasEnvironmentAccess } from './permissions';

describe('hasEnvironmentAccess', () => {
  it('grants a Developer (deploy.dev + promote.qa) access to DEV and QA but not UAT/PRODUCTION', () => {
    const permissions = [Permissions.DeploymentsDeployDev, Permissions.DeploymentsPromoteQa];
    expect(hasEnvironmentAccess(permissions, 'DEV')).toBe(true);
    expect(hasEnvironmentAccess(permissions, 'QA')).toBe(true);
    expect(hasEnvironmentAccess(permissions, 'UAT')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'PRODUCTION')).toBe(false);
  });

  it('grants a QA-role user (approve.qa + deploy.qa) access to QA only', () => {
    const permissions = [Permissions.DeploymentsApproveQa, Permissions.DeploymentsDeployQa];
    expect(hasEnvironmentAccess(permissions, 'QA')).toBe(true);
    expect(hasEnvironmentAccess(permissions, 'DEV')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'UAT')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'PRODUCTION')).toBe(false);
  });

  it('grants a UAT-role user (approve.uat + deploy.uat) access to UAT only', () => {
    const permissions = [Permissions.DeploymentsApproveUat, Permissions.DeploymentsDeployUat];
    expect(hasEnvironmentAccess(permissions, 'UAT')).toBe(true);
    expect(hasEnvironmentAccess(permissions, 'QA')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'PRODUCTION')).toBe(false);
  });

  it('denies access to every environment for a user with no relevant permission', () => {
    const permissions = ['users.view'];
    expect(hasEnvironmentAccess(permissions, 'DEV')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'QA')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'UAT')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'PRODUCTION')).toBe(false);
  });

  it('does NOT grant access to any environment from the blanket deployments.view permission alone', () => {
    // Regression test: deployments.view is granted to anyone with access to ANY
    // single environment (see AppDbContextExtensions.cs's "base tier" bundle),
    // so treating it as "sees everything" here would show every environment to
    // every user regardless of what they were actually granted.
    const permissions = [Permissions.DeploymentsView];
    for (const tier of ['DEV', 'QA', 'UAT', 'PRODUCTION'] as const) {
      expect(hasEnvironmentAccess(permissions, tier)).toBe(false);
    }
  });

  it('grants a DEV-only user (base tier + deploy.dev, as actually issued server-side) access to DEV only', () => {
    // Mirrors exactly what AppDbContextExtensions.GetRolesAndPermissionsAsync
    // issues for a user with a single DEV UserEnvironmentAccess row: the base
    // tier bundle (including deployments.view) plus DEV's own permission.
    const permissions = [
      Permissions.ApplicationsView, Permissions.EnvironmentsView, Permissions.DeploymentsView,
      Permissions.ContainersView, Permissions.ContainersControl, Permissions.ContainersRecreate,
      Permissions.BuildsView, Permissions.BuildsRequest, Permissions.SecretsView, Permissions.SecretsReveal,
      Permissions.DeploymentsDeployDev,
    ];
    expect(hasEnvironmentAccess(permissions, 'DEV')).toBe(true);
    expect(hasEnvironmentAccess(permissions, 'QA')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'UAT')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'PRODUCTION')).toBe(false);
  });

  it('grants a Production-approval-only user (CanApproveProduction, no environment rows) access to PRODUCTION only', () => {
    const permissions = [Permissions.DeploymentsApproveProduction];
    expect(hasEnvironmentAccess(permissions, 'PRODUCTION')).toBe(true);
    expect(hasEnvironmentAccess(permissions, 'DEV')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'QA')).toBe(false);
    expect(hasEnvironmentAccess(permissions, 'UAT')).toBe(false);
  });
});
