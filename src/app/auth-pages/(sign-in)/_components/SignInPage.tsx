"use client";
import React, { useState } from "react";
import {
  Container,
  Row,
  Col,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
  Spinner,
} from "reactstrap";
import Link from "next/link";
import {
  IconBrandFacebook,
  IconBrandGithub,
  IconBrandGoogle,
} from "@tabler/icons-react";
import { loginUser } from "@/lib/auth/action";

const SignInPage = () => {
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
                      <img
                        src="/images/logo/polytronx-dark.svg"
                        alt="logo"
                        className="img-fluid"
                      />
                    </span>
                  </div>
                  <div className="signup-bg-img">
                    <img
                      src="/images/login/01.png"
                      alt="background"
                      className="img-fluid"
                    />
                  </div>
                </div>
              </Col>
              <Col lg={6} className="form-contentbox">
                <div className="form-container">
                  <Form
                    action={loginUser}
                    onSubmit={handleSubmit}
                    className="app-form rounded-control"
                  >
                    <Row>
                      <Col xs={12} className="mb-5 text-center text-lg-start">
                        <h2 className="text-primary-dark f-w-600">
                          Welcome To PolytronX!
                        </h2>
                        <p>
                          Sign in with your data that you entered during your
                          registration
                        </p>
                      </Col>

                      <Col xs={12}>
                        <FormGroup>
                          <Label for="username">Username</Label>
                          <Input
                            className="form-control"
                            id="username"
                            name="username"
                            placeholder="Enter Your Username"
                            type="text"
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup>
                          <Label for="password">Password</Label>
                          <Link
                            href="/auth-pages/password-reset"
                            className="link-primary-dark float-end"
                          >
                            Forgot Password?
                          </Link>
                          <Input
                            id="password"
                            name="password"
                            placeholder="Enter Your Password"
                            type="password"
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup check className="mb-3">
                          <Input type="checkbox" id="checkDefault" />
                          <Label
                            check
                            for="checkDefault"
                            className="text-secondary"
                          >
                            Remember me
                          </Label>
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <div className="mb-3">
                          <Button
                            type="submit"
                            color="light-primary"
                            className="w-100"
                          >
                            {isSubmitting ? <Spinner size="sm" /> : "Sign In"}
                          </Button>
                        </div>
                      </Col>

                      <Col xs={12} className="text-center text-lg-start mt-3">
                        Don&#39;t have an account?
                        <Link
                          href="/auth-pages/sign-up"
                          className="link-primary text-decoration-underline ms-1"
                        >
                          Sign up
                        </Link>
                      </Col>

                      <div className="app-divider-v justify-content-center my-4">
                        <p className="text-center mb-0">Or sign in with</p>
                      </div>

                      <Col xs={12} className="text-center">
                        <Button
                          type="button"
                          color="light-primary"
                          className="btn btn-light-facebook icon-btn b-r-22 m-1"
                        >
                          <IconBrandFacebook size={18} />
                        </Button>
                        <Button
                          type="button"
                          color="light-danger"
                          className="btn btn-light-gmail icon-btn b-r-22 m-1"
                        >
                          <IconBrandGoogle size={18} />
                        </Button>
                        <Button
                          type="button"
                          color="light-dark"
                          className="btn btn-light-github icon-btn b-r-22 m-1"
                        >
                          <IconBrandGithub size={18} />
                        </Button>
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

export default SignInPage;
