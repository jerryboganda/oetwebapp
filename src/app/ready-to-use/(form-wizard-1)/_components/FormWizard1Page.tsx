"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import PaymentWizard from "@/app/ready-to-use/(form-wizard-1)/_components/PaymentWizard";
import AccountWizard from "@/app/ready-to-use/(form-wizard-1)/_components/AccountWizard";
import BusinessWizard from "@/app/ready-to-use/(form-wizard-1)/_components/BusinessWizard";
import { IconHeartHandshake } from "@tabler/icons-react";

const FormWizard1Page = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Form Wizards 1"
          title="Ready to use"
          path={["Form wizards 1"]}
          Icon={IconHeartHandshake}
        />
        <Row>
          <Col xs={12}>
            <PaymentWizard />
          </Col>
          <Col xs={12}>
            <AccountWizard />
          </Col>
          <Col xs={12}>
            <BusinessWizard />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default FormWizard1Page;
