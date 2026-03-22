"use client";
import React, { useState } from "react";
import {
  Col,
  Container,
  Row,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
  Spinner,
} from "reactstrap";
import { createPassword } from "@/lib/auth/action";

const PasswordCreatePage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = () => {
    setIsSubmitting(true);
  };

  return (
    <div className="sign-in-bg">
      <div className="app-wrapper d-block">
        <div className="main-container">
          <Container>
            <Row className="sign-in-content-bg">
              <Col lg={6} className="image-contentbox d-none d-lg-block">
                <div className="form-container">
                  <div className="signup-content mt-4 text-center">
                    <span>
                      {" "}
                      <img
                        src="/images/logo/polytronx-dark.svg"
                        alt="logo"
                        className="img-fluid"
                      />
                    </span>
                  </div>
                  <div className="signup-bg-img">
                    <img
                      src="/images/login/05.png"
                      alt="background"
                      className="img-fluid"
                    />
                  </div>
                </div>
              </Col>

              <Col lg={6} className="form-contentbox">
                <div className="form-container">
                  <Form
                    className="app-form rounded-control"
                    action={createPassword}
                    onSubmit={handleSubmit}
                  >
                    <Row>
                      <Col xs={12}>
                        <div className="mb-5 text-center text-lg-start">
                          <h2 className="text-primary-dark f-w-600">
                            Create Password
                          </h2>
                          <p>
                            Your new password must be different from previously
                            used passwords.
                          </p>
                        </div>
                      </Col>

                      <Col xs={12}>
                        <FormGroup className="mb-3">
                          <Label for="currentPassword">Current Password</Label>
                          <Input
                            type="password"
                            id="currentPassword"
                            name="currentPassword"
                            placeholder="Enter Your Current Password"
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup className="mb-3">
                          <Label for="newPassword">New Password</Label>
                          <Input
                            type="password"
                            id="newPassword"
                            name="newPassword"
                            placeholder="Enter Your New Password"
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup className="mb-3">
                          <Label for="confirmPassword">Confirm Password</Label>
                          <Input
                            type="password"
                            id="confirmPassword"
                            name="confirmPassword"
                            placeholder="Confirm Your New Password"
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup className="mb-0">
                          <Button
                            type="submit"
                            color="light-primary"
                            className="w-100"
                          >
                            {isSubmitting ? (
                              <Spinner size="sm" />
                            ) : (
                              "Create Password"
                            )}
                          </Button>
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>
                </div>
              </Col>
            </Row>
          </Container>
        </div>
      </div>
    </div>
  );
};

export default PasswordCreatePage;
