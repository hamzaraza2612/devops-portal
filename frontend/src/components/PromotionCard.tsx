import { Link } from 'react-router-dom';
import { ActionButton } from './ActionButton';
import { Can } from './Common';
import { ApprovalStatusBadge } from './StatusBadge';
import { ApprovePermissionByEnvironment, DeployPermissionByEnvironment, type EnvironmentTier } from '../auth/permissions';
import { PromotionsApi } from '../api/endpoints';
import { ApprovalStatus, type PromotionRequestDto } from '../types/api';
import { formatDateTime, shortSha } from '../utils/format';
import { DeploymentStatusBadge } from './StatusBadge';

/** One pending (or recently decided) promotion request, laid out so the
 * reader immediately sees which application, which environment, which
 * commit, who requested it, when, and what happens next — never a bare,
 * unlabeled list row (master requirements §5). Reused by both the
 * environment-specific Pending Requests page and the application details
 * page's compact environment cards. */
export function PromotionCard({ promotion, onChanged }: { promotion: PromotionRequestDto; onChanged: () => void }) {
  const tier = promotion.toEnvironmentName as EnvironmentTier;
  const approvePermission = ApprovePermissionByEnvironment[tier];
  const deployPermission = DeployPermissionByEnvironment[tier];
  const productionGateOpen = !promotion.requiresCtoApproval || promotion.ctoApprovalStatus === ApprovalStatus.Approved;
  const canDeploy = promotion.status === ApprovalStatus.Approved && productionGateOpen;

  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <Link to={`/applications/${promotion.applicationId}`} className="text-sm font-semibold text-slate-900 hover:underline">
            {promotion.applicationName}
          </Link>
          <p className="text-xs text-slate-500">
            {promotion.fromEnvironmentName} → {promotion.toEnvironmentName} · commit <span className="font-mono">{shortSha(promotion.commitSha)}</span>
          </p>
        </div>
        <ApprovalStatusBadge status={promotion.status} />
      </div>

      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-slate-600">
        <div className="flex justify-between gap-2"><dt className="text-slate-400">Requested by</dt><dd>{promotion.requestedByUsername ?? 'unknown'}</dd></div>
        <div className="flex justify-between gap-2"><dt className="text-slate-400">Requested at</dt><dd>{formatDateTime(promotion.requestedAt)}</dd></div>
        {promotion.decidedAt && (
          <>
            <div className="flex justify-between gap-2"><dt className="text-slate-400">Approver</dt><dd>{promotion.decidedByUsername ?? 'unknown'}</dd></div>
            <div className="flex justify-between gap-2"><dt className="text-slate-400">Decided at</dt><dd>{formatDateTime(promotion.decidedAt)}</dd></div>
          </>
        )}
        {promotion.linkedDeploymentStatus !== null && (
          <div className="col-span-2 flex items-center justify-between gap-2">
            <dt className="text-slate-400">Deployment status</dt>
            <dd><DeploymentStatusBadge status={promotion.linkedDeploymentStatus} /></dd>
          </div>
        )}
      </dl>

      {promotion.requiresCtoApproval && (
        <div className="mt-2 rounded-md bg-slate-50 px-2 py-1.5 text-xs">
          <div className="flex items-center justify-between">
            <span className="font-medium text-slate-600">CTO approval required for Production</span>
            {promotion.ctoApprovalStatus !== null ? (
              <ApprovalStatusBadge status={promotion.ctoApprovalStatus} />
            ) : (
              <span className="text-slate-400">Not yet requested</span>
            )}
          </div>
          {promotion.ctoDecidedAt && (
            <div className="mt-1 flex justify-between text-slate-400">
              <span>{promotion.ctoDecidedByUsername ?? 'unknown'}</span>
              <span>{formatDateTime(promotion.ctoDecidedAt)}</span>
            </div>
          )}
        </div>
      )}

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <span className="text-xs font-medium text-slate-500">
          Next action: {nextActionLabel(promotion, canDeploy)}
        </span>
      </div>

      {promotion.status === ApprovalStatus.PendingApproval && approvePermission && (
        <div className="mt-2 flex flex-wrap gap-2">
          <Can permission={approvePermission}>
            <ActionButton label="Approve" onAction={() => PromotionsApi.approve(promotion.id, { notes: null })} onSuccess={onChanged} />
            <ActionButton
              label="Reject"
              variant="secondary"
              confirmLabel="Confirm reject"
              onAction={() => PromotionsApi.reject(promotion.id, { notes: null })}
              onSuccess={onChanged}
            />
          </Can>
        </div>
      )}

      {deployPermission && promotion.status === ApprovalStatus.Approved && (
        <div className="mt-2">
          <Can permission={deployPermission}>
            <ActionButton
              label={`Deploy to ${promotion.toEnvironmentName}`}
              disabled={!canDeploy}
              disabledReason="Waiting for CTO approval."
              onAction={() => PromotionsApi.deploy(promotion.id)}
              onSuccess={onChanged}
            />
          </Can>
        </div>
      )}
    </div>
  );
}

function nextActionLabel(promotion: PromotionRequestDto, canDeploy: boolean): string {
  if (promotion.status === ApprovalStatus.Rejected) return 'None — request was rejected.';
  if (promotion.status === ApprovalStatus.PendingApproval) return `Awaiting ${promotion.toEnvironmentName} approval.`;
  if (promotion.requiresCtoApproval && promotion.ctoApprovalStatus !== ApprovalStatus.Approved) return 'Awaiting CTO approval.';
  if (canDeploy) return `Ready — deploy to ${promotion.toEnvironmentName}.`;
  return 'Awaiting approval.';
}
