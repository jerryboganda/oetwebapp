"use client";
import React, { useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import CloudTabData from "@/app/apps/(filemanager)/_components/CloudTabData";
import FileManagerSidebar from "@/app/apps/(filemanager)/_components/FileManagerSidebar";
import DeletedTabData from "@/app/apps/(filemanager)/_components/DeletedTabData";
import StarredTabData from "@/app/apps/(filemanager)/_components/StarredTabData";
import { IconStack2 } from "@tabler/icons-react";

type ItemType = {
  title: string;
  type: "file" | "folder";
  image?: string;
  icon?: string;
  used?: string;
  total?: string;
  starred?: boolean;
};

const FileManagerPage = () => {
  const [activeTab, setActiveTab] = useState("1");

  const [deletedFiles, setDeletedFiles] = useState<
    {
      title: string;
      type: "file" | "folder";
      image?: string;
      icon?: string;
      used?: string;
      total?: string;
    }[]
  >([]);

  const handleDelete = (item: {
    title: string;
    type: "file" | "folder";
    image?: string;
    icon?: string;
    used?: string;
    total?: string;
  }) => {
    setDeletedFiles((prev) => [...prev, item]);
  };
  const [starredItems, setStarredItems] = useState<ItemType[]>([]);

  const handleStar = (item: ItemType, isStarred: boolean) => {
    if (isStarred) {
      setStarredItems((prev) => {
        if (!prev.some((i) => i.title === item.title)) {
          return [...prev, item];
        }
        return prev;
      });
    } else {
      setStarredItems((prev) => prev.filter((i) => i.title !== item.title));
    }
  };

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="File Manager"
        title="Apps"
        path={["File Manager"]}
        Icon={IconStack2}
      />
      <Row>
        <Col lg={4} xxl={3}>
          <FileManagerSidebar
            activeTab={activeTab}
            setActiveTab={setActiveTab}
          />
        </Col>
        <Col lg={8} xxl={9}>
          <div className="content-wrapper">
            {/* Tab 1 - Cloud */}
            <div
              id="tab-1"
              className={`tabs-content ${activeTab === "1" ? "active" : ""}`}
            >
              <CloudTabData onDelete={handleDelete} onStar={handleStar} />
            </div>

            {/* Tab 2 - Starred */}
            <div
              id="tab-2"
              className={`tabs-content ${activeTab === "2" ? "active" : ""}`}
            >
              <Card className="documents-section">
                <CardHeader>
                  <h5>Starred Documents & Files</h5>
                </CardHeader>
                <CardBody>
                  <Row>
                    <StarredTabData starredItems={starredItems} />
                  </Row>
                </CardBody>
              </Card>
            </div>

            {/* Tab 3 - Deleted */}
            <div
              id="tab-3"
              className={`tabs-content ${activeTab === "3" ? "active" : ""}`}
            >
              <Card className="deleted-file documents-sections">
                <CardHeader>
                  <h5>Deleted Files</h5>
                </CardHeader>
                <CardBody>
                  <Row>
                    <DeletedTabData deletedFiles={deletedFiles} />
                  </Row>
                </CardBody>
              </Card>
            </div>

            {/* Tab 4 - Recent */}
            <div
              id="tab-4"
              className={`tabs-content ${activeTab === "4" ? "active" : ""}`}
            >
              <Card>
                <CardHeader>
                  <h5>Recent Added</h5>
                </CardHeader>
                <CardBody>
                  <Row></Row>
                </CardBody>
              </Card>
            </div>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export default FileManagerPage;
