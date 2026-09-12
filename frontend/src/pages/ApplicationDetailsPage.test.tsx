import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApplicationDetailsPage } from './ApplicationDetailsPage';
import { ApplicationsApi, DeploymentsApi, EnvironmentsApi, PromotionsApi, TargetServersApi } from '../api/endpoints';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';
import {
  ContainerState,
  DeploymentMode,
  HealthCheckType,
  type ApplicationDto,
  type ApplicationEnvironmentDto,
  type ContainerEnvironmentStatusDto,
  type EnvironmentDefinitionDto,
} from '../types/api';

vi.mock('../api/endpoints', () => ({
  ApplicationsApi: {
    get: vi.fn(),
    environments: vi.fn(),
    deployToDev: vi.fn(),
    requestPromotion: vi.fn(),
    rollback: vi.fn(),
    releases: vi.fn(),
    containerStatus: vi.fn(),
    containerLogs: vi.fn(),
    restartContainers: vi.fn(),
    startContainers: vi.fn(),
    stopContainers: vi.fn(),
    recreateContainers: vi.fn(),
  },
  EnvironmentsApi: { list: vi.fn() },
  DeploymentsApi: { list: vi.fn() },
  PromotionsApi: { listPending: vi.fn() },
  TargetServersApi: { list: vi.fn() },
}));

function authValue(permissions: string[]): AuthContextValue {
  return {
    user: {
      id: '1',
      username: 'u',
      email: 'u@example.local',
      fullName: 'U',
      isActive: true,
      isAdmin: false,
      canApproveProduction: false,
      environmentAccess: [],
      createdAt: '',
      lastLoginAt: null,
      roles: [],
      permissions,
    },
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
  { id: 'dev', name: 'DEV', sortOrder: 0, isProductionLike: false, isActive: true, primaryTargetServerId: null, primaryTargetServerName: null },
  { id: 'qa', name: 'QA', sortOrder: 1, isProductionLike: false, isActive: true, primaryTargetServerId: null, primaryTargetServerName: null },
  { id: 'uat', name: 'UAT', sortOrder: 2, isProductionLike: false, isActive: true, primaryTargetServerId: null, primaryTargetServerName: null },
  { id: 'prod', name: 'PRODUCTION', sortOrder: 3, isProductionLike: true, isActive: true, primaryTargetServerId: null, primaryTargetServerName: null },
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
  syncSourceFromRepository: false,
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
    vi.mocked(TargetServersApi.list).mockResolvedValue([]);
    vi.mocked(ApplicationsApi.containerStatus).mockResolvedValue({
      applicationId: 'app-1',
      environmentDefinitionId: 'dev',
      environmentName: 'DEV',
      isConfigured: false,
      isReachable: false,
      unreachableReason: null,
      targetServerName: null,
      expectedServiceName: null,
      expectedContainerName: null,
      containers: [],
      healthCheck: null,
      currentImageOrVersion: null,
      lastRestartAt: null,
      latestDeploymentId: null,
      latestDeploymentStatus: null,
    });
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
      releaseId: null,
    });
  });

  it('does not show the Deploy to DEV action to a user without deploy.dev permission', async () => {
    renderPage(['users.view']);

    expect(await screen.findByText('DEV')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Deploy to DEV' })).not.toBeInTheDocument();
    expect(screen.getByText("You don't have permission to deploy to DEV.")).toBeInTheDocument();
  });

  describe('container monitoring — CPU/memory stats and logs', () => {
    const reachableStatus: ContainerEnvironmentStatusDto = {
      applicationId: 'app-1',
      environmentDefinitionId: 'dev',
      environmentName: 'DEV',
      isConfigured: true,
      isReachable: true,
      unreachableReason: null,
      targetServerName: 'server-1',
      expectedServiceName: null,
      expectedContainerName: null,
      containers: [
        {
          serviceName: 'web',
          containerName: 'dmsapi-web-1',
          image: 'dmsapi',
          imageTag: 'abc1234',
          state: ContainerState.Running,
          dockerHealthStatus: null,
          startedAt: '2025-01-01T00:00:00Z',
          uptime: null,
          restartCount: 2,
          ports: [],
          stats: {
            cpuPercent: 1.23,
            memoryUsage: '128MiB',
            memoryLimit: '1.952GiB',
            memoryPercent: 6.4,
            networkIO: '1.2kB / 3.4kB',
            blockIO: '0B / 0B',
            pidCount: 7,
          },
        },
      ],
      healthCheck: null,
      currentImageOrVersion: 'dmsapi:abc1234',
      lastRestartAt: null,
      latestDeploymentId: null,
      latestDeploymentStatus: null,
    };

    it('renders container CPU and memory stats', async () => {
      vi.mocked(ApplicationsApi.containerStatus).mockResolvedValue(reachableStatus);
      renderPage([Permissions.ContainersView]);

      expect(await screen.findByText('dmsapi-web-1')).toBeInTheDocument();
      expect(screen.getByText('CPU: 1.2%')).toBeInTheDocument();
      expect(screen.getByText('Mem: 128MiB / 1.952GiB (6.4%)')).toBeInTheDocument();
      expect(screen.getByText('PIDs: 7')).toBeInTheDocument();
    });

    it('shows "stats unavailable" when no stats snapshot was returned', async () => {
      vi.mocked(ApplicationsApi.containerStatus).mockResolvedValue({
        ...reachableStatus,
        containers: [{ ...reachableStatus.containers[0], stats: null }],
      });
      renderPage([Permissions.ContainersView]);

      expect(await screen.findByText('dmsapi-web-1')).toBeInTheDocument();
      expect(screen.getByText('stats unavailable')).toBeInTheDocument();
    });

    it('opens the log viewer, shows a loading state, then renders fetched logs', async () => {
      const user = userEvent.setup();
      vi.mocked(ApplicationsApi.containerStatus).mockResolvedValue(reachableStatus);
      let resolveLogs!: (value: { containerName: string; success: boolean; logs: string }) => void;
      vi.mocked(ApplicationsApi.containerLogs).mockReturnValue(
        new Promise((resolve) => {
          resolveLogs = resolve;
        }),
      );
      renderPage([Permissions.ContainersView]);

      await user.click(await screen.findByRole('button', { name: 'Logs' }));

      expect(ApplicationsApi.containerLogs).toHaveBeenCalledWith('app-1', 'dev', 'dmsapi-web-1', 200);
      expect(screen.getByText('Loading logs…')).toBeInTheDocument();

      resolveLogs({ containerName: 'dmsapi-web-1', success: true, logs: 'line 1\nline 2\n' });

      // The default text-matcher normalizer collapses internal whitespace
      // (including newlines) into single spaces before comparing.
      expect(await screen.findByText('line 1 line 2')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Hide logs' })).toBeInTheDocument();
    });

    it('shows an error state when the logs request fails', async () => {
      const user = userEvent.setup();
      vi.mocked(ApplicationsApi.containerStatus).mockResolvedValue(reachableStatus);
      vi.mocked(ApplicationsApi.containerLogs).mockRejectedValue(new Error('Network error'));
      renderPage([Permissions.ContainersView]);

      await user.click(await screen.findByRole('button', { name: 'Logs' }));

      expect(await screen.findByText('Network error')).toBeInTheDocument();
    });

    it('shows a message when the server responds but could not fetch logs', async () => {
      const user = userEvent.setup();
      vi.mocked(ApplicationsApi.containerStatus).mockResolvedValue(reachableStatus);
      vi.mocked(ApplicationsApi.containerLogs).mockResolvedValue({
        containerName: 'dmsapi-web-1',
        success: false,
        logs: 'not a container of this application environment',
      });
      renderPage([Permissions.ContainersView]);

      await user.click(await screen.findByRole('button', { name: 'Logs' }));

      expect(await screen.findByText('not a container of this application environment')).toBeInTheDocument();
    });
  });
});
