import { Link } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, PromotionsApi } from '../api/endpoints';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { useAuth } from '../auth/AuthContext';
import { EnvironmentTiers, hasEnvironmentAccess, type EnvironmentTier } from '../auth/permissions';
import { DeploymentStatus, type ApplicationDto, type DeploymentDto, type PromotionRequestDto } from '../types/api';
import { appEnvKey, latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatRelative, shortSha } from '../utils/format';

async function loadEnvironments() {
  const [applications, deployments, pendingPromotions] = await Promise.all([
    ApplicationsApi.list(),
    DeploymentsApi.list(),
    PromotionsApi.listPending({ includeApprovedAwaitingDeploy: true }),
  ]);
  return { applications, deployments, pendingPromotions };
}

/** Environment-oriented board: one column per pipeline stage, so it's
 * immediately clear which environment needs attention right now rather
 * than having to check each application individually. */
export function EnvironmentsPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadEnvironments, []);
  const { user } = useAuth();

  if (isLoading) return <LoadingSpinner label="Loading environments…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  const latest = latestByAppEnvironment(data.deployments);
  const activeApps = data.applications.filter((a) => a.isActive);

  return (
    <div>
      <PageHeader title="Environments" subtitle="Per-environment status across all applications." />

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {EnvironmentTiers.map((tier) => (
          <EnvironmentColumn
            key={tier}
            tier={tier}
            apps={activeApps}
            latest={latest}
            pendingPromotions={data.pendingPromotions.filter((p) => p.toEnvironmentName === tier)}
            visible={hasEnvironmentAccess(user?.permissions ?? [], tier)}
          />
        ))}
      </div>
    </div>
  );
}

function EnvironmentColumn({
  tier,
  apps,
  latest,
  pendingPromotions,
  visible,
}: {
  tier: EnvironmentTier;
  apps: ApplicationDto[];
  latest: Map<string, DeploymentDto>;
  pendingPromotions: PromotionRequestDto[];
  visible: boolean;
}) {
  if (!visible) {
    return (
      <Card>
        <h2 className="text-sm font-semibold text-slate-900">{tier}</h2>
        <p className="mt-3 text-xs text-slate-400">You don't have access to this environment.</p>
      </Card>
    );
  }

  const rows = apps
    .map((app) => ({ app, deployment: latest.get(appEnvKey(app.id, tier)) }))
    .filter((row) => row.deployment);
  const needsAttention = rows.some((r) => r.deployment?.status === DeploymentStatus.Failed) || pendingPromotions.length > 0;

  return (
    <Card className={needsAttention ? 'ring-1 ring-amber-300' : ''}>
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-slate-900">{tier}</h2>
        {needsAttention && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">Needs attention</span>}
      </div>

      {pendingPromotions.length > 0 && (
        <div className="mt-2 rounded-md bg-amber-50 px-2 py-1.5 text-xs text-amber-800">
          {pendingPromotions.length} pending promotion request(s) —{' '}
          <Link to="/pending" className="font-medium hover:underline">review</Link>
        </div>
      )}

      <div className="mt-3 divide-y divide-slate-100">
        {rows.length === 0 ? (
          <EmptyState title={`No applications deployed to ${tier} yet.`} />
        ) : (
          rows.map(({ app, deployment }) => (
            <Link key={app.id} to={`/applications/${app.id}`} className="flex items-center justify-between gap-2 py-2 text-sm hover:text-slate-600">
              <span className="flex min-w-0 items-baseline gap-1">
                <span className="truncate font-medium text-slate-800">{app.name}</span>
                <span className="shrink-0 text-slate-400">· {shortSha(deployment?.commitSha)}</span>
              </span>
              <span className="flex shrink-0 items-center gap-2">
                <span className="text-[11px] text-slate-400">{formatRelative(deployment?.requestedAt)}</span>
                {deployment && <DeploymentStatusBadge status={deployment.status} />}
              </span>
            </Link>
          ))
        )}
      </div>
    </Card>
  );
}

