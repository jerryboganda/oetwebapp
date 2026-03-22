import React, { useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
} from "reactstrap";

interface FormData {
  textarea: string;
  checkbox: boolean;
  radio: boolean;
  select: string;
  file: File | null;
}

const SupportedValidation: React.FC = () => {
  const [formData, setFormData] = useState<FormData>({
    textarea: "",
    checkbox: false,
    radio: false,
    select: "",
    file: null,
  });

  const handleChange = (
    e: React.ChangeEvent<
      HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement
    >
  ) => {
    const { name, type, value, checked } = e.target as HTMLInputElement;
    setFormData((prev) => ({
      ...prev,
      [name]: type === "checkbox" || type === "radio" ? checked : value,
    }));
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setFormData((prev) => ({
      ...prev,
      file: e.target.files?.[0] ?? null,
    }));
  };

  return (
    <Card>
      <CardHeader>
        <h5>Supported Elements</h5>
        <p className="text-secondary">
          Not interested in custom validation feedback messages or writing
          JavaScript to change form behaviors? All good, you can use the browser
          defaults. Try submitting the form below. Depending on your browser and
          OS, you’ll see a slightly different style of feedback. While these
          feedback styles cannot be styled with CSS, you can still customize the
          feedback text through JavaScript.
        </p>
      </CardHeader>

      <CardBody>
        <Form className="was-validated">
          <FormGroup className="mb-3">
            <Label for="validationTextarea">Textarea</Label>
            <Input
              type="textarea"
              id="validationTextarea"
              name="textarea"
              value={formData.textarea}
              onChange={handleChange}
              className="is-invalid"
              required
            />
            <div className="invalid-feedback">
              Please enter a message in the textarea.
            </div>
          </FormGroup>

          <FormGroup className="form-check mb-3">
            <Input
              type="checkbox"
              id="validationFormCheck1"
              name="checkbox"
              checked={formData.checkbox}
              onChange={handleChange}
              className="form-check-input"
              required
            />
            <Label className="form-check-label" for="validationFormCheck1">
              Check this checkbox
            </Label>
            <div className="invalid-feedback">
              Example invalid feedback text
            </div>
          </FormGroup>

          <FormGroup className="form-check mb-3">
            <Input
              type="radio"
              id="validationFormCheck3"
              name="radio"
              checked={formData.radio}
              onChange={handleChange}
              className="form-check-input"
              required
            />
            <Label className="form-check-label" for="validationFormCheck3">
              Or toggle this other radio
            </Label>
            <div className="invalid-feedback">
              More example invalid feedback text
            </div>
          </FormGroup>

          <FormGroup className="mb-3">
            <Input
              type="select"
              name="select"
              value={formData.select}
              onChange={handleChange}
              required
            >
              <option value="">Open this select menu</option>
              <option value="1">One</option>
              <option value="2">Two</option>
              <option value="3">Three</option>
            </Input>
            <div className="invalid-feedback">
              Example invalid select feedback
            </div>
          </FormGroup>

          <FormGroup className="mb-3">
            <Input
              type="file"
              name="file"
              onChange={handleFileChange}
              required
            />
            <div className="invalid-feedback">
              Example invalid form file feedback
            </div>
          </FormGroup>

          <div className="text-end">
            <Button color="primary" type="submit">
              Submit form
            </Button>
          </div>
        </Form>
      </CardBody>
    </Card>
  );
};

export default SupportedValidation;
