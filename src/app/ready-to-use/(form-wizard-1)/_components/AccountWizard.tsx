import React, { useState } from "react";
import {
  Button,
  Form,
  FormGroup,
  Input,
  Label,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  Row,
  Col,
  Card,
  CardBody,
  CardHeader,
} from "reactstrap";
import {
  IconChecks,
  IconFileCheck,
  IconFileDollar,
  IconUserCircle,
} from "@tabler/icons-react";

const AccountWizard: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>("1");

  const toggleTab = (tab: string): void => {
    if (activeTab !== tab) setActiveTab(tab);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Create Account Wizard</h5>
        </CardHeader>
        <CardBody className="card-body">
          <Nav
            pills
            className="nav custom-navstpes d-flex"
            id="justify-about-tab"
            role="tablist"
          >
            <NavItem className="flex-grow-1 text-center">
              <NavLink
                className={`btn nav-link ${activeTab === "1" ? "active" : ""}`}
                onClick={() => toggleTab("1")}
                role="tab"
                aria-controls="account-tab-pane"
                aria-selected={activeTab === "1"}
              >
                <span className="d-flex align-items-center">
                  <span>
                    <IconUserCircle className="w-35 h-35" />
                  </span>
                  <span className="text-start ms-3 custom-title d-flex flex-column">
                    <span className="f-w-500 f-s-16">Personal info</span>
                    <span className="f-s-14 text-secondary">
                      Enter step 1 details
                    </span>
                  </span>
                </span>
              </NavLink>
            </NavItem>

            <NavItem className="flex-grow-1 text-center">
              <NavLink
                className={`btn nav-link ${activeTab === "2" ? "active" : ""}`}
                onClick={() => toggleTab("2")}
                role="tab"
                aria-controls="profile-tab-pane"
                aria-selected={activeTab === "2"}
              >
                <span className="d-flex align-items-center">
                  <span>
                    <IconFileDollar className="w-35 h-35" />
                  </span>
                  <span className="text-start ms-3 custom-title d-flex flex-column">
                    <span className="f-w-500 f-s-16">Billing Information</span>
                    <span className="f-s-14 text-secondary">
                      Enter step 2 details
                    </span>
                  </span>
                </span>
              </NavLink>
            </NavItem>

            <NavItem className="flex-grow-1 text-center">
              <NavLink
                className={`btn nav-link ${activeTab === "3" ? "active" : ""}`}
                onClick={() => toggleTab("3")}
                role="tab"
                aria-controls="review-tab-pane"
                aria-selected={activeTab === "3"}
              >
                <span className="d-flex align-items-center">
                  <span>
                    <IconFileCheck className="w-35 h-35" />
                  </span>
                  <span className="text-start ms-3 custom-title d-flex flex-column">
                    <span className="f-w-500 f-s-16">Review Order</span>
                    <span className="f-s-14 text-secondary">
                      Enter step 3 details
                    </span>
                  </span>
                </span>
              </NavLink>
            </NavItem>

            <NavItem className="flex-grow-1 text-center">
              <NavLink
                className={`btn nav-link ${activeTab === "4" ? "active" : ""}`}
                onClick={() => toggleTab("4")}
                role="tab"
                aria-controls="finish-tab-pane"
                aria-selected={activeTab === "4"}
              >
                <span className="d-flex align-items-center">
                  <span>
                    <IconChecks className="w-35 h-35" />
                  </span>
                  <span className="text-start ms-3 custom-title d-flex flex-column">
                    <span className="f-w-500 f-s-16">Order Confirmation</span>
                    <span className="f-s-14 text-secondary">
                      Enter step 4 details
                    </span>
                  </span>
                </span>
              </NavLink>
            </NavItem>
          </Nav>

          {/* Tab Content */}
          <TabContent activeTab={activeTab} className="mt-3">
            <TabPane tabId="1">
              <Form className="app-form">
                <Row>
                  <Col md={6}>
                    <FormGroup>
                      <Label>Cardholder Name</Label>
                      <Input type="text" placeholder="Enter Cardholder Name" />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label>Last Name</Label>
                      <Input type="text" placeholder="Enter Last Name" />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label>Phone Number</Label>
                      <Input type="text" placeholder="Phone Number" />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label>Email Address</Label>
                      <Input type="email" placeholder="Enter Email" />
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label>Country</Label>
                      <Input type="select">
                        <option>Select Country</option>
                        <option>UK</option>
                        <option>US</option>
                        <option>Italy</option>
                      </Input>
                    </FormGroup>
                  </Col>
                  <Col md={6}>
                    <FormGroup>
                      <Label>Language</Label>
                      <Input type="select">
                        <option>Select Language</option>
                        <option>English</option>
                        <option>Italian</option>
                        <option>Spanish</option>
                      </Input>
                    </FormGroup>
                  </Col>
                </Row>
                <div className="text-end">
                  <Button color="primary" onClick={() => toggleTab("2")}>
                    Next
                  </Button>
                </div>
              </Form>
            </TabPane>

            <TabPane tabId="2">
              <TabPane tabId="2">
                <Form>
                  <Row>
                    <Col md="6" className="mb-3">
                      <FormGroup>
                        <Label for="username3">Username</Label>
                        <Input
                          type="text"
                          id="username3"
                          placeholder="Enter Your Username"
                        />
                      </FormGroup>
                    </Col>
                    <Col md="6" className="mb-3">
                      <FormGroup>
                        <Label for="username4">Credit/Debit Card Number</Label>
                        <Input
                          type="text"
                          id="username4"
                          placeholder="Enter Your Card Number"
                        />
                      </FormGroup>
                    </Col>
                    <Col md="6" className="mb-3">
                      <FormGroup>
                        <Label for="username5">ZIP Code</Label>
                        <Input
                          type="text"
                          id="username5"
                          placeholder="ZIP Code"
                        />
                      </FormGroup>
                    </Col>
                  </Row>
                  <div className="text-end">
                    <Button color="primary" size="lg" className="me-2">
                      Previous
                    </Button>
                    <Button color="primary" size="lg">
                      Next
                    </Button>
                  </div>
                </Form>
              </TabPane>
            </TabPane>

            <TabPane tabId="3">
              <TabPane tabId="3">
                <Form>
                  <Row>
                    <Col md="6" className="mb-3">
                      <FormGroup>
                        <Label for="username6">Product Name</Label>
                        <Input
                          type="text"
                          id="username6"
                          placeholder="Enter Product Name"
                        />
                      </FormGroup>
                    </Col>
                    <Col md="6" className="mb-3">
                      <FormGroup>
                        <Label for="username7">Quantity</Label>
                        <Input
                          type="text"
                          id="username7"
                          placeholder="Quantity"
                        />
                      </FormGroup>
                    </Col>
                    <Col md="6" className="mb-3">
                      <FormGroup>
                        <Label for="username8">Price per Unit</Label>
                        <Input
                          type="text"
                          id="username8"
                          placeholder="Enter Price per Unit"
                        />
                      </FormGroup>
                    </Col>
                    <Col md="6" className="mb-3">
                      <FormGroup>
                        <Label for="username10">Billing Address</Label>
                        <Input
                          type="text"
                          id="username10"
                          placeholder="Enter Billing Address"
                        />
                      </FormGroup>
                    </Col>
                  </Row>
                  <div className="text-end">
                    <Button color="primary" size="lg" className="me-2">
                      Previous
                    </Button>
                    <Button color="primary" size="lg">
                      Next
                    </Button>
                  </div>
                </Form>
              </TabPane>
            </TabPane>

            <TabPane tabId="4">
              <div className="completed-contents">
                <div className="completbox text-center">
                  <img src="/images/form/done.png" alt="Completion" />
                  <h6 className="mb-0">Thank You!</h6>
                  <p className="mb-0">Your booking is completed.</p>
                </div>
              </div>
            </TabPane>
          </TabContent>
        </CardBody>
      </Card>
    </>
  );
};

export default AccountWizard;
