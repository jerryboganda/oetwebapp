import React, { useState, useRef, ChangeEvent } from "react";
import {
  Row,
  Col,
  Card,
  CardHeader,
  CardBody,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
} from "reactstrap";

const Fileuploads: React.FC = () => {
  const [fileName, setFileName] = useState<string>("No file chosen, yet.");
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleCustomFileUpload = (): void => {
    fileInputRef.current?.click();
  };

  const handleFileChange = (e: ChangeEvent<HTMLInputElement>): void => {
    setFileName(e.target.files?.[0]?.name || "No file chosen, yet.");
  };

  return (
    <div>
      <Card>
        <CardHeader>
          <h5>Basic File Upload</h5>
        </CardHeader>
        <CardBody>
          <Row>
            <Col sm="12" xl="6">
              <Form className="app-form">
                <FormGroup>
                  <Label className="mt-2">File Upload</Label>
                  <Input type="file" className="form-control" />
                </FormGroup>
              </Form>
            </Col>
            <Col sm="12" xl="6">
              <FormGroup>
                <Label className="mt-2">Custom File Upload</Label>
                <input
                  type="file"
                  ref={fileInputRef}
                  hidden
                  onChange={handleFileChange}
                />
                <div className="d-flex align-items-center gap-3">
                  <Button
                    color="primary"
                    onClick={handleCustomFileUpload}
                    className="flex-shrink-0"
                  >
                    Add file
                  </Button>
                  <span className="custom-text">{fileName}</span>
                </div>
              </FormGroup>
            </Col>
          </Row>
        </CardBody>
      </Card>
    </div>
  );
};

export default Fileuploads;
