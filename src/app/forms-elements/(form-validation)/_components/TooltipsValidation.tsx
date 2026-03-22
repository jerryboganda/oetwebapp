import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Form,
  FormGroup,
  Input,
  Label,
  FormFeedback,
  Spinner,
} from "reactstrap";

interface FormErrors {
  userName: string;
  email: string;
  password: string;
  address: string;
  address2: string;
  city: string;
  zipCode: string;
}

const TooltipsValidation: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState({
    userName: "",
    email: "",
    password: "",
    address: "",
    address2: "",
    city: "",
    zipCode: "",
  });

  const [errors, setErrors] = useState<FormErrors>({
    userName: "",
    email: "",
    password: "",
    address: "",
    address2: "",
    city: "",
    zipCode: "",
  });

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = e.target;
    setFormData((prev) => ({
      ...prev,
      [name]: value,
    }));
  };

  const validate = () => {
    const newErrors: FormErrors = {
      userName: formData.userName ? "" : "User Name is required",
      email: formData.email ? "" : "Email is required",
      password: formData.password ? "" : "Password is required",
      address: formData.address ? "" : "Address is required",
      address2: "",
      city: formData.city ? "" : "City is required",
      zipCode: formData.zipCode ? "" : "Zip Code is required",
    };
    setErrors(newErrors);
    return Object.values(newErrors).every((error) => error === "");
  };

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (validate()) {
      setLoading(true);

      setTimeout(() => {
        setLoading(false);
        setFormData({
          userName: "",
          email: "",
          password: "",
          address: "",
          address2: "",
          city: "",
          zipCode: "",
        });
      }, 1500);
    }
  };

  return (
    <Card>
      <CardHeader>
        <h5>Tooltips</h5>
        <p className="text-secondary">
          If your form layout allows it, you can swap the{" "}
          <span className="text-danger">.{`valid|invalid`}-feedback</span>{" "}
          classes for{" "}
          <span className="text-danger">.{`valid|invalid`}-tooltip</span>{" "}
          classes to display validation feedback in a styled tooltip. Ensure the
          parent has <span className="text-danger">position: relative</span> for
          tooltip positioning.
        </p>
      </CardHeader>

      <CardBody>
        <Form
          className="row g-3 app-form"
          id="form-validation"
          onSubmit={handleSubmit}
        >
          <Col md="6">
            <FormGroup className="position-relative">
              <Label for="userName">User Name</Label>
              <Input
                type="text"
                id="userName"
                name="userName"
                value={formData.userName}
                onChange={handleChange}
                invalid={!!errors.userName}
              />
              <FormFeedback tooltip>{errors.userName}</FormFeedback>
            </FormGroup>
          </Col>

          <Col md="6">
            <FormGroup className="position-relative">
              <Label for="email">Email</Label>
              <Input
                type="email"
                id="email"
                name="email"
                value={formData.email}
                onChange={handleChange}
                invalid={!!errors.email}
              />
              <FormFeedback tooltip>{errors.email}</FormFeedback>
            </FormGroup>
          </Col>

          <Col md="6">
            <FormGroup className="position-relative">
              <Label for="password">Password</Label>
              <Input
                type="password"
                id="password"
                name="password"
                value={formData.password}
                onChange={handleChange}
                invalid={!!errors.password}
              />
              <FormFeedback tooltip>{errors.password}</FormFeedback>
            </FormGroup>
          </Col>

          <Col md="6">
            <FormGroup className="position-relative">
              <Label for="address">Address</Label>
              <Input
                type="text"
                id="address"
                name="address"
                placeholder="1234 Main St"
                value={formData.address}
                onChange={handleChange}
                invalid={!!errors.address}
              />
              <FormFeedback tooltip>{errors.address}</FormFeedback>
            </FormGroup>
          </Col>

          <Col md="5">
            <FormGroup className="position-relative">
              <Label for="address2">Address 2</Label>
              <Input
                type="text"
                id="address2"
                name="address2"
                placeholder="Apartment, studio, or floor"
                value={formData.address2}
                onChange={handleChange}
                invalid={!!errors.address2}
              />
              <FormFeedback tooltip>{errors.address2}</FormFeedback>
            </FormGroup>
          </Col>

          <Col md="5">
            <FormGroup className="position-relative">
              <Label for="city">City</Label>
              <Input
                type="text"
                id="city"
                name="city"
                value={formData.city}
                onChange={handleChange}
                invalid={!!errors.city}
              />
              <FormFeedback tooltip>{errors.city}</FormFeedback>
            </FormGroup>
          </Col>

          <Col md="2">
            <FormGroup className="position-relative">
              <Label for="zipCode">Zip</Label>
              <Input
                type="text"
                id="zipCode"
                name="zipCode"
                value={formData.zipCode}
                onChange={handleChange}
                invalid={!!errors.zipCode}
              />
              <FormFeedback tooltip>{errors.zipCode}</FormFeedback>
            </FormGroup>
          </Col>

          <Col xs="12">
            <FormGroup check className="d-flex gap-1">
              <Input type="checkbox" id="gridCheck" />
              <Label for="gridCheck" check>
                Check me out
              </Label>
            </FormGroup>
          </Col>

          <Col xs="12" className="text-end">
            <Button type="submit" color="primary" className="mt-3">
              {loading ? <Spinner size="sm" /> : "Submit form"}
            </Button>
          </Col>
        </Form>
      </CardBody>
    </Card>
  );
};

export default TooltipsValidation;
