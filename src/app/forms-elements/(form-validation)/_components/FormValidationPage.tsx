"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import TooltipsValidation from "@/app/forms-elements/(form-validation)/_components/TooltipsValidation";
import CustomValidation from "@/app/forms-elements/(form-validation)/_components/CustomValidation";
import DefaultsValidation from "@/app/forms-elements/(form-validation)/_components/DefaultsValidation";
import SupportedValidation from "@/app/forms-elements/(form-validation)/_components/SupportedValidation";
import { IconCreditCard } from "@tabler/icons-react";

const FormValidationPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Form Validation"
          title="Forms Elements"
          path={["Form validation"]}
          Icon={IconCreditCard}
        />
        <Row>
          <Col xs="12">
            <TooltipsValidation />
          </Col>
          <Col xs="12">
            <CustomValidation />
          </Col>
          <Col xs="12">
            <DefaultsValidation />
          </Col>
          <Col xs="12">
            <SupportedValidation />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default FormValidationPage;
