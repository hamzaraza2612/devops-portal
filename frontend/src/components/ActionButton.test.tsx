import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ActionButton } from './ActionButton';

describe('ActionButton', () => {
  it('invokes onAction (the API call) when clicked, and reports success', async () => {
    const onAction = vi.fn().mockResolvedValue(undefined);
    const onSuccess = vi.fn();
    const user = userEvent.setup();

    render(<ActionButton label="Deploy to DEV" onAction={onAction} onSuccess={onSuccess} />);
    await user.click(screen.getByRole('button', { name: 'Deploy to DEV' }));

    expect(onAction).toHaveBeenCalledTimes(1);
    expect(await screen.findByRole('button', { name: 'Deploy to DEV' })).toBeInTheDocument();
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  it('requires a second confirming click for a destructive action before calling the API', async () => {
    const onAction = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();

    render(<ActionButton label="Rollback" confirmLabel="Confirm rollback" onAction={onAction} />);

    await user.click(screen.getByRole('button', { name: 'Rollback' }));
    expect(onAction).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Confirm rollback' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Confirm rollback' }));
    expect(onAction).toHaveBeenCalledTimes(1);
  });

  it('shows the backend error message inline when the API call fails, without throwing', async () => {
    const onAction = vi.fn().mockRejectedValue(new Error('CommitSha must be a 7-40 character hexadecimal git commit SHA.'));
    const user = userEvent.setup();

    render(<ActionButton label="Deploy to DEV" onAction={onAction} />);
    await user.click(screen.getByRole('button', { name: 'Deploy to DEV' }));

    expect(await screen.findByText(/CommitSha must be a 7-40 character/)).toBeInTheDocument();
  });

  it('does not call the API when disabled', async () => {
    const onAction = vi.fn();
    const user = userEvent.setup();

    render(<ActionButton label="Deploy to Production" disabled onAction={onAction} />);
    await user.click(screen.getByRole('button', { name: 'Deploy to Production' }));

    expect(onAction).not.toHaveBeenCalled();
  });
});
