import React, { useState, ChangeEvent } from "react";
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
import { IconInfoCircle, IconPhonePlus, IconUsers } from "@tabler/icons-react";

interface FormData {
  name: string;
  aboutSelf: string;
  email: string;
  password: string;
  confirmPassword: string;
  facebookUrl: string;
  twitterUrl: string;
  githubUrl: string;
}

interface Errors {
  [key: string]: string | undefined;
}

const VerticalIconWizards: React.FC = () => {
  const [step, setStep] = useState<number>(1);
  const [formData, setFormData] = useState<FormData>({
    name: "",
    aboutSelf: "",
    email: "",
    password: "",
    confirmPassword: "",
    facebookUrl: "",
    twitterUrl: "",
    githubUrl: "",
  });
  const [errors, setErrors] = useState<Errors>({});

  const handleChange = (
    e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>
  ): void => {
    const { id, value } = e.target;
    setFormData((prev) => ({ ...prev, [id]: value }));
  };

  const validateStep = (step: number): Errors => {
    const newErrors: Errors = {};

    if (step === 1) {
      if (!formData.name) newErrors.name = "Name is required";
      if (!formData.aboutSelf) newErrors.aboutSelf = "About Self is required";
    }

    if (step === 2) {
      if (!formData.email) newErrors.email = "Email is required";
      if (!formData.password) newErrors.password = "Password is required";
      if (formData.password !== formData.confirmPassword) {
        newErrors.confirmPassword = "Passwords do not match";
      }
    }

    return newErrors;
  };

  const handleNext = (): void => {
    const validation = validateStep(step);
    if (Object.keys(validation).length === 0) {
      setStep(step + 1);
    } else {
      setErrors(validation);
    }
  };

  const handlePrev = (): void => {
    setStep(step - 1);
  };

  return (
    <>
      <Card className="equal-card">
        <CardHeader>
          <h5>Vertical Wizard With Icon</h5>
        </CardHeader>
        <CardBody>
          <div className="form-wizard">
            <div className="row">
              {/* Step Indicator with Icons */}
              <div className="col-3">
                <div className="form-wizard-header">
                  <ul className="vertica-wizard-steps">
                    <li
                      className={`${
                        step >= 1 ? "activated" : ""
                      } ${step === 1 ? "active" : ""}`}
                    >
                      <span className="wizard-steps">
                        <IconUsers />
                      </span>
                    </li>
                    <li
                      className={`${
                        step >= 2 ? "activated" : ""
                      } ${step === 2 ? "active" : ""}`}
                    >
                      <span className="wizard-steps">
                        <IconPhonePlus />
                      </span>
                    </li>
                    <li
                      className={`${
                        step >= 3 ? "activated" : ""
                      } ${step === 3 ? "active" : ""}`}
                    >
                      <span className="wizard-steps">
                        <IconInfoCircle />
                      </span>
                    </li>
                  </ul>
                </div>
              </div>

              {/* Form Fields */}
              <div className="col-9">
                {/* Step 1 */}
                {step === 1 && (
                  <div className="wizard-fieldset show">
                    <Form className="app-form">
                      <Row>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="name">Name*</Label>
                            <Input
                              type="text"
                              id="name"
                              value={formData.name}
                              onChange={handleChange}
                              placeholder="Johnson"
                            />
                            {errors.name && (
                              <div className="wizard-form-error">
                                {errors.name}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="aboutSelf">About Self</Label>
                            <Input
                              type="textarea"
                              id="aboutSelf"
                              value={formData.aboutSelf}
                              onChange={handleChange}
                              placeholder="Type About Self"
                            />
                            {errors.aboutSelf && (
                              <div className="wizard-form-error">
                                {errors.aboutSelf}
                              </div>
                            )}
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
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="email">Email*</Label>
                            <Input
                              type="email"
                              id="email"
                              value={formData.email}
                              onChange={handleChange}
                              placeholder="StevenSHarmon@rhyta.com"
                            />
                            {errors.email && (
                              <div className="wizard-form-error">
                                {errors.email}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="password">Password*</Label>
                            <Input
                              type="password"
                              id="password"
                              value={formData.password}
                              onChange={handleChange}
                              placeholder="*****"
                            />
                            {errors.password && (
                              <div className="wizard-form-error">
                                {errors.password}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="confirmPassword">
                              Confirm Password*
                            </Label>
                            <Input
                              type="password"
                              id="confirmPassword"
                              value={formData.confirmPassword}
                              onChange={handleChange}
                              placeholder="*****"
                            />
                            {errors.confirmPassword && (
                              <div className="wizard-form-error">
                                {errors.confirmPassword}
                              </div>
                            )}
                          </FormGroup>
                        </Col>
                        <Col xs={12} className="text-end mb-3">
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
                          <FormGroup>
                            <Label for="facebookUrl">Facebook Url</Label>
                            <Input
                              type="url"
                              id="facebookUrl"
                              value={formData.facebookUrl}
                              onChange={handleChange}
                              placeholder="https://"
                            />
                          </FormGroup>
                        </Col>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="twitterUrl">Twitter Url</Label>
                            <Input
                              type="url"
                              id="twitterUrl"
                              value={formData.twitterUrl}
                              onChange={handleChange}
                              placeholder="https://"
                            />
                          </FormGroup>
                        </Col>
                        <Col xs={12}>
                          <FormGroup>
                            <Label for="githubUrl">Github Url</Label>
                            <Input
                              type="url"
                              id="githubUrl"
                              value={formData.githubUrl}
                              onChange={handleChange}
                              placeholder="https://"
                            />
                          </FormGroup>
                        </Col>
                        <Col xs={12} className="text-end mb-3">
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
              </div>
            </div>
          </div>
        </CardBody>
      </Card>
    </>
  );
};

export default VerticalIconWizards;
