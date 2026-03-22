"use client";
import React from "react";
import { Card, CardBody, CardHeader, Row, Col, Container } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import SimplePricingCard from "@/app/apps/(pricing)/_components/SimplePricingCard";
import ComparePricing from "@/app/apps/(pricing)/_components/ComparePricing";
import PricingCard from "@/app/apps/(pricing)/_components/PricingCard";
import { IconStack2 } from "@tabler/icons-react";

const PricingPage = () => {
  const plans = [
    {
      plan: "Basic Plans",
      price: 20,
      yearlyPrice: 80,
      features: [
        "5 Free Websites",
        "1 GB Hard disk Storage",
        "1 field",
        "10 Users",
        "Email Support",
      ],
      imgSrc: "/images/pricing/1.png",
      imgSrcBack: "/images/pricing/9.png",
      bgColor: "secondary",
    },
    {
      plan: "Business Plans",
      price: 80,
      yearlyPrice: 100,
      features: [
        "40 Free Websites",
        "20 GB Hard disk Storage",
        "50 fields",
        "30 Users",
        "Email Support",
      ],
      imgSrc: "/images/pricing/2.png",
      imgSrcBack: "/images/pricing/8.png",
      isBest: true,
      bgColor: "primary",
    },
    {
      plan: "Premium Plans",
      price: 200,
      yearlyPrice: 500,
      features: [
        "Unlimited Websites",
        "40 GB Hard disk Storage",
        "Unlimited fields",
        "40 Users",
        "Email Support",
      ],
      imgSrc: "/images/pricing/3.png",
      imgSrcBack: "/images/pricing/7.png",
      bgColor: "secondary",
    },
    {
      plan: "Golden Premium Plans",
      price: 500,
      yearlyPrice: 1000,
      features: [
        "Unlimited Websites",
        "40 GB Hard disk Storage",
        "Unlimited fields",
        "Unlimited Users",
        "Email Support",
      ],
      imgSrc: "/images/pricing/15.png",
      imgSrcBack: "/images/pricing/14.png",
      isBest: true,
      bgColor: "primary",
    },
  ];
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Pricing"
          title="Apps"
          path={["Pricing"]}
          Icon={IconStack2}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Pricing Plans</h5>
              </CardHeader>
              <CardBody>
                <Row className="flip-pricing-container app-arrow">
                  {plans.map((plan, index) => (
                    <PricingCard key={index} {...plan} />
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Simple Pricing Plans</h5>
              </CardHeader>
              <CardBody>
                <SimplePricingCard />
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Compare plans</h5>
              </CardHeader>
              <CardBody>
                <ComparePricing />
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default PricingPage;
