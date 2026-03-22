import React, { useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Row,
  Col,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
} from "reactstrap";
import { IconCheck, IconX } from "@tabler/icons-react";

const ClientRegistration = () => {
  const [hasApplied, setHasApplied] = useState("yes");
  const [hasApplied1, setHasApplied1] = useState("yes1");

  const handleRadioChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = e.target;
    if (name === "radiocheck") setHasApplied(value);
    if (name === "radiocheck1") setHasApplied1(value);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Client Registration</h5>
        </CardHeader>
        <CardBody>
          <Form className="app-form">
            <Row>
              <Col md="12">
                <FormGroup>
                  <Label for="companyName">Company Name</Label>
                  <Input
                    type="text"
                    id="companyName"
                    placeholder="Enter Company Name"
                    required
                  />
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label for="firstName">First Name</Label>
                  <Input
                    type="text"
                    id="firstName"
                    placeholder="Enter Your First Name"
                    required
                  />
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label for="lastName">Last Name</Label>
                  <Input
                    type="text"
                    id="lastName"
                    placeholder="Enter Your Last Name"
                    required
                  />
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label for="address">Address</Label>
                  <Input
                    type="textarea"
                    id="address"
                    rows="3"
                    placeholder="Enter Address"
                  />
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label for="companyAddress">Company Address</Label>
                  <Input
                    type="textarea"
                    id="companyAddress"
                    rows="3"
                    placeholder="Enter Company Address"
                  />
                </FormGroup>
              </Col>
              <Col md="4">
                <FormGroup>
                  <Label for="city">City</Label>
                  <Input type="text" id="city" placeholder="Enter Your City" />
                </FormGroup>
              </Col>
              <Col md="4">
                <FormGroup>
                  <Label for="state">State</Label>
                  <Input
                    type="text"
                    id="state"
                    placeholder="Enter Your State"
                  />
                </FormGroup>
              </Col>
              <Col md="4">
                <FormGroup>
                  <Label for="zip">Zip</Label>
                  <Input
                    type="text"
                    id="zip"
                    placeholder="xxxxx"
                    maxLength={5}
                  />
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label for="email">Email Address</Label>
                  <Input
                    type="email"
                    id="email"
                    placeholder="Enter Your Email"
                    required
                  />
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label for="profileUrl">LinkedIn/Online Profile URL</Label>
                  <Input
                    type="url"
                    id="profileUrl"
                    placeholder="Enter Your URL"
                  />
                </FormGroup>
              </Col>
              <Col md="12">
                <FormGroup>
                  <Label for="registrationRequests">
                    Specific Registration Requests/Details
                  </Label>
                  <Input
                    type="textarea"
                    id="registrationRequests"
                    rows="3"
                    placeholder="Enter Your Message"
                  />
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label>Would you like to receive our monthly email?</Label>
                  <div>
                    <FormGroup className="form-selectgroup">
                      <div className="d-flex gap-2">
                        <Label
                          check
                          className="d-flex align-items-center select-items"
                        >
                          <Input
                            type="radio"
                            name="radiocheck"
                            value="yes"
                            checked={hasApplied === "yes"}
                            onChange={handleRadioChange}
                            className="me-2 select-input"
                          />
                          <span className="select-box">
                            <span className="selectitem">
                              <IconCheck
                                size={16}
                                className="align-text-bottom me-1"
                              />
                              Yes
                            </span>
                          </span>
                        </Label>

                        <Label
                          check
                          className="d-flex align-items-center select-items"
                        >
                          <Input
                            type="radio"
                            name="radiocheck"
                            value="no"
                            checked={hasApplied === "no"}
                            onChange={handleRadioChange}
                            className="me-2 select-input"
                          />
                          <span className="select-box">
                            <span className="selectitem">
                              <IconX
                                size={16}
                                className="align-text-bottom me-1"
                              />{" "}
                              No
                            </span>
                          </span>
                        </Label>
                      </div>
                    </FormGroup>
                  </div>
                </FormGroup>
              </Col>
              <Col md="6">
                <FormGroup>
                  <Label>
                    Would you like to participate in our client surveys?
                  </Label>
                  <div>
                    <FormGroup className="form-selectgroup">
                      <div className="d-flex gap-2">
                        <Label
                          check
                          className="d-flex align-items-center select-items"
                        >
                          <Input
                            type="radio"
                            name="radiocheck1"
                            value="yes1"
                            checked={hasApplied1 === "yes1"}
                            onChange={handleRadioChange}
                            className="me-2 select-input"
                          />
                          <span className="select-box">
                            <span className="selectitem">
                              <IconCheck
                                size={16}
                                className="align-text-bottom me-1"
                              />
                              Yes
                            </span>
                          </span>
                        </Label>
                        <Label
                          check
                          className="d-flex align-items-center select-items"
                        >
                          <Input
                            type="radio"
                            name="radiocheck1"
                            value="no"
                            checked={hasApplied1 === "no"}
                            onChange={handleRadioChange}
                            className="me-2 select-input"
                          />
                          <span className="select-box">
                            <span className="selectitem">
                              <IconX
                                size={16}
                                className="align-text-bottom me-1"
                              />
                              No
                            </span>
                          </span>
                        </Label>
                      </div>
                    </FormGroup>
                  </div>
                </FormGroup>
              </Col>
              <Col md="12" className="text-end">
                <Button color="primary" type="submit" className="me-2">
                  Submit
                </Button>
                <Button color="secondary" type="reset">
                  Reset
                </Button>
              </Col>
            </Row>
          </Form>
        </CardBody>
      </Card>
    </>
  );
};

export default ClientRegistration;
