import React from "react";
import { Card, CardBody, Col, Container, Row } from "reactstrap";
import SectionHeading from "@/app/other-pages/(landing)/_components/SectionHeading";
import PreviewBrandImage from "@/Component/CommonElements/PreviewBrandImage";

const customizationCards = [
  {
    id: "sidebar",
    colProps: { lg: 7 },
    cardClass: "sidebar-option-card",
    bodyClass: "p-0",
    title: (
      <h2 className="text-white f-w-600">
        Customize your <br />
        <span className="text-dark">sidebar</span> with ease
      </h2>
    ),
    image: "/images/landing/layout.png",
    imageAlt: "layout-img",
    imageClass: "img-fluid mt-3",
    align: "end",
  },
  {
    id: "color-hint",
    colProps: { md: 6, lg: 5 },
    cardClass: "equal-card color-hint-card",
    title: (
      <div className="marquee-animated">
        <p>Preview variant colors for perfect customization!</p>
      </div>
    ),
    image: "/images/landing/color-hint.gif",
    imageAlt: "color hint",
    imageClass: "img-fluid",
  },
  {
    id: "performance",
    colProps: { md: 6, lg: 4 },
    cardClass: "speed-performance-card",
    bodyClass: "text-center",
    title: (
      <h3 className="text-dark f-w-600">
        <span className="text-success">Quick </span> Response
      </h3>
    ),
    image: "/images/landing/speed-performance.png",
    imageAlt: "speed-performance",
    imageClass: "img-fluid",
    extraContent: (
      <div className="performance-content">
        <div className="text-end">
          <img
            src="/images/landing/arrow-shape.png"
            className="arrow-shape"
            alt="arrow-shape"
          />
        </div>
        <div className="performance-number">
          <p className="f-w-500 f-s-18 text-dark">Performance</p>
        </div>
      </div>
    ),
  },
  {
    id: "layout-option",
    colProps: { lg: 8 },
    cardClass: "equal-card layout-option-card",
    customContent: (
      <Row>
        <Col sm="4" className="position-relative">
          <h3 className="text-light mt-3">
            One-Click and change your{" "}
            <span className="text-primary f-w-600">Layout.</span>
          </h3>
        </Col>
        <Col sm="8" className="z-1">
          <PreviewBrandImage
            src="/images/landing/layout-option.png"
            className="img-fluid mt-4"
            alt="layout-option"
          />
        </Col>
      </Row>
    ),
  },
];

const CustomizationOptionSection = () => {
  return (
    <Container>
      <SectionHeading
        highlight="Customization"
        title="and control"
        description="Let users personalize settings and layouts in real time with a smooth, tailored admin experience."
        highlightFirst
      />
      <Row>
        {customizationCards.map((item) => (
          <Col key={item.id} {...item.colProps}>
            <Card className={item.cardClass}>
              <CardBody className={item.bodyClass || ""}>
                {item.customContent ? (
                  item.customContent
                ) : (
                  <>
                    {item.title && <div>{item.title}</div>}
                    <div className={`text-${item.align || "center"}`}>
                      <PreviewBrandImage
                        src={item.image}
                        className={item.imageClass}
                        alt={item.imageAlt}
                      />
                    </div>
                    {item.extraContent}
                  </>
                )}
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </Container>
  );
};

export default CustomizationOptionSection;
