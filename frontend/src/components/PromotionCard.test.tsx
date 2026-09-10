import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { PromotionCard } from './PromotionCard';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';
import { ApprovalStatus, type PromotionRequestDto } from '../types/api';

function authValue(permissions: string[]): AuthContextValue {
  return {
    user: { id: '1', username: 'u', email: 'u@example.local', fullName: 'U', isActive: true, createdAt: '', lastLoginAt: null, roles: [], permissions },
    isLoading: false,
    error: null,
    login: async () => {},
    logout: () => {},
    can: (p: string) => permissions.includes(p),
  };
}

function renderCard(promotion: PromotionRequestDto, permissions: string[]) {
  return render(
    <MemoryRouter>
      <AuthContext.Provider value={authValue(permissions)}>
        <PromotionCard promotion={promotion} onChanged={() => {}} />
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const baseQaPromotion: PromotionRequestDto = {
  id: 'promo-1',
  applicationId: 'app-1',
  applicationName: 'DmsApi',
  fromEnvironmentDefinitionId: 'dev-env',
  fromEnvironmentName: 'DEV',
  toEnvironmentDefinitionId: 'qa-env',
  toEnvironmentName: 'QA',
  sourceDeploymentId: 'dep-1',
  commitSha: 'abc1234567',
  status: ApprovalStatus.PendingApproval,
  requestedByUserId: 'user-1',
  requestedByUsername: 'developer1',
  requestedAt: '2025-01-01T00:00:00Z',
  decidedByUserId: null,
  decidedAt: null,
  decisionNotes: null,
  requiresCtoApproval: false,
  ctoApprovalStatus: null,
  ctoEmailSentAt: null,
};

describe('PromotionCard', () => {
  it('shows application, environment, commit, requester and status exactly per the required format', () => {
    renderCard(baseQaPromotion, [Permissions.DeploymentsApproveQa]);

    expect(screen.getByText('DmsApi')).toBeInTheDocument();
    expect(screen.getByText(/DEV → QA/)).toBeInTheDocument();
    expect(screen.getByText(/abc12345/)).toBeInTheDocument();
    expect(screen.getByText('developer1')).toBeInTheDocument();
    expect(screen.getByText('Pending approval')).toBeInTheDocument();
  });

  it('shows Approve/Reject only for a user holding the environment-specific approve permission', () => {
    renderCard(baseQaPromotion, [Permissions.DeploymentsApproveQa]);
    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument();
  });

  it('hides Approve/Reject for a user without the QA approve permission', () => {
    renderCard(baseQaPromotion, [Permissions.DeploymentsDeployDev]);
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument();
  });

  it('shows the CTO approval status separately for a Production promotion, and blocks deploy until it is granted', () => {
    const productionPromotion: PromotionRequestDto = {
      ...baseQaPromotion,
      toEnvironmentName: 'PRODUCTION',
      status: ApprovalStatus.Approved,
      requiresCtoApproval: true,
      ctoApprovalStatus: ApprovalStatus.PendingApproval,
    };
    renderCard(productionPromotion, [Permissions.DeploymentsDeployProduction]);

    expect(screen.getByText('CTO approval required for Production')).toBeInTheDocument();
    const deployButton = screen.getByRole('button', { name: /Deploy to PRODUCTION/ });
    expect(deployButton).toBeDisabled();
  });

  it('enables the deploy action once both the promotion and CTO approval are granted', () => {
    const productionPromotion: PromotionRequestDto = {
      ...baseQaPromotion,
      toEnvironmentName: 'PRODUCTION',
      status: ApprovalStatus.Approved,
      requiresCtoApproval: true,
      ctoApprovalStatus: ApprovalStatus.Approved,
    };
    renderCard(productionPromotion, [Permissions.DeploymentsDeployProduction]);

    const deployButton = screen.getByRole('button', { name: /Deploy to PRODUCTION/ });
    expect(deployButton).not.toBeDisabled();
  });
});
