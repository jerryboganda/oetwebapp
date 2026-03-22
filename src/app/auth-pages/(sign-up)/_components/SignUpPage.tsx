"use client";
import React from "react";
import {
  Col,
  Container,
  Row,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
} from "reactstrap";
import Link from "next/link";
import {
  IconBrandFacebook,
  IconBrandGithub,
  IconBrandGoogle,
} from "@tabler/icons-react";

const SignUpPage = () => {
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
                      <img
                        src="/images/logo/polytronx-dark.svg"
                        alt="logo"
                        className="img-fluid"
                      />
                    </span>
                  </div>
                  <div className="signup-bg-img">
                    <img
                      src="/images/login/02.png"
                      alt="signup"
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
                            Create Account
                          </h2>
                          <p>Get Started For Free Today!</p>
                        </div>
                      </Col>

                      <Col xs={12}>
                        <FormGroup className="mb-3">
                          <Label for="username">Username</Label>
                          <Input
                            type="text"
                            id="username"
                            placeholder="Enter Your Username"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup className="mb-3">
                          <Label for="email">Email</Label>
                          <Input
                            type="email"
                            id="email"
                            placeholder="Enter Your Email"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col md={6}>
                        <FormGroup className="mb-3">
                          <Label for="password">Password</Label>
                          <Input
                            type="password"
                            id="password"
                            placeholder="Enter Your Password"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col md={6}>
                        <FormGroup className="mb-3">
                          <Label for="confirmPassword">Confirm Password</Label>
                          <Input
                            type="password"
                            id="confirmPassword"
                            placeholder="Enter Your Password"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup check className="mb-3">
                          <Input
                            type="checkbox"
                            id="checkDefault"
                            className="form-check-input"
                          />
                          <Label
                            check
                            htmlFor="checkDefault"
                            className="form-check-label text-secondary"
                          >
                            Accept Terms & Conditions
                          </Label>
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <div className="mb-3">
                          <Button
                            color="light-primary"
                            type="submit"
                            className="w-100"
                            href="/dashboard/project"
                          >
                            Sign Up
                          </Button>
                        </div>
                      </Col>

                      <Col xs={12}>
                        <div className="text-center text-lg-start">
                          Already Have An Account?{" "}
                          <Link
                            href="/auth-pages/sign-in"
                            className="link-primary text-decoration-underline"
                          >
                            Sign in
                          </Link>
                        </div>
                      </Col>

                      <div className="app-divider-v justify-content-center">
                        <p>Or sign up with</p>
                      </div>

                      <Col xs={12}>
                        <div className="text-center">
                          <Button
                            type="button"
                            color="light-primary"
                            className="btn-light-facebook  icon-btn b-r-22 m-1"
                          >
                            <IconBrandFacebook size={18} />
                          </Button>
                          <Button
                            type="button"
                            color="light-primary"
                            className="btn-light-gmail icon-btn b-r-22 m-1"
                          >
                            <IconBrandGoogle size={18} />
                          </Button>
                          <Button
                            type="button"
                            color="light-primary"
                            className="btn-light-github icon-btn b-r-22 m-1"
                          >
                            <IconBrandGithub size={18} />
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

export default SignUpPage;
