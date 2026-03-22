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
import { loginUserImg } from "@/lib/auth/action";

const SignInBgImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = () => {
    setIsSubmitting(true);
  };

  const [formData, setFormData] = useState({
    username: "",
    email: "",
    password: "",
    remember: false,
  });

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value, type, checked } = e.target;
    setFormData((prev) => ({
      ...prev,
      [name]: type === "checkbox" ? checked : value,
    }));
  };

  return (
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
              <Form
                action={loginUserImg}
                onSubmit={handleSubmit}
                className="app-form rounded-control"
              >
                <div className="mb-3 text-center">
                  <h3 className="text-primary-dark">Login to your Account</h3>
                  <p className="f-s-12 text-secondary">
                    Get started with our app, just create an account and enjoy
                    the experience.
                  </p>
                </div>

                <FormGroup className="mb-">
                  <Label for="username">Email address</Label>
                  <Input
                    type="text"
                    id="username"
                    className="form-control b-r-20"
                    name="username"
                    value={formData.username}
                    onChange={handleChange}
                  />
                  <div className="form-text text">
                    We&#39;ll never share your email with anyone else.
                  </div>
                </FormGroup>
                <FormGroup className="mb-">
                  <Label for="username">Password</Label>
                  <Input
                    type="text"
                    id="password"
                    className="form-control b-r-20"
                    name="password"
                    value={formData.password}
                    onChange={handleChange}
                  />
                </FormGroup>
                <div className="mb-3 form-check">
                  <input
                    className="form-check-input"
                    id="formCheck1"
                    type="checkbox"
                  />
                  <label className="form-check-label" htmlFor="formCheck1">
                    remember me
                  </label>
                </div>

                <Button
                  type="submit"
                  color="light-primary"
                  className="w-100 mb-3 rounded"
                >
                  {isSubmitting ? <Spinner size="sm" /> : "Submit"}
                </Button>

                <div className="app-divider-v justify-content-center">
                  <p>OR</p>
                </div>

                <div className="text-center mb-3">
                  <Button
                    color="light-primary"
                    className="icon-btn btn b-r-5  m-1"
                  >
                    <IconBrandFacebook size={18} />
                  </Button>
                  <Button
                    color="light-danger"
                    className="icon-btn btn b-r-5  m-1"
                  >
                    <IconBrandGoogle size={18} />
                  </Button>
                  <Button
                    color="light-dark"
                    className="icon-btn btn b-r-5  m-1"
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
  );
};

export default SignInBgImgPage;
