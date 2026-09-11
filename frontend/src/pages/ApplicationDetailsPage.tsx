import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, EnvironmentsApi, PromotionsApi } from '../api/endpoints';
import { ActionButton } from '../components/ActionButton';
import { Can, Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { ApprovalStatusBadge, ContainerStateBadge, DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import {
  ApprovePermissionByEnvironment,
  DeployPermissionByEnvironment,
  EnvironmentTiers,
  PromotePermissionByEnvironment,
  Permissions,
  hasEnvironmentAccess,
  type EnvironmentTier,
} from '../auth/permissions';
import { useAuth } from '../auth/AuthContext';
import {
  ApprovalStatus,
  DeploymentMode,
  DeploymentStatus,
  HealthCheckType,
  type ApplicationEnvironmentDto,
  type DeploymentDto,
  type EnvironmentDefinitionDto,
  type PromotionRequestDto,
} from '../types/api';
import { appEnvKey, latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatDateTime, shortSha } from '../utils/format';
import { GitCommitLookup } from '../components/GitCommitLookup';

async function loadDetails(applicationId: string) {
  const [application, environmentDefs, environments, deployments, pendingPromotions] = await Promise.all([
    ApplicationsApi.get(applicationId),
    EnvironmentsApi.list(),
    ApplicationsApi.environments(applicationId),
    DeploymentsApi.list({ applicationId }),
    PromotionsApi.listPending({ applicationId, includeApprovedAwaitingDeploy: true }),
  ]);
  return { application, environmentDefs, environments, deployments, pendingPromotions };
}

export function ApplicationDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const applicationId = id as string;
  const { data, isLoading, error, reload } = useAsyncData(() => loadDetails(applicationId), [applicationId]);
  const { user } = useAuth();

  if (isLoading) return <LoadingSpinner label="Loading application…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  const { application, environmentDefs, environments, deployments, pendingPromotions } = data;
  const latest = latestByAppEnvironment(deployments);
  const succeededByEnv = new Map<string, DeploymentDto>();
  for (const d of deployments) {
    if (d.status !== DeploymentStatus.Succeeded) continue;
    const existing = succeededByEnv.get(d.environmentName);
    if (!existing || new Date(d.requestedAt) > new Date(existing.requestedAt)) succeededByEnv.set(d.environmentName, d);
  }

  return (
    <div>
      <PageHeader
        title={application.name}
        subtitle={application.description ?? undefined}
      />

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <h2 className="text-sm font-semibold text-slate-900">Application</h2>
          <dl className="mt-2 space-y-1 text-sm">
            <Row label="Slug" value={application.slug} />
            <Row label="Repository" value={application.repositoryName ?? '—'} />
            <Row label="Deployment mode" value={application.deploymentMode === 0 ? 'Legacy filesystem' : 'Container image'} />
            <Row label="Status" value={application.isActive ? 'Active' : 'Inactive'} />
          </dl>
        </Card>
      </div>

      <h2 className="mt-8 mb-3 text-sm font-semibold text-slate-900">Environments</h2>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {EnvironmentTiers.map((tier) => (
          <EnvironmentCard
            key={tier}
            tier={tier}
            applicationId={applicationId}
            deploymentMode={application.deploymentMode}
            environmentDef={environmentDefs.find((e) => e.name === tier)}
            environmentConfig={environments.find((e) => e.environmentName === tier)}
            latestAttempt={latest.get(appEnvKey(applicationId, tier))}
            latestSucceeded={succeededByEnv.get(tier)}
            previousSucceeded={tier === 'DEV' ? undefined : succeededByEnv.get(EnvironmentTiers[EnvironmentTiers.indexOf(tier) - 1])}
            pendingPromotion={pendingPromotions.find((p) => p.toEnvironmentName === tier)}
            deploymentsForEnv={deployments.filter((d) => d.environmentName === tier && d.status === DeploymentStatus.Succeeded)}
            canSeeUrl={hasEnvironmentAccess(user?.permissions ?? [], tier)}
            onChanged={reload}
          />
        ))}
      </div>

      <h2 className="mt-8 mb-3 text-sm font-semibold text-slate-900">Deployment history</h2>
      <DeploymentHistoryTable deployments={deployments} />
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-slate-500">{label}</dt>
      <dd className="font-medium text-slate-800">{value}</dd>
    </div>
  );
}

function EnvironmentCard({
  tier,
  applicationId,
  deploymentMode,
  environmentDef,
  environmentConfig,
  latestAttempt,
  latestSucceeded,
  previousSucceeded,
  pendingPromotion,
  deploymentsForEnv,
  canSeeUrl,
  onChanged,
}: {
  tier: EnvironmentTier;
  applicationId: string;
  deploymentMode: DeploymentMode;
  environmentDef?: EnvironmentDefinitionDto;
  environmentConfig?: ApplicationEnvironmentDto;
  latestAttempt?: DeploymentDto;
  latestSucceeded?: DeploymentDto;
  previousSucceeded?: DeploymentDto;
  pendingPromotion?: PromotionRequestDto;
  deploymentsForEnv: DeploymentDto[];
  canSeeUrl: boolean;
  onChanged: () => void;
}) {
  const { can } = useAuth();

  return (
    <Card>
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-slate-900">{tier}</h3>
        {latestAttempt && <DeploymentStatusBadge status={latestAttempt.status} />}
      </div>

      {!environmentConfig ? (
        <p className="mt-3 text-xs text-slate-400">Not configured for this application.</p>
      ) : (
        <>
          <dl className="mt-3 space-y-1 text-xs">
            <Row label="Branch" value={environmentConfig.branchName ?? '—'} />
            <Row label="Deployed commit" value={latestSucceeded ? shortSha(latestSucceeded.commitSha) : 'None'} />
            <Row label="Target server" value={environmentConfig.targetServerName} />
          </dl>

          {canSeeUrl && environmentConfig.applicationUrl && (
            <a
              href={environmentConfig.applicationUrl}
              target="_blank"
              rel="noreferrer noopener"
              className="mt-2 inline-block text-xs font-medium text-blue-600 hover:underline"
            >
              Open application ↗
            </a>
          )}

          <div className="mt-3 border-t border-slate-100 pt-3">
            {tier === 'DEV' && environmentDef && (
              <Can permission={Permissions.DeploymentsDeployDev}>
                {deploymentMode === DeploymentMode.ContainerImage ? (
                  <DeployReleaseForm applicationId={applicationId} onDeployed={onChanged} />
                ) : (
                  <DeployDevForm applicationId={applicationId} environmentDefinitionId={environmentDef.id} onDeployed={onChanged} />
                )}
              </Can>
            )}

            {tier !== 'DEV' && environmentDef && (
              <PromotionControls
                tier={tier}
                applicationId={applicationId}
                environmentDefinitionId={environmentDef.id}
                previousSucceeded={previousSucceeded}
                pendingPromotion={pendingPromotion}
                onChanged={onChanged}
              />
            )}

            {environmentDef && DeployPermissionByEnvironment[tier] && (
              <Can permission={DeployPermissionByEnvironment[tier] as string}>
                <RollbackControl
                  applicationId={applicationId}
                  environmentDefinitionId={environmentDef.id}
                  candidates={deploymentsForEnv.filter((d) => d.id !== latestSucceeded?.id)}
                  onChanged={onChanged}
                />
              </Can>
            )}

            {environmentDef && (
              <ContainerMonitoringSection applicationId={applicationId} environmentDefinitionId={environmentDef.id} />
            )}
          </div>

          {!can(Permissions.DeploymentsDeployDev) && tier === 'DEV' && (
            <p className="mt-2 text-xs text-slate-400">You don't have permission to deploy to DEV.</p>
          )}
        </>
      )}
    </Card>
  );
}

function DeployDevForm({
  applicationId,
  environmentDefinitionId,
  onDeployed,
}: {
  applicationId: string;
  environmentDefinitionId: string;
  onDeployed: () => void;
}) {
  const [commitSha, setCommitSha] = useState('');

  return (
    <div className="space-y-2">
      <GitCommitLookup applicationId={applicationId} environmentDefinitionId={environmentDefinitionId} onPick={(sha) => setCommitSha(sha)} />
      <input
        value={commitSha}
        onChange={(e) => setCommitSha(e.target.value)}
        placeholder="Commit SHA to deploy"
        className="w-full rounded-md border border-slate-300 px-2 py-1 text-xs focus:border-slate-500 focus:outline-none"
      />
      <ActionButton
        label="Deploy to DEV"
        disabled={!commitSha.trim()}
        disabledReason="Enter a commit SHA first."
        onAction={() =>
          ApplicationsApi.deployToDev(applicationId, {
            commitSha: commitSha.trim(),
            commitMessage: null,
            commitAuthor: null,
            branch: null,
            releaseId: null,
          })
        }
        onSuccess={() => {
          setCommitSha('');
          onDeployed();
        }}
      />
    </div>
  );
}

/** ContainerImage-mode DEV deploy — picks an immutable Release rather than a
 * raw commit; CommitSha/Branch/ImageReference are all derived server-side
 * from the selected Release (master requirements §16/§17). */
function DeployReleaseForm({ applicationId, onDeployed }: { applicationId: string; onDeployed: () => void }) {
  const { data: releases, isLoading, error } = useAsyncData(() => ApplicationsApi.releases(applicationId), [applicationId]);
  const [releaseId, setReleaseId] = useState('');

  if (isLoading) return <p className="text-xs text-slate-400">Loading releases…</p>;
  if (error) return <p className="text-xs text-rose-600">{error}</p>;
  if (!releases || releases.length === 0) return <p className="text-xs text-slate-400">No releases yet — request a build first.</p>;

  return (
    <div className="space-y-2">
      <select
        value={releaseId}
        onChange={(e) => setReleaseId(e.target.value)}
        className="w-full rounded-md border border-slate-300 px-2 py-1 text-xs focus:border-slate-500 focus:outline-none"
      >
        <option value="">Select a release to deploy…</option>
        {releases.map((r) => (
          <option key={r.id} value={r.id}>
            build #{r.buildNumber} — {shortSha(r.commitSha)} — {r.imageReference}
          </option>
        ))}
      </select>
      <ActionButton
        label="Deploy to DEV"
        disabled={!releaseId}
        disabledReason="Select a release first."
        onAction={() =>
          ApplicationsApi.deployToDev(applicationId, {
            commitSha: null,
            commitMessage: null,
            commitAuthor: null,
            branch: null,
            releaseId,
          })
        }
        onSuccess={() => {
          setReleaseId('');
          onDeployed();
        }}
      />
    </div>
  );
}

function PromotionControls({
  tier,
  applicationId,
  environmentDefinitionId,
  previousSucceeded,
  pendingPromotion,
  onChanged,
}: {
  tier: EnvironmentTier;
  applicationId: string;
  environmentDefinitionId: string;
  previousSucceeded?: DeploymentDto;
  pendingPromotion?: PromotionRequestDto;
  onChanged: () => void;
}) {
  const promotePermission = PromotePermissionByEnvironment[tier];
  const approvePermission = ApprovePermissionByEnvironment[tier];
  const deployPermission = DeployPermissionByEnvironment[tier];

  if (pendingPromotion) {
    const productionGateOpen = !pendingPromotion.requiresCtoApproval || pendingPromotion.ctoApprovalStatus === ApprovalStatus.Approved;
    const canDeploy = pendingPromotion.status === ApprovalStatus.Approved && productionGateOpen;

    return (
      <div className="space-y-2 text-xs">
        <div className="flex items-center justify-between">
          <span className="text-slate-500">Promotion request</span>
          <ApprovalStatusBadge status={pendingPromotion.status} />
        </div>
        <Row label="Commit" value={shortSha(pendingPromotion.commitSha)} />
        <Row label="Requested by" value={pendingPromotion.requestedByUsername ?? 'unknown'} />
        <Row label="Requested" value={formatDateTime(pendingPromotion.requestedAt)} />
        {pendingPromotion.requiresCtoApproval && (
          <div className="flex items-center justify-between">
            <span className="text-slate-500">CTO approval</span>
            {pendingPromotion.ctoApprovalStatus !== null ? (
              <ApprovalStatusBadge status={pendingPromotion.ctoApprovalStatus} />
            ) : (
              <span className="text-slate-400">—</span>
            )}
          </div>
        )}

        {pendingPromotion.status === ApprovalStatus.PendingApproval && approvePermission && (
          <div className="flex flex-wrap gap-2 pt-1">
            <Can permission={approvePermission}>
              <ActionButton
                label="Approve"
                onAction={() => PromotionsApi.approve(pendingPromotion.id, { notes: null })}
                onSuccess={onChanged}
              />
              <ActionButton
                label="Reject"
                variant="secondary"
                onAction={() => PromotionsApi.reject(pendingPromotion.id, { notes: null })}
                onSuccess={onChanged}
              />
            </Can>
          </div>
        )}

        {deployPermission && (
          <Can permission={deployPermission}>
            <div className="pt-1">
              <ActionButton
                label={`Deploy to ${tier}`}
                disabled={!canDeploy}
                disabledReason={
                  pendingPromotion.status !== ApprovalStatus.Approved
                    ? 'Waiting for approval.'
                    : 'Waiting for CTO approval.'
                }
                onAction={() => PromotionsApi.deploy(pendingPromotion.id)}
                onSuccess={onChanged}
              />
            </div>
          </Can>
        )}
      </div>
    );
  }

  if (!promotePermission) return null;

  return (
    <Can permission={promotePermission}>
      <ActionButton
        label={`Go Ahead to ${tier}`}
        disabled={!previousSucceeded}
        disabledReason="No successful deployment in the previous environment to promote."
        onAction={() =>
          ApplicationsApi.requestPromotion(applicationId, environmentDefinitionId, {
            sourceDeploymentId: (previousSucceeded as DeploymentDto).id,
          })
        }
        onSuccess={onChanged}
      />
    </Can>
  );
}

function RollbackControl({
  applicationId,
  environmentDefinitionId,
  candidates,
  onChanged,
}: {
  applicationId: string;
  environmentDefinitionId: string;
  candidates: DeploymentDto[];
  onChanged: () => void;
}) {
  const [target, setTarget] = useState('');
  if (candidates.length === 0) return null;

  return (
    <div className="mt-3 space-y-2 border-t border-slate-100 pt-3">
      <label className="block text-xs font-medium text-slate-500">Rollback to</label>
      <select
        value={target}
        onChange={(e) => setTarget(e.target.value)}
        className="w-full rounded-md border border-slate-300 px-2 py-1 text-xs focus:border-slate-500 focus:outline-none"
      >
        <option value="">Select a previous successful deployment…</option>
        {candidates.map((d) => (
          <option key={d.id} value={d.id}>
            {shortSha(d.commitSha)} — {formatDateTime(d.completedAt)}
          </option>
        ))}
      </select>
      <ActionButton
        label="Rollback"
        variant="danger"
        confirmLabel="Confirm rollback"
        disabled={!target}
        onAction={() => ApplicationsApi.rollback(applicationId, environmentDefinitionId, { targetDeploymentId: target })}
        onSuccess={() => {
          setTarget('');
          onChanged();
        }}
      />
    </div>
  );
}

/** Real container status/health from the configured TargetServer, with
 * controlled Start/Stop/Restart/Recreate actions (master requirements
 * §8/§9/§10). Never shows anything for an environment with no compose config
 * (IsConfigured=false) — that's a distinct state from "configured but the
 * target server is currently unreachable" (IsReachable=false). */
function ContainerMonitoringSection({
  applicationId,
  environmentDefinitionId,
}: {
  applicationId: string;
  environmentDefinitionId: string;
}) {
  const { can } = useAuth();
  const { data: status, isLoading, error, reload } = useAsyncData(
    () => ApplicationsApi.containerStatus(applicationId, environmentDefinitionId),
    [applicationId, environmentDefinitionId],
  );
  const [openLogsFor, setOpenLogsFor] = useState<string | null>(null);

  if (!can(Permissions.ContainersView)) return null;
  if (isLoading) return <p className="mt-3 border-t border-slate-100 pt-3 text-xs text-slate-400">Loading containers…</p>;
  if (error) return <p className="mt-3 border-t border-slate-100 pt-3 text-xs text-rose-600">{error}</p>;
  if (!status || !status.isConfigured) return null;

  return (
    <div className="mt-3 space-y-2 border-t border-slate-100 pt-3">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-slate-500">Containers</span>
        {status.targetServerName && <span className="text-xs text-slate-400">{status.targetServerName}</span>}
      </div>

      {!status.isReachable ? (
        <p className="text-xs text-rose-600">{status.unreachableReason ?? 'Target server is unreachable.'}</p>
      ) : status.containers.length === 0 ? (
        <p className="text-xs text-slate-400">No containers found.</p>
      ) : (
        <ul className="space-y-1.5">
          {status.containers.map((c) => (
            <li key={c.containerName} className="rounded-md bg-slate-50 px-2 py-1.5 text-xs">
              <div className="flex items-center justify-between gap-2">
                <div className="min-w-0">
                  <p className="truncate font-medium text-slate-800">{c.containerName}</p>
                  <p className="truncate text-slate-500">
                    {c.image}
                    {c.imageTag ? `:${c.imageTag}` : ''}
                  </p>
                </div>
                <ContainerStateBadge state={c.state} />
              </div>

              <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-slate-500">
                <span>Restarts: {c.restartCount}</span>
                {c.stats ? (
                  <>
                    {c.stats.cpuPercent !== null && <span>CPU: {c.stats.cpuPercent.toFixed(1)}%</span>}
                    {c.stats.memoryUsage !== null && (
                      <span>
                        Mem: {c.stats.memoryUsage}
                        {c.stats.memoryLimit ? ` / ${c.stats.memoryLimit}` : ''}
                        {c.stats.memoryPercent !== null ? ` (${c.stats.memoryPercent.toFixed(1)}%)` : ''}
                      </span>
                    )}
                    {c.stats.pidCount !== null && <span>PIDs: {c.stats.pidCount}</span>}
                  </>
                ) : (
                  <span className="text-slate-400">stats unavailable</span>
                )}
                <button
                  type="button"
                  className="ml-auto text-slate-500 underline hover:text-slate-700"
                  onClick={() => setOpenLogsFor(openLogsFor === c.containerName ? null : c.containerName)}
                >
                  {openLogsFor === c.containerName ? 'Hide logs' : 'Logs'}
                </button>
              </div>

              {openLogsFor === c.containerName && (
                <ContainerLogsPanel
                  applicationId={applicationId}
                  environmentDefinitionId={environmentDefinitionId}
                  containerName={c.containerName}
                />
              )}
            </li>
          ))}
        </ul>
      )}

      {status.healthCheck && status.healthCheck.type !== HealthCheckType.None && (
        <p className="text-xs text-slate-500">
          Health check:{' '}
          {status.healthCheck.lastProbePassed === null ? 'unknown' : status.healthCheck.lastProbePassed ? 'passing' : 'failing'}
        </p>
      )}

      {status.isReachable && (can(Permissions.ContainersControl) || can(Permissions.ContainersRecreate)) && (
        <div className="flex flex-wrap gap-2 pt-1">
          {can(Permissions.ContainersControl) && (
            <>
              <ActionButton
                label="Restart"
                variant="secondary"
                onAction={() => ApplicationsApi.restartContainers(applicationId, environmentDefinitionId)}
                onSuccess={reload}
              />
              <ActionButton
                label="Start"
                variant="secondary"
                onAction={() => ApplicationsApi.startContainers(applicationId, environmentDefinitionId)}
                onSuccess={reload}
              />
              <ActionButton
                label="Stop"
                variant="secondary"
                onAction={() => ApplicationsApi.stopContainers(applicationId, environmentDefinitionId)}
                onSuccess={reload}
              />
            </>
          )}
          {can(Permissions.ContainersRecreate) && (
            <ActionButton
              label="Recreate (destroys volumes)"
              variant="danger"
              confirmLabel="Confirm: this destroys volumes"
              onAction={() => ApplicationsApi.recreateContainers(applicationId, environmentDefinitionId, { confirm: true })}
              onSuccess={reload}
            />
          )}
        </div>
      )}
    </div>
  );
}

const LOG_TAIL_OPTIONS = [100, 200, 500, 1000] as const;

/** Recent `docker logs --tail N` output for one container, fetched on demand
 * (never auto-polled) — a configurable tail-lines select, a manual refresh,
 * and readable monospace output with its own loading/error states. */
function ContainerLogsPanel({
  applicationId,
  environmentDefinitionId,
  containerName,
}: {
  applicationId: string;
  environmentDefinitionId: string;
  containerName: string;
}) {
  const [tailLines, setTailLines] = useState<number>(200);
  const { data, isLoading, error, reload } = useAsyncData(
    () => ApplicationsApi.containerLogs(applicationId, environmentDefinitionId, containerName, tailLines),
    [applicationId, environmentDefinitionId, containerName, tailLines],
  );

  return (
    <div className="mt-2 rounded-md border border-slate-200 bg-white p-2">
      <div className="flex items-center justify-between gap-2">
        <label className="flex items-center gap-1.5 text-slate-500">
          Show last
          <select
            className="rounded border border-slate-300 px-1 py-0.5 text-xs"
            value={tailLines}
            onChange={(e) => setTailLines(Number(e.target.value))}
          >
            {LOG_TAIL_OPTIONS.map((n) => (
              <option key={n} value={n}>
                {n} lines
              </option>
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
          {!data.success && <p className="mt-2 text-rose-600">{data.logs || 'Could not fetch logs for this container.'}</p>}
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

function DeploymentHistoryTable({ deployments }: { deployments: DeploymentDto[] }) {
  const sorted = [...deployments].sort((a, b) => new Date(b.requestedAt).getTime() - new Date(a.requestedAt).getTime());
  if (sorted.length === 0) return <EmptyState title="No deployments yet for this application." />;

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
      <table className="min-w-full divide-y divide-slate-200 text-sm">
        <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
          <tr>
            <th className="px-4 py-2">Environment</th>
            <th className="px-4 py-2">Commit</th>
            <th className="px-4 py-2">Status</th>
            <th className="px-4 py-2">Requested by</th>
            <th className="px-4 py-2">Requested</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {sorted.slice(0, 25).map((d) => (
            <tr key={d.id}>
              <td className="px-4 py-2">
                <Link to={`/deployments/${d.id}`} className="hover:underline">
                  {d.environmentName}
                </Link>
              </td>
              <td className="px-4 py-2 font-mono text-xs">{shortSha(d.commitSha)}{d.isRollback && <span className="ml-1 text-amber-600">(rollback)</span>}</td>
              <td className="px-4 py-2"><DeploymentStatusBadge status={d.status} /></td>
              <td className="px-4 py-2">{d.requestedByUsername ?? '—'}</td>
              <td className="px-4 py-2 text-slate-500">{formatDateTime(d.requestedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
