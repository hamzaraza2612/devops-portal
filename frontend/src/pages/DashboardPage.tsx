import { Link } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, PromotionsApi } from '../api/endpoints';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { EnvironmentTiers } from '../auth/permissions';
import { DeploymentStatus, type ApplicationDto, type DeploymentDto, type PromotionRequestDto } from '../types/api';
import { latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatRelative, shortSha } from '../utils/format';
import { isActiveDeployment } from '../utils/status';

interface DashboardData {
  applications: ApplicationDto[];
  deployments: DeploymentDto[];
  pendingPromotions: PromotionRequestDto[];
}

async function loadDashboard(): Promise<DashboardData> {
  const [applications, deployments, pendingPromotions] = await Promise.all([
    ApplicationsApi.list(),
    DeploymentsApi.list(),
    PromotionsApi.listPending(),
  ]);
  return { applications, deployments, pendingPromotions };
}

/** For each environment tier, the set of application ids whose most recent
 * deployment there succeeded — i.e. "currently running in this environment". */
function currentlyDeployedByEnvironment(deployments: DeploymentDto[]): Map<string, Set<string>> {
  const result = new Map<string, Set<string>>();
  for (const tier of EnvironmentTiers) result.set(tier, new Set());
  for (const deployment of latestByAppEnvironment(deployments).values()) {
    if (deployment.status === DeploymentStatus.Succeeded) {
      result.get(deployment.environmentName)?.add(deployment.applicationId);
    }
  }
  return result;
}

export function DashboardPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadDashboard, []);

  if (isLoading) return <LoadingSpinner label="Loading dashboard…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  const running = data.deployments.filter((d) => isActiveDeployment(d.status));
  const succeeded = data.deployments.filter((d) => d.status === DeploymentStatus.Succeeded);
  const failed = data.deployments.filter((d) => d.status === DeploymentStatus.Failed);
  const byEnvironment = currentlyDeployedByEnvironment(data.deployments);
  const recent = [...data.deployments]
    .sort((a, b) => new Date(b.requestedAt).getTime() - new Date(a.requestedAt).getTime())
    .slice(0, 8);

  return (
    <div>
      <PageHeader title="Dashboard" subtitle="Live overview of applications and deployment activity." />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <StatCard label="Applications" value={data.applications.length} to="/applications" />
        <StatCard label="Running" value={running.length} to="/deployments" tone="blue" />
        <StatCard label="Succeeded" value={succeeded.length} to="/deployments" tone="emerald" />
        <StatCard label="Failed" value={failed.length} to="/deployments" tone="red" />
        <StatCard label="Pending approvals" value={data.pendingPromotions.length} to="/pending" tone="amber" />
        <StatCard label="Total deployments" value={data.deployments.length} to="/deployments" />
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-2">
        <Card>
          <h2 className="text-sm font-semibold text-slate-900">Applications by environment</h2>
          <p className="mt-1 text-xs text-slate-500">Apps whose most recent deployment to that environment succeeded.</p>
          <ul className="mt-3 space-y-2">
            {EnvironmentTiers.map((tier) => (
              <li key={tier} className="flex items-center justify-between text-sm">
                <span className="font-medium text-slate-700">{tier}</span>
                <span className="text-slate-500">{byEnvironment.get(tier)?.size ?? 0} application(s)</span>
              </li>
            ))}
          </ul>
        </Card>

        <Card>
          <h2 className="text-sm font-semibold text-slate-900">Recent deployments</h2>
          {recent.length === 0 ? (
            <EmptyState title="No deployments yet" />
          ) : (
            <ul className="mt-3 divide-y divide-slate-100">
              {recent.map((deployment) => (
                <li key={deployment.id} className="py-2">
                  <Link to={`/deployments/${deployment.id}`} className="flex items-center justify-between gap-3 text-sm hover:text-slate-600">
                    <span className="min-w-0">
                      <span className="font-medium text-slate-800">{deployment.applicationName}</span>
                      <span className="text-slate-400"> · {deployment.environmentName} · {shortSha(deployment.commitSha)}</span>
                    </span>
                    <span className="flex shrink-0 items-center gap-2">
                      <span className="text-xs text-slate-400">{formatRelative(deployment.requestedAt)}</span>
                      <DeploymentStatusBadge status={deployment.status} />
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </div>
  );
}

function StatCard({ label, value, to, tone }: { label: string; value: number; to: string; tone?: 'blue' | 'emerald' | 'red' | 'amber' }) {
  const toneClass = tone ? { blue: 'text-blue-600', emerald: 'text-emerald-600', red: 'text-red-600', amber: 'text-amber-600' }[tone] : 'text-slate-900';
  return (
    <Link to={to} className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm transition-shadow hover:shadow">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</p>
      <p className={`mt-1 text-2xl font-semibold ${toneClass}`}>{value}</p>
    </Link>
  );
}
