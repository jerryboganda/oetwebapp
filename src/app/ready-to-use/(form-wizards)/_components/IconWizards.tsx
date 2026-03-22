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
import {
  IconInfoCircle,
  IconTableExport,
  IconUsers,
} from "@tabler/icons-react";

type FormData = {
  emailId: string;
  password: string;
  confirmPassword: string;
  companyName: string;
  department: string;
  departmentCode: string;
  projectName: string;
  file: File | null;
};

type FormErrors = Partial<Record<keyof FormData, string>>;

const IconWizard: React.FC = () => {
  const [step, setStep] = useState<number>(1);
  const [formData, setFormData] = useState<FormData>({
    emailId: "",
    password: "",
    confirmPassword: "",
    companyName: "",
    department: "",
    departmentCode: "",
    projectName: "",
    file: null,
  });
  const [errors, setErrors] = useState<FormErrors>({});

  const handleNext = () => {
    const validation = validateStep(step);
    if (Object.keys(validation).length === 0) {
      setStep(step + 1);
    } else {
      setErrors(validation);
    }
  };

  const handlePrev = () => setStep(step - 1);

  const validateStep = (step: number): FormErrors => {
    const newErrors: FormErrors = {};

    if (step === 1) {
      if (!formData.emailId) newErrors.emailId = "Email is required";
      if (!formData.password) newErrors.password = "Password is required";
      if (formData.password !== formData.confirmPassword)
        newErrors.confirmPassword = "Passwords must match";
    }

    if (step === 2) {
      if (!formData.companyName)
        newErrors.companyName = "Company name is required";
      if (!formData.department) newErrors.department = "Department is required";
      if (!formData.departmentCode)
        newErrors.departmentCode = "Department code is required";
    }

    if (step === 3) {
      if (!formData.projectName)
        newErrors.projectName = "Project name is required";
      if (!formData.file) newErrors.file = "File is required";
    }

    return newErrors;
  };

  const handleChange = (e: ChangeEvent<HTMLInputElement>) => {
    const { id, value } = e.target;
    setFormData({ ...formData, [id]: value });
  };

  const handleFileChange = (e: ChangeEvent<HTMLInputElement>) => {
    const { id, files } = e.target;
    setFormData({ ...formData, [id]: files ? files[0] : null });
  };

  return (
    <Card>
      <CardHeader>
        <h5> Wizard With Icon</h5>
      </CardHeader>
      <CardBody>
        <div className="form-wizard">
          <div className="form-wizard-header">
            <ul className="form-wizard-steps">
              <li
                className={`${step >= 1 ? "activated" : ""} ${step === 1 ? "active" : ""}`}
              >
                <span className="wizard-steps circle-steps">
                  <IconUsers />
                </span>
              </li>
              <li
                className={`${step >= 2 ? "activated" : ""} ${step === 2 ? "active" : ""}`}
              >
                <span className="wizard-steps circle-steps">
                  <IconInfoCircle />
                </span>
              </li>
              <li
                className={`${step >= 3 ? "activated" : ""} ${step === 3 ? "active" : ""}`}
              >
                <span className="wizard-steps circle-steps">
                  <IconTableExport />
                </span>
              </li>
            </ul>
          </div>

          {step === 1 && (
            <div className="wizard-fieldset show">
              <Form className="app-form">
                <Row>
                  <Col xs={12}>
                    <FormGroup>
                      <Label for="emailId">Username*</Label>
                      <Input
                        type="email"
                        id="emailId"
                        placeholder="Allen"
                        value={formData.emailId}
                        onChange={handleChange}
                        required
                      />
                      {errors.emailId && (
                        <div className="wizard-form-error">
                          {errors.emailId}
                        </div>
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
                        required
                      />
                      {errors.password && (
                        <div className="wizard-form-error">
                          {errors.password}
                        </div>
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
                        required
                      />
                      {errors.confirmPassword && (
                        <div className="wizard-form-error">
                          {errors.confirmPassword}
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

          {step === 2 && (
            <div className="wizard-fieldset show">
              <Form className="app-form">
                <Row>
                  <Col xs={12}>
                    <FormGroup>
                      <Label for="companyName">Company Name*</Label>
                      <Input
                        type="text"
                        id="companyName"
                        placeholder="Enter Your Company Name"
                        value={formData.companyName}
                        onChange={handleChange}
                        required
                      />
                      {errors.companyName && (
                        <div className="wizard-form-error">
                          {errors.companyName}
                        </div>
                      )}
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="department">Department*</Label>
                      <Input
                        type="text"
                        id="department"
                        placeholder="Enter Your Department"
                        value={formData.department}
                        onChange={handleChange}
                        required
                      />
                      {errors.department && (
                        <div className="wizard-form-error">
                          {errors.department}
                        </div>
                      )}
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label for="departmentCode">Department Code*</Label>
                      <Input
                        type="text"
                        id="departmentCode"
                        maxLength={6}
                        placeholder="Enter Department Code"
                        value={formData.departmentCode}
                        onChange={handleChange}
                        required
                      />
                      {errors.departmentCode && (
                        <div className="wizard-form-error">
                          {errors.departmentCode}
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

          {step === 3 && (
            <div className="wizard-fieldset show">
              <Form className="app-form">
                <Row>
                  <Col xs={12}>
                    <FormGroup>
                      <Label for="projectName">Project Name*</Label>
                      <Input
                        type="text"
                        id="projectName"
                        placeholder="Enter Your Project Name"
                        value={formData.projectName}
                        onChange={handleChange}
                        required
                      />
                      {errors.projectName && (
                        <div className="wizard-form-error">
                          {errors.projectName}
                        </div>
                      )}
                    </FormGroup>
                  </Col>
                  <Col xs={12}>
                    <FormGroup>
                      <Label for="file">Upload Your File*</Label>
                      <Input
                        type="file"
                        id="file"
                        onChange={handleFileChange}
                        required
                      />
                      {errors.file && (
                        <div className="wizard-form-error">{errors.file}</div>
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
                    <Button type="submit" color="primary" className="btn-lg">
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
  );
};

export default IconWizard;
