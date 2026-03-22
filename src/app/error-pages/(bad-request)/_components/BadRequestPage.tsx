import React from "react";
import { Container, Row, Col } from "reactstrap";
import Link from "next/link";
import { IconHome } from "@tabler/icons-react";

const BadRequestPage = () => {
  return (
    <div className="error-container py-5">
      <Container className="text-center">
        <img
          src="/images/background/error-400.png"
          className="img-fluid mb-4"
          alt="400 Error"
        />

        <Row className="justify-content-center mb-4">
          <Col lg="8">
            <p className="text-secondary fw-medium">
              400 Bad Request response status code indicates that the server
              cannot or will not process the request due to something that is
              perceived to be a client error.
            </p>
          </Col>
        </Row>

        <Link
          href="/dashboard/ecommerce"
          className="btn btn-lg btn-light-danger mt-3 rounded-pill d-inline-flex align-items-center justify-content-center gap-1"
        >
          <IconHome size={18} className="me-2" />
          Back To Home
        </Link>
      </Container>
    </div>
  );
};

export default BadRequestPage;
