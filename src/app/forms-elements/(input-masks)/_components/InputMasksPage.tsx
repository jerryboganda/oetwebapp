"use client";
import React from "react";
import {
  Card,
  CardBody,
  Col,
  Container,
  Row,
  Label,
  FormGroup,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Cleave from "cleave.js/react";
import { IconCreditCard } from "@tabler/icons-react";

const InputMasksPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Input Masks"
          title="Forms elements"
          path={["Input Masks"]}
          Icon={IconCreditCard}
        />
        <Row>
          <Col md="12">
            <Card>
              <div className="card-header">
                <h5>Date Formatting Input Masks</h5>
              </div>
              <CardBody>
                <Row className="app-form">
                  <Col md="6" xl="4">
                    <FormGroup>
                      <Label for="simpleDate">Simple Date:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="date"
                        options={{
                          date: true,
                          delimiter: "/",
                          datePattern: ["d", "m", "Y"],
                        }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6" xl="4">
                    <FormGroup>
                      <Label for="dateMonth">Date & Month:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="date & month"
                        options={{
                          date: true,
                          delimiter: "/",
                          datePattern: ["d", "m"],
                        }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6" xl="4">
                    <FormGroup>
                      <Label for="formattingDate">Formatting Date:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="formatting date"
                        options={{
                          date: true,
                          delimiter: ".",
                          datePattern: ["Y", "m", "d"],
                        }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6" xl="4">
                    <FormGroup>
                      <Label for="formattingDelimiter">
                        Formatting Delimiter:
                      </Label>
                      <Cleave
                        className="form-control"
                        placeholder="delimiter"
                        options={{ date: true, delimiter: "-" }}
                      />
                    </FormGroup>
                  </Col>
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col md="12">
            <Card>
              <div className="card-header">
                <h5>Time Formatting Input Masks</h5>
              </div>
              <CardBody>
                <Row className="app-form">
                  <Col md="6" xl="4">
                    <FormGroup>
                      <Label for="simpleTime">Simple Time:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="time"
                        options={{ time: true, timePattern: ["h", "m", "s"] }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6" xl="4">
                    <FormGroup>
                      <Label for="minSecTime">Minutes & Seconds:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="minutes & seconds"
                        options={{ time: true, timePattern: ["m", "s"] }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6" xl="4">
                    <FormGroup>
                      <Label for="hoursMinTime">Hours & Minutes:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="hours & minutes"
                        options={{ time: true, timePattern: ["h", "m"] }}
                      />
                    </FormGroup>
                  </Col>
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col md="12">
            <Card>
              <div className="card-header">
                <h5>Custom Formatting Input Masks</h5>
              </div>
              <CardBody>
                <Row className="app-form">
                  <Col md="6">
                    <FormGroup>
                      <Label for="simpleContact">Simple Contact:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="xxx-xxx-xxxx"
                        options={{
                          blocks: [3, 3, 4],
                          delimiter: "-",
                        }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6">
                    <FormGroup>
                      <Label for="formattingContact">Formatting Contact:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="(xxx)(xxx)(xxxx)"
                        options={{
                          blocks: [0, 3, 3, 4],
                          delimiter: "(",
                          delimiterLazyShow: true,
                          numeral: true,
                        }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6">
                    <FormGroup>
                      <Label for="creditCardNumber">
                        Credit Card Number Formatting:
                      </Label>
                      <Cleave
                        className="form-control"
                        placeholder="xxxx xxxx xxxx xxxx"
                        options={{
                          creditCard: true,
                        }}
                      />
                    </FormGroup>
                  </Col>
                  <Col md="6">
                    <FormGroup>
                      <Label for="price">Price:</Label>
                      <Cleave
                        className="form-control"
                        placeholder="price"
                        options={{
                          numeral: true,
                          numeralThousandsGroupStyle: "thousand",
                        }}
                      />
                    </FormGroup>
                  </Col>
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col md="12">
            <Card>
              <CardBody>
                <p>
                  clever.js is a Javascript Plugin for Input Masks. For more
                  options, please check:
                  <a
                    href="https://nosir.github.io/cleave.js/"
                    className="text-decoration-underline link-primary"
                  >
                    cleave.js
                  </a>
                  and
                  <a
                    href="https://github.com/nosir/cleave.js"
                    className="text-decoration-underline link-primary"
                  >
                    GitHub
                  </a>
                </p>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default InputMasksPage;
