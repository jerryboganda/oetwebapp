"use client";
import React from "react";
import { Card, CardBody, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import CheckoutTabs from "@/app/apps/e-shop/(eshop)/(checkout)/_components/CheckoutTabs";
import CheckoutProducts from "@/app/apps/e-shop/(eshop)/(checkout)/_components/CheckoutProducts";
import { IconStack2 } from "@tabler/icons-react";

const CheckOutPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Checkout"
          title="Apps"
          path={["E-shop", "Checkout"]}
          Icon={IconStack2}
        />
        <Row>
          <Col lg={8}>
            <CheckoutTabs />
          </Col>

          <Col lg={4}>
            <Card>
              <CardBody>
                <CheckoutProducts />
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default CheckOutPage;
