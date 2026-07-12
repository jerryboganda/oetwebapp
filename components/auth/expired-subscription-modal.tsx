'use client';

import { useRouter } from 'next/navigation';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';

interface ExpiredSubscriptionModalProps {
  open: boolean;
  onClose: () => void;
}

/**
 * Shown on sign-in when the backend reports `subscription_expired`. Routes
 * the learner to /subscriptions to renew.
 */
export function ExpiredSubscriptionModal({ open, onClose }: ExpiredSubscriptionModalProps) {
  const router = useRouter();

  function handleRenew() {
    onClose();
    router.push('/subscriptions');
  }

  return (
    <Modal open={open} onClose={onClose} title="Your Subscription has expired" size="sm">
      <div className="space-y-5 py-2">
        <p className="text-sm text-navy">Please Renew Your subscription</p>
        <div className="flex justify-end gap-3 border-t border-border pt-4">
          <Button variant="primary" onClick={handleRenew}>
            Renew
          </Button>
        </div>
      </div>
    </Modal>
  );
}
