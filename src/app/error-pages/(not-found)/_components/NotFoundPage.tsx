import React from "react";
import { IconHome } from "@tabler/icons-react";
import Link from "next/link";
import { Container, Row, Col } from "reactstrap";

const NotFoundPage = () => {
  return (
    <div className="error-container py-5">
      <Container className="text-center">
        <img
          alt="404 Not Found"
          className="img-fluid mb-4"
          src="/images/background/error-404.png"
        />

        <Row className="justify-content-center mb-4">
          <Col lg="8">
            <p className="text-secondary fw-medium">
              Website owners should regularly check for and fix broken links
              using tools like Google Search Console or other link-checking
              software.
            </p>
          </Col>
        </Row>

        <Link
          href="/dashboard/project"
          className="btn btn-lg btn-light-primary mt-3 rounded-pill d-inline-flex align-items-center justify-content-center gap-1"
        >
          <IconHome size={18} />
          Back To Home
        </Link>
      </Container>
    </div>
  );
};

export default NotFoundPage;
