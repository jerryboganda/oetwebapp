import React, { useState } from "react";
import {
  Row,
  Col,
  Card,
  CardBody,
  Form,
  FormGroup,
  Input,
  Label,
  CardHeader,
} from "reactstrap";
import {
  IconCalendarStats,
  IconCheckbox,
  IconNotebook,
  IconSettingsFilled,
} from "@tabler/icons-react";

const Booking: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>("tabs-1");
  const [openItem, setOpenItem] = useState<string | null>("one");

  const toggleAccordion = (id: string) => {
    setOpenItem((prev) => (prev === id ? null : id));
  };

  const toggleTab = (tab: string): void => {
    setActiveTab(tab);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Booking Wizard</h5>
        </CardHeader>
        <CardBody>
          <Row>
            {/* Sidebar with steps */}
            <Col lg="4" className="mb-3">
              <div className="vertical-tabs">
                {["tabs-1", "tabs-2", "tabs-3", "tabs-4"].map((tab, index) => (
                  <div
                    key={tab}
                    className={`tab ${activeTab === tab ? "current-tab" : ""}`}
                    onClick={() => toggleTab(tab)}
                  >
                    <div className="d-flex">
                      <div className="step ms-2">
                        {tab === "tabs-1" && <IconSettingsFilled />}
                        {tab === "tabs-2" && <IconCalendarStats />}
                        {tab === "tabs-3" && <IconNotebook />}
                        {tab === "tabs-4" && <IconCheckbox />}
                      </div>
                      <div className="ps-3">
                        <h5>
                          {tab === "tabs-1"
                            ? "Service"
                            : tab === "tabs-2"
                              ? "Date & Time"
                              : tab === "tabs-3"
                                ? "Booking Summary"
                                : "Completed"}
                        </h5>
                        <span className="text-secondary">Step {index + 1}</span>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </Col>

            {/* Tab Contents */}
            <Col lg="8">
              <div className="tab-contents-list">
                {/* Tab 1 */}
                {activeTab === "tabs-1" && (
                  <Form className="app-form">
                    <Row>
                      <Col md="6">
                        <FormGroup>
                          <Label>Company Name</Label>
                          <Input type="text" className="form-control" />
                        </FormGroup>
                      </Col>
                      <Col md="6">
                        <FormGroup>
                          <Label>Service</Label>
                          <Input type="text" className="form-control" />
                        </FormGroup>
                      </Col>
                      <Col md="12">
                        <FormGroup>
                          <Label>Company Address</Label>
                          <Input
                            type="textarea"
                            className="form-control"
                            rows={2}
                          />
                        </FormGroup>
                      </Col>
                      <Col md="6">
                        <FormGroup>
                          <Label>Additional Persons</Label>
                          <Input
                            type="number"
                            defaultValue="2"
                            className="form-control"
                          />
                        </FormGroup>
                      </Col>
                      <Col md="6">
                        <FormGroup>
                          <Label>Email</Label>
                          <Input
                            type="text"
                            className="form-control-plaintext"
                            value="email@gmail.com"
                            readOnly
                          />
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>
                )}

                {/* Tab 2 */}
                {activeTab === "tabs-2" && (
                  <Form className="app-form">
                    <Row>
                      <Col md="6" xl="4">
                        <Card className="shadow-none">
                          <CardBody>
                            <FormGroup check>
                              <Label check>
                                <Input type="radio" name="radio-group1" />
                              </Label>
                            </FormGroup>
                            <div className="text-center">
                              <img src="/images/form/19.png" alt="Location 1" />
                              <h6 className="tab-heading">Location 1</h6>
                              <p className="text-muted">
                                A Global Positioning System, or GPS, satellites
                                orbiting Earth.
                              </p>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>

                      <Col md="6" xl="4">
                        <Card className="shadow-none">
                          <CardBody>
                            <FormGroup check>
                              <Label check>
                                <Input type="radio" name="radio-group1" />
                              </Label>
                            </FormGroup>
                            <div className="text-center">
                              <img src="/images/form/20.png" alt="Location 2" />
                              <h6 className="tab-heading">Location 2</h6>
                              <p className="text-muted">
                                Traditionally, those are the three important
                                factors in buying.
                              </p>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>

                      <Col md="6">
                        <FormGroup>
                          <Label>Date</Label>
                          <Input
                            type="datetime-local"
                            className="form-control"
                          />
                        </FormGroup>
                      </Col>
                      <Col md="6">
                        <FormGroup>
                          <Label>Time</Label>
                          <Input
                            type="text"
                            className="form-control"
                            placeholder="10:00"
                            readOnly
                          />
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>
                )}

                {/* Tab 3 - Accordion */}
                {activeTab === "tabs-3" && (
                  <div className="accordion app-accordion accordion-light-primary">
                    {/* Company Details */}
                    <div className="accordion-item">
                      <h6 className="accordion-header">
                        <button
                          type="button"
                          aria-expanded={openItem === "one"}
                          className={`accordion-button ${
                            openItem !== "one" ? "collapsed" : ""
                          }`}
                          onClick={() => toggleAccordion("one")}
                        >
                          Company Details
                        </button>
                      </h6>
                      <div
                        className={`accordion-collapse collapse ${
                          openItem === "one" ? "show" : ""
                        }`}
                      >
                        <div className="accordion-body">
                          <p className="fw-bold">
                            <i className="ti ti-building-skyscraper"></i> AR
                            info
                          </p>
                          <address>
                            120 Silver Point , <br /> Myriam Estate South
                            Carolina, <br /> india
                          </address>
                          <p>Zip: 456730</p>
                          <p>Service : Application</p>
                          <p>Email : ar12@gmail.com</p>
                          <p>+91 6926578398</p>
                        </div>
                      </div>
                    </div>

                    {/* Meeting Time Details */}
                    <div className="accordion-item">
                      <h6 className="accordion-header">
                        <button
                          type="button"
                          aria-expanded={openItem === "two"}
                          className={`accordion-button ${
                            openItem !== "two" ? "collapsed" : ""
                          }`}
                          onClick={() => toggleAccordion("two")}
                        >
                          Meeting Time Details
                        </button>
                      </h6>
                      <div
                        className={`accordion-collapse collapse ${
                          openItem === "two" ? "show" : ""
                        }`}
                      >
                        <div className="accordion-body">
                          <p>
                            <i className="ti ti-calendar-minus me-2"></i>
                            2024-10-1
                          </p>
                          <p>
                            <i className="ti ti-clock-hour-1 me-2"></i>10:00 am
                          </p>
                          <p>
                            <i className="ti ti-map-pin me-2"></i>Location
                            1-(office)
                          </p>
                        </div>
                      </div>
                    </div>
                  </div>
                )}

                {/* Tab 4 */}
                {activeTab === "tabs-4" && (
                  <div className="completed-contents">
                    <div className="completbox text-center">
                      <img src="/images/form/done.png" alt="Completed" />
                      <h6>Thank You!</h6>
                      <p>Your booking is completed.</p>
                    </div>
                  </div>
                )}
              </div>
            </Col>
          </Row>
        </CardBody>
      </Card>
    </>
  );
};

export default Booking;
