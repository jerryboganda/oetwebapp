"use client";

import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Col,
  Row,
  Form,
  FormGroup,
  Label,
  Input,
  Container,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconCreditCard } from "@tabler/icons-react";

const FloatingLabels = () => {
  const [form, setForm] = useState({
    name: "",
    password: "",
    email: "",
    comment: "",
    message: "",
    username: "",
    select: "2",
  });

  const handleChange = (
    e: React.ChangeEvent<
      HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement
    >
  ) => {
    const { name, value } = e.target;
    setForm((prev) => ({ ...prev, [name]: value }));
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Floating Labels"
          title="Forms elements"
          path={["Floating labels"]}
          Icon={IconCreditCard}
        />
        <Row>
          {/* Custom Floating Labels */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Custom Floating Labels</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <Row>
                    <Col xs="12">
                      <FormGroup className="floating-form mb-3">
                        <Input
                          type="text"
                          name="name"
                          value={form.name}
                          onChange={handleChange}
                          required
                          placeholder=" "
                        />
                        <Label className="form-label">Name</Label>
                      </FormGroup>
                    </Col>
                    <Col xs={12}>
                      <FormGroup className="floating-form">
                        <Input
                          type="password"
                          name="password"
                          value={form.password}
                          onChange={handleChange}
                          required
                          placeholder=" "
                        />
                        <Label className="form-label">Password</Label>
                      </FormGroup>
                    </Col>
                  </Row>
                </Form>
              </CardBody>
            </Card>
          </Col>

          {/* Basic Floating Labels */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Basic Floating Label</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <div className="form-floating mb-3">
                    <Input
                      type="email"
                      name="email"
                      className="form-control"
                      id="floatingInput"
                      placeholder="Email address"
                      value={form.email}
                      onChange={handleChange}
                    />
                    <Label htmlFor="floatingInput">Email address</Label>
                  </div>
                  <div className="form-floating">
                    <Input
                      type="password"
                      name="password"
                      className="form-control"
                      id="floatingPassword"
                      placeholder="Password"
                      value={form.password}
                      onChange={handleChange}
                    />
                    <Label htmlFor="floatingPassword">Password</Label>
                  </div>
                </Form>
              </CardBody>
            </Card>
          </Col>

          {/* Textarea Floating Labels */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Textareas Floating Labels</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <div className="form-floating mb-3">
                    <textarea
                      className="form-control"
                      name="comment"
                      placeholder="Type a comment here"
                      value={form.comment}
                      onChange={handleChange}
                    />
                    <Label>Comments</Label>
                  </div>
                  <div className="form-floating mb-3">
                    <textarea
                      className="form-control"
                      name="message"
                      placeholder="Type a message here"
                      value={form.message}
                      onChange={handleChange}
                    />
                    <Label>Message</Label>
                  </div>
                </Form>
              </CardBody>
            </Card>
          </Col>

          {/* Input Groups Floating Labels */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Input Groups Floating Labels</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <div className="input-group mb-3">
                    <span className="input-group-text b-r-left">@</span>
                    <div className="form-floating">
                      <input
                        type="text"
                        className="form-control b-r-right"
                        id="floatingInputGroup1"
                        placeholder="Username"
                        name="username"
                        value={form.username}
                        onChange={handleChange}
                      />
                      <label htmlFor="floatingInputGroup1">Username</label>
                    </div>
                  </div>
                  <div className="input-group mb-3">
                    <span className="input-group-text b-r-left">@</span>
                    <div className="form-floating">
                      <input
                        type="email"
                        className="form-control b-r-right"
                        placeholder="Email Address"
                        name="email"
                        value={form.email}
                        onChange={handleChange}
                      />
                      <label htmlFor="floatingInputGroup2">Email Address</label>
                    </div>
                  </div>
                </Form>
              </CardBody>
            </Card>
          </Col>

          {/* Readonly Floating Labels */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Readonly Floating Labels</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <div className="form-floating mb-3">
                    <input
                      type="email"
                      readOnly
                      className="form-control-plaintext"
                      id="floatingEmptyPlaintextInput"
                      placeholder="name@example.com"
                    />
                    <label htmlFor="floatingEmptyPlaintextInput">
                      Empty input
                    </label>
                  </div>
                  <div className="form-floating">
                    <input
                      type="email"
                      readOnly
                      className="form-control-plaintext"
                      id="floatingPlaintextInput"
                      placeholder="name@example.com"
                      defaultValue="name@example.com"
                    />
                    <label htmlFor="floatingPlaintextInput">
                      Input with value
                    </label>
                  </div>
                </Form>
              </CardBody>
            </Card>
          </Col>

          {/* Floating Input Value */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Floating Input Value</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <div className="form-floating mb-3">
                    <input
                      type="email"
                      className="form-control"
                      id="floatingInputValue"
                      placeholder="name@example.com"
                      defaultValue="test@example.com"
                    />
                    <label htmlFor="floatingInputValue">Input with value</label>
                  </div>
                  <div className="form-floating floating-invalid">
                    <input
                      type="email"
                      className="form-control is-invalid pe-4"
                      id="floatingInputInvalid"
                      placeholder="name@example.com"
                      defaultValue="test@example.com"
                    />
                    <label htmlFor="floatingInputInvalid">Invalid input</label>
                  </div>
                </Form>
              </CardBody>
            </Card>
          </Col>

          {/* Select Floating Labels */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Select Floating Labels</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form floating-select">
                  <div className="form-floating mb-3">
                    <select
                      className="form-select"
                      id="floatingSelect"
                      name="select"
                      value={form.select}
                      onChange={handleChange}
                    >
                      <option value="">Open this select menu</option>
                      <option value="1">One</option>
                      <option value="2">Two</option>
                      <option value="3">Three</option>
                    </select>
                    <label htmlFor="floatingSelect">Works with selects</label>
                  </div>
                  <div className="form-floating">
                    <select
                      className="form-select"
                      id="floatingSelectDisabled"
                      defaultValue="2"
                      disabled
                    >
                      <option value="">Open this select menu</option>
                      <option value="1">One</option>
                      <option value="2">Two</option>
                      <option value="3">Three</option>
                    </select>
                  </div>
                </Form>
              </CardBody>
            </Card>
          </Col>

          {/* Floating labels Layout */}
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Floating labels Layout</h5>
              </CardHeader>
              <CardBody>
                <Form className="app-form">
                  <Row className="g-2">
                    <Col md="6">
                      <div className="form-floating">
                        <Input
                          type="email"
                          className="form-control"
                          id="floatingInputGrid"
                          placeholder="name@example.com"
                          defaultValue="mdo@example.com"
                        />
                        <label htmlFor="floatingInputGrid">Email address</label>
                      </div>
                    </Col>
                    <Col md="6">
                      <div className="form-floating">
                        <select
                          className="form-select"
                          id="floatingSelectGrid"
                          defaultValue="2"
                        >
                          <option>Open this select menu</option>
                          <option value="1">One</option>
                          <option value="2">Two</option>
                          <option value="3">Three</option>
                        </select>
                        <label htmlFor="floatingSelectGrid">
                          Works with selects
                        </label>
                      </div>
                    </Col>
                    <Col xs={12}>
                      <div className="form-floating">
                        <Input
                          type="password"
                          className="form-control"
                          id="floatingPassword1"
                          placeholder="Password"
                        />
                        <label htmlFor="floatingPassword1">Password</label>
                      </div>
                    </Col>
                  </Row>
                </Form>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};
export default FloatingLabels;
