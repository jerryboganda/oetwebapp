"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import PatientsTable from "@/app/ready-to-use/(ready-to-use-tables)/_components/PatientsTable";
import StudentsTable from "@/app/ready-to-use/(ready-to-use-tables)/_components/StudentsTable";
import PaymentTable from "@/app/ready-to-use/(ready-to-use-tables)/_components/PaymentTable";
import JobTable from "@/app/ready-to-use/(ready-to-use-tables)/_components/JobTable";
import TicketTable from "@/app/ready-to-use/(ready-to-use-tables)/_components/TicketTable";
import { IconHeartHandshake } from "@tabler/icons-react";

const ReadyTouseTablePage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Ready To Use Tables"
          title="Ready to use"
          path={["Ready to use tables"]}
          Icon={IconHeartHandshake}
        />
        <Row>
          <Col xs={12}>
            <PatientsTable />
          </Col>
          <Col xs={12}>
            <StudentsTable />
          </Col>
          <Col xs={12}>
            <PaymentTable />
          </Col>
          <Col xs={12}>
            <JobTable />
          </Col>
          <Col xs={12}>
            <TicketTable />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ReadyTouseTablePage;
