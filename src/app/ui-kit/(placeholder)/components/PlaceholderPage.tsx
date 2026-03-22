import React from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Placeholder,
  Row,
} from "reactstrap";
import { IconBriefcase } from "@tabler/icons-react";

const cards1 = [
  {
    id: 1,
    img: "/images/placeholder/placeholder-3.jpg",
    title: "Card title",
    text: "Some quick example text to build on the card title and make up content.",
    placeholder: true,
  },
  {
    id: 2,
    img: "/images/placeholder/placholder-1.jpg",
    title: "Card title",
    text: "Some quick example text to build on the card title the card's content.",
  },
  { id: 3, img: "/images/placeholder/placholder-2.jpg", skeleton: true },
  {
    id: 4,
    img: "/images/placeholder/placeholder-4.jpg",
    skeleton: true,
    colored: true,
  },
];

const placeholderVariants = [
  "",
  "bg-primary",
  "bg-secondary",
  "bg-success",
  "bg-danger",
  "bg-warning",
  "bg-info",
  "bg-light",
  "bg-dark",
];

const placeholderSizes = [
  "placeholder-lg",
  "",
  "placeholder-sm",
  "placeholder-xs",
];

const PlaceholderPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Placeholder"
          title="Ui Kits"
          path={["Placeholder"]}
          Icon={IconBriefcase}
        />
        <Row className="list-item">
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Card Placeholder</h5>
              </CardHeader>
              <CardBody>
                <div className="card-placeholder">
                  <Row>
                    {cards1.map(
                      ({
                        id,
                        img,
                        title,
                        text,
                        placeholder,
                        skeleton,
                        colored,
                      }) => (
                        <Col key={id} sm={6} xxl={3}>
                          <Card
                            className="equal-card b-1-light"
                            aria-hidden={skeleton}
                          >
                            <div
                              className={placeholder ? "placeholder-glow" : ""}
                            >
                              <img
                                src={img}
                                className={`card-img-top ${placeholder ? "placeholder" : ""}`}
                                alt="..."
                              />
                            </div>
                            <CardBody>
                              {skeleton ? (
                                <>
                                  <Placeholder className="placeholder-glow w-50" />
                                  <p className="placeholder-glow">
                                    {[7, 4, 4, 6, 8].map((col, i) => (
                                      <Placeholder
                                        key={i}
                                        tag="span"
                                        className={`placeholder col-${col} ${colored ? "bg-secondary" : ""}`}
                                      />
                                    ))}
                                  </p>
                                  <div className="d-flex gap-2">
                                    <span className="w-50 btn btn-secondary disabled">
                                      <Placeholder className="w-100" />
                                    </span>
                                    <span
                                      className={`btn btn-secondary disabled w-50 ${colored ? "btn-primary" : "invisible"}`}
                                    >
                                      <Placeholder className="w-100" />
                                    </span>
                                  </div>
                                </>
                              ) : (
                                <>
                                  <h5 className="card-title">{title}</h5>
                                  <p className="card-text">{text}</p>
                                  <span className="btn btn-primary">
                                    Go somewhere
                                  </span>
                                </>
                              )}
                            </CardBody>
                          </Card>
                        </Col>
                      )
                    )}
                  </Row>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xl={6}>
            <Card>
              <CardHeader>
                <h5>Width</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-column gap-3">
                  <Placeholder className="col-6" />
                  <Placeholder className="w-75" />
                  <Placeholder className="w-25" />
                </div>
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <h5>Animation</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-column gap-4">
                  <p className="placeholder-glow">
                    <Placeholder className="col-12" />
                  </p>
                  <p className="placeholder-wave">
                    <Placeholder className="col-12" />
                  </p>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xl={6}>
            <Card className="equal-card">
              <CardHeader>
                <h5>Color Variant</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-column">
                  {placeholderVariants.map((variant, index) => (
                    <p key={index} className="placeholder-glow">
                      <Placeholder className={`col-12 ${variant}`} />
                    </p>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xs={12}>
            <Card className="equal-card">
              <CardHeader>
                <h5>Sizing</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-column gap-3">
                  {placeholderSizes.map((size, index) => (
                    <Placeholder key={index} className={`col-12 ${size}`} />
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default PlaceholderPage;
