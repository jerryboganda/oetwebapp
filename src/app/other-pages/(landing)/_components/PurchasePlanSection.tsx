import React from "react";
import { Container, Row, Col, Card, CardBody, Button } from "reactstrap";
import { Check } from "phosphor-react";
import SectionHeading from "@/app/other-pages/(landing)/_components/SectionHeading";

const pricingPlans = [
  {
    title: "Starter",
    price: "$14.00",
    badgeColor: "primary",
    image: "/images/landing/wallet.png",
    features: [
      "6 months of support",
      "Single workspace deployment",
      "Future upgrades included",
      "Ready for production use",
    ],
    link: "https://polytronx.com",
    buttonColor: "dark",
    highlight: false,
  },
  {
    title: "Business",
    price: "$499.00",
    badgeColor: "primary",
    image: "",
    features: [
      "6 months of support",
      "Single workspace deployment",
      "Future upgrades included",
      "Ready for production use",
    ],
    link: "https://polytronx.com",
    buttonColor: "primary",
    highlight: true,
  },
];

const PricingPlansSection = () => {
  return (
    <Container>
      <SectionHeading
        title="Choose"
        highlight="Plans"
        description="Compare the available plan options and choose the setup that fits your rollout."
      />

      <Row className="justify-content-center">
        {pricingPlans.map((plan, idx) => (
          <Col
            key={idx}
            md={plan.highlight ? 5 : 7}
            xl={plan.highlight ? 4 : 8}
          >
            <Card
              className={`pricing-cards mb-0 ${plan.highlight ? "active" : ""}`}
            >
              <CardBody className={plan.highlight ? "p-0" : ""}>
                <Row className={plan.highlight ? "" : "align-items-center"}>
                  {!plan.highlight && (
                    <Col md={5} xl={6}>
                      <div>
                        <img
                          src={plan.image}
                          alt={plan.title}
                          className="w-120"
                        />
                      </div>
                      <p className={`badge bg-${plan.badgeColor} f-s-16 mt-3`}>
                        {plan.title}
                      </p>
                      <h1 className="text-dark f-w-700 mt-3">{plan.price}</h1>
                      <p className="text-secondary txt-ellipsis-3 f-w-500 f-s-16">
                        Essential features at the best value. Get started today
                        with our budget-friendly pricing!
                      </p>
                    </Col>
                  )}

                  <Col
                    md={plan.highlight ? 12 : 7}
                    xl={plan.highlight ? 12 : 6}
                  >
                    <div className="pricing-details">
                      <div className="price-title">
                        {!plan.highlight && (
                          <h3 className="text-dark f-w-600 txt-ellipsis-1">
                            {plan.title} List
                          </h3>
                        )}
                        {plan.highlight && (
                          <>
                            <p>{plan.title}</p>
                            <h2>{plan.price}</h2>
                          </>
                        )}
                        <ul className="pricing-list-menu">
                          {plan.features.map((feature, i) => (
                            <li
                              key={i}
                              className={`pricing-listitem ${
                                plan.highlight ? "text-dark" : ""
                              }`}
                            >
                              <Check size={18} className="text-success me-2" />
                              {feature}
                            </li>
                          ))}
                        </ul>
                        <div className="text-center price-btn">
                          <Button
                            href={plan.link}
                            target="_blank"
                            color={plan.buttonColor}
                            size="lg"
                          >
                            View Plan
                          </Button>
                        </div>
                      </div>
                    </div>
                  </Col>
                </Row>
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </Container>
  );
};

export default PricingPlansSection;
