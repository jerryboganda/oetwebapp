import React from "react";
import { Row, Col, Form, FormGroup, Label, Input, Button } from "reactstrap";

const PaymentDataForm = () => {
  return (
    <Form className="app-form">
      <Row>
        <Col md="12">
          <FormGroup>
            <Label for="cardholderName">Cardholder Name</Label>
            <Input type="text" id="cardholderName" required />
          </FormGroup>
        </Col>
        <Col md="6">
          <FormGroup>
            <Label for="cardNumber">Card Number</Label>
            <Input
              type="text"
              id="cardNumber"
              placeholder="xxxx-xxxx-xxxx-xxx"
              required
            />
          </FormGroup>
        </Col>
        <Col md="3">
          <FormGroup>
            <Label for="expiration">Expiration</Label>
            <Input type="text" id="expiration" placeholder="MM/YY" required />
          </FormGroup>
        </Col>
        <Col md="3">
          <FormGroup>
            <Label for="cvcCode">CVC code</Label>
            <Input type="text" id="cvcCode" placeholder="xxx" required />
          </FormGroup>
        </Col>
        <Col md="12" className="text-end">
          <Button color="primary" type="submit">
            Submit
          </Button>
        </Col>
      </Row>
    </Form>
  );
};

export default PaymentDataForm;
