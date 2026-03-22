import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Button,
  Row,
  Col,
  Input,
  Label,
} from "reactstrap";
import { CheckCircle } from "phosphor-react";

const SubscriptionPlan = () => {
  const [teamCount, setTeamCount] = useState(25);

  const handleIncrement = () => setTeamCount((prev) => prev + 1);
  const handleDecrement = () =>
    setTeamCount((prev) => (prev > 1 ? prev - 1 : prev));

  return (
    <Card>
      <CardHeader>
        <h5>Subscription</h5>
      </CardHeader>
      <CardBody>
        <div className="subscription-plan">
          <Row>
            <Col xl="6">
              <div className="plan-choose">
                <h6 className="mb-0">Choose plan</h6>
                <div>
                  <button
                    color="light-primary"
                    className="btn btn-light-primary"
                  >
                    Quarterly
                  </button>{" "}
                  <button className="btn btn-light-secondary">Yearly</button>
                </div>
              </div>

              <div className="form-selectgroup mt-5">
                <div className="select-item">
                  <Input
                    className="form-check-input form-check-primary w-20 h-20"
                    type="radio"
                    id="planCheckbox2"
                    value="planoption2"
                    name="planoption"
                  />
                  <Label for="planCheckbox2" className="form-check-label">
                    <span className="d-flex align-items-center">
                      <span className="ms-2">
                        <span className="fs-6 mb-0">Mark Moen</span>
                        <span className="d-block text-secondary">
                          UI/UX Designer
                        </span>
                      </span>
                    </span>
                  </Label>
                  <div className="select-item-2 ms-2">
                    <h6 className="fs-6 mb-0">$69.44</h6>
                    <p className="text-secondary">1 users/quarterly</p>
                  </div>
                </div>
              </div>

              <div className="form-selectgroup mt-4">
                <div className="select-item">
                  <Input
                    className="form-check-input form-check-primary w-20 h-20"
                    type="radio"
                    id="planCheckbox1"
                    value="planoption1"
                    name="planoption"
                    defaultChecked
                  />
                  <Label for="planCheckbox1" className="form-check-label">
                    <span className="d-flex align-items-center">
                      <span className="ms-2">
                        <span className="fs-6 mb-0">Mark Moen</span>
                        <span className="d-block text-secondary mb-0">
                          UI/UX Designer
                        </span>
                      </span>
                    </span>
                  </Label>
                  <div className="select-item-2 ms-2">
                    <h6 className="fs-6 mb-0">$69.44</h6>
                    <p className="text-secondary mb-0">1 users/quarterly</p>
                  </div>
                </div>
                <ul className="feachers-list">
                  <li className="f-s-16 text-secondary">
                    <CheckCircle
                      weight="bold"
                      size={16}
                      className="me-2 text-success"
                    />{" "}
                    40 downloads per day.
                  </li>
                  <li className="f-s-16 text-secondary">
                    <CheckCircle
                      weight="bold"
                      size={16}
                      className="me-2 text-success"
                    />{" "}
                    Access to all products or bundles.
                  </li>
                  <li className="f-s-16 text-secondary">
                    <CheckCircle
                      weight="bold"
                      size={16}
                      className="me-2 text-success"
                    />{" "}
                    Early access to new/beta release features.
                  </li>
                </ul>
                <div className="app-divider-v py-3 mb-4"></div>

                <div className="team-accounts mb-4">
                  <span className="privacy-content">
                    <span className="mb-0 f-s-16 f-w-500">Team Accounts</span>
                    <span className="text-secondary f-s-14 mb-0">
                      Starting at 5 users in the team plan, you can increase.
                    </span>
                  </span>
                  <div className="simplespin ms-3">
                    <button
                      className="circle-btn btn-light-primary decrement"
                      onClick={handleDecrement}
                    >
                      -
                    </button>
                    <Input
                      type="text"
                      className="app-small-touchspin count"
                      value={teamCount}
                      readOnly
                    />
                    <button
                      className="circle-btn btn-light-primary increment"
                      onClick={handleIncrement}
                    >
                      +
                    </button>
                  </div>
                </div>
              </div>

              <div className="form-selectgroup mt-4">
                <div className="select-item">
                  <Input
                    className="form-check-input form-check-primary w-20 h-20"
                    type="radio"
                    id="planCheckbox3"
                    value="planoption3"
                    name="planoption"
                  />
                  <Label for="planCheckbox3" className="form-check-label">
                    <span className="d-flex align-items-center">
                      <span className="ms-2">
                        <span className="fs-6 mb-0">Business Pro</span>
                        <span className="d-block text-secondary mb-0">
                          for big teams
                        </span>
                      </span>
                    </span>
                  </Label>
                  <div className="feachers-list ms-2">
                    <h6 className="fs-6 mb-0">$250.44</h6>
                    <p className="text-secondary">31 users/quarterly</p>
                  </div>
                </div>
              </div>
            </Col>
            <Col xl="6" className="mt-xxl-0 mt-5">
              <div className="plan-choose">
                <h6 className="mb-0">Payment plan</h6>
                <div>
                  <button
                    color="light-primary"
                    className="btn btn-light-primary"
                  >
                    Credit card
                  </button>{" "}
                  <button color="light" className="btn btn-light-secondary">
                    PayPal
                  </button>
                </div>
              </div>

              <div className="form-selectgroup mt-5">
                <div className="select-item">
                  <Input
                    className="form-check-input form-check-primary w-20 h-20"
                    type="radio"
                    id="paymentCheckbox1"
                    value="paymentoption1"
                    name="paymentoption"
                  />
                  <Label for="paymentCheckbox1" className="form-check-label">
                    <span className="d-flex align-items-center">
                      <span className="ms-2">
                        <span className="f-s-16 mb-0 f-w-500">**** 4426</span>
                        <span className="d-block text-secondary f-s-16 mb-0">
                          Visa card
                        </span>
                      </span>
                    </span>
                  </Label>
                  <div className="ms-2">
                    <img src="/images/icons/visa-icon.png" alt="Visa" />
                  </div>
                </div>
              </div>

              <div className="form-selectgroup mt-4">
                <div className="select-item">
                  <Input
                    className="form-check-input form-check-primary w-20 h-20"
                    type="radio"
                    id="paymentCheckbox2"
                    value="paymentoption2"
                    name="paymentoption"
                    defaultChecked
                  />
                  <Label for="paymentCheckbox2" className="form-check-label">
                    <span className="d-flex align-items-center">
                      <span className="ms-2">
                        <span className="f-s-16 mb-0 f-w-500">**** 6790</span>
                        <span className="d-block text-secondary f-s-16 mb-0">
                          Master card
                        </span>
                      </span>
                    </span>
                  </Label>
                  <div className="ms-2">
                    <img src="/images/icons/master-icon.png" alt="MasterCard" />
                  </div>
                </div>
              </div>

              <Button className="btn-success w-100 mt-4" type="button">
                + Add New Card
              </Button>

              <div className="form-selectgroup p-3 mt-4">
                <h6 className="mb-2">Discount code</h6>
                <div className="input-group mb-3">
                  <Input
                    type="text"
                    className="form-control form-check-primary b-r-left"
                    placeholder="20FGJKYSD"
                  />
                  <Button className="btn-secondary b-r-right" type="button">
                    Apply
                  </Button>
                </div>

                <p className="text-success f-s-16 mb-0">
                  30% discount code applied
                </p>

                <div className="team-accounts mt-3  p-0">
                  <span className="privacy-content me-2">
                    <span className="mb-0 f-s-16 f-w-500">Team Plan</span>
                    <span className="text-secondary f-s-14 mb-0">
                      5 Users Quarterly
                    </span>
                  </span>
                  <h4>$789.0</h4>
                </div>

                <div className="app-divider-v py-3" />

                <div className="plan-choose">
                  <h6 className="mb-0">Payment plan</h6>
                  <h5 className="text-success f-s-18">-$57.90</h5>
                </div>

                <div className="app-divider-v py-3" />

                <div className="team-accounts mt-3 mb-4 p-0">
                  <span className="privacy-content">
                    <span className="mb-0 f-s-16 f-w-500">Total</span>
                    <span className="text-secondary f-s-14 mb-0">
                      Next payment will charge 10th of January 2030
                    </span>
                  </span>
                  <h6>$789.0</h6>
                </div>

                <Button className="btn-success w-100" type="button">
                  PAY NOW
                </Button>
              </div>
            </Col>
          </Row>
        </div>
      </CardBody>
    </Card>
  );
};

export default SubscriptionPlan;
