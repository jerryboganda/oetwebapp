import React from "react";
import { Card, CardBody, Col, Input, Label, Progress } from "reactstrap";
import Link from "next/link";
import { IconStarFilled } from "@tabler/icons-react";
const ratingData = [
  { star: 5, count: 4567, percent: 74, color: "primary" },
  { star: 4, count: 2765, percent: 65, color: "secondary" },
  { star: 3, count: 1682, percent: 40, color: "success" },
  { star: 2, count: 2380, percent: 25, color: "warning" },
  { star: 1, count: 19211, percent: 10, color: "danger" },
];
const ProductInfo = () => {
  return (
    <Col xxl={6}>
      <Card>
        <CardBody>
          <div className="product-details-contentbox">
            <h4>Trendy &amp; Stylish Loafers For Men</h4>
            <div className="mt-2 d-flex align-items-center ">
              <IconStarFilled size={20} className="text-warning" />
              <IconStarFilled size={20} className="text-warning" />
              <IconStarFilled size={20} className="text-warning" />
              <IconStarFilled size={20} className="text-warning-dark" />
              <IconStarFilled size={20} className="text-warning-dark" />
              <div>
                <h6 className="m-0 text-warning-dark">
                  (<span className="f-w-600">4.50k</span> Review )
                </h6>
              </div>
            </div>
            <div className="mt-4 product-details">
              <h3>
                $26.00 <span>(54% OFF)</span>
              </h3>
            </div>

            <div className="app-divider-v dotted py-4 m-0"></div>

            <div className="product-detailbox">
              <div>
                <h5>Size:</h5>
                <div className="form-selectgroup d-flex gap-1">
                  {[6, 7, 25, 30, 40].map((size, index) => (
                    <Label key={size} className="select-items">
                      <Input
                        type="checkbox"
                        className="select-input"
                        defaultChecked={index === 0}
                      />
                      <span className="select-box">
                        <span className="selectitem">{size}</span>
                      </span>
                    </Label>
                  ))}
                </div>
              </div>

              <div>
                <h5>Color:</h5>
                <div className="option-color-list check-container">
                  {[
                    "primary",
                    "secondary",
                    "success",
                    "danger",
                    "warning",
                    "info",
                    "light",
                    "dark",
                  ].map((color, index) => (
                    <Label key={index} className="check-box">
                      <Input
                        type="radio"
                        name="radio-group1"
                        defaultChecked={index === 0}
                      />
                      <span className={`radiomark check-${color} ms-2`}></span>
                    </Label>
                  ))}
                </div>
              </div>
            </div>

            <div className="mt-4">
              <h5>Description :</h5>
              <div className="mt-3">
                <p>
                  A product description is a piece of writing that conveys the
                  features and benefits of a product, ranging from basic facts
                  to stories that make a product compelling to an ideal buyer.
                </p>
                <p>
                  Aside from educating and enticing potential customers, the
                  best descriptions can help you differentiate your product and
                  brand from your competitors by putting forward your most
                  salient features and benefits.
                </p>
              </div>
            </div>

            <div className="mt-4">
              <div className="d-flex align-items-center">
                <div className="me-2">
                  <IconStarFilled size={30} className="text-warning" />
                </div>
                <div>
                  <h5 className="text-dark f-w-600 m-0">3.2 out of 5</h5>
                  <p className="mb-0 text-secondary f-s-13">
                    Based on (20,435) ratings
                  </p>
                </div>
              </div>

              <div className="mt-3">
                {ratingData.map((rating, index) => (
                  <div className="d-flex align-items-center mt-3" key={index}>
                    <h6 className="mb-0">{rating.star}</h6>
                    <IconStarFilled size={9} className="text-warning ms-1" />
                    <Progress
                      value={rating.percent}
                      color={rating.color}
                      striped
                      className="w-100 ms-2 me-2"
                    />
                    <h6 className="mb-0">({rating.count.toLocaleString()})</h6>
                  </div>
                ))}
              </div>
            </div>

            <div className="product-details-btn text-end mt-4">
              <Link
                href="/apps/e-shop/cart"
                role="button"
                className="btn btn-primary rounded"
              >
                Add To Cart
              </Link>{" "}
              <Link
                href="/apps/e-shop/checkout"
                role="button"
                className="btn btn-success rounded"
              >
                Buy Now
              </Link>{" "}
              <Link
                href="/apps/e-shop/wishlist"
                role="button"
                className="btn btn-danger rounded"
              >
                Add to Wishlist
              </Link>
            </div>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default ProductInfo;
