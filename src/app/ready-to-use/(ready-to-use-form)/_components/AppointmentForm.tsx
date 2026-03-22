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

const AppointmentForm = () => {
  const [hasApplied, setHasApplied] = useState("yes");

  const handleRadioChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setHasApplied(e.target.value);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Book Appointment Form</h5>
        </CardHeader>
        <CardBody>
          <Form className="app-form">
            <Row>
              <Col md={6}>
                <FormGroup>
                  <Label for="firstName">First Name</Label>
                  <Input
                    type="text"
                    name="firstName"
                    id="firstName"
                    placeholder="Enter Your First Name"
                    required
                  />
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="lastName">Last Name</Label>
                  <Input
                    type="text"
                    name="lastName"
                    id="lastName"
                    placeholder="Enter Your Last Name"
                    required
                  />
                </FormGroup>
              </Col>
            </Row>

            <Row>
              <Col md={4}>
                <FormGroup>
                  <Label for="dob">Date of Birth</Label>
                  <Input type="date" name="dob" id="dob" />
                </FormGroup>
              </Col>
              <Col md={4}>
                <FormGroup>
                  <Label for="email">Email Address</Label>
                  <Input
                    type="email"
                    name="email"
                    id="email"
                    placeholder="Enter Your Email"
                    required
                  />
                </FormGroup>
              </Col>
              <Col md={4}>
                <FormGroup>
                  <Label for="contact">Contact</Label>
                  <div className="input-group">
                    <span className="input-group-text">+91</span>
                    <Input
                      type="text"
                      name="contact"
                      id="contact"
                      placeholder="Enter Your Contact"
                      required
                    />
                  </div>
                </FormGroup>
              </Col>
            </Row>

            <Row>
              <Col md={6} xl={4}>
                <FormGroup>
                  <Label for="gender">Gender</Label>
                  <Input type="select" name="gender" id="gender">
                    <option>Select Gender</option>
                    <option value="1">Male</option>
                    <option value="2">Female</option>
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6} xl={8}>
                <FormGroup>
                  <Label for="appointmentType">Appointment Type</Label>
                  <Input
                    type="select"
                    name="appointmentType"
                    id="appointmentType"
                  >
                    <option>Select Your Appointment</option>
                    <option value="1">Medical Examination</option>
                    <option value="2">Doctor Check</option>
                    <option value="3">Result Analysis</option>
                    <option value="4">Check Up</option>
                  </Input>
                </FormGroup>
              </Col>
            </Row>

            <Row>
              <Col md={4}>
                <FormGroup>
                  <Label for="doctor">Doctor</Label>
                  <Input type="select" name="doctor" id="doctor">
                    <option>Select Your Doctor</option>
                    <option value="1">Dr.Cedric Kelly</option>
                    <option value="2">Dr.Haley Kennedy</option>
                    <option value="3">Dr.Angelica Ramos</option>
                    <option value="4">Dr.Timothy Mooney</option>
                  </Input>
                </FormGroup>
              </Col>
              <Col md={4}>
                <FormGroup>
                  <Label for="appointmentDate">Appointment Date</Label>
                  <Input
                    type="date"
                    name="appointmentDate"
                    id="appointmentDate"
                    required
                  />
                </FormGroup>
              </Col>
              <Col md={4}>
                <FormGroup>
                  <Label for="appointmentTime">Appointment Time</Label>
                  <Input
                    type="time"
                    name="appointmentTime"
                    id="appointmentTime"
                    required
                  />
                </FormGroup>
              </Col>
            </Row>

            <Row>
              <Col md={12}>
                <FormGroup>
                  <Label for="message">Message</Label>
                  <textarea
                    className="form-control"
                    rows={3}
                    placeholder="Enter Your Message"
                  ></textarea>
                </FormGroup>
              </Col>
            </Row>

            <Row>
              <Col md={12}>
                <FormGroup>
                  <Label>Have you ever applied to our facility before?</Label>
                  <div className="d-flex">
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
            </Row>

            <Row>
              <Col md={12} className="text-end">
                <Button type="submit" color="primary">
                  Submit
                </Button>{" "}
                <Button type="reset" color="secondary" className="ml-2">
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

export default AppointmentForm;
