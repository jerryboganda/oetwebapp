import React from "react";
import { Modal, ModalBody, Button } from "reactstrap";

interface DeleteModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
}

const DeleteModal: React.FC<DeleteModalProps> = ({
  isOpen,
  onClose,
  onConfirm,
}) => {
  return (
    <Modal isOpen={isOpen} toggle={onClose} centered>
      <ModalBody>
        <div className="text-center p-3">
          <img
            src="/images/icons/delete-icon.png"
            alt="delete-icon"
            className="img-fluid mb-3"
          />
          <h4 className="text-danger fw-bold">Are You Sure?</h4>
          <p className="text-secondary fs-6">
            You won&#39;t be able to revert this!
          </p>

          <div className="d-flex justify-content-center gap-2 mt-4">
            <Button color="secondary" onClick={onClose}>
              Close
            </Button>
            <Button color="primary" outline onClick={onConfirm}>
              Yes, Delete it
            </Button>
          </div>
        </div>
      </ModalBody>
    </Modal>
  );
};

export default DeleteModal;
