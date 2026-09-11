import { Link, Navigate, useParams } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, PromotionsApi } from '../api/endpoints';
import { EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { PromotionCard } from '../components/PromotionCard';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { useAuth } from '../auth/AuthContext';
import { EnvironmentTiers, hasEnvironmentAccess, type EnvironmentTier } from '../auth/permissions';
import type { ApplicationDto, DeploymentDto } from '../types/api';
import { appEnvKey, latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatRelative, shortSha } from '../utils/format';

async function loadDashboard() {
  const [applications, deployments, pendingPromotions] = await Promise.all([
    ApplicationsApi.list(),
    DeploymentsApi.list(),
    PromotionsApi.listPending({ includeApprovedAwaitingDeploy: true }),
  ]);
  return { applications, deployments, pendingPromotions };
}

/** A single environment's own dedicated view — never mixed in with the
 * others. Answers, at a glance, per master requirements: which application,
 * which environment, what's waiting for me, what can I do about it. */
export function EnvironmentDashboardPage() {
  const { tier: tierParam } = useParams<{ tier: string }>();
  const tier = tierParam?.toUpperCase() as EnvironmentTier | undefined;
  const { data, isLoading, error, reload } = useAsyncData(loadDashboard, []);
  const { user } = useAuth();

  if (!tier || !EnvironmentTiers.includes(tier)) return <Navigate to="/environments" replace />;
  if (isLoading) return <LoadingSpinner label={`Loading ${tier}…`} />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  const visible = hasEnvironmentAccess(user?.permissions ?? [], tier);
  if (!visible) {
    return (
      <div>
        <PageHeader title={tier} />
        <EmptyState title="You don't have access to this environment." />
      </div>
    );
  }

  const latest = latestByAppEnvironment(data.deployments);
  const pendingForTier = data.pendingPromotions.filter((p) => p.toEnvironmentName === tier);
  const activeApps = data.applications.filter((a) => a.isActive);
  const rows = activeApps
    .map((app) => ({ app, deployment: latest.get(appEnvKey(app.id, tier)), pending: pendingForTier.find((p) => p.applicationId === app.id) }))
    .filter((row) => row.deployment || row.pending);

  return (
    <div>
      <div className="mb-4 flex gap-1 border-b border-slate-200">
        {EnvironmentTiers.map((t) => (
          <Link
            key={t}
            to={`/environments/${t.toLowerCase()}`}
            className={`border-b-2 px-3 py-2 text-sm font-medium ${
              t === tier ? 'border-slate-900 text-slate-900' : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}
          >
            {t}
          </Link>
        ))}
      </div>

      <PageHeader
        title={tier}
        subtitle={`Applications, deployment status, and pending actions in ${tier} only.`}
      />

      {pendingForTier.length > 0 && (
        <>
          <h2 className="mb-3 text-sm font-semibold text-slate-900">Pending your action</h2>
          <div className="mb-8 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {pendingForTier.map((promotion) => (
              <PromotionCard key={promotion.id} promotion={promotion} onChanged={reload} />
            ))}
          </div>
        </>
      )}

      <h2 className="mb-3 text-sm font-semibold text-slate-900">Deployed here</h2>
      {rows.filter((r) => r.deployment && !r.pending).length === 0 ? (
        <EmptyState title={`No applications currently deployed to ${tier}.`} />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Application</th>
                <th className="px-4 py-2">Commit</th>
                <th className="px-4 py-2">Status</th>
                <th className="px-4 py-2">Requested by</th>
                <th className="px-4 py-2">When</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows
                .filter((r) => r.deployment && !r.pending)
                .map(({ app, deployment }) => (
                  <DeployedRow key={app.id} app={app} deployment={deployment} />
                ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function DeployedRow({ app, deployment }: { app: ApplicationDto; deployment?: DeploymentDto }) {
  return (
    <tr>
      <td className="px-4 py-2.5">
        <Link to={`/applications/${app.id}`} className="font-medium text-slate-900 hover:underline">
          {app.name}
        </Link>
      </td>
      <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{deployment ? shortSha(deployment.commitSha) : '—'}</td>
      <td className="px-4 py-2.5">{deployment && <DeploymentStatusBadge status={deployment.status} />}</td>
      <td className="px-4 py-2.5 text-slate-600">{deployment?.requestedByUsername ?? '—'}</td>
      <td className="px-4 py-2.5 text-xs text-slate-400">{deployment ? formatRelative(deployment.requestedAt) : '—'}</td>
    </tr>
  );
}
