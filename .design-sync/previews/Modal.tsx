// Authored preview — Modal. OVERLAY component (single-card mode, already
// configured). Rendered in its OPEN state — pass open + onClose so AnimatePresence
// mounts the dialog. Each named export = one labeled card cell (full-card each).
import { Modal, Button, InlineAlert } from 'oet-prep';

export const Confirmation = () => (
  <Modal open={true} onClose={() => {}} title="Submit for marking?">
    <p style={{ margin: 0, fontSize: 14, lineHeight: 1.7 }}>
      Once you submit your Writing referral letter it is sent for marking and can no longer be
      edited. Your detailed feedback usually arrives within 24 hours.
    </p>
    <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', marginTop: 24 }}>
      <Button variant="ghost" onClick={() => {}}>
        Keep editing
      </Button>
      <Button onClick={() => {}}>Submit for marking</Button>
    </div>
  </Modal>
);

export const Destructive = () => (
  <Modal open={true} onClose={() => {}} title="Delete this attempt?" size="sm">
    <InlineAlert variant="warning">
      This permanently removes your Reading mock attempt and its score. This cannot be undone.
    </InlineAlert>
    <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', marginTop: 20 }}>
      <Button variant="ghost" onClick={() => {}}>
        Cancel
      </Button>
      <Button variant="destructive" onClick={() => {}}>
        Delete attempt
      </Button>
    </div>
  </Modal>
);
