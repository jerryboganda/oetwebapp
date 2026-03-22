"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import TicketDetailsData from "@/app/apps/ticket-page/(ticket-details)/_components/TicketDetailsData";
import TicketFileUpload from "@/app/apps/ticket-page/(ticket-details)/_components/TicketFileUpload";
import TicketInfo from "@/app/apps/ticket-page/(ticket-details)/_components/TicketInfo";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconStack2 } from "@tabler/icons-react";

const TicketDetails = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Ticket Details"
          title="Apps"
          path={["Ticket Details"]}
          Icon={IconStack2}
        />
        <Row className="ticket-details">
          <Col md={5} lg={4} xxl={3}>
            <TicketInfo />
            <TicketFileUpload />
          </Col>
          <Col md={7} lg={8} xxl={9}>
            <TicketDetailsData />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default TicketDetails;
