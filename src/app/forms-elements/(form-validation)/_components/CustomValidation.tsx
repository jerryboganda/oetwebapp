import React, { useRef, useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  FormGroup,
  Input,
  Label,
  Spinner,
} from "reactstrap";

interface FormData {
  firstName: string;
  lastName: string;
  username: string;
  city: string;
  state: string;
  zip: string;
  termsAgreed: boolean;
}

const CustomValidation: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState<FormData>({
    firstName: "Mark",
    lastName: "Otto",
    username: "",
    city: "",
    state: "",
    zip: "",
    termsAgreed: false,
  });

  const formRef = useRef<HTMLFormElement | null>(null);

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>
  ) => {
    const { name, value, type } = e.target;

    setFormData((prev) => ({
      ...prev,
      [name]:
        type === "checkbox" ? (e.target as HTMLInputElement).checked : value,
    }));
  };

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    const formElement = formRef.current;
    if (!formElement) return;

    if (!formElement.checkValidity()) {
      e.stopPropagation();
      formElement.classList.add("was-validated");
      return;
    }

    formElement.classList.add("was-validated");
    setLoading(true);

    setTimeout(() => {
      setLoading(false);
    }, 1500);
  };
  return (
    <Card>
      <CardHeader>
        <h5>Custom Styles</h5>
      </CardHeader>

      <CardBody>
        <form
          ref={formRef}
          onSubmit={handleSubmit}
          noValidate
          className="app-form row g-3"
        >
          <Col md="4">
            <FormGroup>
              <Label for="validationCustom01">First name</Label>
              <Input
                type="text"
                id="validationCustom01"
                name="firstName"
                value={formData.firstName}
                onChange={handleChange}
                required
              />
              <div className="valid-feedback">Looks good!</div>
            </FormGroup>
          </Col>

          <Col md="4">
            <FormGroup>
              <Label for="validationCustom02">Last name</Label>
              <Input
                type="text"
                id="validationCustom02"
                name="lastName"
                value={formData.lastName}
                onChange={handleChange}
                required
              />
              <div className="valid-feedback">Looks good!</div>
            </FormGroup>
          </Col>

          <Col md="4">
            <FormGroup>
              <Label for="validationCustomUsername">Username</Label>
              <div className="input-group has-validation">
                <span className="input-group-text" id="inputGroupPrepend">
                  @
                </span>
                <Input
                  type="text"
                  id="validationCustomUsername"
                  name="username"
                  value={formData.username}
                  onChange={handleChange}
                  aria-describedby="inputGroupPrepend"
                  required
                />
                <div className="invalid-feedback">
                  Please choose a username.
                </div>
              </div>
            </FormGroup>
          </Col>

          <Col md="6">
            <FormGroup>
              <Label for="validationCustom03">City</Label>
              <Input
                type="text"
                id="validationCustom03"
                name="city"
                value={formData.city}
                onChange={handleChange}
                required
              />
              <div className="invalid-feedback">
                Please provide a valid city.
              </div>
            </FormGroup>
          </Col>

          <Col md="3">
            <FormGroup>
              <Label for="validationCustom04">State</Label>
              <Input
                type="select"
                id="validationCustom04"
                name="state"
                value={formData.state}
                onChange={handleChange}
                required
              >
                <option value="" disabled>
                  Choose...
                </option>
                <option value="NY">New York</option>
                <option value="CA">California</option>
                <option value="TX">Texas</option>
              </Input>
              <div className="invalid-feedback">
                Please select a valid state.
              </div>
            </FormGroup>
          </Col>

          <Col md="3">
            <FormGroup>
              <Label for="validationCustom05">Zip</Label>
              <Input
                type="text"
                id="validationCustom05"
                name="zip"
                value={formData.zip}
                onChange={handleChange}
                required
              />
              <div className="invalid-feedback">
                Please provide a valid zip.
              </div>
            </FormGroup>
          </Col>

          <Col md="12">
            <div className="form-check d-flex flex-wrap gap-1">
              <Input
                className="form-check-input"
                type="checkbox"
                id="invalidCheck"
                name="termsAgreed"
                checked={formData.termsAgreed}
                onChange={handleChange}
                required
              />
              <Label className="form-check-label" for="invalidCheck">
                Agree to terms and conditions
              </Label>
              <div className="invalid-feedback">
                You must agree before submitting.
              </div>
            </div>
          </Col>

          <Col md="12">
            <Button color="primary" type="submit">
              {loading ? <Spinner size="sm" /> : "Submit form"}
            </Button>
          </Col>
        </form>
      </CardBody>
    </Card>
  );
};

export default CustomValidation;
