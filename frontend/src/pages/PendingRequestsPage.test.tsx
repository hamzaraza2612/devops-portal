import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PendingRequestsPage } from './PendingRequestsPage';
import { PromotionsApi } from '../api/endpoints';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';
import { ApprovalStatus, type PromotionRequestDto } from '../types/api';

vi.mock('../api/endpoints', () => ({
  PromotionsApi: { listPending: vi.fn() },
}));

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

function renderPage(permissions: string[]) {
  return render(
    <MemoryRouter>
      <AuthContext.Provider value={authValue(permissions)}>
        <PendingRequestsPage />
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const qaPromotion: PromotionRequestDto = {
  id: 'promo-qa',
  applicationId: 'app-1',
  applicationName: 'DmsApi',
  fromEnvironmentDefinitionId: 'dev',
  fromEnvironmentName: 'DEV',
  toEnvironmentDefinitionId: 'qa',
  toEnvironmentName: 'QA',
  sourceDeploymentId: 'dep-1',
  commitSha: 'abc1234',
  status: ApprovalStatus.PendingApproval,
  requestedByUserId: 'u1',
  requestedByUsername: 'developer1',
  requestedAt: '2025-01-01T00:00:00Z',
  decidedByUserId: null,
  decidedByUsername: null,
  decidedAt: null,
  decisionNotes: null,
  notifiedAt: null,
  requiresCtoApproval: false,
  ctoApprovalStatus: null,
  ctoDecidedByUserId: null,
  ctoDecidedByUsername: null,
  ctoDecidedAt: null,
  ctoNotifiedAt: null,
  linkedDeploymentId: null,
  linkedDeploymentStatus: null,
};

const uatPromotion: PromotionRequestDto = { ...qaPromotion, id: 'promo-uat', toEnvironmentDefinitionId: 'uat', toEnvironmentName: 'UAT' };

describe('PendingRequestsPage', () => {
  beforeEach(() => {
    vi.mocked(PromotionsApi.listPending).mockResolvedValue([qaPromotion, uatPromotion]);
  });

  it('shows only the QA tab, with the QA request, for a user who only has QA-environment access', async () => {
    renderPage([Permissions.DeploymentsApproveQa]);

    expect(await screen.findByText('DmsApi')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /QA/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /UAT/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /PRODUCTION/ })).not.toBeInTheDocument();
  });

  it('shows only the UAT tab, with the UAT request, for a user who only has UAT-environment access', async () => {
    renderPage([Permissions.DeploymentsApproveUat]);

    expect(await screen.findByText('DmsApi')).toBeInTheDocument();
    expect(screen.getByText(/DEV → UAT/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^QA/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /PRODUCTION/ })).not.toBeInTheDocument();
  });

  it('shows a clear message instead of any tab for a user with no environment access at all', async () => {
    renderPage(['users.view']);

    expect(await screen.findByText(/don't have access to any environment/)).toBeInTheDocument();
    expect(screen.queryByText('DmsApi')).not.toBeInTheDocument();
  });
});
