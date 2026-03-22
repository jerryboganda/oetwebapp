import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
  Row,
  Col,
  Spinner,
} from "reactstrap";
import { IconPhotoHeart } from "@tabler/icons-react";

function SettingProfile() {
  const [loading, setLoading] = useState(false);

  const handleSubmit = (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setLoading(true);

    setTimeout(() => {
      setLoading(false);
    }, 1000);
  };
  return (
    <>
      <Card className="setting-profile-tab">
        <CardHeader>
          <h5>Profile</h5>
        </CardHeader>
        <CardBody>
          <div className="profile-tab profile-container">
            {/* Profile Picture Section */}
            <div className="image-details">
              <div className="profile-image"></div>
              <div className="profile-pic">
                <div className="avatar-upload">
                  <div className="avatar-edit">
                    <Input
                      type="file"
                      id="imageUpload"
                      accept=".png, .jpg, .jpeg"
                    />
                    <Label htmlFor="imageUpload">
                      <IconPhotoHeart size={16} />
                    </Label>
                  </div>
                  <div className="avatar-preview">
                    <div id="imgPreview"></div>
                  </div>
                </div>
              </div>
            </div>

            {/* Personal Details */}
            <div className="person-details">
              <h5 className="fw-bold">
                Ninfa Monaldo{" "}
                <img
                  width={20}
                  height={20}
                  src="/images/profile/01.png"
                  alt="instagram-check-mark"
                />
              </h5>
              <p>Web designer & Developer</p>
            </div>

            {/* User Info Form */}
            <Form className="app-form" onSubmit={handleSubmit}>
              <h5 className="mb-2 text-dark fw-bold">User Info</h5>
              <Row>
                <Col md="12">
                  <FormGroup>
                    <Label>Username</Label>
                    <Input type="text" placeholder="Maria C. Eck" />
                  </FormGroup>
                </Col>
                <Col md="12">
                  <FormGroup>
                    <Label>Email address</Label>
                    <Input type="email" placeholder="MariaCEck@teleworm.us" />
                  </FormGroup>
                </Col>
                <Col md="6">
                  <FormGroup>
                    <Label>Password</Label>
                    <Input type="password" placeholder="*******" />
                  </FormGroup>
                </Col>
                <Col md="6">
                  <FormGroup>
                    <Label>Confirm Password</Label>
                    <Input type="password" placeholder="*******" />
                  </FormGroup>
                </Col>
              </Row>

              {/* Divider */}
              <div className="app-divider-v dotted my-3"></div>

              <h5 className="mb-2 text-dark fw-bold">Personal Info</h5>
              <Row>
                <Col md="12">
                  <FormGroup>
                    <Label>Address</Label>
                    <Input
                      type="textarea"
                      placeholder="1098 Asylum Avenue, New Haven, CT 06510"
                    />
                  </FormGroup>
                </Col>
                <Col md="12">
                  <FormGroup>
                    <Label>Address 2</Label>
                    <Input
                      type="textarea"
                      placeholder="51244 Ankunding Villages, Reicheltown, IL 84366"
                    />
                  </FormGroup>
                </Col>
                <Col md="6">
                  <FormGroup>
                    <Label>City</Label>
                    <Input type="text" placeholder="Oregon" />
                  </FormGroup>
                </Col>
                <Col md="4">
                  <FormGroup>
                    <Label>State</Label>
                    <Input type="select">
                      <option>Choose...</option>
                      <option>USA</option>
                      <option>Canada</option>
                      <option>Australia</option>
                    </Input>
                  </FormGroup>
                </Col>
                <Col md="2">
                  <FormGroup>
                    <Label>Zip</Label>
                    <Input type="text" placeholder="CT 06510" />
                  </FormGroup>
                </Col>
                <Col md="6">
                  <FormGroup>
                    <Label>Language</Label>
                    <Input type="select">
                      <option value="EN">English</option>
                      <option value="GU">Gujarati</option>
                      <option value="HI">Hindi</option>
                      <option value="DU">Dutch</option>
                      <option value="FR">French</option>
                      <option value="RU">Russian</option>
                      <option value="KO">Korean</option>
                      <option value="TA">Tamil</option>
                      <option value="SP">Spanish</option>
                    </Input>
                  </FormGroup>
                </Col>
                <Col md="12">
                  <div className="text-end">
                    <Button color="primary" type="submit">
                      {loading ? <Spinner size="sm" /> : "Submit"}
                    </Button>
                  </div>
                </Col>
              </Row>
            </Form>
          </div>
        </CardBody>
      </Card>
    </>
  );
}

export default SettingProfile;
