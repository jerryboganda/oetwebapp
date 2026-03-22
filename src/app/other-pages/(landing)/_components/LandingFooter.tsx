import React from "react";
import { Button, Col, Container, Row } from "reactstrap";

const LandingFooter = () => {
  return (
    <Container>
      <Row>
        <Col className="footer-content text-center">
          <img src="/images/logo/polytronx-dark.svg" alt="PolytronX logo" />
          <h1>
            Build a <span className="highlight-title"> polished </span>{" "}
            workspace
          </h1>
          <p className="txt-ellipsis-3">
            Launch your next admin experience with PolytronX. Explore the
            workspace, review the available plans, and reach out whenever you
            need support.
          </p>
          <div className="footer-btn">
            <Button
              href="https://polytronx.com"
              target="_blank"
              color="primary"
              size="lg"
              className="me-3"
            >
              Explore Plans
            </Button>
            <Button
              href="mailto:support@polytronx.com"
              target="_blank"
              color="danger"
              size="lg"
            >
              Contact Support
            </Button>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export default LandingFooter;
