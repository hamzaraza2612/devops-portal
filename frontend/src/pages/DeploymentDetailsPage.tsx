import { useEffect, useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { DeploymentsApi } from '../api/endpoints';
import { describeError } from '../api/client';
import { Card, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { DeploymentLogLevel, type DeploymentLogEntryDto } from '../types/api';
import { formatDateTime, formatDuration, shortSha } from '../utils/format';
import { isActiveDeployment } from '../utils/status';

const AUTO_REFRESH_MS = 4000;

export function DeploymentDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const deploymentId = id as string;
  const { data: deployment, isLoading, error, reload } = useAsyncData(() => DeploymentsApi.get(deploymentId), [deploymentId]);
  const running = deployment ? isActiveDeployment(deployment.status) : false;

  // While the deployment is still active, poll its own record too (not just
  // the logs) so the status badge/health/failure panel update once it
  // finishes, instead of only being correct after a manual page reload.
  useEffect(() => {
    if (!running) return;
    const interval = window.setInterval(reload, AUTO_REFRESH_MS);
    return () => window.clearInterval(interval);
  }, [running, reload]);

  if (isLoading) return <LoadingSpinner label="Loading deployment…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!deployment) return null;

  return (
    <div>
      <PageHeader
        title={`Deployment · ${deployment.applicationName} / ${deployment.environmentName}`}
        subtitle={`Commit ${shortSha(deployment.commitSha)}${deployment.isRollback ? ' (rollback)' : ''}`}
        actions={<DeploymentStatusBadge status={deployment.status} />}
      />

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <h2 className="text-sm font-semibold text-slate-900">Details</h2>
          <dl className="mt-2 space-y-1.5 text-sm">
            <Row label="Deployment ID" value={deployment.id} mono />
            <Row label="Application" value={<Link to={`/applications/${deployment.applicationId}`} className="hover:underline">{deployment.applicationName}</Link>} />
            <Row label="Environment" value={deployment.environmentName} />
            <Row label="Commit" value={deployment.commitSha} mono />
            <Row label="Branch" value={deployment.branch ?? '—'} />
            {deployment.versionLabel && <Row label="Version/build" value={deployment.versionLabel} />}
            {deployment.imageReference && <Row label="Image" value={deployment.imageReference} mono />}
            <Row label="Requested by" value={deployment.requestedByUsername ?? '—'} />
            <Row label="Requested at" value={formatDateTime(deployment.requestedAt)} />
            <Row label="Started at" value={formatDateTime(deployment.startedAt)} />
            <Row label="Completed at" value={formatDateTime(deployment.completedAt)} />
            <Row label="Duration" value={deployment.startedAt ? formatDuration(deployment.startedAt, deployment.completedAt) : '—'} />
            {deployment.isRollback && (
              <Row
                label="Rollback of"
                value={
                  deployment.rollbackOfDeploymentId ? (
                    <Link to={`/deployments/${deployment.rollbackOfDeploymentId}`} className="hover:underline">
                      {deployment.rollbackOfDeploymentId.slice(0, 8)}
                    </Link>
                  ) : (
                    '—'
                  )
                }
              />
            )}
            {deployment.promotionRequestId && <Row label="Promotion request" value={deployment.promotionRequestId} mono />}
          </dl>
        </Card>

        <Card>
          <h2 className="text-sm font-semibold text-slate-900">Health check</h2>
          <dl className="mt-2 space-y-1.5 text-sm">
            <Row label="Result" value={deployment.healthCheckPassed === null ? 'Not evaluated' : deployment.healthCheckPassed ? 'Passed' : 'Failed'} />
            {deployment.healthCheckDetail && <Row label="Detail" value={deployment.healthCheckDetail} />}
          </dl>
          {deployment.failureReason && (
            <div className="mt-3">
              <h3 className="text-xs font-semibold uppercase tracking-wide text-red-600">Failure reason</h3>
              <p className="mt-1 text-sm text-red-700">{deployment.failureReason}</p>
            </div>
          )}
        </Card>
      </div>

      <h2 className="mt-8 mb-3 text-sm font-semibold text-slate-900">Logs</h2>
      <DeploymentLogs deploymentId={deployment.id} isRunning={running} />
    </div>
  );
}

function Row({ label, value, mono = false }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <dt className="shrink-0 text-slate-500">{label}</dt>
      <dd className={`text-right font-medium text-slate-800 ${mono ? 'font-mono text-xs break-all' : ''}`}>{value}</dd>
    </div>
  );
}

function DeploymentLogs({ deploymentId, isRunning }: { deploymentId: string; isRunning: boolean }) {
  const [logs, setLogs] = useState<DeploymentLogEntryDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [autoRefresh, setAutoRefresh] = useState(isRunning);

  async function fetchLogs() {
    try {
      const data = await DeploymentsApi.logs(deploymentId);
      setLogs(data);
      setError(null);
    } catch (err) {
      setError(describeError(err));
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    void fetchLogs();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deploymentId]);

  useEffect(() => {
    if (!autoRefresh) return;
    const interval = window.setInterval(() => void fetchLogs(), AUTO_REFRESH_MS);
    return () => window.clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRefresh, deploymentId]);

  // The deployment finished — stop polling. Does not force auto-refresh
  // back on if the user turned it off while it was still running.
  useEffect(() => {
    if (!isRunning) setAutoRefresh(false);
  }, [isRunning]);

  return (
    <Card>
      <div className="mb-3 flex items-center justify-between">
        <label className="flex items-center gap-1.5 text-xs text-slate-600">
          <input type="checkbox" checked={autoRefresh} onChange={(e) => setAutoRefresh(e.target.checked)} />
          Auto-refresh while running
        </label>
        <button type="button" onClick={() => void fetchLogs()} className="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium hover:bg-slate-50">
          Refresh now
        </button>
      </div>

      {isLoading && <LoadingSpinner label="Loading logs…" />}
      {error && <ErrorBanner message={error} />}
      {!isLoading && !error && logs && (
        logs.length === 0 ? (
          <p className="text-sm text-slate-400">No log entries yet.</p>
        ) : (
          <pre className="log-viewer max-h-[32rem] overflow-y-auto">
            {logs.map((entry) => `[${entry.sequence}] ${formatDateTime(entry.timestamp)} ${levelLabel(entry.level)}  ${entry.message}`).join('\n')}
          </pre>
        )
      )}
    </Card>
  );
}

function levelLabel(level: DeploymentLogLevel): string {
  if (level === DeploymentLogLevel.Error) return 'ERROR';
  if (level === DeploymentLogLevel.Warning) return 'WARN ';
  return 'INFO ';
}
