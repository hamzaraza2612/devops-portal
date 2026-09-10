import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, EnvironmentsApi, PromotionsApi } from '../api/endpoints';
import { ActionButton } from '../components/ActionButton';
import { Can, Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { ApprovalStatusBadge, DeploymentStatusBadge } from '../components/StatusBadge';
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
  DeploymentStatus,
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
                <DeployDevForm applicationId={applicationId} environmentDefinitionId={environmentDef.id} onDeployed={onChanged} />
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

            {environmentDef && (
              <Can permission={Permissions.DeploymentsRollback}>
                <RollbackControl
                  applicationId={applicationId}
                  environmentDefinitionId={environmentDef.id}
                  candidates={deploymentsForEnv.filter((d) => d.id !== latestSucceeded?.id)}
                  onChanged={onChanged}
                />
              </Can>
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
        onAction={() => ApplicationsApi.deployToDev(applicationId, { commitSha: commitSha.trim(), commitMessage: null, commitAuthor: null, branch: null })}
        onSuccess={() => {
          setCommitSha('');
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
        label={`Request ${tier} promotion`}
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
