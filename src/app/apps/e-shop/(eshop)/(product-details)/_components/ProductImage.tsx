import React from "react";
import { Card, CardBody, Col, Row } from "reactstrap";
import { IconTag } from "@tabler/icons-react";

const ProductImage = () => {
  return (
    <Col lg={6} xxl={3}>
      <Card>
        <CardBody>
          <Row className="g-2 my-2">
            <Col xs={12}>
              <div className="product-image-box">
                <img
                  src="/images/ecommerce/25.jpg"
                  className="img-fluid rounded"
                  alt="image"
                />
              </div>
            </Col>
            <Col xs={3}>
              <div className="product-image-box">
                <img
                  src="/images/ecommerce/25.jpg"
                  className="img-fluid rounded"
                  alt="image"
                />
              </div>
            </Col>
            <Col xs={3}>
              <div className="product-image-box">
                <img
                  src="/images/ecommerce/26.jpg"
                  className="img-fluid rounded"
                  alt="image"
                />
              </div>
            </Col>
            <Col xs={3}>
              <div className="product-image-box">
                <img
                  src="/images/ecommerce/27.jpg"
                  className="img-fluid rounded"
                  alt="image"
                />
              </div>
            </Col>
            <Col xs={3}>
              <div className="product-image-box">
                <img
                  src="/images/ecommerce/28.jpg"
                  className="img-fluid rounded"
                  alt="image"
                />
              </div>
            </Col>
          </Row>

          <div className="mt-4">
            <h6 className="text-danger f-s-18">
              <b>Only 2 left in stock.</b>
            </h6>
            <p className="mb-0 f-s-15 text-secondary">
              Sold by
              <a className="link-primary  text-decoration-underline">
                Regaltos PVT.LTD
              </a>
              and quality checked by
              <a className="link-primary  text-decoration-underline">
                Spruko Tchnologies
              </a>
            </p>
            <div className="app-divider-v dotted p-2 m-0"></div>
            <ul className="offer-details-list">
              <li className="f-w-500">
                <IconTag size={18} className="text-primary me-1" />
                <b className="text-dark">Bank Offer</b> 10% Instant Discount on
                ICICI Bank Credit Card, up to ₹1250 on orders of ₹5,000 and
                above
              </li>
              <li className="f-w-500">
                <IconTag size={18} className="text-primary me-1" />
                <b className="text-dark">Bank Offer</b>
                Kotak Bank Credit Card, up to ₹1250 on orders of ₹5,000 and
                above
              </li>
            </ul>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default ProductImage;
