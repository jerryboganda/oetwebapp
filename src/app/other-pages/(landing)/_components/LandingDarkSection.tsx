import React from "react";
import { Container, Row, Col, Button } from "reactstrap";
import SectionHeading from "@/app/other-pages/(landing)/_components/SectionHeading";
import PreviewBrandImage from "@/Component/CommonElements/PreviewBrandImage";

const LandingDarkSection = () => {
  return (
    <Container>
      <SectionHeading
        title="Discover Our "
        highlight="Dark Layout"
        description="Embrace the elegance of the dark layout, where simplicity meets sophistication.
         Navigate effortlessly through your admin tasks with style."
      />

      <Row>
        <Col xs={12}>
          <div className="slider-container">
            <div className="slider-container-box">
              <div className="slider-slideLeft">
                <PreviewBrandImage
                  src="/images/landing/dark-layout.png"
                  className="img-fluid"
                  alt="Dark Layout"
                  lightBadge
                />
              </div>
            </div>
            <div className="slider-container-box slider-left">
              <div className="slider-slideRight">
                <PreviewBrandImage
                  src="/images/landing/dark-layout-1.png"
                  className="img-fluid"
                  alt="Dark Layout Alt"
                  lightBadge
                />
              </div>
            </div>
          </div>
        </Col>

        <Col xs={12} className="text-center">
          <Button
            color="primary"
            size="lg"
            className="mt-5"
            id="darkDemoBtn"
            onClick={() => {
              localStorage.setItem("theme-mode", "dark");
              window.location.href = "/dashboard/ecommerce";
            }}
          >
            Check Now <i className="ti ti-chevrons-right ms-2"></i>
          </Button>
        </Col>
      </Row>
    </Container>
  );
};

export default LandingDarkSection;
