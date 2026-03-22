"use client";
import React from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Container, Row } from "reactstrap";
import ProductImage from "@/app/apps/e-shop/(eshop)/(product-details)/_components/ProductImage";
import ProductInfo from "@/app/apps/e-shop/(eshop)/(product-details)/_components/ProductInfo";
import ProductReturns from "@/app/apps/e-shop/(eshop)/(product-details)/_components/ProductReturns";
import { IconStack2 } from "@tabler/icons-react";

const ProductDetailsPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Product Details"
          title="Apps"
          path={["E-shop", "Product Details"]}
          Icon={IconStack2}
        />
        <Row>
          <ProductImage />
          <ProductInfo />
          <ProductReturns />
        </Row>
      </Container>
    </div>
  );
};

export default ProductDetailsPage;
