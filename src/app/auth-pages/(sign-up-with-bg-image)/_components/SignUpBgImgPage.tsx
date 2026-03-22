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

const SignUpBgImgPage = () => {
  return (
    <div>
      <Container fluid>
        <Row>
          <Col xs={12} className="p-0">
            <div className="login-form-container">
              <div className="mb-4">
                <Link className="logo d-inline-block" href="/">
                  <img
                    src="/images/logo/polytronx-dark.svg"
                    className="w-250"
                    alt="logo"
                  />
                </Link>
              </div>

              <div className="form_container">
                <Form className="app-form p-3 rounded-control">
                  <div className="mb-3 text-center">
                    <h3 className="text-primary-dark">Create Account</h3>
                    <p className="f-s-12 text-secondary">
                      Get started For Free Today.
                    </p>
                  </div>

                  <FormGroup className="mb-3">
                    <Label className="form-label">Username</Label>
                    <Input
                      type="text"
                      placeholder="Enter Your Username"
                      required
                    />
                  </FormGroup>

                  <FormGroup className="mb-3">
                    <Label className="form-label">Email</Label>
                    <Input
                      type="email"
                      placeholder="Enter Your Email"
                      required
                    />
                  </FormGroup>

                  <FormGroup className="mb-3">
                    <Label className="form-label">Password</Label>
                    <Input
                      type="password"
                      placeholder="Enter Your Password"
                      required
                    />
                  </FormGroup>

                  <FormGroup check className="mb-3">
                    <Input
                      type="checkbox"
                      id="formCheck1"
                      className="form-check-input"
                    />
                    <Label
                      check
                      htmlFor="formCheck1"
                      className="form-check-label"
                    >
                      Remember me
                    </Label>
                  </FormGroup>

                  <div className="mb-3">
                    <Button
                      color="light-primary"
                      type="submit"
                      className="w-100 "
                    >
                      Submit
                    </Button>
                  </div>

                  <div className="app-divider-v justify-content-center">
                    <p>OR</p>
                  </div>

                  <div className="mb-3 text-center">
                    <Button
                      type="button"
                      color="light-primary"
                      className="icon-btn b-r-5 m-1"
                    >
                      <IconBrandFacebook size={18} />
                    </Button>
                    <Button
                      type="button"
                      color="light-danger"
                      className="icon-btn b-r-5 m-1"
                    >
                      <IconBrandGoogle size={18} />
                    </Button>
                    <Button
                      type="button"
                      color="light-dark"
                      className="icon-btn b-r-5 m-1"
                    >
                      <IconBrandGithub size={18} />
                    </Button>
                  </div>

                  <div className="text-center">
                    <Link
                      href="/other-pages/terms-condition"
                      className="text-secondary text-decoration-underline"
                    >
                      Terms of use &amp; Conditions
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

export default SignUpBgImgPage;
