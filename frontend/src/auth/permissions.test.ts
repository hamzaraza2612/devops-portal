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

  it('grants access to every environment for a holder of the blanket deployments.view permission', () => {
    const permissions = [Permissions.DeploymentsView];
    for (const tier of ['DEV', 'QA', 'UAT', 'PRODUCTION'] as const) {
      expect(hasEnvironmentAccess(permissions, tier)).toBe(true);
    }
  });
});
