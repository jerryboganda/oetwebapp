import React from "react";
import { IconHome } from "@tabler/icons-react";
import Link from "next/link";
import { Container, Row, Col } from "reactstrap";

const ServiceUnavailablePage = () => {
  return (
    <div className="error-container py-5">
      <Container className="text-center">
        <img
          alt="503 Service Unavailable"
          className="img-fluid mb-4"
          src="/images/background/error-503.png"
        />

        <Row className="justify-content-center mb-4">
          <Col lg="8">
            <p className="text-secondary fw-medium">
              503 status code (Service Unavailable) typically indicates a
              performance issue on the origin server.
            </p>
          </Col>
        </Row>

        <Link
          href="/dashboard/project"
          className="btn btn-lg btn-light-danger mt-3 rounded-pill d-inline-flex align-items-center justify-content-center gap-1"
        >
          <IconHome size={18} />
          Back To Home
        </Link>
      </Container>
    </div>
  );
};

export default ServiceUnavailablePage;
