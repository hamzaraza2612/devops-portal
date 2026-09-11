import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { EnvironmentDashboardPage } from './EnvironmentDashboardPage';
import { ApplicationsApi, DeploymentsApi, EnvironmentsApi, PromotionsApi } from '../api/endpoints';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';
import { ContainerState, type EnvironmentDefinitionDto, type EnvironmentInfrastructureDto } from '../types/api';

vi.mock('../api/endpoints', () => ({
  ApplicationsApi: { list: vi.fn(), recreateContainers: vi.fn() },
  DeploymentsApi: { list: vi.fn() },
  PromotionsApi: { listPending: vi.fn() },
  EnvironmentsApi: {
    list: vi.fn(),
    infrastructure: vi.fn(),
    startContainer: vi.fn(),
    stopContainer: vi.fn(),
    restartContainer: vi.fn(),
    containerLogs: vi.fn(),
  },
}));

function authValue(permissions: string[]): AuthContextValue {
  return {
    user: {
      id: '1', username: 'u', email: 'u@example.local', fullName: 'U', isActive: true, isAdmin: false,
      canApproveProduction: false, environmentAccess: [], createdAt: '', lastLoginAt: null, roles: [], permissions,
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
    <MemoryRouter initialEntries={['/environments/dev']}>
      <AuthContext.Provider value={authValue(permissions)}>
        <Routes>
          <Route path="/environments/:tier" element={<EnvironmentDashboardPage />} />
        </Routes>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const devEnvironmentDef: EnvironmentDefinitionDto = {
  id: 'dev-id', name: 'DEV', sortOrder: 0, isProductionLike: false, isActive: true,
  primaryTargetServerId: null, primaryTargetServerName: null,
};

function baseSetup() {
  vi.mocked(ApplicationsApi.list).mockResolvedValue([]);
  vi.mocked(DeploymentsApi.list).mockResolvedValue([]);
  vi.mocked(PromotionsApi.listPending).mockResolvedValue([]);
  vi.mocked(EnvironmentsApi.list).mockResolvedValue([devEnvironmentDef]);
}

describe('EnvironmentDashboardPage — Environment Infrastructure Dashboard', () => {
  beforeEach(() => {
    baseSetup();
  });

  it('shows a clear "not assigned" message, never an empty container list, when no target server is configured', async () => {
    vi.mocked(EnvironmentsApi.infrastructure).mockResolvedValue({
      environmentDefinitionId: 'dev-id',
      environmentName: 'DEV',
      server: {
        targetServerId: null, targetServerName: null, hostname: null, isConfigured: false, sshConnected: false,
        authenticatedUser: null, osInfo: null, uptimeInfo: null, dockerAvailable: false, dockerVersion: null,
        composeAvailable: false, composeVersion: null, metrics: null,
        errorMessage: 'No target server is assigned to this environment yet — an admin can assign one from the Environments admin page.',
        retrievedAt: '2026-01-01T00:00:00Z',
      },
      containers: [],
    } satisfies EnvironmentInfrastructureDto);

    renderPage([Permissions.DeploymentsView, Permissions.ContainersView]);

    expect(await screen.findByText(/No target server is assigned/)).toBeInTheDocument();
    expect(screen.queryByText(/No containers found/)).not.toBeInTheDocument();
  });

  it('shows a clear SSH-failure message, never "no containers", when the server is configured but unreachable', async () => {
    vi.mocked(EnvironmentsApi.infrastructure).mockResolvedValue({
      environmentDefinitionId: 'dev-id',
      environmentName: 'DEV',
      server: {
        targetServerId: 'srv-1', targetServerName: 'DEV-Techbey', hostname: '10.0.0.5', isConfigured: true, sshConnected: false,
        authenticatedUser: null, osInfo: null, uptimeInfo: null, dockerAvailable: false, dockerVersion: null,
        composeAvailable: false, composeVersion: null, metrics: null,
        errorMessage: 'SSH connection failed: Connection timed out',
        retrievedAt: '2026-01-01T00:00:00Z',
      },
      containers: [],
    } satisfies EnvironmentInfrastructureDto);

    renderPage([Permissions.DeploymentsView, Permissions.ContainersView]);

    expect(await screen.findByText(/Unable to connect to DEV-Techbey via SSH/)).toBeInTheDocument();
    expect(screen.queryByText(/No containers found/)).not.toBeInTheDocument();
  });

  it('shows every discovered container, marking an unmapped one as "Unregistered container"', async () => {
    vi.mocked(EnvironmentsApi.infrastructure).mockResolvedValue({
      environmentDefinitionId: 'dev-id',
      environmentName: 'DEV',
      server: {
        targetServerId: 'srv-1', targetServerName: 'DEV-Techbey', hostname: '10.0.0.5', isConfigured: true, sshConnected: true,
        authenticatedUser: 'deploy', osInfo: 'Linux', uptimeInfo: 'up 1 day', dockerAvailable: true, dockerVersion: '24.0.0',
        composeAvailable: true, composeVersion: '2.20.0',
        metrics: { load1: 0.1, load5: 0.2, load15: 0.3, memTotalBytes: 1000, memUsedBytes: 500, memAvailableBytes: 500, diskTotalBytes: 1000, diskUsedBytes: 200, diskAvailableBytes: 800, diskUsePercent: 20 },
        errorMessage: null,
        retrievedAt: '2026-01-01T00:00:00Z',
      },
      containers: [
        {
          containerId: 'c1', name: 'dmsapi-web-1', image: 'dmsapi', imageTag: '1.0', state: ContainerState.Running,
          dockerHealthStatus: 'healthy', createdAt: '2026-01-01T00:00:00Z', startedAt: '2026-01-01T00:00:00Z', restartCount: 0,
          ports: [], stats: null, isMapped: true, applicationId: 'app-1', applicationName: 'DmsApi', environmentDefinitionId: 'dev-id', canRecreateWithVolumes: false,
        },
        {
          containerId: 'c2', name: 'mystery-container', image: 'mystery', imageTag: null, state: ContainerState.Running,
          dockerHealthStatus: null, createdAt: '2026-01-01T00:00:00Z', startedAt: null, restartCount: 0,
          ports: [], stats: null, isMapped: false, applicationId: null, applicationName: null, environmentDefinitionId: null, canRecreateWithVolumes: false,
        },
      ],
    } satisfies EnvironmentInfrastructureDto);

    renderPage([Permissions.DeploymentsView, Permissions.ContainersView]);

    expect(await screen.findByText('DmsApi')).toBeInTheDocument();
    expect(screen.getByText('mystery-container')).toBeInTheDocument();
    expect(screen.getByText('Unregistered container')).toBeInTheDocument();
  });

  it('renders nothing for the infrastructure section when the user lacks ContainersView', async () => {
    vi.mocked(EnvironmentsApi.infrastructure).mockResolvedValue({
      environmentDefinitionId: 'dev-id',
      environmentName: 'DEV',
      server: {
        targetServerId: null, targetServerName: null, hostname: null, isConfigured: false, sshConnected: false,
        authenticatedUser: null, osInfo: null, uptimeInfo: null, dockerAvailable: false, dockerVersion: null,
        composeAvailable: false, composeVersion: null, metrics: null, errorMessage: 'not configured',
        retrievedAt: '2026-01-01T00:00:00Z',
      },
      containers: [],
    } satisfies EnvironmentInfrastructureDto);

    renderPage(['deployments.view']);

    expect(await screen.findByRole('heading', { name: 'DEV' })).toBeInTheDocument();
    expect(screen.queryByText(/not configured/)).not.toBeInTheDocument();
    expect(EnvironmentsApi.infrastructure).not.toHaveBeenCalled();
  });
});
