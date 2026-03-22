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
  FormText,
} from "reactstrap";

type FormData = {
  fname: string;
  lname: string;
  contactNum: string;
  address: string;
  city: string;
  state: string;
  zip: string;
  email: string;
  password: string;
  confirmPassword: string;
};

type Errors = Partial<Record<keyof FormData, string>>;

const NumberWizard: React.FC = () => {
  const [step, setStep] = useState<number>(1);
  const [errors, setErrors] = useState<Errors>({});
  const [formData, setFormData] = useState<FormData>({
    fname: "",
    lname: "",
    contactNum: "",
    address: "",
    city: "",
    state: "",
    zip: "",
    email: "",
    password: "",
    confirmPassword: "",
  });

  const handleNext = (): void => {
    if (validateStep(step)) {
      setStep(step + 1);
    }
  };

  const handlePrev = (): void => setStep(step - 1);

  const validateStep = (step: number): boolean => {
    const currentErrors: Errors = {};

    // Step 1 validation (Personal Information)
    if (step === 1) {
      if (!formData.fname) currentErrors.fname = "First name is required";
      if (!formData.lname) currentErrors.lname = "Last name is required";
      if (!formData.contactNum)
        currentErrors.contactNum = "Contact number is required";
    }

    // Step 2 validation (Address Information)
    if (step === 2) {
      if (!formData.address) currentErrors.address = "Address is required";
      if (!formData.city) currentErrors.city = "City is required";
      if (!formData.state) currentErrors.state = "State is required";
      if (!formData.zip || formData.zip.length !== 5)
        currentErrors.zip = "Zip code must be 5 digits";
    }

    // Step 3 validation (Account Information)
    if (step === 3) {
      if (!formData.email) currentErrors.email = "Email is required";
      if (!formData.password) currentErrors.password = "Password is required";
      if (formData.password !== formData.confirmPassword)
        currentErrors.confirmPassword = "Passwords must match";
    }

    setErrors(currentErrors);
    return Object.keys(currentErrors).length === 0;
  };

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>): void => {
    const { id, value } = e.target;
    setFormData((prevData) => ({
      ...prevData,
      [id]: value,
    }));
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5> Wizard With Number</h5>
        </CardHeader>
        <CardBody>
          <div className="form-wizard">
            <div className="form-wizard-header">
              <ul className="form-wizard-steps">
                <li
                  className={`${step >= 1 ? "activated" : ""} ${step === 1 ? "active" : ""}`}
                >
                  <span className="wizard-steps">1</span>
                </li>
                <li
                  className={`${step >= 2 ? "activated" : ""} ${step === 2 ? "active" : ""}`}
                >
                  <span className="wizard-steps">2</span>
                </li>
                <li
                  className={`${step >= 3 ? "activated" : ""} ${step === 3 ? "active" : ""}`}
                >
                  <span className="wizard-steps">3</span>
                </li>
              </ul>
            </div>

            {/* Step 1: Personal Information */}
            {step === 1 && (
              <div className="wizard-fieldset show">
                <Form className="app-form">
                  <Row>
                    <Col md={6}>
                      <FormGroup>
                        <Label for="fname">First Name*</Label>
                        <Input
                          type="text"
                          id="fname"
                          placeholder="Martin"
                          value={formData.fname}
                          onChange={handleChange}
                          invalid={!!errors.fname}
                        />
                        {errors.fname && (
                          <FormText color="danger">{errors.fname}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col md={6}>
                      <FormGroup>
                        <Label for="lname">Last Name*</Label>
                        <Input
                          type="text"
                          id="lname"
                          placeholder="Payne"
                          value={formData.lname}
                          onChange={handleChange}
                          invalid={!!errors.lname}
                        />
                        {errors.lname && (
                          <FormText color="danger">{errors.lname}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col xs={12}>
                      <FormGroup>
                        <Label for="contactNum">Contact*</Label>
                        <div className="input-group">
                          <span className="input-group-text">+91</span>
                          <Input
                            type="text"
                            id="contactNum"
                            placeholder="**********"
                            value={formData.contactNum}
                            onChange={handleChange}
                            invalid={!!errors.contactNum}
                          />
                        </div>
                        {errors.contactNum && (
                          <FormText color="danger">
                            {errors.contactNum}
                          </FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col className="text-end">
                      <Button
                        onClick={handleNext}
                        className="btn-lg"
                        color="primary"
                      >
                        Next
                      </Button>
                    </Col>
                  </Row>
                </Form>
              </div>
            )}

            {/* Step 2: Address Information */}
            {step === 2 && (
              <div className="wizard-fieldset show">
                <Form className="app-form">
                  <Row>
                    <Col xs={12}>
                      <FormGroup>
                        <Label for="address">Address</Label>
                        <Input
                          type="textarea"
                          id="address"
                          placeholder="2107 Deer Ridge Drive"
                          value={formData.address}
                          onChange={handleChange}
                          invalid={!!errors.address}
                        />
                        {errors.address && (
                          <FormText color="danger">{errors.address}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col md={4}>
                      <FormGroup>
                        <Label for="city">City</Label>
                        <Input
                          type="text"
                          id="city"
                          placeholder="US"
                          value={formData.city}
                          onChange={handleChange}
                          invalid={!!errors.city}
                        />
                        {errors.city && (
                          <FormText color="danger">{errors.city}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col md={4}>
                      <FormGroup>
                        <Label for="state">State</Label>
                        <Input
                          type="text"
                          id="state"
                          placeholder="Enter State"
                          value={formData.state}
                          onChange={handleChange}
                          invalid={!!errors.state}
                        />
                        {errors.state && (
                          <FormText color="danger">{errors.state}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col md={4}>
                      <FormGroup>
                        <Label for="zip">Zip</Label>
                        <Input
                          type="text"
                          id="zip"
                          maxLength={5}
                          placeholder="Enter Zip"
                          value={formData.zip}
                          onChange={handleChange}
                          invalid={!!errors.zip}
                        />
                        {errors.zip && (
                          <FormText color="danger">{errors.zip}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col className="text-end">
                      <Button
                        onClick={handlePrev}
                        color="primary"
                        className="me-2"
                      >
                        Previous
                      </Button>
                      <Button onClick={handleNext} color="primary">
                        Next
                      </Button>
                    </Col>
                  </Row>
                </Form>
              </div>
            )}

            {/* Step 3: Account Information */}
            {step === 3 && (
              <div className="wizard-fieldset show">
                <Form className="app-form">
                  <Row>
                    <Col xs={12}>
                      <FormGroup>
                        <Label for="email">Email*</Label>
                        <Input
                          type="email"
                          id="email"
                          placeholder="example@domain.com"
                          value={formData.email}
                          onChange={handleChange}
                          invalid={!!errors.email}
                        />
                        {errors.email && (
                          <FormText color="danger">{errors.email}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col md={6}>
                      <FormGroup>
                        <Label for="password">Password*</Label>
                        <Input
                          type="password"
                          id="password"
                          placeholder="******"
                          value={formData.password}
                          onChange={handleChange}
                          invalid={!!errors.password}
                        />
                        {errors.password && (
                          <FormText color="danger">{errors.password}</FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col md={6}>
                      <FormGroup>
                        <Label for="confirmPassword">Confirm Password*</Label>
                        <Input
                          type="password"
                          id="confirmPassword"
                          placeholder="******"
                          value={formData.confirmPassword}
                          onChange={handleChange}
                          invalid={!!errors.confirmPassword}
                        />
                        {errors.confirmPassword && (
                          <FormText color="danger">
                            {errors.confirmPassword}
                          </FormText>
                        )}
                      </FormGroup>
                    </Col>
                    <Col className="text-end">
                      <Button
                        onClick={handlePrev}
                        color="primary"
                        className="me-2"
                      >
                        Previous
                      </Button>
                      <Button type="submit" color="primary">
                        Submit
                      </Button>
                    </Col>
                  </Row>
                </Form>
              </div>
            )}
          </div>
        </CardBody>
      </Card>
    </>
  );
};

export default NumberWizard;
