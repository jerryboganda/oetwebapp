"use client";
import React from "react";
import { Container, Row } from "reactstrap";
import EcommerceDetail from "./EcommerceDetail";
import TopProduct from "./TopProduct";
import EcommerceCard from "./EcommerceCard";
import EcommerceSale from "./EcommerceSale";

const EcommerceDashboard = () => {
  return (
    <Container fluid className="mt-3">
      <Row>
        <EcommerceCard />
        <EcommerceDetail />
        <TopProduct />
        <EcommerceSale />
      </Row>
    </Container>
  );
};

export default EcommerceDashboard;
