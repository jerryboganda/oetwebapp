import React from "react";
import { Card, CardBody, Col, Row, Table } from "reactstrap";
import { IconStar } from "@tabler/icons-react";
import Image from "next/image";
const products = [
  {
    id: "#AB9875",
    name: "Sports shoes",
    price: 450,
    image: "/images/ecommerce/29.png",
  },
  {
    id: "#AB8394",
    name: "Smartwatch",
    price: 920,
    image: "/images/ecommerce/33.png",
  },
  {
    id: "#AB3804",
    name: "T-shirt",
    price: 100,
    image: "/images/ecommerce/34.png",
  },
  {
    id: "#AB2903",
    name: "Airpods",
    price: 10900,
    image: "/images/ecommerce/30.png",
  },
];
const ProductReturns = () => {
  return (
    <Col lg={6} xxl={3}>
      <Card>
        <CardBody>
          <div className="product-details-contentbox">
            <div>
              <h5>Returns:</h5>
              <p>
                Returns is a scheme provided by respective sellers directly
                under this policy in terms of which the option of exchange,
                replacement and/ or refund is offered by the respective sellers
                to you.
              </p>
            </div>

            <div className="mt-4">
              <div className="product-review">
                {/* Review 1 */}
                <div className="mb-3">
                  <Row className="align-items-start">
                    <Col xs="auto" className="position-relative">
                      <div className="w-35 h-35 bg-danger text-white d-flex justify-content-center align-items-center rounded-circle">
                        <span className="fw-bold">EM</span>
                      </div>
                    </Col>
                    <Col className="">
                      <div className="d-flex justify-content-between align-items-center">
                        <span className="fs-6 fw-medium text-secondary">
                          Elyssa Moen
                        </span>
                        <IconStar size={18} />
                      </div>
                      <p className="text-muted mt-2 mb-0">
                        I got your first assignment. It was quite good. 🥳 We
                        can continue with the next assignment.
                      </p>
                    </Col>
                  </Row>
                </div>

                {/* Divider */}
                <div className="border-top border-dashed py-2"></div>

                {/* Review 2 */}
                <div>
                  <Row className="align-items-start">
                    <Col xs="auto" className="position-relative">
                      <div className="w-35 h-35 bg-secondary d-flex justify-content-center align-items-center rounded-circle overflow-hidden">
                        <Image
                          src="/images/avatar/16.png"
                          alt="Mark"
                          width={35}
                          height={35}
                          className="img-fluid rounded-circle"
                        />
                      </div>
                    </Col>
                    <Col className="">
                      <div className="d-flex justify-content-between align-items-center">
                        <span className="fs-6 fw-medium text-secondary">
                          Mark
                        </span>
                        <IconStar size={18} />
                      </div>
                      <p className="text-muted mt-2 mb-0">
                        &#34;This Top is not only stylish but also incredibly
                        warm.&#34;
                      </p>
                      <div className="mt-3 text-end">
                        <Image
                          src="/images/ecommerce/01.jpg"
                          alt="product 1"
                          width={40}
                          height={40}
                          className="rounded me-2"
                        />
                        <Image
                          src="/images/ecommerce/02.jpg"
                          alt="product 2"
                          width={40}
                          height={40}
                          className="rounded"
                        />
                      </div>
                    </Col>
                  </Row>
                </div>
              </div>
            </div>

            <div className="mt-4">
              <h5>Products:</h5>
              <div className="product-details-table">
                <Table className="table-bottom-border align-middle products-data-table">
                  <tbody>
                    {products.map((product, index) => (
                      <tr key={index} className="">
                        <td>
                          <div className="position-relative">
                            <Image
                              src={product.image}
                              alt={product.name}
                              width={45}
                              height={45}
                              className="w-45 h-45 position-absolute"
                            />
                            <div className="mg-s-40">
                              <h6 className="text-dark f-w-600 txt-ellipsis-1">
                                {product.name}
                              </h6>
                              <p className="text-secondary mb-0">
                                {product.id}
                              </p>
                            </div>
                          </div>
                        </td>
                        <td className="text-end">
                          <h6 className="f-s-15 text-success">
                            ${product.price.toLocaleString()}
                          </h6>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              </div>
            </div>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default ProductReturns;
