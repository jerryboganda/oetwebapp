import React, { useState } from "react";
import { Card, CardBody, CardFooter, CardHeader, Col, Row } from "reactstrap";
import FileCommonCard from "@/app/apps/(filemanager)/_components/FileCommonCard";
import FolderCommonCard from "@/app/apps/(filemanager)/_components/FolderCommonCard";
import {
  fileData as initialFileData,
  foldersData as initialFoldersData,
} from "@/Data/Apps/Filemanager/Filemanager";
import RecentCardTable from "@/app/apps/(filemanager)/_components/RecentCardTable";
import RenameModal from "./RenameModal";
import DeleteModal from "./DeleteModal";

type ItemType = {
  title: string;
  type: "file" | "folder";
  image?: string;
  icon?: string;
  used?: string;
  total?: string;
  starred?: boolean;
};

type Props = {
  onDelete: (item: ItemType) => void;
  onStar: (item: ItemType, isStarred: boolean) => void;
};

const CloudTabData = ({ onDelete, onStar }: Props) => {
  const [fileData, setFileData] = useState(
    initialFileData.map((f) => ({ ...f, starred: false }))
  );
  const [foldersData, setFoldersData] = useState(
    initialFoldersData.map((f) => ({ ...f, starred: false }))
  );

  const [renameModalOpen, setRenameModalOpen] = useState(false);
  const [deleteModalOpen, setDeleteModalOpen] = useState(false);

  const [currentItem, setCurrentItem] = useState<ItemType | null>(null);

  const [onRenameConfirm, setOnRenameConfirm] = useState<
    (name: string) => void
  >(() => {});
  const [onDeleteConfirm, setOnDeleteConfirm] = useState<() => void>(() => {});

  const openRenameModal = (item: ItemType) => {
    setCurrentItem(item);

    setOnRenameConfirm(() => (newName: string) => {
      const trimmedName = newName.trim();
      if (!trimmedName) return;

      if (item.type === "file") {
        setFileData((prev) =>
          prev.map((f) =>
            f.title === item.title ? { ...f, title: trimmedName } : f
          )
        );
      } else {
        setFoldersData((prev) =>
          prev.map((f) =>
            f.title === item.title ? { ...f, title: trimmedName } : f
          )
        );
      }
    });

    setRenameModalOpen(true);
  };

  const openDeleteModal = (item: ItemType) => {
    setCurrentItem(item);

    setOnDeleteConfirm(() => () => {
      if (item.type === "file") {
        setFileData((prev) => prev.filter((f) => f.title !== item.title));
      } else {
        setFoldersData((prev) => prev.filter((f) => f.title !== item.title));
      }

      onDelete(item);
    });

    setDeleteModalOpen(true);
  };

  const handleStar = (item: ItemType, isStarred: boolean) => {
    if (item.type === "file") {
      setFileData((prev) =>
        prev.map((f) =>
          f.title === item.title ? { ...f, starred: isStarred } : f
        )
      );
    } else {
      setFoldersData((prev) =>
        prev.map((f) =>
          f.title === item.title ? { ...f, starred: isStarred } : f
        )
      );
    }
    onStar(item, isStarred);
  };

  return (
    <>
      {/* Quick Access */}
      <Card>
        <CardHeader>
          <h5>Quick-Access</h5>
        </CardHeader>
        <CardBody>
          <Row>
            {fileData.map((file, index) => (
              <Col key={index} sm="6" xl="4" xxl="3">
                <FileCommonCard
                  title={file.title}
                  image={file.image}
                  starred={file.starred}
                  onStar={(starred) =>
                    handleStar({ ...file, type: "file" }, starred)
                  }
                  onRename={() => openRenameModal({ ...file, type: "file" })}
                  onDelete={() => openDeleteModal({ ...file, type: "file" })}
                />
              </Col>
            ))}
          </Row>
        </CardBody>
      </Card>

      {/* Folders */}
      <Card>
        <CardHeader>
          <h5>Folders</h5>
        </CardHeader>
        <CardBody>
          <Row>
            {foldersData.map((folder, index) => (
              <Col key={index} sm="6" xl="4" xxl="3">
                <FolderCommonCard
                  {...folder}
                  onStar={(starred) =>
                    handleStar({ ...folder, type: "folder" }, starred)
                  }
                  onRename={() =>
                    openRenameModal({ ...folder, type: "folder" })
                  }
                  onDelete={() =>
                    openDeleteModal({ ...folder, type: "folder" })
                  }
                />
              </Col>
            ))}
          </Row>
        </CardBody>
      </Card>

      {/* Recent Added */}
      <Card>
        <CardHeader>
          <h5>Recent Added</h5>
        </CardHeader>
        <CardBody className="p-0">
          <RecentCardTable />
        </CardBody>
        <CardFooter className="card-footer">
          <div className="seller-table-footer d-flex justify-content-between align-items-center">
            <p className="text-secondary text-truncate">
              Showing 1 to 6 of 24 order entries
            </p>
            <ul className="pagination app-pagination">
              <li className="page-item bg-light-secondary disabled">
                <a className="page-link b-r-left">Previous</a>
              </li>
              <li className="page-item">
                <a className="page-link">1</a>
              </li>
              <li className="page-item active">
                <a className="page-link">2</a>
              </li>
              <li className="page-item">
                <a className="page-link">3</a>
              </li>
              <li className="page-item">
                <a className="page-link">Next</a>
              </li>
            </ul>
          </div>
        </CardFooter>
      </Card>

      {/* Modals */}
      <RenameModal
        isOpen={renameModalOpen}
        currentName={currentItem?.title || ""}
        onClose={() => setRenameModalOpen(false)}
        onConfirm={(newName) => {
          onRenameConfirm(newName);
          setRenameModalOpen(false);
        }}
      />

      <DeleteModal
        isOpen={deleteModalOpen}
        onClose={() => setDeleteModalOpen(false)}
        onConfirm={() => {
          onDeleteConfirm();
          setDeleteModalOpen(false);
        }}
      />
    </>
  );
};

export default CloudTabData;
