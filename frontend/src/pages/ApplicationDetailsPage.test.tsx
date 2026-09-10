import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApplicationDetailsPage } from './ApplicationDetailsPage';
import { ApplicationsApi, DeploymentsApi, EnvironmentsApi, PromotionsApi } from '../api/endpoints';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';
import {
  DeploymentMode,
  HealthCheckType,
  type ApplicationDto,
  type ApplicationEnvironmentDto,
  type EnvironmentDefinitionDto,
} from '../types/api';

vi.mock('../api/endpoints', () => ({
  ApplicationsApi: { get: vi.fn(), environments: vi.fn(), deployToDev: vi.fn(), requestPromotion: vi.fn(), rollback: vi.fn() },
  EnvironmentsApi: { list: vi.fn() },
  DeploymentsApi: { list: vi.fn() },
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
    <MemoryRouter initialEntries={['/applications/app-1']}>
      <AuthContext.Provider value={authValue(permissions)}>
        <Routes>
          <Route path="/applications/:id" element={<ApplicationDetailsPage />} />
        </Routes>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const application: ApplicationDto = {
  id: 'app-1',
  name: 'DmsApi',
  slug: 'dmsapi',
  description: null,
  deploymentMode: DeploymentMode.LegacyFilesystem,
  repositoryId: null,
  repositoryName: null,
  sourcePath: null,
  isActive: true,
  createdAt: '2025-01-01T00:00:00Z',
  updatedAt: null,
};

const environmentDefs: EnvironmentDefinitionDto[] = [
  { id: 'dev', name: 'DEV', sortOrder: 0, isProductionLike: false, isActive: true },
  { id: 'qa', name: 'QA', sortOrder: 1, isProductionLike: false, isActive: true },
  { id: 'uat', name: 'UAT', sortOrder: 2, isProductionLike: false, isActive: true },
  { id: 'prod', name: 'PRODUCTION', sortOrder: 3, isProductionLike: true, isActive: true },
];

const devEnvironment: ApplicationEnvironmentDto = {
  id: 'appenv-dev',
  applicationId: 'app-1',
  environmentDefinitionId: 'dev',
  environmentName: 'DEV',
  targetServerId: 'server-1',
  targetServerName: 'server-1',
  branchName: 'develop',
  deploymentRootPath: '/opt/apps/dmsapi',
  publishSubPath: 'publish',
  backupSubPath: 'backups',
  backupRetentionCount: 3,
  composeFilePath: 'docker-compose.yml',
  composeProjectName: null,
  serviceName: null,
  containerName: null,
  externalNetworkName: null,
  useDownWithVolumesOnDeploy: false,
  healthCheckType: HealthCheckType.None,
  healthCheckEndpoint: null,
  healthCheckIntervalSeconds: 30,
  healthCheckTimeoutSeconds: 5,
  applicationUrl: 'https://dev.dmsapi.example.com',
  isActive: true,
  createdAt: '2025-01-01T00:00:00Z',
  updatedAt: null,
};

describe('ApplicationDetailsPage', () => {
  beforeEach(() => {
    vi.mocked(ApplicationsApi.get).mockResolvedValue(application);
    vi.mocked(EnvironmentsApi.list).mockResolvedValue(environmentDefs);
    vi.mocked(ApplicationsApi.environments).mockResolvedValue([devEnvironment]);
    vi.mocked(DeploymentsApi.list).mockResolvedValue([]);
    vi.mocked(PromotionsApi.listPending).mockResolvedValue([]);
  });

  it('renders the configured application URL as a link that opens in a new tab', async () => {
    renderPage([Permissions.DeploymentsDeployDev, Permissions.DeploymentsView]);

    const link = await screen.findByRole('link', { name: /Open application/ });
    expect(link).toHaveAttribute('href', 'https://dev.dmsapi.example.com');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'));
  });

  it('does not show the application URL to a user without any access to that environment', async () => {
    renderPage(['users.view']);

    expect(await screen.findByText('DEV')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Open application/ })).not.toBeInTheDocument();
  });

  it('invokes the deploy-to-DEV API with the entered commit SHA when a Developer clicks Deploy to DEV', async () => {
    vi.mocked(ApplicationsApi.deployToDev).mockResolvedValue({
      ...devEnvironment,
    } as never);
    const user = userEvent.setup();
    renderPage([Permissions.DeploymentsDeployDev]);

    const input = await screen.findByPlaceholderText('Commit SHA to deploy');
    await user.type(input, 'abc1234');
    await user.click(screen.getByRole('button', { name: 'Deploy to DEV' }));

    expect(ApplicationsApi.deployToDev).toHaveBeenCalledWith('app-1', {
      commitSha: 'abc1234',
      commitMessage: null,
      commitAuthor: null,
      branch: null,
    });
  });

  it('does not show the Deploy to DEV action to a user without deploy.dev permission', async () => {
    renderPage(['users.view']);

    expect(await screen.findByText('DEV')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Deploy to DEV' })).not.toBeInTheDocument();
    expect(screen.getByText("You don't have permission to deploy to DEV.")).toBeInTheDocument();
  });
});
