import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Form,
  FormGroup,
  Input,
  Label,
  Row,
} from "reactstrap";
import {
  IconBrandMastercard,
  IconChecks,
  IconInfoCircle,
  IconUserCircle,
} from "@tabler/icons-react";

const PaymentWizard = () => {
  const [activeTab, setActiveTab] = useState("v-features-tab");
  return (
    <div>
      <Card>
        <CardHeader>
          <h5>Payment Method Wizards</h5>
        </CardHeader>
        <CardBody>
          <div className="form-wizard">
            <Row>
              <Col xl={3} className="mb-3">
                <div
                  className="nav navstpes flex-column"
                  id="Basic"
                  role="tablist"
                >
                  <Button
                    className={`nav-link ${activeTab === "v-features-tab" ? "active" : ""}`}
                    onClick={() => setActiveTab("v-features-tab")}
                    color="link"
                  >
                    <IconUserCircle className="w-35 h-35" />
                    <span className="ms-3 fw-normal">Create Account</span>
                  </Button>
                  <Button
                    className={`nav-link ${activeTab === "v-history-tab" ? "active" : ""}`}
                    onClick={() => setActiveTab("v-history-tab")}
                    color="link"
                  >
                    <IconInfoCircle className="w-35 h-35" />
                    <span className="ms-3 fw-normal">Personal Info</span>
                  </Button>
                  <Button
                    className={`nav-link ${activeTab === "v-reviews-tab" ? "active" : ""}`}
                    onClick={() => setActiveTab("v-reviews-tab")}
                    color="link"
                  >
                    <IconBrandMastercard className="w-35 h-35" />
                    <span className="ms-3 fw-normal">Payment Method</span>
                  </Button>
                  <Button
                    className={`nav-link ${activeTab === "v-reviews-tab1" ? "active" : ""}`}
                    onClick={() => setActiveTab("v-reviews-tab1")}
                    color="link"
                  >
                    <IconChecks className="w-35 h-35" />
                    <span className="ms-3 fw-normal">Confirm order</span>
                  </Button>
                </div>
              </Col>
              <Col xl={9}>
                <div className="tab-content" id="BasicContent">
                  <div
                    className={`tab-pane fade ${activeTab === "v-features-tab" ? "show active" : ""}`}
                    id="v-features-tab-pane"
                    role="tabpanel"
                    aria-labelledby="v-features-tab"
                    tabIndex={-1}
                  >
                    <Form className="app-form">
                      <Row>
                        <Col xs="12">
                          <FormGroup className="mb-3">
                            <Label
                              className="form-label f-w-500"
                              for="username"
                            >
                              Username
                            </Label>
                            <Input
                              type="text"
                              id="username"
                              placeholder="James"
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col xs="12">
                          <FormGroup className="mb-3">
                            <Label className="form-label f-w-500" for="email">
                              Email Address
                            </Label>
                            <Input
                              type="email"
                              id="email"
                              placeholder="@gmail.com"
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col md="6">
                          <FormGroup className="mb-3">
                            <Label
                              className="form-label f-w-500"
                              for="password"
                            >
                              Password
                            </Label>
                            <Input
                              type="password"
                              id="password"
                              placeholder="******"
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col md="6">
                          <FormGroup className="mb-3">
                            <Label
                              className="form-label f-w-500"
                              for="confirmPassword"
                            >
                              Confirm Password
                            </Label>
                            <Input
                              type="password"
                              id="confirmPassword"
                              placeholder="******"
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>
                      </Row>
                    </Form>
                  </div>

                  <div
                    className={`tab-pane fade ${activeTab === "v-history-tab" ? "show active" : ""}`}
                    id="v-history-tab-pane"
                    role="tabpanel"
                    aria-labelledby="v-history-tab"
                    tabIndex={-1}
                  >
                    <Form className="app-form">
                      <Row>
                        <Col xs="12" className="mb-3">
                          <FormGroup>
                            <Label className="form-label f-w-500">
                              Contact Number
                            </Label>
                            <Input
                              type="text"
                              placeholder="+91"
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col xs="12" className="mb-3">
                          <FormGroup>
                            <Label className="form-label f-w-500">
                              Address
                            </Label>
                            <Input
                              type="text"
                              placeholder="156/A ..."
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col xs="12" className="mb-3">
                          <FormGroup>
                            <Label className="form-label f-w-500">
                              Address 2
                            </Label>
                            <Input
                              type="text"
                              placeholder="Apartment, studio, or floor"
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col md="6" className="mb-3">
                          <FormGroup>
                            <Label className="form-label f-w-500">City</Label>
                            <Input
                              type="text"
                              placeholder="UK"
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col md="4" className="mb-3">
                          <FormGroup>
                            <Label
                              for="inputState"
                              className="form-label f-w-500"
                            >
                              State
                            </Label>
                            <Input
                              type="select"
                              id="inputState"
                              defaultValue="1"
                              className="form-select"
                            >
                              <option>Choose...</option>
                              <option>1</option>
                              <option>2</option>
                            </Input>
                          </FormGroup>
                        </Col>

                        <Col md="2" className="mb-3">
                          <FormGroup>
                            <Label
                              for="inputZip1"
                              className="form-label f-w-500"
                            >
                              Zip
                            </Label>
                            <Input
                              type="text"
                              id="inputZip1"
                              placeholder="xxxxx"
                              maxLength={5}
                              className="form-control"
                            />
                          </FormGroup>
                        </Col>

                        <Col xs="12" className="mb-3">
                          <FormGroup check>
                            <Input
                              type="checkbox"
                              id="gridCheck"
                              className="form-check-input"
                            />
                            <Label
                              for="gridCheck"
                              check
                              className="form-check-label f-w-500"
                            >
                              Check me out
                            </Label>
                          </FormGroup>
                        </Col>
                      </Row>
                    </Form>
                  </div>

                  <div
                    className={`tab-pane fade ${activeTab === "v-reviews-tab" ? "show active" : ""}`}
                    id="v-reviews-tab-pane"
                    role="tabpanel"
                    aria-labelledby="v-reviews-tab"
                    tabIndex={-1}
                  >
                    <div className="custome-radio-list">
                      <Row>
                        {/* Credit/Debit Card Option with Form */}
                        <Col xs="12">
                          <Card className="shadow-none">
                            <CardBody className="select-content">
                              <FormGroup className="mb-3">
                                <Label check className="check-box f-w-500">
                                  <Input type="radio" name="radio-group1" />
                                  <span className="radiomark outline-secondary"></span>
                                  <span className="fs-6">
                                    Credit / Debit Card
                                  </span>
                                </Label>
                              </FormGroup>

                              <Form className="app-form">
                                <Row>
                                  <Col xs="12">
                                    <FormGroup className="mb-3">
                                      <Label className="form-label f-w-500">
                                        Cardholder Name
                                      </Label>
                                      <Input
                                        type="text"
                                        placeholder="Olaf"
                                        className="form-control"
                                      />
                                    </FormGroup>
                                  </Col>

                                  <Col md="6">
                                    <FormGroup className="mb-3">
                                      <Label className="form-label f-w-500">
                                        Card Number
                                      </Label>
                                      <Input
                                        type="text"
                                        placeholder="xxxx-xxxx-xxxx-xxx"
                                        className="form-control"
                                        required
                                      />
                                    </FormGroup>
                                  </Col>

                                  <Col md="6" xl="3">
                                    <FormGroup className="mb-3">
                                      <Label className="form-label f-w-500">
                                        Expiration Date
                                      </Label>
                                      <Input
                                        type="text"
                                        placeholder="pin"
                                        className="form-control"
                                      />
                                    </FormGroup>
                                  </Col>

                                  <Col md="6" xl="3">
                                    <FormGroup className="mb-3">
                                      <Label className="form-label f-w-500">
                                        CVC code
                                      </Label>
                                      <Input
                                        type="text"
                                        placeholder="xxx"
                                        className="form-control"
                                      />
                                    </FormGroup>
                                  </Col>

                                  <Col xs="12">
                                    <div className="text-end">
                                      <Button type="button" color="primary">
                                        Submit
                                      </Button>
                                    </div>
                                  </Col>
                                </Row>
                              </Form>
                            </CardBody>
                          </Card>
                        </Col>

                        {/* Visa Card Option */}
                        <Col md="6">
                          <Card className="shadow-none">
                            <CardBody className="select-content">
                              <div className="position-relative">
                                <Label check className="check-box">
                                  <Input type="radio" name="radio-group1" />
                                  <span className="radiomark outline-secondary position-absolute"></span>
                                  <span className="d-flex align-items-center mg-s-25">
                                    <img
                                      src="/images/checkbox-radio/logo1.png"
                                      alt=""
                                      className="w-30 h-30"
                                    />
                                    <span className="ms-2">
                                      <span className="fs-6 f-w-500">
                                        Visa Card
                                      </span>
                                      <span className="d-block text-secondary">
                                        Select Visa card payment method
                                      </span>
                                    </span>
                                  </span>
                                </Label>
                              </div>
                            </CardBody>
                          </Card>
                        </Col>

                        {/* PayPal Option */}
                        <Col md="6">
                          <Card className="shadow-none">
                            <CardBody className="select-content">
                              <div className="position-relative">
                                <Label check className="check-box">
                                  <Input type="radio" name="radio-group1" />
                                  <span className="radiomark outline-secondary position-absolute"></span>
                                  <span className="d-flex align-items-center mg-s-25">
                                    <img
                                      src="/images/checkbox-radio/logo3.png"
                                      alt=""
                                      className="w-30 h-30"
                                    />
                                    <span className="ms-2">
                                      <span className="fs-6 f-w-500">
                                        Paypal
                                      </span>
                                      <span className="d-block text-secondary">
                                        Select PayPal payment method
                                      </span>
                                    </span>
                                  </span>
                                </Label>
                              </div>
                            </CardBody>
                          </Card>
                        </Col>
                      </Row>
                    </div>
                  </div>

                  <div
                    className={`tab-pane fade ${activeTab === "v-reviews-tab1" ? "show active" : ""}`}
                    id="v-reviews-tab-pane1"
                    role="tabpanel"
                    aria-labelledby="v-reviews-tab1"
                    tabIndex={-1}
                  >
                    <div className="completed-contents">
                      <div className="completbox text-center">
                        <img src="/images/form/done.png" alt="" />
                        <h6 className="mb-0">Thank You !</h6>
                        <p>
                          Successfully Completed your order process !
                          Confirmation will be sent your email
                        </p>
                      </div>
                    </div>
                  </div>
                </div>
              </Col>
            </Row>
          </div>
        </CardBody>
      </Card>
    </div>
  );
};

export default PaymentWizard;
