import { useState, ChangeEvent } from "react";
import {
  Row,
  Col,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
  Card,
  CardHeader,
  CardBody,
} from "reactstrap";
import { IconPhotoHeart } from "@tabler/icons-react";

const StudentForm: React.FC = () => {
  const [imagePreview, setImagePreview] = useState<string | null>(null);

  // Handle image preview
  const handleImageUpload = (event: ChangeEvent<HTMLInputElement>): void => {
    const file = event.target.files?.[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = () => setImagePreview(reader.result as string);
      reader.readAsDataURL(file);
    }
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Student Details Form</h5>
        </CardHeader>
        <CardBody>
          <Form className="app-form">
            <Row className="justify-content-center">
              <Col xs="12" className="text-center mb-3">
                <div className="d-flex justify-content-center mb-3">
                  <div className="avatar-upload">
                    <div className="avatar-edit">
                      <Input
                        type="file"
                        id="imageUpload"
                        accept=".png, .jpg, .jpeg"
                        onChange={handleImageUpload}
                        hidden
                      />
                      <Label htmlFor="imageUpload">
                        <IconPhotoHeart />{" "}
                      </Label>
                    </div>
                    <div className="avatar-preview">
                      <div
                        id="imgPreview"
                        style={{
                          width: "100%",
                          height: "100%",
                          borderRadius: "50%",
                          backgroundSize: "cover",
                          backgroundImage: `url(${imagePreview || "/images/avatar/9.png"})`,
                        }}
                      ></div>
                    </div>
                  </div>
                </div>
              </Col>

              <Col md="4">
                <FormGroup>
                  <Label>Surname</Label>
                  <Input type="text" placeholder="Enter Surname" />
                </FormGroup>
              </Col>

              <Col md="4">
                <FormGroup>
                  <Label>Student Name</Label>
                  <Input type="text" placeholder="Enter Name" />
                </FormGroup>
              </Col>

              <Col md="4">
                <FormGroup>
                  <Label>Parent Name</Label>
                  <Input type="text" placeholder="Enter Parent Name" />
                </FormGroup>
              </Col>

              <Col md="6" xl="3">
                <FormGroup>
                  <Label>Date of Birth</Label>
                  <Input type="date" />
                </FormGroup>
              </Col>

              <Col md="6" xl="3">
                <FormGroup>
                  <Label>Gender</Label>
                  <Input type="select">
                    <option>Select Gender</option>
                    <option>Male</option>
                    <option>Female</option>
                  </Input>
                </FormGroup>
              </Col>

              <Col xl="6">
                <FormGroup>
                  <Label>Email Address</Label>
                  <div className="input-group">
                    <span className="input-group-text">@</span>
                    <Input type="email" placeholder="Email Address" />
                  </div>
                </FormGroup>
              </Col>

              <Col md="6">
                <FormGroup>
                  <Label>Father Contact</Label>
                  <Input type="text" placeholder="xxx-xxxx-xxx" />
                </FormGroup>
              </Col>

              <Col md="6">
                <FormGroup>
                  <Label>Mother Contact</Label>
                  <Input type="text" placeholder="xxx-xxxx-xxx" />
                </FormGroup>
              </Col>

              <Col xl="6">
                <FormGroup>
                  <Label>Address</Label>
                  <Input
                    type="textarea"
                    rows={1}
                    placeholder="Enter Your Address"
                  />
                </FormGroup>
              </Col>

              <Col md="6" xl="3">
                <FormGroup>
                  <Label>City</Label>
                  <Input type="text" placeholder="Enter Your City" />
                </FormGroup>
              </Col>

              <Col md="6" xl="3">
                <FormGroup>
                  <Label>Zip</Label>
                  <Input type="text" placeholder="xxxxx" maxLength={5} />
                </FormGroup>
              </Col>

              <Col xs="12" className="text-end">
                <Button color="primary" type="submit" className="me-2">
                  Add
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

export default StudentForm;
