import React from "react";
import { Col, Container, Row } from "reactstrap";

const cardImages = [
  ["/images/landing/landing-bg.png", "slider-slideUp"],
  ["/images/landing/landing-bg-1.png", "slider-slideDown"],
  ["/images/landing/landing-bg-2.png", "slider-slideUp"],
];

const LandingCardSection = () => {
  return (
    <Container>
      <Row>
        <Col lg="4">
          <div className="landing-title">
            <h2>
              Sophisticated <span className="highlight-title">Cards</span>
            </h2>
            <p>
              Cards offer enhanced design flexibility and interactive
              capabilities, incorporating dynamic elements like animations,
              real-time updates, or embedded multimedia to provide a richer and
              more engaging user experience in a concise card format.
            </p>
          </div>

          <ul className="card-details-list">
            <li>200+ Cards Collection</li>
            <li>Basic Components Included</li>
            <li>Advanced Functionality</li>
            <li>Customization and Personalization</li>
            <li>Responsive Card Design</li>
            <li>Styleguide Included</li>
          </ul>
        </Col>

        <Col xs="12" lg="8">
          <div className="card-section-right">
            <div className="slider-container">
              {cardImages.map(([imgPath, animationClass], idx) => (
                <div
                  className={`slider-container-box ${idx === 1 ? "slider-left" : ""}`}
                  key={idx}
                >
                  {[...Array(3)].map((_, i) => (
                    <div className={animationClass} key={i}>
                      <img
                        src={imgPath}
                        className="img-fluid"
                        alt={`card-img-${idx}-${i}`}
                      />
                    </div>
                  ))}
                </div>
              ))}
            </div>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export default LandingCardSection;
