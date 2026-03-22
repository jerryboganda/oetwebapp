"use client";
import React from "react";
import {
  Container,
  Row,
  Col,
  Card,
  CardHeader,
  CardBody,
  InputGroup,
  InputGroupText,
  Input,
  Label,
  Button,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import {
  faCreditCard,
  faDownload,
  faLinkSlash,
  faMagnifyingGlass,
  faMagnifyingGlassMinus,
  faPaste,
  faPencil,
  faPhone,
  faUsers,
} from "@fortawesome/free-solid-svg-icons";
import { IconCreditCard } from "@tabler/icons-react";

const InputGroupsPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Input Groups"
          title="Forms elements"
          path={["Input groups"]}
          Icon={IconCreditCard}
        />
        <Row>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Basic Input Groups</h5>
              </CardHeader>
              <CardBody>
                <div className="app-form">
                  <Row>
                    <Col xs="12">
                      <Label for="leftAddon">Left Addon</Label>
                      <InputGroup className="mb-3">
                        <InputGroupText
                          className="b-r-left text-bg-primary"
                          id="basic-addon1"
                        >
                          @
                        </InputGroupText>
                        <Input
                          placeholder="Username"
                          aria-label="Username"
                          aria-describedby="basic-addon1"
                        />
                      </InputGroup>
                    </Col>

                    <Col xs="12">
                      <Label for="rightAddon">Right Addon</Label>
                      <InputGroup className="mb-3">
                        <Input
                          placeholder="Recipient's username"
                          aria-label="Recipient's username"
                          aria-describedby="basic-addon2"
                        />
                        <InputGroupText
                          className="b-r-right text-bg-secondary"
                          id="basic-addon2"
                        >
                          @example.com
                        </InputGroupText>
                      </InputGroup>
                    </Col>

                    <Col xs="12">
                      <Label for="jointAddon">Joint Addon</Label>
                      <InputGroup className="mb-3">
                        <InputGroupText
                          className="b-r-left text-bg-primary"
                          id="basic-addon3"
                        >
                          $
                        </InputGroupText>
                        <InputGroupText
                          className="b-r-0 text-bg-primary"
                          id="basic-addon4"
                        >
                          0.00
                        </InputGroupText>
                        <Input
                          placeholder="Dollar amount (with dot and two decimal places)"
                          aria-label="Dollar amount"
                        />
                      </InputGroup>
                    </Col>

                    <Col xs="12">
                      <Label for="leftRightAddon">Left & Right Addon</Label>
                      <InputGroup className="mb-3">
                        <InputGroupText
                          className="b-r-left text-bg-warning"
                          id="basic-addon5"
                        >
                          $
                        </InputGroupText>
                        <Input placeholder="Amount (to the nearest dollar)" />
                        <InputGroupText
                          className="b-r-right text-bg-warning"
                          id="basic-addon6"
                        >
                          .00
                        </InputGroupText>
                      </InputGroup>
                    </Col>

                    <Col xs="12">
                      <Label for="solidStyle">Solid style</Label>
                      <InputGroup className="mb-3">
                        <InputGroupText
                          className="b-r-left text-bg-danger"
                          id="addon-wrapping1"
                        >
                          @
                        </InputGroupText>
                        <Input
                          placeholder="Email"
                          aria-label="Email"
                          aria-describedby="addon-wrapping"
                        />
                      </InputGroup>
                    </Col>

                    <Col xs="12">
                      <Label for="squareStyle">Square style</Label>
                      <InputGroup className="mb-3">
                        <InputGroupText
                          className="b-r-left text-bg-secondary"
                          id="addon-wrapping2"
                        >
                          +
                        </InputGroupText>
                        <Input
                          placeholder="Email"
                          aria-label="Email"
                          aria-describedby="addon-wrapping"
                        />
                      </InputGroup>
                    </Col>

                    <Col xs="12">
                      <Label for="raiseStyle">Raise style</Label>
                      <InputGroup className="mb-3">
                        <InputGroupText
                          className="b-r-left text-bg-primary"
                          id="addon-wrapping3"
                        >
                          #
                        </InputGroupText>
                        <Input
                          placeholder="Email"
                          aria-label="Email"
                          aria-describedby="addon-wrapping"
                        />
                      </InputGroup>
                    </Col>

                    <Col xs="12">
                      <Label for="roundedAddon">Left & Right Addon</Label>
                      <InputGroup className="mb-3">
                        <InputGroupText className="bg-dark h-45 w-45 d-flex justify-content-center align-items-center b-r-50">
                          $
                        </InputGroupText>
                        <Input placeholder="Amount (to the nearest dollar)" />
                        <InputGroupText className="bg-dark h-45 w-45 d-flex justify-content-center align-items-center b-r-50">
                          .00
                        </InputGroupText>
                      </InputGroup>
                    </Col>
                  </Row>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Basic Input groups</h5>
              </CardHeader>
              <CardBody>
                <div className="app-form">
                  <form>
                    <div className="mb-3">
                      <Label for="leftAddon">Left Addon</Label>
                      <InputGroup>
                        <InputGroupText className="b-r-left bg-light-primary b-1-primary">
                          <FontAwesomeIcon
                            icon={faPencil}
                            className="text-dark"
                          />
                        </InputGroupText>
                        <Input type="text" placeholder="Email" />
                      </InputGroup>
                    </div>

                    <div className="mb-3">
                      <Label for="rightAddon">Right Addon</Label>
                      <InputGroup>
                        <Input
                          type="text"
                          placeholder="Recipient's username"
                          aria-label="Recipient's username"
                        />
                        <InputGroupText className="bg-light-secondary b-1-secondary">
                          <FontAwesomeIcon icon={faPhone} />
                        </InputGroupText>
                      </InputGroup>
                    </div>

                    <div className="mb-3">
                      <Label for="leftSpinner">Left spinner</Label>
                      <InputGroup>
                        <InputGroupText className="b-r-left bg-light-success b-1-success">
                          <span
                            className="spinner-border spinner-border-sm"
                            role="status"
                          >
                            <span className="visually-hidden">Loading...</span>
                          </span>
                        </InputGroupText>
                        <Input type="text" placeholder="Loading..." />
                      </InputGroup>
                    </div>

                    <div className="mb-3">
                      <Label for="rightSpinner">Right spinner</Label>
                      <InputGroup>
                        <Input
                          type="text"
                          placeholder="Loading..."
                          aria-label="Recipient's username"
                        />
                        <InputGroupText className="b-r-left bg-light-warning b-1-warning">
                          <span
                            className="spinner-border text-warning spinner-border-sm"
                            role="status"
                          >
                            <span className="visually-hidden">Loading...</span>
                          </span>
                        </InputGroupText>
                      </InputGroup>
                    </div>

                    <div className="mb-3">
                      <Label for="jointAddon">Joint Addon</Label>
                      <InputGroup>
                        <InputGroupText className="b-r-left bg-light-danger b-1-danger">
                          <FontAwesomeIcon
                            icon={faLinkSlash}
                            className="text-secondary"
                          />
                        </InputGroupText>
                        <InputGroupText className="bg-light-danger b-1-danger">
                          0.00
                        </InputGroupText>
                        <Input
                          type="text"
                          aria-label="Amount (to the nearest dollar)"
                        />
                      </InputGroup>
                    </div>

                    <div className="mb-3">
                      <Label for="leftRightAddon">Left &amp; Right Addon</Label>
                      <InputGroup>
                        <InputGroupText className="b-r-left bg-light-dark b-1-dark">
                          <FontAwesomeIcon
                            icon={faMagnifyingGlassMinus}
                            size="lg"
                            className="text-muted"
                          />
                        </InputGroupText>
                        <Input
                          type="text"
                          aria-label="Amount (to the nearest dollar)"
                        />
                        <InputGroupText className="bg-light-dark b-1-dark">
                          <FontAwesomeIcon
                            icon={faMagnifyingGlassMinus}
                            size="lg"
                            className="text-muted"
                          />
                        </InputGroupText>
                      </InputGroup>
                    </div>

                    <div className="mb-3 input-group-solid">
                      <Label for="solidStyle">Solid style</Label>
                      <InputGroup>
                        <InputGroupText className="b-r-left bg-light-primary b-1-primary">
                          <FontAwesomeIcon icon={faUsers} />
                        </InputGroupText>
                        <Input type="text" placeholder="999999" />
                      </InputGroup>
                    </div>

                    <div className="mb-3 input-group-square">
                      <Label for="flatStyle">Flat style</Label>
                      <InputGroup>
                        <InputGroupText className="b-r-left bg-light-secondary b-1-secondary">
                          <FontAwesomeIcon icon={faCreditCard} />
                        </InputGroupText>
                        <Input type="text" placeholder="" />
                      </InputGroup>
                    </div>

                    <div className="mb-3 input-group-square">
                      <Label for="raiseStyle">Raise style</Label>
                      <InputGroup>
                        <InputGroupText className="b-r-left bg-light-warning b-1-warning">
                          <FontAwesomeIcon icon={faDownload} />
                        </InputGroupText>
                        <Input
                          className="input-group-air"
                          type="text"
                          placeholder="https://www.example.com"
                        />
                      </InputGroup>
                    </div>

                    <div className="mb-3">
                      <Label for="pillInputGroup">Left &amp; Right Addon</Label>
                      <InputGroup className="pill-input-group">
                        <InputGroupText className="b-r-left bg-light-danger b-1-danger">
                          <FontAwesomeIcon icon={faPaste} size="2x" />
                        </InputGroupText>
                        <Input
                          type="text"
                          aria-label="Amount (to the nearest dollar)"
                        />
                        <InputGroupText className="bg-light-danger b-1-danger">
                          <FontAwesomeIcon icon={faMagnifyingGlass} size="2x" />
                        </InputGroupText>
                      </InputGroup>
                    </div>
                  </form>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Multiple Addons</h5>
              </CardHeader>
              <CardBody>
                <div className="app-form">
                  <div className="input-group mb-3">
                    <InputGroupText className="b-r-left bg-warning">
                      $
                    </InputGroupText>
                    <InputGroupText className="bg-warning">0.00</InputGroupText>
                    <Input
                      type="text"
                      className="form-control"
                      aria-label="Dollar amount (with dot and two decimal places)"
                    />
                  </div>

                  {/* Second Input Group with Addons on Both Sides */}
                  <div className="input-group">
                    <Input
                      type="text"
                      className="form-control b-r-left"
                      aria-label="Dollar amount (with dot and two decimal places)"
                    />
                    <InputGroupText className="bg-danger">$</InputGroupText>
                    <InputGroupText className="b-r-right bg-danger">
                      0.00
                    </InputGroupText>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Checkboxes and Radios</h5>
              </CardHeader>
              <CardBody>
                <div className="app-form">
                  <div className="input-group mb-3">
                    <InputGroupText className="b-r-left bg-light-primary b-1-primary">
                      <input
                        className="form-check-input mt-0"
                        type="checkbox"
                        aria-label="Checkbox for following text input"
                      />
                    </InputGroupText>
                    <Input
                      type="text"
                      className="form-control b-r-right"
                      aria-label="Text input with checkbox"
                    />
                  </div>

                  <div className="input-group">
                    <InputGroupText className="b-r-left bg-light-primary b-1-primary">
                      <input
                        className="form-check-input mt-0"
                        type="radio"
                        aria-label="Radio button for following text input"
                      />
                    </InputGroupText>
                    <Input
                      type="text"
                      className="form-control b-r-right"
                      aria-label="Text input with radio button"
                    />
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card className="equal-card">
              <CardHeader>
                <h5>Sizing</h5>
              </CardHeader>
              <CardBody>
                <div className="input-group input-group-sm mb-3 mt-3">
                  <InputGroupText
                    className="text-light-primary b-r-left"
                    id="inputGroup-sizing-sm"
                  >
                    Small
                  </InputGroupText>
                  <Input
                    type="text"
                    className="form-control b-r-right"
                    aria-label="Sizing example input"
                    aria-describedby="inputGroup-sizing-sm"
                  />
                </div>

                <div className="input-group mb-3 mt-3">
                  <InputGroupText
                    className="text-light-secondary b-r-left"
                    id="inputGroup-sizing-default"
                  >
                    Default
                  </InputGroupText>
                  <Input
                    type="text"
                    className="form-control b-r-right"
                    aria-label="Sizing example input"
                    aria-describedby="inputGroup-sizing-default"
                  />
                </div>

                <div className="input-group input-group-lg mt-3">
                  <InputGroupText
                    className="text-light-success b-r-left"
                    id="inputGroup-sizing-lg"
                  >
                    Large
                  </InputGroupText>
                  <Input
                    type="text"
                    className="form-control b-r-right"
                    aria-label="Sizing example input"
                    aria-describedby="inputGroup-sizing-lg"
                  />
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Button Addons</h5>
              </CardHeader>
              <CardBody>
                <div className="input-group mb-3">
                  <Button
                    color="outline-primary"
                    className="b-r-left"
                    type="button"
                    id="button-addon1"
                  >
                    Button
                  </Button>
                  <Input
                    type="text"
                    className="form-control b-r-right"
                    placeholder=""
                    aria-label="Example text with button addon"
                    aria-describedby="button-addon1"
                  />
                </div>

                {/* Button on the Right */}
                <div className="input-group mb-3">
                  <Input
                    type="text"
                    className="form-control b-r-left"
                    placeholder="Recipient's username"
                    aria-label="Recipient's username"
                    aria-describedby="button-addon2"
                  />
                  <Button
                    color="outline-secondary"
                    className="b-r-right"
                    type="button"
                    id="button-addon2"
                  >
                    Button
                  </Button>
                </div>

                <div className="input-group mb-3">
                  <Button
                    color="outline-success"
                    className="b-r-left"
                    type="button"
                  >
                    Button
                  </Button>
                  <Button color="outline-success" type="button">
                    Button
                  </Button>
                  <Input
                    type="text"
                    className="form-control b-r-right"
                    placeholder=""
                    aria-label="Example text with two button addons"
                  />
                </div>

                <div className="input-group">
                  <Input
                    type="text"
                    className="form-control b-r-left"
                    placeholder="Recipient's username"
                    aria-label="Recipient's username with two button addons"
                  />
                  <Button color="outline-danger" type="button">
                    Button
                  </Button>
                  <Button
                    color="outline-danger"
                    className="b-r-right"
                    type="button"
                  >
                    Button
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default InputGroupsPage;
