import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi } from '../api/endpoints';
import { Can, Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { EnvironmentTiers, Permissions } from '../auth/permissions';
import type { ApplicationDto, DeploymentDto } from '../types/api';
import { appEnvKey, latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatRelative } from '../utils/format';

async function loadApplications() {
  const [applications, deployments] = await Promise.all([ApplicationsApi.list(), DeploymentsApi.list()]);
  return { applications, deployments };
}

export function ApplicationsListPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadApplications, []);
  const [search, setSearch] = useState('');
  const [showInactive, setShowInactive] = useState(false);

  const latest = useMemo(() => (data ? latestByAppEnvironment(data.deployments) : new Map<string, DeploymentDto>()), [data]);

  const filtered = useMemo(() => {
    if (!data) return [];
    const term = search.trim().toLowerCase();
    return data.applications
      .filter((app) => showInactive || app.isActive)
      .filter((app) => !term || app.name.toLowerCase().includes(term) || app.slug.toLowerCase().includes(term) || (app.repositoryName ?? '').toLowerCase().includes(term))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [data, search, showInactive]);

  if (isLoading) return <LoadingSpinner label="Loading applications…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;

  return (
    <div>
      <PageHeader
        title="Applications"
        subtitle="Applications you're authorized to view, with current status per environment."
        actions={
          <Can permission={Permissions.ApplicationsManage}>
            <span className="text-xs text-slate-400">Manage applications via the API/admin tooling.</span>
          </Can>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <input
          type="search"
          placeholder="Search by name, slug, or repository…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="w-full max-w-sm rounded-md border border-slate-300 px-3 py-1.5 text-sm focus:border-slate-500 focus:outline-none"
        />
        <label className="flex items-center gap-1.5 text-sm text-slate-600">
          <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
          Show inactive
        </label>
      </div>

      {filtered.length === 0 ? (
        <EmptyState title="No applications match your search." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Application</th>
                <th className="px-4 py-2">Repository</th>
                {EnvironmentTiers.map((tier) => (
                  <th key={tier} className="px-4 py-2">{tier}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {filtered.map((app) => (
                <ApplicationRow key={app.id} app={app} latest={latest} />
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Card className="mt-4 text-xs text-slate-500">
        Branch and latest-available-commit details are shown on each application's page to avoid a live repository
        lookup for every row in this list.
      </Card>
    </div>
  );
}

function ApplicationRow({ app, latest }: { app: ApplicationDto; latest: Map<string, DeploymentDto> }) {
  return (
    <tr className={app.isActive ? '' : 'opacity-60'}>
      <td className="px-4 py-2.5">
        <Link to={`/applications/${app.id}`} className="font-medium text-slate-900 hover:underline">
          {app.name}
        </Link>
        {!app.isActive && <span className="ml-2 text-xs text-slate-400">(inactive)</span>}
      </td>
      <td className="px-4 py-2.5 text-slate-600">{app.repositoryName ?? '—'}</td>
      {EnvironmentTiers.map((tier) => {
        const deployment = latest.get(appEnvKey(app.id, tier));
        return (
          <td key={tier} className="px-4 py-2.5">
            {deployment ? (
              <div className="flex flex-col gap-0.5">
                <DeploymentStatusBadge status={deployment.status} />
                <span className="text-[11px] text-slate-400">{formatRelative(deployment.requestedAt)}</span>
              </div>
            ) : (
              <span className="text-xs text-slate-400">Not deployed</span>
            )}
          </td>
        );
      })}
    </tr>
  );
}
