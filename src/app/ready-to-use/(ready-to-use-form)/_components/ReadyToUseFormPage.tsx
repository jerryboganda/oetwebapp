"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import AppointmentForm from "@/app/ready-to-use/(ready-to-use-form)/_components/AppointmentForm";
import PaymentForm from "@/app/ready-to-use/(ready-to-use-form)/_components/PaymentForm";
import DepartmentsForm from "@/app/ready-to-use/(ready-to-use-form)/_components/DepartmentsForm";
import StudentForm from "@/app/ready-to-use/(ready-to-use-form)/_components/StudentForm";
import ClientRegistration from "@/app/ready-to-use/(ready-to-use-form)/_components/ClientRegistration";
import { IconHeartHandshake } from "@tabler/icons-react";

const ReadyToUseFormPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Ready to use form"
          title="Ready to use"
          path={["Ready to use form"]}
          Icon={IconHeartHandshake}
        />
        <Row>
          <Col xs={12}>
            <AppointmentForm />
          </Col>
          <Col xs={12}>
            <PaymentForm />
          </Col>
          <Col xs={12}>
            <DepartmentsForm />
          </Col>
          <Col xs={12}>
            <StudentForm />
          </Col>
          <Col xs={12}>
            <ClientRegistration />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ReadyToUseFormPage;
