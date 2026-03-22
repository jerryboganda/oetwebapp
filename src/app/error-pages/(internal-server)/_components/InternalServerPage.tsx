import React from "react";
import { IconHome } from "@tabler/icons-react";
import Link from "next/link";
import { Container, Row, Col } from "reactstrap";

const InternalServerPage = () => {
  return (
    <div className="error-container py-5">
      <Container className="text-center">
        <img
          alt="500 Internal Server Error"
          className="img-fluid mb-4"
          src="/images/background/error-500.png"
        />

        <Row className="justify-content-center mb-4">
          <Col lg="8">
            <p className="text-secondary fw-medium">
              500 Internal Server Error response code indicates that the server
              encountered an unexpected condition that prevented it from
              fulfilling the request.
            </p>
          </Col>
        </Row>

        <Link
          href="/dashboard/project"
          className="btn btn-lg btn-light-info mt-3 rounded-pill d-inline-flex align-items-center justify-content-center gap-1"
        >
          <IconHome size={18} />
          Back To Home
        </Link>
      </Container>
    </div>
  );
};

export default InternalServerPage;
