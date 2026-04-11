import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import { Drawer, Modal } from '../modal';

function ModalHarness() {
  const [open, setOpen] = useState(false);

  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>
        Open modal
      </button>
      <Modal open={open} onClose={() => setOpen(false)} title="Adjust Credits">
        <button type="button">First action</button>
      </Modal>
    </div>
  );
}

function DrawerHarness() {
  const [open, setOpen] = useState(false);

  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>
        Open drawer
      </button>
      <Drawer open={open} onClose={() => setOpen(false)} title="Audit Event Detail">
        <button type="button">Drawer action</button>
      </Drawer>
    </div>
  );
}

describe('Modal', () => {
  it('restores focus to the trigger after the dialog closes', async () => {
    render(<ModalHarness />);

    const trigger = screen.getByRole('button', { name: /open modal/i });
    trigger.focus();
    fireEvent.click(trigger);

    const dialog = await screen.findByRole('dialog', { name: /adjust credits/i });
    expect(dialog).toBeInTheDocument();

    fireEvent.keyDown(document, { key: 'Escape' });

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /adjust credits/i })).not.toBeInTheDocument();
      expect(document.activeElement).toBe(trigger);
    });
  });

  it('restores focus to the trigger after the drawer closes', async () => {
    render(<DrawerHarness />);

    const trigger = screen.getByRole('button', { name: /open drawer/i });
    trigger.focus();
    fireEvent.click(trigger);

    const dialog = await screen.findByRole('dialog', { name: /audit event detail/i });
    expect(dialog).toBeInTheDocument();

    fireEvent.keyDown(document, { key: 'Escape' });

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /audit event detail/i })).not.toBeInTheDocument();
      expect(document.activeElement).toBe(trigger);
    });
  });
});
