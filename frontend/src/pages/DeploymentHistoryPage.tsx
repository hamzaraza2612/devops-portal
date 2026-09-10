import { useMemo, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi } from '../api/endpoints';
import { EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { EnvironmentTiers } from '../auth/permissions';
import { DeploymentStatus, type DeploymentDto } from '../types/api';
import { formatDateTime, formatDuration, shortSha } from '../utils/format';

async function loadHistory() {
  const [applications, deployments] = await Promise.all([ApplicationsApi.list(), DeploymentsApi.list()]);
  return { applications, deployments };
}

const statusOptions: Array<{ label: string; value: DeploymentStatus | 'all' }> = [
  { label: 'All statuses', value: 'all' },
  { label: 'Pending', value: DeploymentStatus.Pending },
  { label: 'Queued', value: DeploymentStatus.Queued },
  { label: 'Running', value: DeploymentStatus.Running },
  { label: 'Succeeded', value: DeploymentStatus.Succeeded },
  { label: 'Failed', value: DeploymentStatus.Failed },
  { label: 'Cancelled', value: DeploymentStatus.Cancelled },
];

export function DeploymentHistoryPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadHistory, []);
  const [applicationId, setApplicationId] = useState('all');
  const [environment, setEnvironment] = useState('all');
  const [status, setStatus] = useState<DeploymentStatus | 'all'>('all');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const filtered = useMemo(() => {
    if (!data) return [];
    return data.deployments
      .filter((d) => applicationId === 'all' || d.applicationId === applicationId)
      .filter((d) => environment === 'all' || d.environmentName === environment)
      .filter((d) => status === 'all' || d.status === status)
      .filter((d) => !from || new Date(d.requestedAt) >= new Date(from))
      .filter((d) => !to || new Date(d.requestedAt) <= new Date(`${to}T23:59:59`))
      .sort((a, b) => new Date(b.requestedAt).getTime() - new Date(a.requestedAt).getTime());
  }, [data, applicationId, environment, status, from, to]);

  if (isLoading) return <LoadingSpinner label="Loading deployment history…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader title="Deployment History" subtitle="Every deployment attempt, filterable by application, environment, status, and date." />

      <div className="mb-4 flex flex-wrap gap-3">
        <Select label="Application" value={applicationId} onChange={setApplicationId}>
          <option value="all">All applications</option>
          {data.applications.map((app) => (
            <option key={app.id} value={app.id}>{app.name}</option>
          ))}
        </Select>
        <Select label="Environment" value={environment} onChange={setEnvironment}>
          <option value="all">All environments</option>
          {EnvironmentTiers.map((tier) => (
            <option key={tier} value={tier}>{tier}</option>
          ))}
        </Select>
        <Select label="Status" value={String(status)} onChange={(v) => setStatus(v === 'all' ? 'all' : (Number(v) as DeploymentStatus))}>
          {statusOptions.map((opt) => (
            <option key={opt.label} value={opt.value}>{opt.label}</option>
          ))}
        </Select>
        <label className="flex flex-col text-xs font-medium text-slate-500">
          From
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="mt-1 rounded-md border border-slate-300 px-2 py-1 text-sm" />
        </label>
        <label className="flex flex-col text-xs font-medium text-slate-500">
          To
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="mt-1 rounded-md border border-slate-300 px-2 py-1 text-sm" />
        </label>
      </div>

      {filtered.length === 0 ? (
        <EmptyState title="No deployments match these filters." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Application</th>
                <th className="px-4 py-2">Environment</th>
                <th className="px-4 py-2">Commit</th>
                <th className="px-4 py-2">Status</th>
                <th className="px-4 py-2">Requested by</th>
                <th className="px-4 py-2">Started</th>
                <th className="px-4 py-2">Duration</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {filtered.map((d: DeploymentDto) => (
                <tr key={d.id} className="hover:bg-slate-50">
                  <td className="px-4 py-2">
                    <Link to={`/deployments/${d.id}`} className="font-medium text-slate-900 hover:underline">
                      {d.applicationName}
                    </Link>
                  </td>
                  <td className="px-4 py-2">{d.environmentName}</td>
                  <td className="px-4 py-2 font-mono text-xs">
                    {shortSha(d.commitSha)}
                    {d.isRollback && <span className="ml-1 text-amber-600">rollback</span>}
                  </td>
                  <td className="px-4 py-2"><DeploymentStatusBadge status={d.status} /></td>
                  <td className="px-4 py-2">{d.requestedByUsername ?? '—'}</td>
                  <td className="px-4 py-2 text-slate-500">{formatDateTime(d.startedAt ?? d.requestedAt)}</td>
                  <td className="px-4 py-2 text-slate-500">{d.startedAt ? formatDuration(d.startedAt, d.completedAt) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function Select({
  label,
  value,
  onChange,
  children,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  children: ReactNode;
}) {
  return (
    <label className="flex flex-col text-xs font-medium text-slate-500">
      {label}
      <select value={value} onChange={(e) => onChange(e.target.value)} className="mt-1 rounded-md border border-slate-300 px-2 py-1 text-sm">
        {children}
      </select>
    </label>
  );
}
