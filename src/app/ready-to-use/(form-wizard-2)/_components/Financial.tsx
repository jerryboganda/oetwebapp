import React, { useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Row,
  Col,
  Nav,
  NavLink,
  TabContent,
  TabPane,
  FormGroup,
  Label,
  InputGroupText,
  InputGroup,
  Form,
  Input,
  Button,
} from "reactstrap";
import {
  IconCurrencyDollar,
  IconDisc,
  IconUserCircle,
  IconUserPlus,
} from "@tabler/icons-react";

const Financial = () => {
  const [activeTab, setActiveTab] = useState("tab-1");

  const [accountType, setAccountType] = useState("existing");
  const [selectedRole, setSelectedRole] = useState("manager");
  const [selectedRole1, setSelectedRole1] = useState("marketing");
  const [accordionOpen, setAccordionOpen] = useState<string | null>(null);

  const handleRadioChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setAccountType(e.target.value);
    setSelectedRole(e.target.value);
    setSelectedRole1(e.target.value);
  };

  const toggleAccordion = (id: string) => {
    setAccordionOpen(accordionOpen === id ? null : id);
  };

  const toggleTab = (tab: string) => {
    if (activeTab !== tab) setActiveTab(tab);
  };

  return (
    <>
      <Card>
        <CardHeader className="card-header">
          <h5>Financial Wizard</h5>
        </CardHeader>
        <CardBody>
          <Row>
            <Col xs={12}>
              <Nav className="tabs-step">
                <NavLink
                  className={
                    activeTab === "tab-1" ? "tab current d-flex" : "tab d-flex"
                  }
                  onClick={() => toggleTab("tab-1")}
                >
                  <div className="step">
                    <IconUserCircle />
                  </div>
                  <div className="px-2">
                    <h6 className="mb-0">About</h6>
                    <span className="text-secondary">Step 1</span>
                  </div>
                </NavLink>

                <NavLink
                  className={
                    activeTab === "tab-2" ? "tab current d-flex" : "tab d-flex"
                  }
                  onClick={() => toggleTab("tab-2")}
                >
                  <div className="step">
                    <IconDisc />
                  </div>
                  <div className="px-2">
                    <h6 className="mb-0">Account</h6>
                    <span className="text-secondary">Step 2</span>
                  </div>
                </NavLink>

                <NavLink
                  className={
                    activeTab === "tab-3" ? "tab current d-flex" : "tab d-flex"
                  }
                  onClick={() => toggleTab("tab-3")}
                >
                  <div className="step">
                    <IconUserPlus />
                  </div>
                  <div className="px-2">
                    <h6 className="mb-0">Ownership</h6>
                    <span className="text-secondary">Step 3</span>
                  </div>
                </NavLink>

                <NavLink
                  className={
                    activeTab === "tab-4" ? "tab current d-flex" : "tab d-flex"
                  }
                  onClick={() => toggleTab("tab-4")}
                >
                  <div className="step">
                    <IconCurrencyDollar />
                  </div>
                  <div className="px-2">
                    <h6 className="mb-0">Financing</h6>
                    <span className="text-secondary">Step 4</span>
                  </div>
                </NavLink>

                <NavLink
                  className={
                    activeTab === "tab-5" ? "tab current d-flex" : "tab d-flex"
                  }
                  onClick={() => toggleTab("tab-5")}
                >
                  <div className="step">
                    <IconDisc />
                  </div>
                  <div className="px-2">
                    <h6 className="mb-0">Finish</h6>
                    <span className="text-secondary">Step 5</span>
                  </div>
                </NavLink>
              </Nav>
            </Col>
            <Col xs={12} className="my-3">
              <TabContent activeTab={activeTab} className="tab-contents-list">
                <TabPane tabId="tab-1">
                  <Form className="app-form">
                    <Row>
                      <Col md={6}>
                        <FormGroup>
                          <Label for="firstName">First Name</Label>
                          <Input type="text" id="firstName" name="firstName" />
                        </FormGroup>
                      </Col>

                      <Col md={6}>
                        <FormGroup>
                          <Label for="lastName">Last Name</Label>
                          <Input type="text" id="lastName" name="lastName" />
                        </FormGroup>
                      </Col>

                      <Col md={6} xl={4}>
                        <FormGroup>
                          <Label for="dob">Date of Birth</Label>
                          <Input type="date" id="dob" name="dob" />
                        </FormGroup>
                      </Col>

                      <Col md={6} xl={4}>
                        <FormGroup>
                          <Label for="email">Email Address</Label>
                          <Input
                            type="email"
                            id="email"
                            name="email"
                            placeholder="Enter Your Email"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col md={6} xl={4}>
                        <FormGroup>
                          <Label for="contact">Contact</Label>
                          <InputGroup>
                            <InputGroupText>+91</InputGroupText>
                            <Input
                              type="text"
                              id="contact"
                              name="contact"
                              placeholder="Enter Your Contact"
                              required
                            />
                          </InputGroup>
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <FormGroup>
                          <Label for="address">Address</Label>
                          <Input
                            type="textarea"
                            id="address"
                            name="address"
                            rows="3"
                            placeholder="Enter Your Address"
                          />
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>
                </TabPane>

                <TabPane tabId="tab-2">
                  <Form className="app-form">
                    <Row>
                      <Col xs={12}>
                        <h6 className="tab-heading mb-3">
                          Do you have an account?
                        </h6>
                      </Col>
                      <Col md={6} xl={4}>
                        <Card className="shadow-none">
                          <CardBody className="select-content">
                            <FormGroup check>
                              <Label check>
                                <Input
                                  type="radio"
                                  name="radio-group1"
                                  value="existing"
                                  checked={accountType === "existing"}
                                  onChange={handleRadioChange}
                                />
                                <span className="d-flex align-items-center">
                                  <span className="ms-2">
                                    <span className="fs-6">
                                      I already have an account.
                                    </span>
                                  </span>
                                </span>
                              </Label>
                            </FormGroup>
                          </CardBody>
                        </Card>
                      </Col>

                      <Col md={6} xl={4}>
                        <Card className="shadow-none">
                          <CardBody className="select-content">
                            <FormGroup check>
                              <Label check>
                                <Input
                                  type="radio"
                                  name="radio-group1"
                                  value="newbie"
                                  checked={accountType === "newbie"}
                                  onChange={handleRadioChange}
                                />
                                <span className="d-flex align-items-center">
                                  <span className="ms-2">
                                    <span className="fs-6">
                                      I&#39;m a newbie
                                    </span>
                                  </span>
                                </span>
                              </Label>
                            </FormGroup>
                          </CardBody>
                        </Card>
                      </Col>

                      <Col xs={12}>
                        <FormGroup>
                          <Label for="email">Email</Label>
                          <Input
                            type="email"
                            id="email"
                            name="email"
                            placeholder="Enter your email"
                            required
                          />
                        </FormGroup>
                      </Col>

                      {/* Password Field */}
                      <Col md={6}>
                        <FormGroup>
                          <Label for="password">Password*</Label>
                          <Input
                            type="password"
                            id="password"
                            name="password"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col md={6}>
                        <FormGroup>
                          <Label for="confirmPassword">Confirm Password*</Label>
                          <Input
                            type="password"
                            id="confirmPassword"
                            name="confirmPassword"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col xs={12}>
                        <div className="text-end">
                          <Button color="primary" type="submit">
                            Sign in
                          </Button>
                        </div>
                      </Col>
                    </Row>
                  </Form>
                </TabPane>

                <TabPane tabId="tab-3">
                  <Form className="app-form">
                    <Row>
                      <Col lg={4}>
                        <Card className="shadow-none">
                          <CardBody className="select-content">
                            <div className="position-relative">
                              <FormGroup check>
                                <Label
                                  check
                                  className="d-flex align-items-center"
                                >
                                  <Input
                                    type="radio"
                                    name="radio-group1"
                                    value="manager"
                                    checked={selectedRole === "manager"}
                                    onChange={handleRadioChange}
                                  />
                                  <span className="radiomark outline-secondary position-absolute"></span>
                                  <span className="d-flex align-items-center">
                                    <span className="ms-2">
                                      <img
                                        src="/images/form/15.png"
                                        alt="Manager"
                                      />
                                      <span className="fs-6 tab-heading">
                                        {" "}
                                        Manager
                                      </span>
                                    </span>
                                  </span>
                                </Label>
                              </FormGroup>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>

                      <Col lg={4}>
                        <Card className="shadow-none">
                          <CardBody className="select-content">
                            <div className="position-relative">
                              <FormGroup check>
                                <Label
                                  check
                                  className="d-flex align-items-center"
                                >
                                  <Input
                                    type="radio"
                                    name="radio-group1"
                                    value="employee"
                                    checked={selectedRole === "employee"}
                                    onChange={handleRadioChange}
                                  />
                                  <span className="radiomark outline-secondary position-absolute"></span>
                                  <span className="d-flex align-items-center">
                                    <span className="ms-2">
                                      <img
                                        src="/images/form/13.png"
                                        alt="Employee"
                                      />
                                      <span className="fs-6 tab-heading">
                                        {" "}
                                        Employee
                                      </span>
                                    </span>
                                  </span>
                                </Label>
                              </FormGroup>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>

                      <Col lg={4}>
                        <Card className="shadow-none">
                          <CardBody className="select-content">
                            <div className="position-relative">
                              <FormGroup check>
                                <Label
                                  check
                                  className="d-flex align-items-center"
                                >
                                  <Input
                                    type="radio"
                                    name="radio-group1"
                                    value="director"
                                    checked={selectedRole === "director"}
                                    onChange={handleRadioChange}
                                  />
                                  <span className="radiomark outline-secondary position-absolute"></span>
                                  <span className="d-flex align-items-center">
                                    <span className="ms-2">
                                      <img
                                        src="/images/form/14.png"
                                        alt="Director"
                                      />
                                      <span className="fs-6 tab-heading">
                                        {" "}
                                        Director
                                      </span>
                                    </span>
                                  </span>
                                </Label>
                              </FormGroup>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>

                      <Col md={6}>
                        <FormGroup>
                          <Label for="username">Username</Label>
                          <Input
                            type="text"
                            id="username"
                            name="username"
                            required
                          />
                        </FormGroup>
                      </Col>

                      <Col md={6}>
                        <FormGroup>
                          <Label for="password">Password*</Label>
                          <Input
                            type="password"
                            id="password"
                            name="password"
                            required
                          />
                        </FormGroup>
                      </Col>
                    </Row>

                    <Button color="primary" type="submit">
                      Submit
                    </Button>
                  </Form>
                </TabPane>

                <TabPane tabId="tab-4">
                  <div className="custome-radio-list">
                    <Row>
                      {/* Marketing Option */}
                      <Col md={6} xl={4}>
                        <Card className="shadow-none">
                          <CardBody className="address-content">
                            <FormGroup check>
                              <Label check>
                                <Input
                                  type="radio"
                                  name="radio-group1"
                                  value="marketing"
                                  checked={selectedRole1 === "marketing"}
                                  onChange={handleRadioChange}
                                />
                              </Label>
                            </FormGroup>
                            <div className="text-center">
                              <img src="/images/form/16.png" alt="Marketing" />
                              <h6 className="tab-heading">Marketing</h6>
                              <p className="text-muted">
                                Identifying a company’s target market.
                              </p>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>

                      {/* Finance Option */}
                      <Col md={6} xl={4}>
                        <Card className="shadow-none">
                          <CardBody className="address-content">
                            <FormGroup check>
                              <Label check>
                                <Input
                                  type="radio"
                                  name="radio-group1"
                                  value="finance"
                                  checked={selectedRole1 === "finance"}
                                  onChange={handleRadioChange}
                                />
                              </Label>
                            </FormGroup>
                            <div className="text-center">
                              <img src="/images/form/17.png" alt="Finance" />
                              <h6 className="tab-heading">Finance</h6>
                              <p className="text-muted">
                                Researching the business’s target market and
                                customer base.
                              </p>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>

                      {/* IT Support Option */}
                      <Col md={6} xl={4}>
                        <Card className="shadow-none">
                          <CardBody className="address-content">
                            <FormGroup check>
                              <Label check>
                                <Input
                                  type="radio"
                                  name="radio-group1"
                                  value="it-support"
                                  checked={selectedRole1 === "it-support"}
                                  onChange={handleRadioChange}
                                />
                              </Label>
                            </FormGroup>
                            <div className="text-center">
                              <img src="/images/form/18.png" alt="IT Support" />
                              <h6 className="tab-heading">IT Support</h6>
                              <p className="text-muted">
                                Creating digital and/or print content to
                                generate brand.
                              </p>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>
                    </Row>
                  </div>

                  <div
                    className="accordion accordion-flush"
                    id="accordionFlushExample"
                  >
                    <div className="accordion-item">
                      <h6 className="accordion-header">
                        <button
                          className={`accordion-button ${accordionOpen === "flush-collapseOne" ? "" : "collapsed"}`}
                          type="button"
                          onClick={() => toggleAccordion("flush-collapseOne")}
                        >
                          Buy Inventory
                        </button>
                      </h6>
                      <div
                        id="flush-collapseOne"
                        className={`accordion-collapse collapse ${accordionOpen === "flush-collapseOne" ? "show" : ""}`}
                      >
                        <div className="accordion-body">
                          <div className="custome-radio-list">
                            <div className="row">
                              <div className="col-md-6 col-xl-4">
                                <div className="card shadow-none">
                                  <div className="card-body address-content">
                                    <div className="">
                                      <label className="check-box">
                                        <input
                                          type="radio"
                                          name="radio-group1"
                                        />
                                        <span className="radiomark outline-secondary"></span>
                                      </label>
                                    </div>
                                    <div className="text-center">
                                      <img
                                        src="/images/form/yes.png"
                                        alt="image"
                                      />
                                      <p>YES</p>
                                      <p className="text-muted">
                                        Lorem, ipsum dolor sit amet consectetur
                                        adipisicing elit.
                                      </p>
                                    </div>
                                  </div>
                                </div>
                              </div>
                              <div className="col-md-6 col-xl-4">
                                <div className="card shadow-none">
                                  <div className="card-body address-content">
                                    <div className="">
                                      <label className="check-box">
                                        <input
                                          type="radio"
                                          name="radio-group1"
                                        />
                                        <span className="radiomark outline-secondary"></span>
                                      </label>
                                    </div>
                                    <div className="text-center">
                                      <img
                                        src="/images/form/no.png"
                                        alt="image"
                                      />
                                      <p>NO</p>
                                      <p className="text-muted">
                                        Lorem, ipsum dolor sit amet consectetur
                                        adipisicing elit.
                                      </p>
                                    </div>
                                  </div>
                                </div>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                    </div>
                    <div className="accordion-item">
                      <h6 className="accordion-header">
                        <button
                          className={`accordion-button ${accordionOpen === "flush-collapseTwo" ? "" : "collapsed"}`}
                          type="button"
                          onClick={() => toggleAccordion("flush-collapseTwo")}
                        >
                          Do you have existing business financing?
                        </button>
                      </h6>
                      <div
                        id="flush-collapseTwo"
                        className={`accordion-collapse collapse ${accordionOpen === "flush-collapseTwo" ? "show" : ""}`}
                      >
                        <div className="accordion-body">
                          <div className="custome-radio-list">
                            <div className="row">
                              <div className="col-md-6 col-xl-4">
                                <div className="card shadow-none">
                                  <div className="card-body address-content">
                                    <div className="">
                                      <label className="check-box">
                                        <input
                                          type="radio"
                                          name="radio-group1"
                                        />
                                        <span className="radiomark outline-secondary"></span>
                                      </label>
                                    </div>
                                    <div className="text-center">
                                      <img
                                        src="/images/form/yes.png"
                                        alt="image"
                                      />
                                      <p>YES</p>
                                      <p className="text-muted">
                                        Lorem, ipsum dolor sit amet consectetur
                                        adipisicing elit.
                                      </p>
                                    </div>
                                  </div>
                                </div>
                              </div>
                              <div className="col-md-6 col-xl-4">
                                <div className="card shadow-none">
                                  <div className="card-body address-content">
                                    <div className="">
                                      <label className="check-box">
                                        <input
                                          type="radio"
                                          name="radio-group1"
                                        />
                                        <span className="radiomark outline-secondary"></span>
                                      </label>
                                    </div>
                                    <div className="text-center">
                                      <img
                                        src="/images/form/no.png"
                                        alt="image"
                                      />
                                      <p>NO</p>
                                      <p className="text-muted">
                                        Lorem, ipsum dolor sit amet consectetur
                                        adipisicing elit.
                                      </p>
                                    </div>
                                  </div>
                                </div>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                    </div>
                    <div className="accordion-item">
                      <h6 className="accordion-header">
                        <button
                          className={`accordion-button ${accordionOpen === "flush-collapseThree" ? "" : "collapsed"}`}
                          type="button"
                          onClick={() => toggleAccordion("flush-collapseThree")}
                        >
                          Existing Balance
                        </button>
                      </h6>
                      <div
                        id="flush-collapseThree"
                        className={`accordion-collapse collapse ${accordionOpen === "flush-collapseThree" ? "show" : ""}`}
                      >
                        <div className="accordion-body">
                          <form className="app-form">
                            <div className="mb-3 row">
                              <label className="col-sm-2 col-form-label">
                                Business
                              </label>
                              <div className="col-sm-10">
                                <input type="text" className="form-control" />
                              </div>
                            </div>
                            <div className="mb-3 row">
                              <label className="col-sm-2 col-form-label">
                                Current Balance
                              </label>
                              <div className="col-sm-10">
                                <div className="input-group">
                                  <span className="input-group-text text-light-info">
                                    <i className="fa-solid fa-credit-card"></i>
                                  </span>
                                  <input
                                    className="form-control"
                                    type="text"
                                    placeholder=""
                                  />
                                </div>
                              </div>
                            </div>
                            <button type="submit" className="btn btn-primary">
                              Submit
                            </button>
                          </form>
                        </div>
                      </div>
                    </div>
                  </div>
                </TabPane>

                <TabPane tabId="tab-5">
                  <div className="completed-contents">
                    <div className="completbox text-center">
                      <img src="/images/form/done.png" alt="image" />
                      <h6 className="mb-0">Thank You !</h6>
                      <p> your booking is completed</p>
                    </div>
                  </div>
                </TabPane>
              </TabContent>
            </Col>
          </Row>
        </CardBody>
      </Card>
    </>
  );
};

export default Financial;
