import { ApprovalStatus, DeploymentStatus } from '../types/api';

export const DeploymentStatusLabel: Record<DeploymentStatus, string> = {
  [DeploymentStatus.Pending]: 'Pending',
  [DeploymentStatus.Queued]: 'Queued',
  [DeploymentStatus.Running]: 'Running',
  [DeploymentStatus.Succeeded]: 'Succeeded',
  [DeploymentStatus.Failed]: 'Failed',
  [DeploymentStatus.Cancelled]: 'Cancelled',
};

/** Tailwind class pairs (background/text) per status — kept in one place so
 * every page renders the same status the same color. */
export const DeploymentStatusColor: Record<DeploymentStatus, string> = {
  [DeploymentStatus.Pending]: 'bg-slate-100 text-slate-700',
  [DeploymentStatus.Queued]: 'bg-slate-100 text-slate-700',
  [DeploymentStatus.Running]: 'bg-blue-100 text-blue-700',
  [DeploymentStatus.Succeeded]: 'bg-emerald-100 text-emerald-700',
  [DeploymentStatus.Failed]: 'bg-red-100 text-red-700',
  [DeploymentStatus.Cancelled]: 'bg-amber-100 text-amber-700',
};

export const ApprovalStatusLabel: Record<ApprovalStatus, string> = {
  [ApprovalStatus.PendingApproval]: 'Pending approval',
  [ApprovalStatus.Approved]: 'Approved',
  [ApprovalStatus.Rejected]: 'Rejected',
};

export const ApprovalStatusColor: Record<ApprovalStatus, string> = {
  [ApprovalStatus.PendingApproval]: 'bg-amber-100 text-amber-700',
  [ApprovalStatus.Approved]: 'bg-emerald-100 text-emerald-700',
  [ApprovalStatus.Rejected]: 'bg-red-100 text-red-700',
};

export function isActiveDeployment(status: DeploymentStatus): boolean {
  return status === DeploymentStatus.Pending || status === DeploymentStatus.Queued || status === DeploymentStatus.Running;
}
