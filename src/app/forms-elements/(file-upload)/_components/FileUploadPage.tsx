"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import BasicFileupload from "@/app/forms-elements/(file-upload)/_components/BasicFileupload";
import FilepondUploader from "@/app/forms-elements/(file-upload)/_components/FilepondUploader";
import CircleUploader from "@/app/forms-elements/(file-upload)/_components/CircleUploader";
import SolidFileupload from "@/app/forms-elements/(file-upload)/_components/SolidFileupload";
import LightFileupload from "@/app/forms-elements/(file-upload)/_components/LightFileupload";
import { IconCreditCard } from "@tabler/icons-react";

const FileUploadPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="File Upload"
          title="Forms elements"
          path={["File Upload"]}
          Icon={IconCreditCard}
        />
        <Row>
          <Col xs="12">
            <BasicFileupload />
          </Col>
          <Col xl="8">
            <FilepondUploader />
          </Col>
          <Col xl="4">
            <CircleUploader />
          </Col>
          <Col xs="12">
            <SolidFileupload />
          </Col>
          <Col xs="12">
            <LightFileupload />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default FileUploadPage;
