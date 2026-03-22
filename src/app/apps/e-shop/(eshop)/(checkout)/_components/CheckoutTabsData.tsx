import React, { FC, useState } from "react";
import {
  Form,
  FormGroup,
  Label,
  Input,
  Button,
  Row,
  Col,
  Card,
  CardBody,
  Modal,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Accordion,
  AccordionItem,
  AccordionBody,
  AccordionHeader,
} from "reactstrap";
import { IconInfoCircle, IconPlus } from "@tabler/icons-react";
import Link from "next/link";

interface CheckoutTabsDataPops {
  currentStep: number;
}

const CheckoutTabsData: FC<CheckoutTabsDataPops> = ({ currentStep }) => {
  const [accordionOpen, setAccordionOpen] = useState("1");
  const [selectedAddress, setSelectedAddress] = useState("home");
  const [selectedDelivery, setSelectedDelivery] = useState("economy");

  const [modalOpen, setModalOpen] = useState(false);
  const toggleModal = () => setModalOpen(!modalOpen);

  const [selectedPayment, setSelectedPayment] = useState("card");
  const handlePaymentSelect = (method: string) => setSelectedPayment(method);
  const toggleAccordion = (id: string) => {
    setAccordionOpen(accordionOpen === id ? "" : id);
  };

  return (
    <>
      <div className="mt-4">
        {currentStep === 1 && (
          <div>
            <Form className="app-form">
              <Row>
                <Col md="6">
                  <FormGroup>
                    <Label for="firstName">First Name</Label>
                    <Input type="text" name="firstName" id="firstName" />
                  </FormGroup>
                </Col>
                <Col md="6">
                  <FormGroup>
                    <Label for="lastName">Last Name</Label>
                    <Input type="text" name="lastName" id="lastName" />
                  </FormGroup>
                </Col>
                <Col xs="12">
                  <FormGroup>
                    <Label for="email">Email address</Label>
                    <Input type="email" name="email" id="email" />
                    <small>
                      We&#39;ll never share your email with anyone else.
                    </small>
                  </FormGroup>
                </Col>
                <Col md="6">
                  <FormGroup>
                    <Label for="contact">Contact</Label>
                    <div className="input-group">
                      <Input
                        type="text"
                        name="contact"
                        id="contact"
                        aria-label="Recipient's username"
                      />
                      <span className="input-group-text">
                        <i className="fa-solid fa-phone"></i>
                      </span>
                    </div>
                  </FormGroup>
                </Col>
                <Col md="6">
                  <FormGroup>
                    <Label for="telephone">Telephone</Label>
                    <Input
                      type="tel"
                      name="telephone"
                      id="telephone"
                      pattern="[0-9]{3}-[0-9]{2}-[0-9]{3}"
                    />
                  </FormGroup>
                </Col>
                <Col md="8">
                  <FormGroup>
                    <Label for="city">City</Label>
                    <Input type="text" name="city" id="city" />
                  </FormGroup>
                </Col>
                <Col md="4">
                  <FormGroup>
                    <Label for="state">State</Label>
                    <Input type="select" name="state" id="state">
                      <option>Choose...</option>
                      <option>...</option>
                    </Input>
                  </FormGroup>
                </Col>
                <Col xs="12">
                  <FormGroup check>
                    <Input
                      type="checkbox"
                      id="rememberMe"
                      label="Remember me"
                      name="rememberMe"
                    />
                    <Label className="form-check-label">remember me</Label>
                  </FormGroup>
                </Col>
                <Col xs="12">
                  <div className="text-end">
                    <Button type="submit" color="primary" className="rounded">
                      Submit
                    </Button>
                  </div>
                </Col>
              </Row>
            </Form>
          </div>
        )}

        {currentStep === 2 && (
          <div>
            <Row>
              {/* Address Selection */}
              <Col md={12}>
                <h5 className="tab-heading mb-3">Your Address</h5>
              </Col>
              <Col md={6}>
                <Card className="shadow-none">
                  <CardBody className="address-content">
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="radio"
                          name="address"
                          checked={selectedAddress === "home"}
                          onChange={() => setSelectedAddress("home")}
                        />
                        <span className="fs-6 tab-heading ms-2">
                          Home Address
                        </span>
                      </Label>
                    </FormGroup>
                    <p className="text-muted">
                      260 Zulma Stravenue, Wisozkton, KY 44193-0738
                      <br />
                      collins@stroman.net
                    </p>
                    <p>+1 828-726-3669</p>
                  </CardBody>
                </Card>
              </Col>
              <Col md={6}>
                <Card className="shadow-none">
                  <CardBody className="address-content">
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="radio"
                          name="address"
                          checked={selectedAddress === "office"}
                          onChange={() => setSelectedAddress("office")}
                        />
                        <span className="fs-6 tab-heading ms-2">
                          Office Address
                        </span>
                      </Label>
                    </FormGroup>
                    <p className="text-muted">
                      90897 Franecki Haven, West Maximo, KY 40770-5993
                      <br />
                      bjast@hotmail.com
                    </p>
                    <p>+1 863-422-2979</p>
                  </CardBody>
                </Card>
              </Col>
              <Col md={6}>
                <Card className="shadow-none">
                  <CardBody className="added-content">
                    <a
                      role="button"
                      className="link-primary"
                      onClick={toggleModal}
                    >
                      <IconPlus size={18} /> Add A New Address
                    </a>
                  </CardBody>
                </Card>
              </Col>

              {/* Modal for Adding a New Address */}
              <Modal isOpen={modalOpen} toggle={toggleModal}>
                <ModalHeader toggle={toggleModal}>Add New Address</ModalHeader>
                <ModalBody>
                  <Form className="app-form">
                    <Row form>
                      <Col md={12}>
                        <FormGroup>
                          <Label for="address1">Address</Label>
                          <Input
                            type="text"
                            id="address1"
                            placeholder="Enter address"
                          />
                        </FormGroup>
                      </Col>
                      <Col md={12}>
                        <FormGroup>
                          <Label for="address2">Address 2</Label>
                          <Input
                            type="text"
                            id="address2"
                            placeholder="Enter additional address info"
                          />
                        </FormGroup>
                      </Col>
                      <Col md={6}>
                        <FormGroup>
                          <Label for="city">City</Label>
                          <Input
                            type="text"
                            id="city"
                            placeholder="Enter city"
                          />
                        </FormGroup>
                      </Col>
                      <Col md={4}>
                        <FormGroup>
                          <Label for="state">State</Label>
                          <Input type="select" id="state">
                            <option>Choose...</option>
                            <option>NY</option>
                            <option>CA</option>
                          </Input>
                        </FormGroup>
                      </Col>
                      <Col md={2}>
                        <FormGroup>
                          <Label for="zip">Zip</Label>
                          <Input
                            type="text"
                            id="zip"
                            placeholder="Enter zip code"
                          />
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>
                </ModalBody>
                <ModalFooter>
                  <Button color="secondary" onClick={toggleModal}>
                    Close
                  </Button>
                  <Button color="primary">Add</Button>
                </ModalFooter>
              </Modal>

              {/* Delivery Method Selection */}
              <Col md={12}>
                <h5 className="fs-6 tab-heading mb-3">Delivery Method</h5>
              </Col>
              <Col md={6}>
                <Card className="shadow-none">
                  <CardBody className="select-content">
                    <FormGroup check>
                      <Label check className="d-flex align-items-center gap-3">
                        <Input
                          type="radio"
                          name="delivery"
                          checked={selectedDelivery === "economy"}
                          onChange={() => setSelectedDelivery("economy")}
                        />
                        <span className="d-flex align-items-center">
                          <span className="ms-2">
                            <span className="fs-6 tab-heading">
                              Economy Shipping - FREE
                            </span>
                            <span className="d-block text-secondary">
                              5 to 10 business days
                            </span>
                          </span>
                        </span>
                      </Label>
                    </FormGroup>
                  </CardBody>
                </Card>
              </Col>
              <Col md={6}>
                <Card className="shadow-none">
                  <CardBody className="select-content">
                    <FormGroup check>
                      <Label check className="d-flex align-items-center gap-3">
                        <Input
                          type="radio"
                          name="delivery"
                          checked={selectedDelivery === "priority"}
                          onChange={() => setSelectedDelivery("priority")}
                        />

                        <span className="d-flex align-items-center">
                          <span className="ms-2">
                            <span className="fs-6 tab-heading">
                              Priority overnight -$18.50
                            </span>
                            <span className="d-block text-secondary">
                              Based on chosen delivery date
                            </span>
                          </span>
                        </span>
                      </Label>
                    </FormGroup>
                  </CardBody>
                </Card>
              </Col>
              <Col md={12}>
                <div className="alert alert-border-info" role="alert">
                  <p>
                    <IconInfoCircle size={18} className="text-info" />
                    Expected delivery data is not guaranteed, if you are
                    ordering for an event we suggest allowing 1 extra days.
                  </p>
                  <div className="text-end">
                    <Link href="#" className="link-primary text-d-underline">
                      Don&#39;t allow
                    </Link>
                    <a href="#" className="link-primary text-d-underline ms-2">
                      Allow
                    </a>
                  </div>
                </div>
              </Col>
            </Row>
          </div>
        )}

        {currentStep === 3 && (
          <div>
            <Row>
              <Col md={12}>
                <Card className="shadow-none">
                  <CardBody className="select-content">
                    <FormGroup check className="mb-3">
                      <Label check>
                        <Input
                          type="radio"
                          name="payment"
                          checked={selectedPayment === "card"}
                          onChange={() => handlePaymentSelect("card")}
                        />
                        <span className="fs-6 tab-heading">
                          Credit / Debit Card
                        </span>
                      </Label>
                    </FormGroup>
                    {selectedPayment === "card" && (
                      <Form className="app-form">
                        <Row>
                          <Col md={12}>
                            <FormGroup>
                              <Label for="cardName">Cardholder Name</Label>
                              <Input
                                type="text"
                                id="cardName"
                                placeholder="Olaf"
                                required
                              />
                            </FormGroup>
                          </Col>
                          <Col md={12}>
                            <FormGroup>
                              <Label for="cardNumber">Card Number</Label>
                              <Input
                                type="text"
                                id="cardNumber"
                                placeholder="xxxx-xxxx-xxxx-xxxx"
                                required
                              />
                            </FormGroup>
                          </Col>
                          <Col md={6}>
                            <FormGroup>
                              <Label for="expirationDate">
                                Expiration Date
                              </Label>
                              <Input
                                type="text"
                                id="expirationDate"
                                placeholder="MM/YY"
                                required
                              />
                            </FormGroup>
                          </Col>
                          <Col md={6}>
                            <FormGroup>
                              <Label for="cvc">CVC code</Label>
                              <Input
                                type="text"
                                id="cvc"
                                placeholder="xxx"
                                required
                              />
                            </FormGroup>
                          </Col>
                          <Col md={12} className="text-end">
                            <Button color="primary">Submit</Button>
                          </Col>
                        </Row>
                      </Form>
                    )}
                  </CardBody>
                </Card>
              </Col>

              <Col md={6}>
                <Card className="shadow-none">
                  <CardBody className="select-content">
                    <FormGroup check className="position-relative">
                      <Label check className="d-flex align-items-center">
                        <Input
                          type="radio"
                          name="payment"
                          checked={selectedPayment === "visa"}
                          onChange={() => handlePaymentSelect("visa")}
                        />
                        <span className="d-flex align-items-center ms-2">
                          <img
                            src="/images/checkbox-radio/logo1.png"
                            className="w-30 h-30"
                            alt="Visa"
                          />
                          <span className="ms-2">
                            <span className="fs-6 tab-heading">Visa Card</span>
                            <span className="d-block text-secondary">
                              Select Visa
                            </span>
                          </span>
                        </span>
                      </Label>
                    </FormGroup>
                  </CardBody>
                </Card>
              </Col>

              <Col md={6}>
                <Card className="shadow-none">
                  <CardBody className="select-content">
                    <FormGroup check className="position-relative">
                      <Label check className="d-flex align-items-center">
                        <Input
                          type="radio"
                          name="payment"
                          checked={selectedPayment === "paypal"}
                          onChange={() => handlePaymentSelect("paypal")}
                        />
                        <span className="d-flex align-items-center ms-2">
                          <img
                            src="/images/checkbox-radio/logo3.png"
                            className="w-30 h-30"
                            alt="PayPal"
                          />
                          <span className="ms-2">
                            <span className="fs-6 tab-heading">Paypal</span>
                            <span className="d-block text-secondary">
                              Select PayPal
                            </span>
                          </span>
                        </span>
                      </Label>
                    </FormGroup>
                  </CardBody>
                </Card>
              </Col>

              <Col md={12}>
                <Card className="shadow-none">
                  <CardBody className="select-content">
                    <FormGroup check className="position-relative">
                      <Label check className="d-flex align-items-center">
                        <Input
                          type="radio"
                          name="payment"
                          checked={selectedPayment === "upi"}
                          onChange={() => handlePaymentSelect("upi")}
                        />
                        <span className="d-flex align-items-center">
                          <span className="ms-2">
                            <span className="fs-6 tab-heading">
                              Other UPI Apps
                            </span>
                            <span className="d-block text-secondary">
                              Select Other UPI Apps for payment
                            </span>
                          </span>
                        </span>
                      </Label>
                    </FormGroup>
                  </CardBody>
                </Card>
              </Col>

              <Col md={12}>
                <Card className="shadow-none">
                  <CardBody className="select-content">
                    <FormGroup check className="position-relative">
                      <Label check className="d-flex align-items-center">
                        <Input
                          type="radio"
                          name="payment"
                          checked={selectedPayment === "cod"}
                          onChange={() => handlePaymentSelect("cod")}
                        />
                        <span className="d-flex align-items-center">
                          <span className="ms-2">
                            <span className="fs-6 tab-heading">
                              Cash on Delivery/Pay on Delivery
                            </span>
                            <span className="d-block text-secondary">
                              Cash,UPI and Cards accepted.
                            </span>
                          </span>
                        </span>
                      </Label>
                    </FormGroup>
                  </CardBody>
                </Card>
              </Col>
            </Row>
          </div>
        )}

        {currentStep === 4 && (
          <div>
            <Row>
              <Col xs={6} sm={2} className="mb-3">
                <Card className="bg-light-dark b-r-10">
                  <img
                    src="/images/ecommerce/23.jpg"
                    alt="product-img"
                    className="img-fluid bg-light-dark b-r-10"
                  />
                </Card>
              </Col>
              <Col xs={6} sm={2} className="mb-3">
                <Card className="bg-light-dark b-r-10">
                  <img
                    src="/images/ecommerce/25.jpg"
                    alt="product-img"
                    className="img-fluid bg-light-dark b-r-10"
                  />
                </Card>
              </Col>
              <Col xs={6} sm={2} className="mb-3 mt-sm-0">
                <Card className="bg-light-dark b-r-10">
                  <img
                    src="/images/ecommerce/13.jpg"
                    alt="product-img"
                    className="img-fluid bg-light-dark b-r-10"
                  />
                </Card>
              </Col>

              <Col md={12}>
                <div className="mb-3">
                  <h6 className="tab-heading mb-0">
                    Estimated Delivery Date: 27 Dec 2024
                  </h6>
                  <p className="text-secondary">Items dispatched by MQ Store</p>
                </div>
              </Col>

              <Col xs={12}>
                <div>
                  <h6 className="tab-heading">Product details</h6>
                  <Accordion
                    flush
                    id="nestingExample"
                    open={accordionOpen}
                    toggle={toggleAccordion}
                  >
                    <AccordionItem>
                      <AccordionHeader targetId="1">
                        Tom &amp; Jerry Printed T-Shirt
                      </AccordionHeader>
                      <AccordionBody accordionId="1">
                        <p>
                          <span className="text-secondary f-w-500">size</span> :
                          medium
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">color</span>{" "}
                          : Green
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">Items</span>{" "}
                          : 2
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">Rating</span>{" "}
                          : 3+
                        </p>
                      </AccordionBody>
                    </AccordionItem>

                    <AccordionItem>
                      <AccordionHeader targetId="2">
                        Swatch Watch Irony
                      </AccordionHeader>
                      <AccordionBody accordionId="2">
                        <p>
                          <span className="text-secondary f-w-500">color</span>{" "}
                          : Dark
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">Items</span>{" "}
                          : 2
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">Rating</span>{" "}
                          : 4+
                        </p>
                      </AccordionBody>
                    </AccordionItem>

                    <AccordionItem>
                      <AccordionHeader targetId="3">
                        Nike Air Max 1
                      </AccordionHeader>
                      <AccordionBody accordionId="3">
                        <p>
                          <span className="text-secondary f-w-500">size</span> :
                          large
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">color</span>{" "}
                          : White
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">Items</span>{" "}
                          : 1
                        </p>
                        <p>
                          <span className="text-secondary f-w-500">Rating</span>{" "}
                          : 5+
                        </p>
                      </AccordionBody>
                    </AccordionItem>
                  </Accordion>
                </div>
              </Col>
            </Row>
          </div>
        )}

        {currentStep === 5 && (
          <div>
            <Row>
              <Col xs={12}>
                <div className="text-center">
                  <div className="mb-3">
                    <img src="/images/form/done.gif" alt="form" />
                  </div>
                  <h5 className="text-success">
                    {" "}
                    Your Order #7AD6728 Placed, Thank you!
                  </h5>
                  <p>Confirmation will be sent to Your Email</p>
                </div>
                <div className="app-divider-v dashed"></div>
              </Col>
              <Col xs={12}>
                <Row className="mb-3">
                  <Col md={12}>
                    <h6 className="tab-heading">
                      In the unlikely case of items being unavailable, what
                      would you prefer?
                    </h6>
                  </Col>

                  {/* Option 1 */}
                  <Col md={6} xl={4} className="mt-4">
                    <Card className="shadow-none">
                      <CardBody className="address-content">
                        <FormGroup check>
                          <Label check>
                            <Input type="radio" name="preference" />
                            <span className="fs-6 tab-heading ms-2">
                              Select 1
                            </span>
                          </Label>
                        </FormGroup>
                        <p className="mb-0">
                          Call Help Center for any substitutes
                        </p>
                      </CardBody>
                    </Card>
                  </Col>

                  {/* Option 2 */}
                  <Col md={6} xl={4} className="mt-4">
                    <Card className="shadow-none equal-card">
                      <CardBody className="address-content">
                        <FormGroup check>
                          <Label check>
                            <Input type="radio" name="preference" />
                            <span className="fs-6 tab-heading ms-2">
                              Select 2
                            </span>
                          </Label>
                        </FormGroup>
                        <p className="mb-0">Let us pick your substitutes</p>
                      </CardBody>
                    </Card>
                  </Col>

                  {/* Option 3 */}
                  <Col md={6} xl={4} className="mt-4">
                    <Card className="shadow-none equal-card">
                      <CardBody className="address-content">
                        <FormGroup check>
                          <Label check>
                            <Input type="radio" name="preference" />
                            <span className="fs-6 tab-heading ms-2">
                              Select 3
                            </span>
                          </Label>
                        </FormGroup>
                        <p className="mb-0">I do not need any substitutes</p>
                      </CardBody>
                    </Card>
                  </Col>
                </Row>
              </Col>
            </Row>
          </div>
        )}
      </div>
    </>
  );
};

export default CheckoutTabsData;
