"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Checkout from "@/app/ready-to-use/(form-wizard-2)/_components/Checkout";
import Financial from "@/app/ready-to-use/(form-wizard-2)/_components/Financial";
import Booking from "@/app/ready-to-use/(form-wizard-2)/_components/Booking";
import { IconHeartHandshake } from "@tabler/icons-react";

const FormWizard2Page = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Form Wizards 2"
          title="Ready to use"
          path={["Form wizards 2"]}
          Icon={IconHeartHandshake}
        />
        <Row>
          <Col xs={12}>
            <Checkout />
          </Col>
          <Col xs={12}>
            <Financial />
          </Col>
          <Col xs={12}>
            <Booking />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default FormWizard2Page;
