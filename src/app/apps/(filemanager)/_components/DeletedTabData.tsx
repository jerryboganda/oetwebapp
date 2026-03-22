import React from "react";
import { Col, Row } from "reactstrap";
import FileCommonCard from "@/app/apps/(filemanager)/_components/FileCommonCard";
import FolderCommonCard from "@/app/apps/(filemanager)/_components/FolderCommonCard";

type DeletedItem = {
  title: string;
  type: "file" | "folder";
  image?: string;
  icon?: string;
  used?: string;
  total?: string;
};

type Props = {
  deletedFiles: DeletedItem[];
};

const DeletedTabData: React.FC<Props> = ({ deletedFiles }) => {
  const deletedFileItems = deletedFiles.filter((item) => item.type === "file");
  const deletedFolderItems = deletedFiles.filter(
    (item) => item.type === "folder"
  );

  return (
    <>
      <Row className="mb-4">
        {deletedFileItems.map((item, idx) => (
          <Col sm="6" xl="4" xxl="3" key={`file-${idx}`}>
            <FileCommonCard
              title={item.title}
              image={item.image || "/images/icons/default-file.png"}
            />
          </Col>
        ))}
      </Row>

      <Row>
        {deletedFolderItems.map((item, idx) => (
          <Col sm="6" xl="4" xxl="3" key={`folder-${idx}`}>
            <FolderCommonCard
              title={item.title}
              icon={item.icon || "/images/icons/folder.png"}
              used={item.used || "-"}
              total={item.total || "-"}
            />
          </Col>
        ))}
      </Row>
    </>
  );
};

export default DeletedTabData;
