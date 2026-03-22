"use client";
import React, { useState, ChangeEvent } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Form,
  FormGroup,
  Label,
  Row,
  Input,
} from "reactstrap";
import TextEditor from "@/Component/CommonElements/TextEditor";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconStack2 } from "@tabler/icons-react";

interface FileDetails {
  fileUrl: string;
  fileName: string;
}

const AddBlogPage: React.FC = () => {
  const [fileDetails, setFileDetails] = useState<FileDetails | null>(null);

  const handleFileChange = (e: ChangeEvent<HTMLInputElement>) => {
    if (typeof window === "undefined") return;

    const rawFile = e.target.files;
    if (rawFile && rawFile[0]) {
      const selectedFile = rawFile[0];
      const fileUrl = URL.createObjectURL(selectedFile);
      setFileDetails({ fileUrl, fileName: selectedFile.name });
    }
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Add Blog"
          title="Apps"
          path={["Blog-Page", "Blog"]}
          Icon={IconStack2}
        />
        <Row>
          <Col xl="12">
            <Card className="add-blog">
              <CardHeader>
                <h5>Add Blog</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <Row>
                    <Col md="6">
                      <FormGroup>
                        <Label for="title">Blog Title</Label>
                        <Input
                          className="form-control"
                          type="text"
                          name="title"
                          id="title"
                          placeholder="Blog Title"
                        />
                      </FormGroup>
                      <FormGroup>
                        <Label for="category">Category</Label>
                        <Input
                          className="form-control"
                          type="select"
                          name="category"
                          id="category"
                        >
                          <option value="">Select Category</option>
                          <option value="1">Category One</option>
                          <option value="2">Category Two</option>
                          <option value="3">Category Three</option>
                        </Input>
                      </FormGroup>
                      <FormGroup>
                        <Label for="description">Blog Description</Label>
                        <Input
                          className="form-control"
                          type="textarea"
                          name="description"
                          id="description"
                          placeholder="Type a description here"
                        />
                      </FormGroup>
                      <FormGroup>
                        <Label for="date">Blog Date</Label>
                        <Input
                          className="form-control"
                          type="date"
                          name="date"
                          id="date"
                        />
                      </FormGroup>
                    </Col>
                    <Col md="6">
                      <FormGroup className="file-uploader position-relative">
                        <Label for="file-input" id="uploaded_image">
                          <i className="fa-solid fa-cloud-arrow-up me-2 fs-1 mb-3 text-secondary"></i>
                          <span className="fs-5">Choose a file</span>
                          <span className="fs-6 text-secondary text-center ms-3 me-3">
                            JPEG, PNG & PDF formats, up to 50MB
                          </span>
                        </Label>
                        <Input
                          className="form-control"
                          type="file"
                          id="file-input"
                          name="file"
                          accept="image/jpeg, image/png, application/pdf"
                          onChange={handleFileChange}
                        />
                        {fileDetails && (
                          <div className="position-absolute top-0 start-50 translate-middle-x ">
                            <img
                              src={fileDetails.fileUrl}
                              alt="preview"
                              className="uploaded-image mt-10 img-fluid"
                            />
                            <p>{fileDetails.fileName}</p>
                          </div>
                        )}
                      </FormGroup>
                    </Col>
                  </Row>
                  <Row>
                    <Col xl="12" className="editor-details">
                      <TextEditor />
                    </Col>
                  </Row>
                  <Row>
                    <Col className="mt-3">
                      <div className="text-end">
                        <Button color="primary" type="submit">
                          Add Blog
                        </Button>
                        <Button
                          color="outline-primary"
                          type="button"
                          className="ms-2"
                        >
                          Cancel
                        </Button>
                      </div>
                    </Col>
                  </Row>
                </Form>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default AddBlogPage;
