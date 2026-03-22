"use client";
import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
} from "reactstrap";
import { products } from "@/Data/Apps/Eshopproduct/Eshopproduct";
import Link from "next/link";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconAlignJustified,
  IconEye,
  IconHeart,
  IconListTree,
  IconSearch,
  IconShoppingCartBolt,
  IconStack2,
  IconStarFilled,
} from "@tabler/icons-react";

import FilterSidebar from "@/app/apps/e-shop/(eshop)/(product)/_components/ProductFilter";

const ProductPage = () => {
  const [layout, setLayout] = useState("col-xxl-3");
  const [isListView, setIsListView] = useState(false);

  const handleGridLayout = () => {
    setIsListView(true);
    setLayout("col-sm-6");
  };

  const handleViewChange = (newLayout: string) => {
    setIsListView(false);
    setLayout(newLayout);
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Product"
          title="Apps"
          path={["E-shop", "Product"]}
          Icon={IconStack2}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardBody>
                <div className="product-header d-flex justify-content-between gap-3 align-items-center">
                  <div className="d-flex align-items-center">
                    <a
                      className="me-3 toggle-btn d-inline-block d-lg-none"
                      role="button"
                    >
                      <IconAlignJustified className="f-s-24" />
                    </a>
                    <form
                      className="app-form app-icon-form d-inline-block "
                      action="#"
                    >
                      <div className="position-relative">
                        <input
                          type="search"
                          className="form-control"
                          placeholder="Search..."
                          aria-label="Search"
                        />
                        <IconSearch size={18} className="text-dark" />
                      </div>
                    </form>
                  </div>
                  <div>
                    <Button
                      color="secondary product-view4 d-inline-block"
                      onClick={() => handleViewChange("col-xxl-3")}
                    >
                      IV
                    </Button>{" "}
                    <Button
                      color="secondary product-view3"
                      onClick={() => handleViewChange("col-md-4")}
                    >
                      III
                    </Button>{" "}
                    <Button
                      color="secondary  product-view2  d-none"
                      onClick={() => handleViewChange("col-sm-6")}
                    >
                      II
                    </Button>{" "}
                    <Button
                      color="secondary product-view"
                      onClick={() => handleViewChange("col-12")}
                    >
                      I
                    </Button>{" "}
                    <Button
                      color="primary grid-layout-view"
                      onClick={handleGridLayout}
                    >
                      <IconListTree size={22} />
                    </Button>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xxl={3} lg={4} className="product-box productbox">
            <Card>
              <CardHeader>
                <h5 className="mb-3">Filters</h5>
              </CardHeader>
              <CardBody className="p-0">
                <FilterSidebar />
              </CardBody>
            </Card>
          </Col>

          <Col xxl={9} lg={8}>
            <div className="product-wrapper-grid">
              <Row className={isListView ? "list-view" : ""}>
                {products.map((product) => (
                  <Col xs={12} key={product.id} className={layout}>
                    <Card className="overflow-hidden">
                      <CardBody className="p-0">
                        <div className="product-content-box">
                          <div className="product-grid">
                            <div className="product-image">
                              <a href="#" className="image">
                                <img
                                  className="pic-1"
                                  src={product.image1}
                                  alt=""
                                />
                                <img
                                  className="images_box"
                                  src={product.image2}
                                  alt=""
                                />
                              </a>
                              <ul className="product-links">
                                <li>
                                  <Link
                                    target="_blank"
                                    href="/apps/e-shop/wishlist"
                                    className="bg-danger h-30 w-30 d-flex-center b-r-20"
                                  >
                                    <IconHeart
                                      size={18}
                                      className="text-light"
                                    />
                                  </Link>
                                </li>
                                <li>
                                  <Link
                                    target="_blank"
                                    href="/apps/e-shop/cart"
                                    className="bg-primary h-30 w-30 d-flex-center b-r-20"
                                  >
                                    <IconShoppingCartBolt
                                      size={18}
                                      className="text-light"
                                    />
                                  </Link>
                                </li>
                                <li>
                                  <Link
                                    target="_blank"
                                    href="/apps/e-shop/product-details"
                                    className="bg-success h-30 w-30 d-flex-center b-r-20"
                                  >
                                    <IconEye size={18} className="text-light" />
                                  </Link>
                                </li>
                              </ul>
                            </div>
                          </div>
                          <div className="p-3">
                            <div className="d-flex justify-content-between align-items-center">
                              <Link
                                href="/apps/e-shop/product-details"
                                className="m-0 f-s-20 f-w-500"
                              >
                                {product.title}
                              </Link>
                              <p className="text-warning m-0">
                                {product.rating}{" "}
                                <span className="text-warning">
                                  <IconStarFilled size={14} />
                                </span>
                              </p>
                            </div>
                            <p className="text-secondary">
                              {product.description}
                            </p>
                            <div className="pricing-box">
                              <h6>
                                ${product.price}{" "}
                                <span>
                                  (<del>${product.originalPrice}</del>)
                                </span>
                                <span className="text-success ms-2">
                                  {product.discount}
                                </span>
                              </h6>
                            </div>
                          </div>
                        </div>
                      </CardBody>
                    </Card>
                  </Col>
                ))}
              </Row>
            </div>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ProductPage;
