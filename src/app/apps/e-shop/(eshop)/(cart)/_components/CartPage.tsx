"use client";
import { useState } from "react";
import { cartData } from "@/Data/Apps/Eshopcart/Eshopcart";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Form,
  Row,
  Table,
} from "react-bootstrap";

import { IconHeart, IconStack2, IconTrash } from "@tabler/icons-react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";

interface Product {
  id: number;
  name: string;
  price: number;
  image: string;
  color: string;
  size: string;
  quantity: number;
}

const CartPage = () => {
  const [products, setProducts] = useState<Product[]>(cartData);

  const updateQuantity = (id: number, amount: number) => {
    setProducts((prev) =>
      prev.map((item) =>
        item.id === id
          ? { ...item, quantity: Math.max(0, item.quantity + amount) }
          : item
      )
    );
  };

  const handleChange = (id: number, value: number) => {
    if (!isNaN(value) && value >= 0) {
      setProducts((prev) =>
        prev.map((item) =>
          item.id === id ? { ...item, quantity: value } : item
        )
      );
    }
  };

  const handleDelete = (id: number) => {
    setProducts((prev) => prev.filter((p) => p.id !== id));
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Cart"
          title="Apps"
          path={["E-shop", "Cart"]}
          Icon={IconStack2}
        />
        <Row>
          <Col lg={8}>
            <Card>
              <div className="card-body p-0">
                <div className="app-scroll table-responsive app-datatable-default">
                  <Table
                    id="example"
                    className="table cart-product-table align-middle text-center"
                  >
                    <thead>
                      <tr>
                        <th className="text-start" scope="col">
                          Product Name
                        </th>
                        <th scope="col">Price</th>
                        <th scope="col">Quantity</th>
                        <th scope="col">Total</th>
                        <th scope="col">Action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {products.length === 0 ? (
                        <tr>
                          <td colSpan={5} className="text-center py-4">
                            Your cart is empty.
                          </td>
                        </tr>
                      ) : (
                        products.map((product) => (
                          <tr key={product.id}>
                            <td className="text-start">
                              <div className="d-flex align-items-center gap-2">
                                <span className="b-1-light bg-primary-200 p-1 h-45 w-45 d-flex-center b-r-12 flex-shrink-0 overflow-hidden box-list-img">
                                  <img
                                    alt={product.name}
                                    src={product.image}
                                    className="img-fluid w-45 h-45 object-fit-cover"
                                  />
                                </span>
                                <div>
                                  <p className="mb-1 fw-bold">{product.name}</p>
                                  <p className="mb-0 small">
                                    Color: {product.color}
                                  </p>
                                  <p className="mb-0 small">
                                    Size: {product.size}
                                  </p>
                                </div>
                              </div>
                            </td>
                            <td>${product.price.toFixed(2)}</td>
                            <td>
                              <div className="d-flex align-items-center justify-content-center">
                                <Button
                                  variant="danger"
                                  className="h-35 w-35 d-flex-center b-r-6 p-0"
                                  onClick={() => updateQuantity(product.id, -1)}
                                >
                                  -
                                </Button>
                                <Form.Control
                                  type="text"
                                  className="h-35 w-55 ms-1 me-1 border b-r-6 text-center"
                                  value={product.quantity}
                                  onChange={(e) =>
                                    handleChange(
                                      product.id,
                                      parseInt(e.target.value)
                                    )
                                  }
                                />
                                <Button
                                  variant="primary"
                                  className="h-35 w-35 d-flex-center b-r-6 p-0"
                                  onClick={() => updateQuantity(product.id, 1)}
                                >
                                  +
                                </Button>
                              </div>
                            </td>
                            <td>
                              ${(product.price * product.quantity).toFixed(2)}
                            </td>
                            <td>
                              <Button
                                variant="outline-primary"
                                className="icon-btn b-r-4"
                              >
                                <IconHeart size={18} />
                              </Button>{" "}
                              <Button
                                variant="outline-danger"
                                className="icon-btn b-r-4 delete-btn"
                                onClick={() => handleDelete(product.id)}
                              >
                                <IconTrash size={18} />
                              </Button>
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </Table>
                </div>
              </div>
            </Card>
          </Col>

          <Col lg="4">
            <Row>
              <Col xs="12">
                <Card>
                  <CardHeader>
                    <h5>Price Details</h5>
                  </CardHeader>
                  <CardBody>
                    <div className="table-responsive">
                      <table className="table cart-side-table mb-0">
                        <tbody>
                          <tr className="total-price">
                            <th>Sub Total :</th>
                            <th className="text-end">
                              <span id="cart-sub">$359.96</span>
                            </th>
                          </tr>
                          <tr>
                            <td>Discount:</td>
                            <td className="text-end" id="cart-discount">
                              - $ 53.00
                            </td>
                          </tr>
                          <tr>
                            <td>Shipping Charge :</td>
                            <td className="text-end" id="cart-shipping">
                              $ 65.00
                            </td>
                          </tr>
                          <tr>
                            <td>Estimated Tax (12.5%) :</td>
                            <td className="text-end" id="cart-tax">
                              $ 44.99
                            </td>
                          </tr>
                          <tr className="total-price">
                            <th>Total (USD) :</th>
                            <th className="text-end">
                              <span id="cart-total">$415.96</span>
                            </th>
                          </tr>
                        </tbody>
                      </table>
                    </div>
                  </CardBody>
                </Card>
              </Col>
              <Col md="6" xl="12">
                <Card className="scratch-card position-relative">
                  <CardBody>
                    <span>
                      <i className="ph-duotone ph-gift f-s-35"></i>
                    </span>
                    <h4>Extra 10% off</h4>
                    <div className="scratch-code-box d-flex align-items-center justify-content-between">
                      <h6 className="mb-0">WIN190EGHY018</h6>
                      <div className="flex-shrink-0">
                        <Button
                          color="primary"
                          size="sm"
                          className="b-r-24"
                          id="copyBtn"
                        >
                          copy
                        </Button>
                      </div>
                    </div>
                    <div className="mt-3">
                      <p className="mb-0">
                        Valid till 4 May 2024. <span>T&amp;C Apply</span>
                      </p>
                    </div>
                    <div className="scratch-overlay"></div>
                  </CardBody>
                </Card>
              </Col>

              <Col md="6" xl="12">
                <Card className="gift-card">
                  <CardBody>
                    <div className="d-flex align-items-center gap-2">
                      <img
                        src="/images/ecommerce/01.gif"
                        alt="Gift"
                        width={35}
                        height={35}
                        className="w-35 h-35"
                      />
                      <h6 className="text-dark fw-bold fs-6 m-0">
                        Buying for a loved one?
                      </h6>
                    </div>
                    <div>
                      <p className="text-secondary mt-2">
                        Gift wrap and personalized message on card, Only for{" "}
                        <span className="text-dark fw-medium">
                          <b>$10.50 USD</b>
                        </span>
                      </p>
                      <div className="cart-gift text-end mt-4">
                        <Button color="primary">Add Gift</Button>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </Col>
            </Row>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default CartPage;
