import type { DeploymentDto } from '../types/api';

export function appEnvKey(applicationId: string, environmentName: string): string {
  return `${applicationId}|${environmentName}`;
}

/** Most recent deployment (any status) per (application, environment) pair —
 * the "current deployment state" shown on application/environment cards.
 * Built client-side from one unfiltered /api/deployments call rather than
 * N per-application status calls. */
export function latestByAppEnvironment(deployments: readonly DeploymentDto[]): Map<string, DeploymentDto> {
  const result = new Map<string, DeploymentDto>();
  for (const deployment of deployments) {
    const key = appEnvKey(deployment.applicationId, deployment.environmentName);
    const existing = result.get(key);
    if (!existing || new Date(deployment.requestedAt) > new Date(existing.requestedAt)) {
      result.set(key, deployment);
    }
  }
  return result;
}
