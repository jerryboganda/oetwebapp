"use client";
import React, { useState, ChangeEvent, startTransition } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  FormGroup,
  Input,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { handleCustomTextarea } from "../action";
import { IconCreditCard } from "@tabler/icons-react";

const TextareaPage: React.FC = () => {
  const [writeText, setWriteText] = useState<string>("");
  const [customTextarea, setCustomTextarea] = useState<string>("");
  const [customOutput, setCustomOutput] = useState<string>("");

  // Handler for textarea formatter
  const handleWriteTextChange = (e: ChangeEvent<HTMLInputElement>) => {
    setWriteText(e.target.value);
  };

  // Handler for custom textarea formatter
  const handleSubmit = async (formData: FormData) => {
    const result = await handleCustomTextarea(formData);
    startTransition(() => {
      setCustomOutput(result);
    });
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Textarea"
          title="Forms Elements"
          path={["Textarea"]}
          Icon={IconCreditCard}
        />
        <Row>
          <Col md="12">
            <Card>
              <CardHeader>
                <h5>Basic Textarea</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  <Col md="6">
                    <FormGroup>
                      <label htmlFor="textareaexample" className="form-label">
                        Simple Textarea
                      </label>
                      <Input
                        type="textarea"
                        id="textareaexample"
                        placeholder="Some text..."
                        rows={2}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6">
                    <FormGroup>
                      <label className="form-label">Floating Textarea</label>
                      <div className="form-floating">
                        <Input
                          type="textarea"
                          id="floatingTextarea2"
                          placeholder="Type Your Address"
                        />
                        <label htmlFor="floatingTextarea2">Address</label>
                      </div>
                    </FormGroup>
                  </Col>
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col md="12">
            <Card>
              <CardHeader>
                <h5>Textarea Formatter</h5>
              </CardHeader>
              <CardBody>
                <FormGroup>
                  <div className="mb-3">
                    <Input
                      type="textarea"
                      id="write-textarea"
                      placeholder="Write some text.."
                      value={writeText}
                      onChange={handleWriteTextChange}
                    />
                  </div>
                  <div className="form-control h-40" id="code-output">
                    {writeText}
                  </div>
                </FormGroup>
              </CardBody>
            </Card>
          </Col>

          <Col md="12">
            <Card>
              <CardHeader>
                <h5>Custom Textarea Formatter</h5>
              </CardHeader>
              <CardBody>
                <form action={handleSubmit}>
                  <FormGroup>
                    <div className="mb-3">
                      <Input
                        type="textarea"
                        name="myTextarea"
                        id="myTextarea"
                        placeholder="Write some text..."
                        value={customTextarea}
                        onChange={(e: ChangeEvent<HTMLInputElement>) =>
                          setCustomTextarea(e.target.value)
                        }
                      />
                    </div>
                    <div className="mb-3">
                      <Button id="submitBtn" type="submit" color="primary">
                        Submit Code
                      </Button>
                    </div>
                    <div className="form-control h-40" id="output">
                      {customOutput}
                    </div>
                  </FormGroup>
                </form>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default TextareaPage;
