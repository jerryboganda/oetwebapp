"use client";
import React, { useState } from "react";
import {
  Col,
  Container,
  Row,
  FormGroup,
  Input,
  Label,
  Button,
  Spinner,
} from "reactstrap";
import { unlockScreenImg } from "@/lib/auth/action";

const LockScreenImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = () => {
    setIsSubmitting(true);
  };

  return (
    <Container fluid>
      <Row>
        <Col xs={12} className="p-0">
          <div className="login-form-container">
            <div className="mb-4">
              <a className="logo d-inline-block" href="/">
                <img
                  src="/images/logo/polytronx-dark.svg"
                  className="w-250"
                  alt="logo"
                />
              </a>
            </div>
            <div className="form_container">
              <form
                action={unlockScreenImg}
                onSubmit={handleSubmit}
                className="app-form p-3 rounded-control"
              >
                <Row>
                  <Col xs={12}>
                    <div className="mb-3 text-center ">
                      <h3 className="text-primary-dark">Lock Screen</h3>
                      <p>Hello, enter your password to unlock the screen</p>
                    </div>
                  </Col>
                  <Col xs={12}>
                    <div className="user-container mb-3 text-center">
                      <div className="h-80 w-80 d-flex-center b-r-16 overflow-hidden text-bg-primary mx-auto">
                        <img
                          src="/images/ai_avatar/3.jpg"
                          alt=""
                          className="img-fluid"
                        />
                      </div>
                      <h5 className="f-w-600 mt-2">Sunny Airey</h5>
                      <p className="text-secondary">
                        Enter Your Password to View your Screen
                      </p>
                    </div>
                  </Col>
                  <Col xs={12}>
                    <FormGroup className="mb-3">
                      <Label htmlFor="password">Password</Label>
                      <Input
                        name="password"
                        type="password"
                        id="password"
                        placeholder="Enter Your Password"
                        required
                      />
                    </FormGroup>
                  </Col>
                  <Col xs={12}>
                    <FormGroup check className="mb-3">
                      <Input
                        type="checkbox"
                        name="rememberMe"
                        id="rememberMe"
                      />
                      <Label check htmlFor="rememberMe">
                        Remember me
                      </Label>
                    </FormGroup>
                  </Col>
                  <Col xs={12}>
                    <Button
                      type="submit"
                      color="light-primary"
                      className="w-100 mb-3"
                      disabled={isSubmitting}
                    >
                      {isSubmitting ? <Spinner size="sm" /> : "Unlock"}
                    </Button>
                  </Col>
                </Row>
              </form>
            </div>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export default LockScreenImgPage;
