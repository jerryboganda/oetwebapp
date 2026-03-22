import React from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";

type OverlayItem = {
  title: string;
  description: string;
  image: string;
  position: string;
};

const overlayItems: OverlayItem[] = [
  {
    title: "Left Overlay",
    description:
      "This overlay could be static, appearing on the page load, or dynamic, appearing based on user interaction.",
    image: "/images/wrapper/content-overlay.jpg",
    position: "left",
  },
  {
    title: "Right Overlay",
    description:
      "It seems like you're referring to a technique or feature related to displaying application content.",
    image: "/images/wrapper/content-overlay-2.jpg",
    position: "right",
  },
  {
    title: "Top Overlay",
    description:
      "To create an overlay that appears over the main content when you hover using HTML, CSS, or more complex behaviors.",
    image: "/images/wrapper/content-overlay-3.jpg",
    position: "top",
  },
  {
    title: "Bottom Overlay",
    description:
      "Bottom overlay related content typically refers to additional information below the main content of a webpage.",
    image: "/images/wrapper/content-overlay-4.jpg",
    position: "bottom",
  },
];
const imageWrapper = [
  {
    image: "/images/wrapper/overlay-1.jpg",
  },
  {
    image: "/images/wrapper/overlay-2.jpg",
    position: "bottom",
  },
  {
    image: "/images/wrapper/overlay-3.jpg",
    position: "top",
  },
  {
    image: "/images/wrapper/overlay-4.jpg",
    position: "top",
  },
];
const ContentOverlay = () => {
  return (
    <>
      <Col xl="12">
        <Card>
          <CardHeader>
            <h5>Content Overlay</h5>
          </CardHeader>
          <CardBody>
            <Row>
              {overlayItems.map((item, index) => (
                <Col xs="6" sm="6" lg="3" key={index}>
                  <div
                    className={`content-overlay content-overlay-${item.position} position-relative`}
                  >
                    <img
                      alt={item.title}
                      src={item.image}
                      className="img-fluid"
                    />
                    <div className="content-overlay-text">
                      <h5 className="mb-2">{item.title}</h5>
                      <p>{item.description}</p>
                    </div>
                  </div>
                </Col>
              ))}
            </Row>
          </CardBody>
        </Card>
      </Col>

      <Col xl="12">
        <Card>
          <CardHeader>
            <h5>Basic Overlay</h5>
          </CardHeader>
          <CardBody>
            <Row>
              {imageWrapper.map((item, index) => (
                <Col xs="6" sm="6" lg="3" key={index}>
                  <div
                    className={`wraper ${item.position ? `wrapper-${item.position}` : ""} position-relative`}
                  >
                    <img
                      alt={`Overlay ${index + 1}`}
                      src={item.image}
                      className="img-fluid"
                    />
                  </div>
                </Col>
              ))}
            </Row>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default ContentOverlay;
