import React from "react";
import { Row, Col } from "reactstrap";

interface SectionHeadingProps {
  title: string;
  highlight: string;
  description: string;
  center?: boolean;
  highlightFirst?: boolean;
}

const SectionHeading: React.FC<SectionHeadingProps> = ({
  title,
  highlight,
  description,
  center = true,
  highlightFirst = false,
}) => {
  return (
    <Row>
      <Col xl={6} className="offset-xl-3">
        <div className={`landing-title ${center ? "text-md-center" : ""}`}>
          <h2>
            {highlightFirst ? (
              <>
                <span className="highlight-title">{highlight} </span> {title}
              </>
            ) : (
              <>
                {title} <span className="highlight-title">{highlight}</span>
              </>
            )}
          </h2>
          <p>{description}</p>
        </div>
      </Col>
    </Row>
  );
};

export default SectionHeading;
