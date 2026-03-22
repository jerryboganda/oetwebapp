"use client";
import React from "react";
import { Col, Container, Row, Form, FormGroup, Label, Input } from "reactstrap";
import Link from "next/link";

const PasswordResetImgPage = () => {
  return (
    <div>
      <Container fluid>
        <Row>
          <Col xs={12} className="p-0">
            <div className="login-form-container">
              <div className="mb-4 text-center">
                <Link className="logo d-inline-block" href="/">
                  <img
                    src="/images/logo/polytronx-dark.svg"
                    className="w-250"
                    alt="logo"
                  />
                </Link>
              </div>

              <div className="form_container">
                <Form className="app-form rounded-control">
                  <div className="mb-3 text-center">
                    <h3 className="text-primary-dark">Reset Your Password</h3>
                    <p className="f-s-12 text-secondary">
                      Create a new password and sign in to admin
                    </p>
                  </div>

                  <FormGroup className="mb-3">
                    <Label for="currentPassword">Current Password</Label>
                    <Input
                      type="password"
                      id="currentPassword"
                      placeholder="Enter Your Password"
                      required
                    />
                  </FormGroup>

                  <FormGroup className="mb-3">
                    <Label for="newPassword">New Password</Label>
                    <Input
                      type="password"
                      id="newPassword"
                      placeholder="Enter Your Password"
                      required
                    />
                  </FormGroup>

                  <FormGroup className="mb-3">
                    <Label for="confirmPassword">Confirm Password</Label>
                    <Input
                      type="password"
                      id="confirmPassword"
                      placeholder="Enter Your Password"
                      required
                    />
                  </FormGroup>

                  <div>
                    <Link
                      href="/"
                      role="button"
                      className="btn btn-light-primary w-100"
                    >
                      Reset Password
                    </Link>
                  </div>
                </Form>
              </div>
            </div>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default PasswordResetImgPage;
