import React, { useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Button,
  Form,
  FormGroup,
  Label,
  Input,
  Row,
  Col,
} from "reactstrap";

interface FormData {
  firstName: string;
  lastName: string;
  email: string;
  contact: string;
  dob: string;
  section: string;
  position: string;
  officeAddress: string;
}

interface Errors {
  firstName?: string;
  lastName?: string;
  email?: string;
  contact?: string;
  dob?: string;
  section?: string;
  position?: string;
  officeAddress?: string;
}

const VerticalNumberWizard: React.FC = () => {
  const [step, setStep] = useState<number>(1);
  const [formData, setFormData] = useState<FormData>({
    firstName: "",
    lastName: "",
    email: "",
    contact: "",
    dob: "",
    section: "",
    position: "",
    officeAddress: "",
  });
  const [errors, setErrors] = useState<Errors>({});

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>
  ) => {
    const { id, value } = e.target;
    setFormData({ ...formData, [id]: value });
  };

  const validateStep = (step: number): Errors => {
    const newErrors: Errors = {};

    if (step === 1) {
      if (!formData.firstName) newErrors.firstName = "First name is required";
      if (!formData.lastName) newErrors.lastName = "Last name is required";
      if (!formData.email) newErrors.email = "Email is required";
    }

    if (step === 2) {
      if (!formData.section) newErrors.section = "Section is required";
      if (!formData.position) newErrors.position = "Position is required";
      if (!formData.officeAddress)
        newErrors.officeAddress = "Office address is required";
    }

    return newErrors;
  };

  const handleNext = () => {
    const validation = validateStep(step);
    if (Object.keys(validation).length === 0) {
      setStep(step + 1);
    } else {
      setErrors(validation);
    }
  };

  const handlePrev = () => {
    setStep(step - 1);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5> Vertical Wizard With Number</h5>
        </CardHeader>
        <CardBody>
          <div className="form-wizard">
            <Row>
              {/* Step Indicator */}
              <Col xs={3}>
                <div className="form-wizard-header">
                  <ul className="vertica-wizard-steps">
                    <li
                      className={`${step >= 1 ? "activated" : ""} ${step === 1 ? "active" : ""}`}
                    >
                      <span className="wizard-steps circle-steps">1</span>
                    </li>
                    <li
                      className={`${step >= 2 ? "activated" : ""} ${step === 2 ? "active" : ""}`}
                    >
                      <span className="wizard-steps circle-steps">2</span>
                    </li>
                    <li
                      className={`${step >= 3 ? "activated" : ""} ${step === 3 ? "active" : ""}`}
                    >
                      <span className="wizard-steps circle-steps">3</span>
                    </li>
                  </ul>
                </div>
              </Col>

              {/* Form Fields */}
              <Col xs={9}>
                {/* Step 1 */}
                {step === 1 && (
                  <div className="wizard-fieldset show">
                    <Form className="app-form">
                      <Row>
                        <Col md={6}>
                          <FormGroup>
                            <Label for="firstName">First Name*</Label>
                            <Input
                              type="text"
                              id="firstName"
                              value={formData.firstName}
                              onChange={handleChange}
                              placeholder="Enter Your First Name"
                            />
                            {errors.firstName && (
                              <div className="wizard-form-error">
                                {errors.firstName}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col md={6}>
                          <FormGroup>
                            <Label for="lastName">Last Name*</Label>
                            <Input
                              type="text"
                              id="lastName"
                              value={formData.lastName}
                              onChange={handleChange}
                              placeholder="Enter Your Last Name"
                            />
                            {errors.lastName && (
                              <div className="wizard-form-error">
                                {errors.lastName}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="email">Email*</Label>
                            <Input
                              type="email"
                              id="email"
                              value={formData.email}
                              onChange={handleChange}
                              placeholder="Enter Your Email"
                            />
                            {errors.email && (
                              <div className="wizard-form-error">
                                {errors.email}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col md={6} xl={7}>
                          <FormGroup>
                            <Label for="contact">Contact*</Label>
                            <Input
                              type="text"
                              id="contact"
                              value={formData.contact}
                              onChange={handleChange}
                              placeholder="**********"
                            />
                          </FormGroup>
                        </Col>
                        <Col md={6} xl={5}>
                          <FormGroup>
                            <Label for="dob">Date of Birth</Label>
                            <Input
                              type="date"
                              id="dob"
                              value={formData.dob}
                              onChange={handleChange}
                            />
                          </FormGroup>
                        </Col>
                        <Col xs={12} className="text-end">
                          <Button
                            color="primary"
                            onClick={handleNext}
                            className="next-btn btn-lg"
                          >
                            Next
                          </Button>
                        </Col>
                      </Row>
                    </Form>
                  </div>
                )}

                {/* Step 2 */}
                {step === 2 && (
                  <div className="wizard-fieldset show">
                    <Form className="app-form">
                      <Row>
                        <Col md={6}>
                          <FormGroup>
                            <Label for="section">Section*</Label>
                            <Input
                              type="text"
                              id="section"
                              value={formData.section}
                              onChange={handleChange}
                              placeholder="Enter Your Section"
                            />
                            {errors.section && (
                              <div className="wizard-form-error">
                                {errors.section}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col md={6}>
                          <FormGroup>
                            <Label for="position">Position*</Label>
                            <Input
                              type="text"
                              id="position"
                              value={formData.position}
                              onChange={handleChange}
                              placeholder="Enter Your Position"
                            />
                            {errors.position && (
                              <div className="wizard-form-error">
                                {errors.position}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="officeAddress">Office Address*</Label>
                            <Input
                              type="textarea"
                              id="officeAddress"
                              value={formData.officeAddress}
                              onChange={handleChange}
                              placeholder="Enter Your Office Address"
                            />
                            {errors.officeAddress && (
                              <div className="wizard-form-error">
                                {errors.officeAddress}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col xs={12} className="text-end">
                          <Button
                            color="primary"
                            onClick={handlePrev}
                            className="pre-btn btn-lg"
                          >
                            Previous
                          </Button>{" "}
                          <Button
                            color="primary"
                            onClick={handleNext}
                            className="next-btn btn-lg"
                          >
                            Next
                          </Button>
                        </Col>
                      </Row>
                    </Form>
                  </div>
                )}

                {/* Step 3 */}
                {step === 3 && (
                  <div className="wizard-fieldset show">
                    <Form className="app-form">
                      <Row>
                        <Col xs={12}>
                          <h6>Check your Details and Submit</h6>
                          <p className="f-w-500">Section :</p>
                          <p>- {formData.section}</p>
                          <p className="f-w-500">Position :</p>
                          <p>- {formData.position}</p>
                          <p className="f-w-500">Office Address :</p>
                          <p>{formData.officeAddress}</p>
                        </Col>
                        <Col xs={12} className="text-end">
                          <Button
                            color="primary"
                            onClick={handlePrev}
                            className="pre-btn btn-lg"
                          >
                            Previous
                          </Button>{" "}
                          <Button
                            type="submit"
                            color="primary"
                            className="btn-lg"
                          >
                            Submit
                          </Button>
                        </Col>
                      </Row>
                    </Form>
                  </div>
                )}
              </Col>
            </Row>
          </div>
        </CardBody>
      </Card>
    </>
  );
};

export default VerticalNumberWizard;
