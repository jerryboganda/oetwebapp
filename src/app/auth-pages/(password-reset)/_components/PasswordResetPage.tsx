"use client";
import React from "react";
import { Col, Container, Row, Form, FormGroup, Label, Input } from "reactstrap";
import Link from "next/link";

const PasswordResetPage = () => {
  return (
    <div>
      <div className="sign-in-bg">
        <div className="app-wrapper d-block">
          <div className="main-container">
            <Container>
              <Row className="sign-in-content-bg">
                <Col lg={6} className="image-contentbox d-none d-lg-block">
                  <div className="form-container">
                    <div className="signup-content mt-4">
                      <span>
                        <img
                          src="/images/logo/polytronx-dark.svg"
                          alt="logo"
                          className="img-fluid"
                        />
                      </span>
                    </div>

                    <div className="signup-bg-img">
                      <img
                        src="/images/login/03.png"
                        alt="background"
                        className="img-fluid"
                      />
                    </div>
                  </div>
                </Col>

                <Col lg={6} className="form-contentbox">
                  <div className="form-container">
                    <Form className="app-form rounded-control">
                      <Row>
                        <Col xs={12}>
                          <div className="mb-5 text-center text-lg-start">
                            <h2 className="text-primary-dark f-w-600">
                              Reset Your Password
                            </h2>
                            <p>Create a new password and sign in to admin</p>
                          </div>
                        </Col>

                        <Col xs={12}>
                          <FormGroup className="mb-3">
                            <Label htmlFor="password">New Password</Label>
                            <Input
                              type="password"
                              id="password"
                              placeholder="Enter Your Password"
                              required
                            />
                          </FormGroup>
                        </Col>

                        <Col xs={12}>
                          <FormGroup className="mb-3">
                            <Label htmlFor="password1">Confirm Password</Label>
                            <Input
                              type="password"
                              id="password1"
                              placeholder="Enter Your Password"
                              required
                            />
                          </FormGroup>
                        </Col>

                        <Col xs={12}>
                          <div className="mb-3">
                            <Link
                              href="/auth-pages/sign-in"
                              role="button"
                              className="btn btn-light-primary w-100"
                            >
                              Reset Password
                            </Link>
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
    </div>
  );
};

export default PasswordResetPage;
