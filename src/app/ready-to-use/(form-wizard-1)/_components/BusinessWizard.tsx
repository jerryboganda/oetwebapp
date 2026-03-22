import { useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
  Col,
  Row,
} from "reactstrap";
import classnames from "classnames";

const BusinessAccountWizard = () => {
  const [activeTab, setActiveTab] = useState("details");

  const toggle = (tab: string) => {
    if (activeTab !== tab) setActiveTab(tab);
  };

  return (
    <Col xs="12">
      <Card>
        <CardHeader>
          <h5>Business Account Wizards</h5>
        </CardHeader>
        <CardBody>
          <Nav tabs className="business-nav d-flex">
            <NavItem className="flex-grow-1 text-center">
              <NavLink
                className={classnames("btn nav-link", "border-0", {
                  active: activeTab === "details",
                })}
                onClick={() => toggle("details")}
              >
                <i className="ph-duotone ph-user-circle-plus"></i>
                <span className="f-s-18 f-w-500">Create account</span>
              </NavLink>
            </NavItem>
            <NavItem className="flex-grow-1 text-center">
              <NavLink
                className={classnames("btn nav-link", {
                  active: activeTab === "personal",
                })}
                onClick={() => toggle("personal")}
              >
                <i className="ph-duotone ph-user-switch"></i>
                <span className="f-s-18 f-w-500">Personal account</span>
              </NavLink>
            </NavItem>
            <NavItem className="flex-grow-1 text-center">
              <NavLink
                className={classnames("btn nav-link", {
                  active: activeTab === "payment",
                })}
                onClick={() => toggle("payment")}
              >
                <i className="ph-duotone ph-credit-card"></i>
                <span className="f-s-18 f-w-500">Payment method</span>
              </NavLink>
            </NavItem>
          </Nav>

          <TabContent activeTab={activeTab} className="mt-3">
            {/* Create Account */}
            <TabPane tabId="details">
              <Form>
                <Row>
                  <Col xs="12">
                    <FormGroup>
                      <Label className="f-w-500">Username</Label>
                      <Input placeholder="James" type="text" />
                    </FormGroup>
                  </Col>
                  <Col xs="12">
                    <FormGroup>
                      <Label className="f-w-500">Email Address</Label>
                      <Input placeholder="@gmail.com" type="email" />
                    </FormGroup>
                  </Col>
                  <Col md="6">
                    <FormGroup>
                      <Label className="f-w-500">Password</Label>
                      <Input placeholder="******" type="password" />
                    </FormGroup>
                  </Col>
                  <Col md="6">
                    <FormGroup>
                      <Label className="f-w-500">Confirm Password</Label>
                      <Input placeholder="******" type="password" />
                    </FormGroup>
                  </Col>
                </Row>
              </Form>
            </TabPane>

            {/* Personal Info */}
            <TabPane tabId="personal">
              <Form>
                <Row>
                  <Col xs="12" className="mb-3">
                    <Label className="f-w-500">Contact Number</Label>
                    <Input type="text" placeholder="+91" />
                  </Col>
                  <Col xs="12" className="mb-3">
                    <Label className="f-w-500">Address</Label>
                    <Input type="text" placeholder="156/A ..." />
                  </Col>
                  <Col xs="12" className="mb-3">
                    <Label className="f-w-500">Address 2</Label>
                    <Input
                      type="text"
                      placeholder="Apartment, studio, or floor"
                    />
                  </Col>
                  <Col md="6" className="mb-3">
                    <Label className="f-w-500">City</Label>
                    <Input type="text" placeholder="UK" />
                  </Col>
                  <Col md="4" className="mb-3">
                    <Label className="f-w-500">State</Label>
                    <Input type="select" id="inputState1">
                      <option>Choose...</option>
                      <option>...</option>
                    </Input>
                  </Col>
                  <Col md="2" className="mb-3">
                    <Label className="f-w-500">Zip</Label>
                    <Input
                      type="text"
                      id="inputZip"
                      placeholder="xxxxx"
                      maxLength={5}
                    />
                  </Col>
                  <Col xs="12" className="mb-3">
                    <FormGroup check>
                      <Input id="gridCheck1" type="checkbox" />
                      <Label check for="gridCheck1">
                        Check me out
                      </Label>
                    </FormGroup>
                  </Col>
                </Row>
              </Form>
            </TabPane>

            {/* Payment Info */}
            <TabPane tabId="payment">
              <Form>
                <Row>
                  <Col xs="12">
                    <Card className="shadow-none">
                      <CardBody className="select-content">
                        <FormGroup check className="mb-3">
                          <Label check className="check-box f-w-500">
                            <Input type="radio" name="payment" />
                            <span className="radiomark outline-secondary"></span>
                            <span className="fs-6">Credit / Debit Card</span>
                          </Label>
                        </FormGroup>
                        <Row>
                          <Col xs="12">
                            <FormGroup>
                              <Label className="f-w-500">Cardholder Name</Label>
                              <Input placeholder="Olaf" type="text" />
                            </FormGroup>
                          </Col>
                          <Col md="6">
                            <FormGroup>
                              <Label className="f-w-500">Card Number</Label>
                              <Input
                                placeholder="xxxx-xxxx-xxxx-xxxx"
                                type="text"
                                required
                              />
                            </FormGroup>
                          </Col>
                          <Col md="6" xl="3">
                            <FormGroup>
                              <Label className="f-w-500">Expiration Date</Label>
                              <Input placeholder="MM/YY" type="text" />
                            </FormGroup>
                          </Col>
                          <Col md="6" xl="3">
                            <FormGroup>
                              <Label className="f-w-500">CVC code</Label>
                              <Input placeholder="xxx" type="text" />
                            </FormGroup>
                          </Col>
                          <Col xs="12" className="text-end">
                            <Button color="primary" type="button">
                              Submit
                            </Button>
                          </Col>
                        </Row>
                      </CardBody>
                    </Card>
                  </Col>

                  {/* Visa Option */}
                  <Col md="6">
                    <Card className="shadow-none">
                      <CardBody className="select-content position-relative">
                        <Label className="check-box">
                          <Input type="radio" name="payment" />
                          <span className="radiomark outline-secondary position-absolute" />
                          <span className="d-flex align-items-center mg-s-25">
                            <img
                              src="/images/checkbox-radio/logo1.png"
                              alt=""
                              className="w-30 h-30"
                            />
                            <span className="ms-2">
                              <span className="fs-6 f-w-500">Visa Card</span>
                              <span className="d-block text-secondary">
                                Select Visa card payment method
                              </span>
                            </span>
                          </span>
                        </Label>
                      </CardBody>
                    </Card>
                  </Col>

                  {/* PayPal Option */}
                  <Col md="6">
                    <Card className="shadow-none">
                      <CardBody className="select-content position-relative">
                        <Label className="check-box">
                          <Input type="radio" name="payment" />
                          <span className="radiomark outline-secondary position-absolute" />
                          <span className="d-flex align-items-center mg-s-25">
                            <img
                              src="/images/checkbox-radio/logo3.png"
                              alt=""
                              className="w-30 h-30"
                            />
                            <span className="ms-2">
                              <span className="fs-6 f-w-500">Paypal</span>
                              <span className="d-block text-secondary">
                                Select Paypal payment method
                              </span>
                            </span>
                          </span>
                        </Label>
                      </CardBody>
                    </Card>
                  </Col>
                </Row>
              </Form>
            </TabPane>
          </TabContent>
        </CardBody>
      </Card>
    </Col>
  );
};

export default BusinessAccountWizard;
