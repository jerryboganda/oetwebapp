import React, { useState } from "react";
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Input,
  Row,
  Table,
  Modal,
  ModalHeader,
  ModalBody,
  ModalFooter,
  FormGroup,
  Label,
  Form,
} from "reactstrap";
import {
  IconAddressBook,
  IconAlignBoxLeftMiddle,
  IconCheckbox,
  IconChecklist,
  IconCurrencyDollar,
  IconGardenCart,
  IconInfoCircle,
  IconPlus,
} from "@tabler/icons-react";

const Checkout = () => {
  const tabs = [
    { id: "tabs1", label: "Cart", icon: <IconGardenCart />, step: "Step 1" },
    {
      id: "tabs2",
      label: "Address",
      icon: <IconAddressBook />,
      step: "Step 2",
    },
    {
      id: "tabs3",
      label: "Payment",
      icon: <IconCurrencyDollar />,
      step: "Step 3",
    },
    { id: "tabs4", label: "Offers", icon: <IconCheckbox />, step: "Step 4" },
    {
      id: "tabs5",
      label: "Review",
      icon: <IconAlignBoxLeftMiddle />,
      step: "Step 5",
    },
    {
      id: "tabs6",
      label: "Completed",
      icon: <IconChecklist />,
      step: "Step 6",
    },
  ];

  const [currentTab, setCurrentTab] = useState("tabs1");

  const [modalOpen, setModalOpen] = useState(false);
  const [selectedAddress, setSelectedAddress] = useState("home");
  const [selectedDelivery, setSelectedDelivery] = useState("economy");
  const [showAlert, setShowAlert] = useState(true);
  const [giftWrapVisible] = useState(true);

  const toggleModal = () => setModalOpen(!modalOpen);

  const [selectedPayment, setSelectedPayment] = useState("card");

  const handlePaymentSelect = (method: string) => setSelectedPayment(method);

  const [openAccordions, setOpenAccordions] = useState({
    nestingcollapseOne: true,
    nestingcollapseTwo: false,
    nestingcollapseThree: false,
  });

  const toggleAccordion = (id: string) => {
    setOpenAccordions((prev) => ({
      ...prev,
      [id]: !prev[id as keyof typeof prev],
    }));
  };

  const cartItems = [
    {
      id: 1,
      image: "/images/ecommerce/23.jpg",
      title: "Sky & stylist - jacket",
      size: "medium",
      color: "Sky",
      items: 2,
      price: 500,
      rating: 3,
    },
    {
      id: 2,
      image: "/images/ecommerce/25.jpg",
      title: "Trendy & stylist shoes - Shoes",
      size: "large",
      color: "Light",
      items: 2,
      price: 850,
      rating: 4,
    },
    {
      id: 3,
      image: "/images/ecommerce/13.jpg",
      title: "Designer golden watch - Watch",
      size: "large",
      color: "Light",
      items: 1,
      price: 350,
      rating: 4,
    },
  ];

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Checkout Wizard</h5>
        </CardHeader>
        <CardBody>
          <Row>
            <Col md={12} xl={3} className="mb-3">
              <div className="checkout-tab">
                {tabs.map((tab) => (
                  <div
                    key={tab.id}
                    className={`tabs ${currentTab === tab.id ? "current-checkout-tab" : ""}`}
                    onClick={() => setCurrentTab(tab.id)}
                  >
                    <div className="d-flex align-items-center">
                      <div className="steps ms-2">{tab.icon}</div>
                      <div className="ps-3">
                        <h5>{tab.label}</h5>
                        <span>{tab.step}</span>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </Col>

            <Col lg={8} xl={6}>
              <div className="tab-contentlist">
                {currentTab === "tabs1" && (
                  <Row>
                    <Col xs={12}>
                      {cartItems.map((item) => (
                        <div key={item.id} className="cart-box">
                          <div className="bg-light-secondary rounded overflow-hidden">
                            <a href="#">
                              <img src={item.image} alt="" className="w-80" />
                            </a>
                          </div>
                          <div className="flex-grow-1 px-3">
                            <h6>{item.title}</h6>
                            <p>
                              <span>Size</span>: {item.size}
                            </p>
                            <p>
                              <span>Color</span>: {item.color}
                            </p>
                            <p>
                              <span>Items</span>: {item.items}
                            </p>
                          </div>
                          <div className="cart-price-box text-right">
                            <a href="#">
                              <i className="ti ti-x text-secondary"></i>
                            </a>
                            <h5>${item.price}</h5>
                            <div className="mb-3">
                              {[...Array(5)].map((_, index) => (
                                <i
                                  key={index}
                                  className={`ti ti-star-filled f-s-14 star-filled me-1 ${index < item.rating ? "text-warning" : ""}`}
                                ></i>
                              ))}
                            </div>
                            <div className="link">
                              <a href="#">Move to Favorites</a>
                            </div>
                          </div>
                        </div>
                      ))}
                    </Col>
                  </Row>
                )}

                {currentTab === "tabs2" && (
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
                            <IconPlus size={16} /> Add A New Address
                          </a>
                        </CardBody>
                      </Card>
                    </Col>

                    {/* Modal for Adding a New Address */}
                    <Modal isOpen={modalOpen} toggle={toggleModal}>
                      <ModalHeader toggle={toggleModal}>
                        Add New Address
                      </ModalHeader>
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
                            <Label
                              check
                              className="d-flex align-items-center gap-3"
                            >
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
                            <Label
                              check
                              className="d-flex align-items-center gap-3"
                            >
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

                    {/* Alert for Expected Delivery Date */}
                    {showAlert && (
                      <Col md={12}>
                        <div className="alert alert-border-info" role="alert">
                          <p>
                            <IconInfoCircle
                              size={16}
                              className="me-1 text-info"
                            />
                            Expected delivery data is not guaranteed, if you are
                            ordering for an event we suggest allowing 1 extra
                            days.
                          </p>
                          <div className="text-end">
                            <a
                              className="link-primary text-d-underline"
                              onClick={() => setShowAlert(false)}
                            >
                              Don&#39;t allow
                            </a>
                            <a
                              className="link-primary text-d-underline ms-2"
                              onClick={() => {
                                setShowAlert(false);
                              }}
                            >
                              Allow
                            </a>
                          </div>
                        </div>
                      </Col>
                    )}
                  </Row>
                )}

                {currentTab === "tabs3" && (
                  <Row>
                    {/* Credit/Debit Card Payment Option */}
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
                                    <Label for="cardName">
                                      Cardholder Name
                                    </Label>
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

                    {/* Visa Card Payment Option */}
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
                                  <span className="fs-6 tab-heading">
                                    Visa Card
                                  </span>
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

                    {/* PayPal Payment Option */}
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
                                  <span className="fs-6 tab-heading">
                                    Paypal
                                  </span>
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

                    {/* Other UPI Apps Payment Option */}
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

                    {/* Cash on Delivery Payment Option */}
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
                )}

                {currentTab === "tabs4" && (
                  <Row className="offer-content-box">
                    {/* Bank Offers */}
                    <Col md={6} xxl={4}>
                      <Card>
                        <CardBody>
                          <h6>Bank Offers</h6>
                          <p>Upto $480 discount on credit card.</p>
                          <div className="text-end">
                            <Button
                              color="link"
                              className="text-d-underline link-primary"
                            >
                              More Offers
                            </Button>
                          </div>
                        </CardBody>
                      </Card>
                    </Col>

                    {/* Partner Offers */}
                    <Col md={6} xxl={4}>
                      <Card>
                        <CardBody>
                          <div className="circle-ribbon circle-right ribbon-primary w-35 h-35">
                            %
                          </div>
                          <h6>Partner Offers</h6>
                          <p>Get GST invoice and save up to 15% on...</p>
                          <div className="text-end">
                            <Button
                              color="link"
                              className="text-d-underline link-primary"
                            >
                              More Offers
                            </Button>
                          </div>
                        </CardBody>
                      </Card>
                    </Col>

                    {/* Other Offers */}
                    <Col md={6} xxl={4}>
                      <Card>
                        <CardBody>
                          <h6>Other Offers</h6>
                          <p>Buy 2 Bags, save up to 5% to 10%.</p>
                          <div className="text-end">
                            <Button
                              color="link"
                              className="text-d-underline link-primary"
                            >
                              More Offers
                            </Button>
                          </div>
                        </CardBody>
                      </Card>
                    </Col>

                    {/* Offer List with Checkboxes */}
                    <Col md={12}>
                      <ul className="list-group b-r-0 list-contact-box offer-list-group mb-3">
                        <li className="list-group-item">
                          <div className="d-flex align-items-center">
                            <input
                              className="form-check-input flex-shrink-0"
                              type="checkbox"
                              id="offerCheck"
                            />
                            <div className="text-truncate ms-2">
                              <h6 className="mb-0">Offer 1</h6>
                              <p className="text-secondary mb-0">
                                12% off on Visa card EMI Transactions,up to $800
                                on orders of $1500 -
                                <a
                                  href="#"
                                  className="link-secondary text-d-underline"
                                >
                                  See Details
                                </a>
                              </p>
                            </div>
                          </div>
                        </li>
                        <li className="list-group-item">
                          <div className="d-flex align-items-center">
                            <input
                              className="form-check-input flex-shrink-0"
                              type="checkbox"
                              id="offerCheck1"
                            />
                            <div className="text-truncate ms-2">
                              <h6 className="mb-0">Offer 2</h6>
                              <p className="text-secondary mb-0">
                                Additional INR 250 Discount on OneCard Credit
                                Card 9 month.. -
                                <a
                                  href="#"
                                  className="link-secondary text-d-underline"
                                >
                                  See Details
                                </a>
                              </p>
                            </div>
                          </div>
                        </li>
                        <li className="list-group-item">
                          <div className="d-flex align-items-center">
                            <input
                              className="form-check-input flex-shrink-0"
                              type="checkbox"
                              id="offerCheck2"
                            />
                            <div className="text-truncate ms-2">
                              <h6 className="mb-0">Offer 3</h6>
                              <p className="text-secondary mb-0">
                                Get extra 20% off (price inclusive of
                                cashback/coupon) -
                                <a
                                  href="#"
                                  className="link-secondary text-d-underline"
                                >
                                  See Details
                                </a>
                              </p>
                            </div>
                          </div>
                        </li>
                      </ul>
                    </Col>

                    {/* Gift Wrap Alert */}
                    <Col md={12}>
                      {giftWrapVisible && (
                        <div
                          className="alert alert-light-border-success d-flex align-items-center justify-content-between"
                          role="alert"
                        >
                          <p className="mb-0">
                            <i className="ti ti-gift f-s-18 me-2"></i>Gift wrap
                            and personalized message on card, Only for $10.50
                            USD
                          </p>
                          <i className="ti ti-x"></i>
                        </div>
                      )}
                    </Col>
                  </Row>
                )}

                {currentTab === "tabs5" && (
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
                        <p className="text-secondary">
                          Items dispatched by MQ Store
                        </p>
                      </div>
                    </Col>

                    <Col xs={12}>
                      <div>
                        <h6 className="tab-heading">Product details</h6>
                        <div
                          className="accordion app-accordion  accordion-flush accordion-secondary app-accordion-plus"
                          id="nestingExample"
                        >
                          <div className="accordion-item">
                            <h2 className="accordion-header">
                              <button
                                className={`accordion-button ${openAccordions.nestingcollapseOne ? "" : "collapsed"}`}
                                type="button"
                                onClick={() =>
                                  toggleAccordion("nestingcollapseOne")
                                }
                              >
                                Tom &amp; Jerry Printed T-Shirt
                              </button>
                            </h2>
                            <div
                              id="nestingcollapseOne"
                              className={`accordion-collapse collapse ${openAccordions.nestingcollapseOne ? "show" : ""}`}
                            >
                              <div className="accordion-body">
                                <p>
                                  <span className="text-secondary f-w-500">
                                    size
                                  </span>{" "}
                                  : medium
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    color
                                  </span>{" "}
                                  : Green
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    Items
                                  </span>{" "}
                                  : 2
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    Rating
                                  </span>{" "}
                                  : 3+{" "}
                                </p>
                              </div>
                            </div>
                          </div>

                          <div className="accordion-item">
                            <h2 className="accordion-header">
                              <button
                                className={`accordion-button ${openAccordions.nestingcollapseTwo ? "" : "collapsed"}`}
                                type="button"
                                onClick={() =>
                                  toggleAccordion("nestingcollapseTwo")
                                }
                                aria-expanded="false"
                                aria-controls="collapseTwo"
                              >
                                Swatch Watch Irony
                              </button>
                            </h2>
                            <div
                              id="nestingcollapseTwo"
                              className={`accordion-collapse collapse ${openAccordions.nestingcollapseTwo ? "show" : ""}`}
                            >
                              <div className="accordion-body">
                                <p>
                                  <span className="text-secondary f-w-500">
                                    color
                                  </span>{" "}
                                  : Dark
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    Items
                                  </span>{" "}
                                  : 2
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    Rating
                                  </span>{" "}
                                  : 4+{" "}
                                </p>
                              </div>
                            </div>
                          </div>

                          <div className="accordion-item">
                            <h2 className="accordion-header">
                              <button
                                className={`accordion-button ${openAccordions.nestingcollapseThree ? "" : "collapsed"}`}
                                type="button"
                                onClick={() =>
                                  toggleAccordion("nestingcollapseThree")
                                }
                                aria-expanded="false"
                              >
                                Nike Air Max 1
                              </button>
                            </h2>
                            <div
                              id="nestingcollapseThree"
                              className={`accordion-collapse collapse ${openAccordions.nestingcollapseThree ? "show" : ""}`}
                            >
                              <div className="accordion-body">
                                <p>
                                  <span className="text-secondary f-w-500">
                                    size
                                  </span>{" "}
                                  : large
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    color
                                  </span>{" "}
                                  : White
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    Items
                                  </span>{" "}
                                  : 1
                                </p>
                                <p>
                                  <span className="text-secondary f-w-500">
                                    Rating
                                  </span>{" "}
                                  : 5+{" "}
                                </p>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                    </Col>
                  </Row>
                )}

                {currentTab === "tabs6" && (
                  <Row>
                    <Col xs={12}>
                      <div className="text-center">
                        <div className="mb-3">
                          <img src="/images/form/done.gif" alt="" />
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
                            In the unlikely case of items being unavailable,
                            what would you prefer?
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
                              <p className="mb-0">
                                Let us pick your substitutes
                              </p>
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
                              <p className="mb-0">
                                I do not need any substitutes
                              </p>
                            </CardBody>
                          </Card>
                        </Col>
                      </Row>
                    </Col>
                  </Row>
                )}
              </div>
            </Col>

            <Col lg={4} xl={3}>
              <Card>
                <CardHeader>
                  <h5>Order Details</h5>
                </CardHeader>
                <CardBody>
                  <div className="pricing-details">
                    <div className="mb-3">
                      <Input
                        type="text"
                        placeholder="Enter promo code / Gift Certificate"
                        className="promo rounded-0"
                      />
                    </div>
                    <Table className="mb-0">
                      <thead>
                        <tr>
                          <th scope="col">Subtotal</th>
                          <th scope="col" className="text-end">
                            $2,550
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr>
                          <td>Estimated Tax</td>
                          <td className="text-end">$5.00</td>
                        </tr>
                        <tr>
                          <td>Estimated Shipping &amp; Handling</td>
                          <td className="text-end">$10.00</td>
                        </tr>
                        <tr>
                          <td>Delivery Charges</td>
                          <td className="text-end">-</td>
                        </tr>
                        <tr>
                          <td className="border-0 ">Coupon Discount</td>
                          <td className="border-0 text-end ">
                            <Badge color="success" className="text-light">
                              Apply
                            </Badge>
                          </td>
                        </tr>
                      </tbody>

                      <thead>
                        <tr>
                          <th scope="col">Total</th>
                          <th scope="col" className="text-end">
                            $2000
                          </th>
                        </tr>
                      </thead>
                    </Table>
                    <Button color="primary" className="w-100 mt-3" id="next">
                      Buy Order
                    </Button>
                  </div>
                </CardBody>
              </Card>
            </Col>
          </Row>
        </CardBody>
      </Card>
    </>
  );
};

export default Checkout;
