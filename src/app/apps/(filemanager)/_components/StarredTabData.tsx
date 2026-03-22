import React from "react";
import { Col, Row } from "reactstrap";
import FileCommonCard from "@/app/apps/(filemanager)/_components/FileCommonCard";
import FolderCommonCard from "@/app/apps/(filemanager)/_components/FolderCommonCard";

interface StarredTabDataProps {
  starredItems: Array<{
    title: string;
    type: "file" | "folder";
    image?: string;
    icon?: string;
    used?: string;
    total?: string;
  }>;
}

const StarredTabData: React.FC<StarredTabDataProps> = ({ starredItems }) => {
  const fileItems = starredItems.filter((item) => item.type === "file");
  const folderItems = starredItems.filter((item) => item.type === "folder");

  if (starredItems.length === 0) {
    return <div className="text-center py-4">No starred items yet</div>;
  }

  return (
    <>
      {fileItems.length > 0 && (
        <Row className="gy-4">
          {fileItems.map((item, index) => (
            <Col key={`file-${index}`} sm="6" xl="4" xxl="3">
              <FileCommonCard
                title={item.title}
                image={item.image || ""}
                starred={true}
                onStar={() => {}}
              />
            </Col>
          ))}
        </Row>
      )}

      {folderItems.length > 0 && (
        <Row className="gy-4">
          {folderItems.map((item, index) => (
            <Col key={`folder-${index}`} sm="6" xl="4" xxl="3">
              <FolderCommonCard
                title={item.title}
                used={item.used || ""}
                total={item.total || ""}
                icon={item.icon || ""}
                starred={true}
                onStar={() => {}}
              />
            </Col>
          ))}
        </Row>
      )}
    </>
  );
};

export default StarredTabData;
