import type { ApprovalStatus, DeploymentStatus } from '../types/api';
import { ApprovalStatusColor, ApprovalStatusLabel, DeploymentStatusColor, DeploymentStatusLabel } from '../utils/status';

export function DeploymentStatusBadge({ status }: { status: DeploymentStatus }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${DeploymentStatusColor[status]}`}>
      {DeploymentStatusLabel[status]}
    </span>
  );
}

export function ApprovalStatusBadge({ status }: { status: ApprovalStatus }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${ApprovalStatusColor[status]}`}>
      {ApprovalStatusLabel[status]}
    </span>
  );
}
