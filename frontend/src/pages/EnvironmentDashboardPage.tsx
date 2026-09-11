import { useEffect, useState } from 'react';
import { Link, Navigate, useParams } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, EnvironmentsApi, PromotionsApi } from '../api/endpoints';
import { ActionButton } from '../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { PromotionCard } from '../components/PromotionCard';
import { ContainerStateBadge, DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { useAuth } from '../auth/AuthContext';
import { EnvironmentTiers, Permissions, hasEnvironmentAccess, type EnvironmentTier } from '../auth/permissions';
import { ContainerState, type ApplicationDto, type DeploymentDto, type DiscoveredContainerDto, type EnvironmentInfrastructureDto } from '../types/api';
import { appEnvKey, latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatBytes, formatRelative, shortSha } from '../utils/format';

const INFRASTRUCTURE_POLL_MS = 20000;

async function loadDashboard() {
  const [applications, deployments, pendingPromotions, environments] = await Promise.all([
    ApplicationsApi.list(),
    DeploymentsApi.list(),
    PromotionsApi.listPending({ includeApprovedAwaitingDeploy: true }),
    EnvironmentsApi.list(),
  ]);
  return { applications, deployments, pendingPromotions, environments };
}

/** A single environment's own dedicated view — never mixed in with the
 * others. Leads with the Environment Infrastructure Dashboard (real
 * server/Docker discovery over SSH — master requirement: this must never
 * depend on an application being configured in the database first), then
 * the existing deployment-workflow status below it, unchanged. */
export function EnvironmentDashboardPage() {
  const { tier: tierParam } = useParams<{ tier: string }>();
  const tier = tierParam?.toUpperCase() as EnvironmentTier | undefined;
  const { data, isLoading, error, reload } = useAsyncData(loadDashboard, []);
  const { user, can } = useAuth();

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

  const environmentDef = data.environments.find((e) => e.name === tier);
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
        subtitle={`Real server/container status and deployment workflow for ${tier} only.`}
      />

      {environmentDef && can(Permissions.ContainersView) && <InfrastructureSection environmentDefinitionId={environmentDef.id} tier={tier} />}

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

      <h2 className="mb-3 text-sm font-semibold text-slate-900">Deployment workflow status</h2>
      {rows.filter((r) => r.deployment && !r.pending).length === 0 ? (
        <EmptyState title={`No applications currently deployed to ${tier} through the portal's deployment workflow.`} />
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

/** The Environment Infrastructure Dashboard: SSHes to this environment's
 * PrimaryTargetServer (via the backend) and shows exactly what's really
 * running there — never database records standing in for it. Polls on an
 * interval and offers a manual Refresh, both re-querying the target server
 * fresh every time (never cached). */
function InfrastructureSection({ environmentDefinitionId, tier }: { environmentDefinitionId: string; tier: EnvironmentTier }) {
  const { can } = useAuth();
  const { data, isLoading, error, reload } = useAsyncData(
    () => EnvironmentsApi.infrastructure(environmentDefinitionId),
    [environmentDefinitionId],
  );
  const [openLogsFor, setOpenLogsFor] = useState<string | null>(null);

  useEffect(() => {
    const interval = window.setInterval(reload, INFRASTRUCTURE_POLL_MS);
    return () => window.clearInterval(interval);
  }, [reload]);

  if (!can(Permissions.ContainersView)) return null;

  return (
    <div className="mb-8">
      {isLoading && !data && <p className="mb-4 text-xs text-slate-400">Loading server status…</p>}
      {error && <ErrorBanner message={error} onDismiss={reload} />}
      {data && <InfrastructureBody data={data} tier={tier} canControl={can(Permissions.ContainersControl)} canRecreate={can(Permissions.ContainersRecreate)} onChanged={reload} openLogsFor={openLogsFor} setOpenLogsFor={setOpenLogsFor} />}
    </div>
  );
}

function InfrastructureBody({
  data,
  tier,
  canControl,
  canRecreate,
  onChanged,
  openLogsFor,
  setOpenLogsFor,
}: {
  data: EnvironmentInfrastructureDto;
  tier: EnvironmentTier;
  canControl: boolean;
  canRecreate: boolean;
  onChanged: () => void;
  openLogsFor: string | null;
  setOpenLogsFor: (id: string | null) => void;
}) {
  const { server, containers } = data;
  const running = containers.filter((c) => c.state === ContainerState.Running).length;
  const stopped = containers.filter((c) => c.state === ContainerState.Exited || c.state === ContainerState.Created).length;
  const unhealthy = containers.filter((c) => c.state === ContainerState.Unhealthy).length;

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex items-center gap-2">
          <StatusDot ok={server.sshConnected && server.dockerAvailable} configured={server.isConfigured} />
          <h2 className="text-sm font-semibold text-slate-900">
            {server.targetServerName ?? `${tier} server`}
          </h2>
          {server.hostname && <span className="text-xs text-slate-400">({server.hostname})</span>}
        </div>
        <button
          type="button"
          onClick={onChanged}
          className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
        >
          Refresh
        </button>
      </div>

      {!server.isConfigured ? (
        <div className="mt-3 rounded-md bg-slate-50 px-3 py-2 text-xs text-slate-600">
          {server.errorMessage ?? 'No target server is assigned to this environment yet.'}{' '}
          <Link to="/admin/environments" className="font-medium text-blue-600 hover:underline">Assign one →</Link>
        </div>
      ) : !server.sshConnected ? (
        <p className="mt-3 rounded-md bg-red-50 px-3 py-2 text-xs font-medium text-red-700">
          Unable to connect to {server.targetServerName} via SSH{server.errorMessage ? `: ${server.errorMessage}` : '.'}
        </p>
      ) : !server.dockerAvailable ? (
        <p className="mt-3 rounded-md bg-amber-50 px-3 py-2 text-xs font-medium text-amber-800">
          Connected to {server.targetServerName} via SSH, but Docker is not available{server.errorMessage ? `: ${server.errorMessage}` : '.'}
        </p>
      ) : (
        <>
          <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-7">
            <SummaryCard label="Servers" value="1" />
            <SummaryCard label="Running" value={String(running)} />
            <SummaryCard label="Stopped" value={String(stopped)} />
            <SummaryCard label="Unhealthy" value={String(unhealthy)} highlight={unhealthy > 0} />
            <SummaryCard label="CPU load (1m)" value={server.metrics?.load1?.toFixed(2) ?? '—'} />
            <SummaryCard
              label="Memory"
              value={
                server.metrics?.memUsedBytes != null && server.metrics.memTotalBytes != null
                  ? `${formatBytes(server.metrics.memUsedBytes)} / ${formatBytes(server.metrics.memTotalBytes)}`
                  : '—'
              }
            />
            <SummaryCard label="Disk" value={server.metrics?.diskUsePercent != null ? `${server.metrics.diskUsePercent.toFixed(0)}%` : '—'} />
          </div>

          <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-slate-500 sm:grid-cols-3">
            <Row label="Docker" value={server.dockerVersion ? `v${server.dockerVersion}` : 'available'} />
            <Row label="Compose" value={server.composeAvailable ? (server.composeVersion ? `v${server.composeVersion}` : 'available') : 'not available'} />
            <Row label="OS" value={server.osInfo ?? '—'} />
            <Row label="Uptime/load" value={server.uptimeInfo ?? '—'} />
            <Row label="SSH user" value={server.authenticatedUser ?? '—'} />
            <Row label="Refreshed" value={formatRelative(server.retrievedAt)} />
          </dl>

          <h3 className="mt-5 mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">Containers ({containers.length})</h3>
          {containers.length === 0 ? (
            <EmptyState title="No containers found on this server." />
          ) : (
            <ul className="space-y-2">
              {containers.map((c) => (
                <ContainerRow
                  key={c.containerId}
                  container={c}
                  environmentDefinitionId={data.environmentDefinitionId}
                  canControl={canControl}
                  canRecreate={canRecreate}
                  onChanged={onChanged}
                  logsOpen={openLogsFor === c.containerId}
                  onToggleLogs={() => setOpenLogsFor(openLogsFor === c.containerId ? null : c.containerId)}
                />
              ))}
            </ul>
          )}
        </>
      )}
    </Card>
  );
}

function StatusDot({ ok, configured }: { ok: boolean; configured: boolean }) {
  const color = !configured ? 'bg-slate-300' : ok ? 'bg-emerald-500' : 'bg-red-500';
  return <span className={`inline-block h-2.5 w-2.5 rounded-full ${color}`} />;
}

function SummaryCard({ label, value, highlight }: { label: string; value: string; highlight?: boolean }) {
  return (
    <div className={`rounded-md border px-3 py-2 ${highlight ? 'border-amber-300 bg-amber-50' : 'border-slate-200 bg-slate-50'}`}>
      <p className="text-[11px] uppercase tracking-wide text-slate-500">{label}</p>
      <p className={`text-sm font-semibold ${highlight ? 'text-amber-800' : 'text-slate-900'}`}>{value}</p>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-2 truncate">
      <dt>{label}</dt>
      <dd className="truncate font-medium text-slate-700" title={value}>{value}</dd>
    </div>
  );
}

function ContainerRow({
  container,
  environmentDefinitionId,
  canControl,
  canRecreate,
  onChanged,
  logsOpen,
  onToggleLogs,
}: {
  container: DiscoveredContainerDto;
  environmentDefinitionId: string;
  canControl: boolean;
  canRecreate: boolean;
  onChanged: () => void;
  logsOpen: boolean;
  onToggleLogs: () => void;
}) {
  const c = container;
  return (
    <li className="rounded-md border border-slate-200 px-3 py-2 text-xs">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <p className="truncate font-medium text-slate-800">{c.name}</p>
            {c.isMapped ? (
              <Link to={`/applications/${c.applicationId}`} className="shrink-0 rounded-full bg-blue-50 px-2 py-0.5 text-[11px] font-medium text-blue-700 hover:underline">
                {c.applicationName}
              </Link>
            ) : (
              <span className="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-500">Unregistered container</span>
            )}
          </div>
          <p className="truncate text-slate-500">
            {c.image}{c.imageTag ? `:${c.imageTag}` : ''} · {c.containerId.slice(0, 12)}
          </p>
        </div>
        <ContainerStateBadge state={c.state} />
      </div>

      <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-slate-500">
        <span>Restarts: {c.restartCount}</span>
        {c.dockerHealthStatus && <span>Health: {c.dockerHealthStatus}</span>}
        {c.stats ? (
          <>
            {c.stats.cpuPercent !== null && <span>CPU: {c.stats.cpuPercent.toFixed(1)}%</span>}
            {c.stats.memoryUsage !== null && (
              <span>Mem: {c.stats.memoryUsage}{c.stats.memoryLimit ? ` / ${c.stats.memoryLimit}` : ''}</span>
            )}
            {c.stats.networkIO !== null && <span>Net: {c.stats.networkIO}</span>}
            {c.stats.blockIO !== null && <span>Block: {c.stats.blockIO}</span>}
            {c.stats.pidCount !== null && <span>PIDs: {c.stats.pidCount}</span>}
          </>
        ) : (
          <span className="text-slate-400">stats unavailable</span>
        )}
        {c.ports.length > 0 && <span>Ports: {c.ports.join(', ')}</span>}
        <span>Created {formatRelative(c.createdAt)}</span>
        {c.startedAt && <span>Started {formatRelative(c.startedAt)}</span>}
      </div>

      <div className="mt-2 flex flex-wrap gap-2">
        <button type="button" onClick={onToggleLogs} className="text-slate-500 underline hover:text-slate-700">
          {logsOpen ? 'Hide logs' : 'Logs'}
        </button>
        {canControl && (
          <>
            <ActionButton label="Start" variant="secondary" onAction={() => EnvironmentsApi.startContainer(environmentDefinitionId, c.containerId)} onSuccess={onChanged} />
            <ActionButton label="Stop" variant="secondary" onAction={() => EnvironmentsApi.stopContainer(environmentDefinitionId, c.containerId)} onSuccess={onChanged} />
            <ActionButton label="Restart" variant="secondary" onAction={() => EnvironmentsApi.restartContainer(environmentDefinitionId, c.containerId)} onSuccess={onChanged} />
          </>
        )}
        {canRecreate && c.canRecreateWithVolumes && c.applicationId && (
          <ActionButton
            label="Recreate (destroys volumes)"
            variant="danger"
            confirmLabel="Confirm: this destroys volumes"
            onAction={() => ApplicationsApi.recreateContainers(c.applicationId!, environmentDefinitionId, { confirm: true })}
            onSuccess={onChanged}
          />
        )}
      </div>

      {logsOpen && <ContainerLogsPanel environmentDefinitionId={environmentDefinitionId} containerId={c.containerId} />}
    </li>
  );
}

const LOG_TAIL_OPTIONS = [100, 200, 500, 1000] as const;

function ContainerLogsPanel({ environmentDefinitionId, containerId }: { environmentDefinitionId: string; containerId: string }) {
  const [tailLines, setTailLines] = useState<number>(200);
  const { data, isLoading, error, reload } = useAsyncData(
    () => EnvironmentsApi.containerLogs(environmentDefinitionId, containerId, tailLines),
    [environmentDefinitionId, containerId, tailLines],
  );

  return (
    <div className="mt-2 rounded-md border border-slate-200 bg-white p-2">
      <div className="flex items-center justify-between gap-2">
        <label className="flex items-center gap-1.5 text-slate-500">
          Show last
          <select className="rounded border border-slate-300 px-1 py-0.5 text-xs" value={tailLines} onChange={(e) => setTailLines(Number(e.target.value))}>
            {LOG_TAIL_OPTIONS.map((n) => (
              <option key={n} value={n}>{n} lines</option>
            ))}
          </select>
        </label>
        <button type="button" className="text-slate-500 underline hover:text-slate-700" onClick={reload} disabled={isLoading}>
          Refresh
        </button>
      </div>

      {isLoading && <p className="mt-2 text-slate-400">Loading logs…</p>}
      {error && <p className="mt-2 text-rose-600">{error}</p>}
      {data && !isLoading && !error && (
        <>
          {!data.success && <p className="mt-2 text-rose-600">{data.error ?? (data.logs || 'Could not fetch logs for this container.')}</p>}
          {data.success && (
            <pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap break-all rounded bg-slate-900 p-2 font-mono text-[11px] leading-4 text-slate-100">
              {data.logs.trim().length > 0 ? data.logs : '(no log output)'}
            </pre>
          )}
        </>
      )}
    </div>
  );
}
