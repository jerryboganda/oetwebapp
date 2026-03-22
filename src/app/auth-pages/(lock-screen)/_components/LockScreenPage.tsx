"use client";
import React, { useState } from "react";
import {
  Col,
  Container,
  Row,
  Form,
  FormGroup,
  Input,
  Label,
  Button,
  Spinner,
} from "reactstrap";
import { unlockScreen } from "@/lib/auth/action";

const LockScreenPage = () => {
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
                  <div className="signup-content mt-4">
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
                      src="/images/login/06.png"
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
                    action={unlockScreen}
                    onSubmit={handleSubmit}
                  >
                    <Row>
                      <Col xs={12}>
                        <div className="mb-3 user-screen">
                          <div className="w-90 h-90 b-r-15 d-flex-center overflow-hidden text-bg-primary">
                            <img
                              src="/images/avatar/14.png"
                              alt="avatar"
                              className="img-fluid"
                            />
                          </div>
                        </div>
                      </Col>

                      <Col xs={12}>
                        <div className="mb-5 text-center text-lg-start">
                          <h2 className="text-primary-dark f-w-600">
                            Lock Screen
                          </h2>
                          <p>Hello, enter your password to unlock the screen</p>
                        </div>
                      </Col>

                      <Col xs={12}>
                        <FormGroup className="mb-3">
                          <Label htmlFor="password" className="form-label">
                            Password
                          </Label>
                          <Input
                            type="password"
                            id="password"
                            name="password"
                            placeholder="Enter Your Password"
                            required
                          />
                          <p className="text-dark f-s-12 mt-2">
                            Enter your password to view your screen
                          </p>
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup check className="mb-3">
                          <Input
                            type="checkbox"
                            id="rememberMe"
                            name="rememberMe"
                          />
                          <Label check htmlFor="rememberMe">
                            Remember me
                          </Label>
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <div className="mb-3">
                          <Button
                            type="submit"
                            color="light-primary"
                            className="w-100 mb-3"
                          >
                            {isSubmitting ? <Spinner size="sm" /> : "Unlock"}
                          </Button>
                        </div>
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

export default LockScreenPage;
