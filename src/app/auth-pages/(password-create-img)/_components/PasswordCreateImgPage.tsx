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
import { resetPassword } from "@/lib/auth/action";

const PasswordCreateImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = () => {
    setIsSubmitting(true);
  };
  return (
    <div>
      <Container fluid>
        <Row>
          <Col xs={12} className="p-0">
            <div className="login-form-container">
              <div className="mb-4 text-center">
                <img
                  src="/images/logo/polytronx-dark.svg"
                  className="w-250"
                  alt="logo"
                />
              </div>
              <div className="form_container">
                <Form
                  className="app-form rounded-control"
                  action={resetPassword}
                  onSubmit={handleSubmit}
                >
                  <div className="mb-3 text-center">
                    <h3 className="text-primary-dark">Create Password</h3>
                    <p className="f-s-12 text-secondary">
                      Your new password must be different from previously used
                      passwords.
                    </p>
                  </div>

                  <FormGroup className="mb-3">
                    <Label className="form-label">Current Password</Label>
                    <Input
                      name="currentPassword"
                      type="password"
                      className="form-control"
                      placeholder="Enter Your Password"
                    />
                  </FormGroup>

                  <FormGroup className="mb-3">
                    <Label className="form-label">New Password</Label>
                    <Input
                      name="newPassword"
                      type="password"
                      className="form-control"
                      placeholder="Enter Your Password"
                    />
                  </FormGroup>

                  <FormGroup className="mb-3">
                    <Label className="form-label">Confirm Password</Label>
                    <Input
                      name="confirmPassword"
                      type="password"
                      className="form-control"
                      placeholder="Enter Your Password"
                    />
                  </FormGroup>

                  <Button type="submit" color="light-primary" className="w-100">
                    {isSubmitting ? <Spinner size="sm" /> : "Reset Password"}
                  </Button>
                </Form>
              </div>
            </div>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default PasswordCreateImgPage;
