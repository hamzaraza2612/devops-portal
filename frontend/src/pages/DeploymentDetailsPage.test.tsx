import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DeploymentDetailsPage } from './DeploymentDetailsPage';
import { DeploymentsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { DeploymentLogLevel, DeploymentStatus, type DeploymentDto } from '../types/api';

vi.mock('../api/endpoints', () => ({
  DeploymentsApi: { get: vi.fn(), logs: vi.fn() },
}));

function renderAt(deploymentId: string) {
  return render(
    <MemoryRouter initialEntries={[`/deployments/${deploymentId}`]}>
      <Routes>
        <Route path="/deployments/:id" element={<DeploymentDetailsPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const succeededDeployment: DeploymentDto = {
  id: 'dep-1',
  applicationId: 'app-1',
  applicationName: 'DmsApi',
  environmentDefinitionId: 'qa',
  environmentName: 'QA',
  commitSha: 'abc1234567',
  commitMessage: 'Fix bug',
  commitAuthor: 'dev1',
  branch: 'main',
  imageReference: null,
  versionLabel: null,
  status: DeploymentStatus.Succeeded,
  isRollback: false,
  rollbackOfDeploymentId: null,
  promotionRequestId: null,
  requestedByUserId: 'u1',
  requestedByUsername: 'dev1',
  requestedAt: '2025-01-01T00:00:00Z',
  startedAt: '2025-01-01T00:00:01Z',
  completedAt: '2025-01-01T00:00:05Z',
  failureReason: null,
  healthCheckPassed: true,
  healthCheckDetail: null,
};

describe('DeploymentDetailsPage', () => {
  beforeEach(() => {
    vi.mocked(DeploymentsApi.get).mockResolvedValue(succeededDeployment);
  });

  it('loads and renders the log entries returned by the API', async () => {
    vi.mocked(DeploymentsApi.logs).mockResolvedValue([
      { sequence: 1, timestamp: '2025-01-01T00:00:01Z', level: DeploymentLogLevel.Info, message: 'Deployment started for DmsApi/QA at commit abc1234567.' },
      { sequence: 2, timestamp: '2025-01-01T00:00:05Z', level: DeploymentLogLevel.Info, message: 'Deployment succeeded.' },
    ]);

    renderAt('dep-1');

    expect(await screen.findByText(/Deployment started for DmsApi\/QA/)).toBeInTheDocument();
    expect(screen.getByText(/Deployment succeeded\./)).toBeInTheDocument();
    expect(DeploymentsApi.logs).toHaveBeenCalledWith('dep-1');
  });

  it('shows a readable message, not a raw error, when the logs request fails', async () => {
    vi.mocked(DeploymentsApi.logs).mockRejectedValue(new ApiError(500, 'An unexpected error occurred.'));

    renderAt('dep-1');

    expect(await screen.findByText('An unexpected error occurred.')).toBeInTheDocument();
  });

  it('shows a not-found message rather than crashing when the deployment does not exist', async () => {
    vi.mocked(DeploymentsApi.get).mockRejectedValue(new ApiError(404, "Deployment 'missing' was not found."));

    renderAt('missing');

    expect(await screen.findByText("Deployment 'missing' was not found.")).toBeInTheDocument();
  });
});
